using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Models;
using NetworkAdmin.Shared.Policies;
using NetworkAdmin.Shared.Protocol;
using NetworkAdmin.Server.Application.Interfaces;
using NetworkAdmin.Server.Audit;
using NetworkAdmin.Server.Infrastructure.Security;
using NetworkAdmin.Server.Infrastructure.Chat;
using System.Security.Cryptography;

namespace NetworkAdmin.Server.Runtime;

// The UI reads snapshots; only the existing backend owns connections and command routing.
internal sealed class ServerRuntime : IServerRuntime
{
    private readonly ClientManager _clients = new();
    private readonly ServerCommandDispatcher _dispatcher;
    private readonly TcpHelloServer _server;
    private readonly SecurityAlertService _securityAlertService;
    private readonly ChatService _chatService;
    private readonly ScreenStreamingService _screenShareService;
    private readonly RemoteAdministrationAuditLog _auditLog = new();
    private event Action<RemoteChatMessage>? _chatMessageReceived;
    private readonly string _adminToken;
    private readonly string _adminId;
    private readonly System.Security.Cryptography.X509Certificates.X509Certificate2? _tlsCertificate;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Dictionary<string, ClientSnapshot> _history = new(StringComparer.OrdinalIgnoreCase);
    private Task? _serverTask;
    private volatile string? _error;
    private volatile string? _screenError;

    public ServerRuntime(ServerOptions options)
    {
        _dispatcher = new(_clients, _auditLog);
        _securityAlertService = new(_clients,
            (_, _, _) => Task.CompletedTask,
            options.SecurityAlertPolicy, _auditLog);
        _chatService = new(_clients, options.AdminId, options.AdminToken);
        _chatService.MessageReceived += OnChatMessageReceived;
        _screenShareService = new(options, _clients, _auditLog);
        _server = new(options, _clients, _dispatcher, securityAlertService: _securityAlertService,
            chatModule: _chatService, screenShareModule: _screenShareService);
        _adminToken = options.AdminToken;
        _adminId = options.AdminId;
        _tlsCertificate = options.TlsCertificate;
        SecurityFingerprint = options.TlsCertificate?.GetCertHashString(HashAlgorithmName.SHA256) ?? "TLS disabled (test configuration)";
        Endpoint = $"{options.Host}:{options.Port} | TLS SHA256 {SecurityFingerprint}";
    }

    public string Endpoint { get; }
    public string SecurityFingerprint { get; }
    public string Status => _error is not null ? $"Stopped: {_error}" :
        _screenError is not null ? $"Running; screen view unavailable: {_screenError}" : _server.IsListening ? "Running" : "Stopped";
    public bool IsRunning => _server.IsListening;
    public bool AutoDisconnectEnabled => _securityAlertService.AutoDisconnectEnabled;
    public event Action<RemoteChatMessage>? ChatMessageReceived
    {
        add => _chatMessageReceived += value;
        remove => _chatMessageReceived -= value;
    }
    public event Action<SecurityAlertMessage>? SecurityAlertReceived
    {
        add => _securityAlertService.AlertReceived += value;
        remove => _securityAlertService.AlertReceived -= value;
    }

