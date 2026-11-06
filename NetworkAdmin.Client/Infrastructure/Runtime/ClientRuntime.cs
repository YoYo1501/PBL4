using NetworkAdmin.Client.Application.Interfaces;
using NetworkAdmin.Client.Infrastructure.Configuration;
using NetworkAdmin.Client.Infrastructure.Monitoring;
using NetworkAdmin.Client.Infrastructure.Networking;
using NetworkAdmin.Client.Infrastructure.Scanning;
using NetworkAdmin.Client.Application.State;
using NetworkAdmin.Client.Infrastructure.Chat;
using NetworkAdmin.Client.Infrastructure.ScreenSharing;

namespace NetworkAdmin.Client.Infrastructure.Runtime;

internal sealed class ClientRuntime
{
    private readonly ClientOptions _options;
    private readonly INetworkInfoService _networkInfoService;
    private readonly IPingService _pingService;
    private readonly INetworkScannerService _scannerService;
    private readonly ISystemMonitorService _monitorService;
    private readonly IClientChatUi _chatUi;
    private readonly IScreenShareConsent _screenConsent;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _runTask;

    public ClientRuntime(ClientOptions options, INetworkInfoService networkInfoService,
        IPingService pingService, INetworkScannerService scannerService, ISystemMonitorService monitorService,
        IClientChatUi chatUi, IScreenShareConsent screenConsent)
    {
        _options = options;
        _networkInfoService = networkInfoService;
        _pingService = pingService;
        _scannerService = scannerService;
        _monitorService = monitorService;
        _chatUi = chatUi;
        _screenConsent = screenConsent;
    }

    public ClientRuntime(ClientOptions options, INetworkInfoService networkInfoService,
        IPingService pingService, INetworkScannerService scannerService, ISystemMonitorService monitorService,
        IClientChatUi chatUi)
        : this(options, networkInfoService, pingService, scannerService, monitorService, chatUi, NullScreenShareConsent.Instance) { }

    public ClientRuntime(ClientOptions options, INetworkInfoService networkInfoService,
        IPingService pingService, INetworkScannerService scannerService, ISystemMonitorService monitorService)
        : this(options, networkInfoService, pingService, scannerService, monitorService,
            NullClientChatUi.Instance, NullScreenShareConsent.Instance) { }

    public void Start() => _runTask ??= Task.Run(RunAsync);

    public async Task StopAsync()
    {
        await _shutdown.CancelAsync();
        if (_runTask is not null) await _runTask.ConfigureAwait(false);
    }

    private async Task RunAsync()
    {
        var retrySeconds = 1;
        while (!_shutdown.IsCancellationRequested)
        {
            // Each attempt owns its stream and cancellation; old results cannot reach a new session.
            await using var connection = string.IsNullOrWhiteSpace(_options.TlsServerSha256)
                ? new ServerConnection() // In-memory loopback fixtures only; ClientOptions.Load requires TLS pinning.
                : new ServerConnection(_options.TlsServerSha256);
            using var session = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            ClientCommandHandler? handler = null;
            ClientChatSession? chatSession = null;
            ClientScreenShareModule? screenShareModule = null;
            Task reader = Task.CompletedTask, telemetry = Task.CompletedTask, networkInfo = Task.CompletedTask;
            try
            {
                using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
                connectTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                await connection.ConnectAsync(_options.Host, _options.Port, connectTimeout.Token).ConfigureAwait(false);
                retrySeconds = 1;
                var networkInfoService = new ClientNetworkInfoService(_networkInfoService, connection);
                await networkInfoService.SendSnapshotAsync(session.Token).ConfigureAwait(false);
                chatSession = new ClientChatSession(connection);
                _chatUi.Attach(chatSession);
                screenShareModule = new ClientScreenShareModule(_options, connection, _screenConsent);
                handler = new ClientCommandHandler(_pingService, connection, chatSession, screenShareModule, _options.AdminToken);
                handler.SetScanner(_scannerService);
                reader = connection.RunReadLoopAsync(handler.HandleAsync, session.Token);
                telemetry = new ClientTelemetryService(_monitorService, connection, _options.TelemetryInterval)
                    .RunAsync(session.Token);
                networkInfo = networkInfoService.RunAsync(_options.NetworkInfoInterval, session.Token);
                var completed = await Task.WhenAny(reader, telemetry, networkInfo).ConfigureAwait(false);
                await completed.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Console.WriteLine($"Client connection stopped: {ex.Message}; retrying in {retrySeconds}s.");
            }
            finally
            {
                await session.CancelAsync();
                await connection.DisconnectAsync().ConfigureAwait(false);
                await ObserveStoppedTaskAsync(reader).ConfigureAwait(false);
                await ObserveStoppedTaskAsync(telemetry).ConfigureAwait(false);
                await ObserveStoppedTaskAsync(networkInfo).ConfigureAwait(false);
                if (handler is not null) await handler.DrainAsync().ConfigureAwait(false);
                if (chatSession is not null)
                {
                    _chatUi.Detach(chatSession);
                    await chatSession.DisposeAsync().ConfigureAwait(false);
                }
                if (screenShareModule is not null) await screenShareModule.DisposeAsync().ConfigureAwait(false);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(retrySeconds), _shutdown.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            retrySeconds = Math.Min(retrySeconds * 2, 10);
        }
    }

    private static async Task ObserveStoppedTaskAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Console.WriteLine($"Client session cleanup: {ex.Message}"); }
    }

    private sealed class NullClientChatUi : IClientChatUi
    {
        public static NullClientChatUi Instance { get; } = new();
        public void Attach(IClientChatSession session) { }
        public void Detach(IClientChatSession session) { }
    }

    private sealed class NullScreenShareConsent : IScreenShareConsent
    {
        public static NullScreenShareConsent Instance { get; } = new();
        public Task<bool> RequestConsentAsync(string serverName, CancellationToken cancellationToken) => Task.FromResult(false);
        public void SetSharing(bool active, Action stop) { }
    }
}
