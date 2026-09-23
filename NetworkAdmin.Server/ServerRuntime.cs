using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Protocol;
using NetworkAdminTool.Models;

namespace NetworkAdmin.Server;

// The UI reads snapshots; only the existing backend owns connections and command routing.
internal sealed class ServerRuntime
{
    private readonly ClientManager _clients = new();
    private readonly ServerCommandDispatcher _dispatcher;
    private readonly TcpHelloServer _server;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Dictionary<string, ClientSnapshot> _history = new();
    private Task? _serverTask;
    private volatile string? _error;

    public ServerRuntime(ServerOptions options)
    {
        _dispatcher = new(_clients);
        _server = new(options, _clients, _dispatcher);
        Endpoint = $"{options.Host}:{options.Port}";
    }

    public string Endpoint { get; }
    public string Status => _error is not null ? $"Stopped: {_error}" : _server.IsListening ? "Running" : "Stopped";
    public bool IsRunning => _server.IsListening;

    public void Start() => _serverTask ??= Task.Run(async () =>
    {
        try { await _server.RunAsync(_shutdown.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception ex) { _error = ex.Message; }
    });

    public async Task StopAsync()
    {
        await _shutdown.CancelAsync();
        if (_serverTask is not null) await _serverTask.ConfigureAwait(false);
    }

    // Called by the UI timer, never by a socket callback. Retain recent disconnected sessions.
    public IReadOnlyList<ClientSnapshot> GetClients()
    {
        var current = new HashSet<string>();
        foreach (var session in _clients.GetSessions())
        {
            lock (session)
            {
                current.Add(session.SessionId);
                _history[session.SessionId] = new ClientSnapshot(session.SessionId, session.ClientId,
                    session.LatestNetworkInfo?.Hostname ?? session.ClientName, session.RemoteAddress,
                    true, session.ConnectedAt, session.LastSeen, session.LastTelemetryAt,
                    session.LatestSystemStats?.CpuUsagePercent, session.LatestSystemStats?.RamUsagePercent,
                    (session.LatestNetworkInfo?.Interfaces ?? []).Select(i => new InterfaceSnapshot(
                        i.Name, i.IpAddress, i.MacAddress, i.Gateway, i.Subnet)).ToArray());
            }
        }
        foreach (var id in _history.Keys.Except(current).ToArray())
            _history[id] = _history[id] with { Connected = false };
        foreach (var old in _history.Values.Where(c => !c.Connected).OrderByDescending(c => c.LastSeen).Skip(200).ToArray())
            _history.Remove(old.SessionId);
        return _history.Values.OrderBy(c => c.ConnectedAt).ToArray();
    }

    public async Task<PingResult> PingAsync(string sessionId, string target, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
        return await _dispatcher.SendPingAsync(sessionId, target.Trim(), TimeSpan.FromSeconds(15), linked.Token)
            .ConfigureAwait(false);
    }

    public async Task<ScanResultMessage> ScanAsync(string sessionId, string subnet, CancellationToken token)
    {
        if (!ScanSubnet.TryNormalize(subnet, out var normalized)) throw new ArgumentException(ScanSubnet.Help);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
        return await _dispatcher.SendScanAsync(sessionId, normalized, TimeSpan.FromSeconds(60), linked.Token)
            .ConfigureAwait(false);
    }

    public static IReadOnlyList<ScanHostSnapshot> MatchScan(ScanResultMessage result, IReadOnlyList<ClientSnapshot> clients) =>
        result.Devices.Select(device =>
        {
            var matches = clients.Where(c => c.Connected && (c.Interfaces.Any(i => i.IpAddress == device.IpAddress)
                || (c.Interfaces.Count == 0 && c.RemoteAddress == device.IpAddress))).ToArray();
            return new ScanHostSnapshot(device.IpAddress, device.MacAddress,
                device.IsOnline ? "Online" : "Offline", matches.Length > 0 ? "Yes" : "No",
                matches.Length > 0 ? string.Join(", ", matches.Select(c => c.Hostname).Distinct()) : "—");
        }).ToArray();
}

internal sealed record InterfaceSnapshot(string Name, string IpAddress, string MacAddress, string Gateway, string Subnet);
internal sealed record ScanHostSnapshot(string IpAddress, string MacAddress, string Status, string ManagedClient, string Hostname);
internal sealed record ClientSnapshot(string SessionId, string ClientId, string Hostname, string RemoteAddress,
    bool Connected, DateTime ConnectedAt, DateTime LastSeen, DateTime? LastTelemetryAt,
    float? Cpu, float? Ram, IReadOnlyList<InterfaceSnapshot> Interfaces)
{
    public string IpAddress => Interfaces.FirstOrDefault()?.IpAddress ?? RemoteAddress;
    public string MacAddress => Interfaces.FirstOrDefault()?.MacAddress ?? "";
    public override string ToString() => $"{Hostname} | {IpAddress} | {(Connected ? "Connected" : "Disconnected")} | SessionId: {SessionId}";
}