    public void Start()
    {
        try { _screenShareService.Start(); }
        catch (Exception ex) { _screenError = ex.GetBaseException().Message; }
        _serverTask ??= Task.Run(async () =>
        {
            try { await _server.RunAsync(_shutdown.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _error = ex.Message;
            }
        });
    }

    public async Task StopAsync()
    {
        await _shutdown.CancelAsync();
        if (_serverTask is not null) await _serverTask.ConfigureAwait(false);
        await _securityAlertService.DisposeAsync().ConfigureAwait(false);
        await _chatService.DisposeAsync().ConfigureAwait(false);
        await _screenShareService.DisposeAsync().ConfigureAwait(false);
        _tlsCertificate?.Dispose();
    }

    public bool SetAutoDisconnectEnabled(bool enabled)
    {
        // The dashboard runs in the configured Server process. Requiring a configured
        // admin token keeps the local policy switch fail-closed in test/unconfigured mode.
        return !string.IsNullOrEmpty(_adminToken) &&
            _securityAlertService.SetAutoDisconnectEnabled(enabled, _adminId);
    }

    public async Task<IScreenViewerSession> RequestScreenAsync(string clientId, string sessionId,
        CancellationToken token, Action? onConnecting = null)
    {
        if (_tlsCertificate is null) throw new InvalidOperationException("Screen viewing requires TLS.");
        if (_screenError is not null) throw new InvalidOperationException($"Screen listener is unavailable: {_screenError}");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
        var authorization = new RemoteSessionAuthorization
        {
            AdminId = _adminId,
            AuthorizationToken = _adminToken,
            Permission = RemoteSessionPermission.ViewOnly,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(1)
        };
        return await _screenShareService.RequestViewAsync(clientId, sessionId, authorization, linked.Token, onConnecting)
            .ConfigureAwait(false);
    }

    public bool IsScreenSharing(string clientId) => _screenShareService.HasSession(clientId);

    public Task SendChatAsync(string clientId, string sessionId, string content, CancellationToken token) =>
        _chatService.SendFromAdminAsync(clientId, sessionId, _adminToken, content, token);

    public IRemoteChatSession OpenChatSession(string clientId, string sessionId)
    {
        if (!_clients.TryGet(sessionId, out var session) || session is null || !session.IsReady ||
            !string.Equals(session.ClientId, clientId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Client session is not active.");
        return _chatService.CreateSession(clientId, sessionId);
    }

    private void OnChatMessageReceived(RemoteChatMessage message)
    {
        try { _chatMessageReceived?.Invoke(message); }
        catch (Exception ex) { Console.WriteLine($"Chat runtime subscriber failed: {ex.Message}"); }
    }

    // Called by the UI timer, never by a socket callback. Retain recent disconnected sessions.
    public IReadOnlyList<ClientSnapshot> GetClients()
    {
        var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in _clients.GetSessions())
        {
            lock (session)
            {
                current.Add(session.ClientId);
                _history[session.ClientId] = new ClientSnapshot(session.SessionId, session.ClientId,
                    session.LatestNetworkInfo?.Hostname ?? session.ClientName, session.RemoteAddress,
                    true, session.ConnectedAt, session.LastSeen, session.LastTelemetryAt,
                    session.LatestSystemStats?.CpuUsagePercent, session.LatestSystemStats?.RamUsagePercent,
                    (session.LatestNetworkInfo?.Interfaces ?? []).Select(i => new InterfaceSnapshot(
                        i.Name, i.IpAddress, i.MacAddress, i.Gateway, i.Subnet, i.Description)).ToArray());
            }
        }
        foreach (var id in _history.Keys.Except(current, StringComparer.OrdinalIgnoreCase).ToArray())
            _history[id] = _history[id] with { Connected = false };
        foreach (var old in _history.Values.Where(c => !c.Connected).OrderByDescending(c => c.LastSeen).Skip(200).ToArray())
            _history.Remove(old.ClientId);
        return _history.Values.OrderBy(c => c.ConnectedAt).ToArray();
    }

    public async Task<PingResult> PingAsync(string sessionId, string target, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
        return await _dispatcher.SendPingAsync(sessionId, target.Trim(), TimeSpan.FromSeconds(15), linked.Token)
            .ConfigureAwait(false);
    }

    public async Task<ScanResultMessage> ScanAsync(string sessionId, string subnet, CancellationToken token,
        int timeoutMs = ScanSubnet.DefaultTimeoutMs)
    {
        if (!ScanSubnet.TryNormalize(subnet, out var normalized)) throw new ArgumentException(ScanSubnet.Help);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
        return await _dispatcher.SendScanAsync(sessionId, normalized, TimeSpan.FromSeconds(60), linked.Token, timeoutMs)
            .ConfigureAwait(false);
    }

    public Task<RemoteAdministrationResultMessage> GetAgentInfoAsync(
        string clientId,
        CancellationToken token,
        TimeSpan? timeout = null)
    {
        return _dispatcher.SendRemoteAdministrationCommandAsync(
            clientId,
            RemoteAdministrationCommandType.GetAgentInfo,
            _adminToken,
            timeout ?? TimeSpan.FromSeconds(15),
            token);
    }

    public Task<RemoteAdministrationResultMessage> PowerClientAsync(
        string clientId,
        RemoteAdministrationCommandType command,
        int countdownSeconds,
        CancellationToken token,
        Action<RemoteAdministrationResultMessage>? progress = null,
        TimeSpan? timeout = null)
    {
        if (!RemoteAdministrationCommandPolicy.IsPowerCommand(command))
            throw new ArgumentException("Only Shutdown or Restart is supported by this operation.", nameof(command));

        return _dispatcher.SendRemoteAdministrationCommandAsync(
            clientId,
            command,
            _adminToken,
            timeout ?? TimeSpan.FromSeconds(90),
            token,
            RemoteAdministrationCommandPolicy.ClampCountdown(countdownSeconds),
            progress);
    }

    public Task<RemoteAdministrationResultMessage> SendRemoteAdministrationAsync(
        string clientId,
        RemoteAdministrationCommandType command,
        CancellationToken token,
        Action<RemoteAdministrationResultMessage>? progress = null,
        TimeSpan? timeout = null)
    {
        return _dispatcher.SendRemoteAdministrationCommandAsync(
            clientId,
            command,
            _adminToken,
            timeout ?? TimeSpan.FromSeconds(30),
            token,
            progress: progress);
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

    public IReadOnlyList<ScanHostSnapshot> MapScanResult(ScanResultMessage result, IReadOnlyList<ClientSnapshot> clients) =>
        MatchScan(result, clients);
}
