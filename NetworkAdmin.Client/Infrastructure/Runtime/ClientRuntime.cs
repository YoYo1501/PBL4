using NetworkAdmin.Client.Application.Interfaces;
using NetworkAdmin.Client.Infrastructure.Configuration;
using NetworkAdmin.Client.Infrastructure.Monitoring;
using NetworkAdmin.Client.Infrastructure.Networking;
using NetworkAdmin.Client.Infrastructure.Scanning;
using NetworkAdmin.Client.Infrastructure.ScreenSharing;
using NetworkAdmin.Client.Infrastructure.Chat;
using NetworkAdmin.Client.Application.State;

namespace NetworkAdmin.Client.Infrastructure.Runtime;

internal sealed class ClientRuntime
{
    private readonly ClientOptions _options;
    private readonly INetworkInfoService _networkInfoService;
    private readonly IPingService _pingService;
    private readonly INetworkScannerService _scannerService;
    private readonly ISystemMonitorService _monitorService;
    private readonly IScreenShareConsent? _screenShareUi;
    private readonly IRemoteInputService? _remoteInputService;
    private readonly IClientChatUi? _chatUi;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _runTask;

    public ClientRuntime(ClientOptions options, INetworkInfoService networkInfoService,
        IPingService pingService, INetworkScannerService scannerService, ISystemMonitorService monitorService,
        IScreenShareConsent? screenShareUi = null, IRemoteInputService? remoteInputService = null,
        IClientChatUi? chatUi = null)
    {
        _options = options;
        _networkInfoService = networkInfoService;
        _pingService = pingService;
        _scannerService = scannerService;
        _monitorService = monitorService;
        _screenShareUi = screenShareUi;
        _remoteInputService = remoteInputService;
        _chatUi = chatUi;
    }

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
            ClientChatService? chatService = null;
            Task reader = Task.CompletedTask, telemetry = Task.CompletedTask, networkInfo = Task.CompletedTask;
            try
            {
                using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
                connectTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                await connection.ConnectAsync(_options.Host, _options.Port, connectTimeout.Token).ConfigureAwait(false);
                retrySeconds = 1;
                var networkInfoService = new ClientNetworkInfoService(_networkInfoService, connection);
                await networkInfoService.SendSnapshotAsync(session.Token).ConfigureAwait(false);
                var screenShareService = _screenShareUi is null
                    ? null
                    : new ClientScreenShareService(_options, connection, _screenShareUi, _remoteInputService);
                chatService = new ClientChatService(connection, _chatUi);
                handler = new ClientCommandHandler(_pingService, connection, _options.AdminToken, screenShareService, chatService);
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
                if (chatService is not null) await chatService.DisposeAsync().ConfigureAwait(false);
                await connection.DisconnectAsync().ConfigureAwait(false);
                await ObserveStoppedTaskAsync(reader).ConfigureAwait(false);
                await ObserveStoppedTaskAsync(telemetry).ConfigureAwait(false);
                await ObserveStoppedTaskAsync(networkInfo).ConfigureAwait(false);
                if (handler is not null) await handler.DrainAsync().ConfigureAwait(false);
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
}
