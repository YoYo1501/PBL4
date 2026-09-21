using System.Net.Sockets;
using NetworkAdminTool.Interfaces;

namespace NetworkAdmin.Client.Services;

internal sealed class ClientRuntime
{
    private readonly ClientOptions _options;
    private readonly INetworkInfoService _networkInfoService;
    private readonly IPingService _pingService;
    private readonly INetworkScannerService _scannerService;
    private readonly ISystemMonitorService _monitorService;
    private readonly ServerConnection _connection = new();
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _runTask;

    public ClientRuntime(
        ClientOptions options,
        INetworkInfoService networkInfoService,
        IPingService pingService,
        INetworkScannerService scannerService,
        ISystemMonitorService monitorService)
    {
        _options = options;
        _networkInfoService = networkInfoService;
        _pingService = pingService;
        _scannerService = scannerService;
        _monitorService = monitorService;
    }

    public void Start()
    {
        _runTask = RunAsync();
    }

    public async Task StopAsync()
    {
        _shutdown.Cancel();
        await _connection.DisconnectAsync().ConfigureAwait(false);

        if (_runTask is not null)
        {
            try
            {
                await _runTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task RunAsync()
    {
        try
        {
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            connectTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            await _connection.ConnectAsync(_options.Host, _options.Port, connectTimeout.Token).ConfigureAwait(false);
            await new ClientNetworkInfoService(_networkInfoService, _connection)
                .SendSnapshotAsync(_shutdown.Token).ConfigureAwait(false);

            var commandHandler = new ClientCommandHandler(_pingService, _connection);
            commandHandler.SetScanner(_scannerService);
            var readLoop = _connection.RunReadLoopAsync(commandHandler.HandleAsync, _shutdown.Token);
            var telemetry = new ClientTelemetryService(_monitorService, _connection, _options.TelemetryInterval)
                .RunAsync(_shutdown.Token);

            await Task.WhenAny(readLoop, telemetry).ConfigureAwait(false);
            _shutdown.Cancel();
            await IgnoreFailureAsync(readLoop).ConfigureAwait(false);
            await IgnoreFailureAsync(telemetry).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (SocketException)
        {
        }
        catch (IOException)
        {
        }
        catch (InvalidDataException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task IgnoreFailureAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (SocketException)
        {
        }
    }
}