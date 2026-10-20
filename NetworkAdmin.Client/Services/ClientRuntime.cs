using NetworkAdminTool.Interfaces;

namespace NetworkAdmin.Client.Services;

internal sealed class ClientRuntime
{
    private readonly ClientOptions _options;
    private readonly INetworkInfoService _networkInfoService;
    private readonly IPingService _pingService;
    private readonly INetworkScannerService _scannerService;
    private readonly ISystemMonitorService _monitorService;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _runTask;

    public ClientRuntime(ClientOptions options, INetworkInfoService networkInfoService,
        IPingService pingService, INetworkScannerService scannerService, ISystemMonitorService monitorService)
    {
        _options = options;
        _networkInfoService = networkInfoService;
        _pingService = pingService;
        _scannerService = scannerService;
        _monitorService = monitorService;
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
            await using var connection = new ServerConnection();
            using var session = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            ClientCommandHandler? handler = null;
            Task reader = Task.CompletedTask, telemetry = Task.CompletedTask, networkInfo = Task.CompletedTask;
            try
            {
                using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
                connectTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                await connection.ConnectAsync(_options.Host, _options.Port, connectTimeout.Token).ConfigureAwait(false);
                retrySeconds = 1;
                var networkInfoService = new ClientNetworkInfoService(_networkInfoService, connection);
                await networkInfoService.SendSnapshotAsync(session.Token).ConfigureAwait(false);
                handler = new ClientCommandHandler(_pingService, connection);
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
