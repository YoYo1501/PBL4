using NetworkAdmin.Shared.Messages;
using NetworkAdminTool.Interfaces;

namespace NetworkAdmin.Client.Services;

internal sealed class ClientTelemetryService
{
    private readonly ISystemMonitorService _monitorService;
    private readonly ServerConnection _connection;
    private readonly TimeSpan _interval;

    public ClientTelemetryService(
        ISystemMonitorService monitorService,
        ServerConnection connection,
        TimeSpan interval)
    {
        _monitorService = monitorService;
        _connection = connection;
        _interval = interval;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(_interval);

            while (!cancellationToken.IsCancellationRequested)
            {
                await SendOnceAsync(cancellationToken).ConfigureAwait(false);

                if (!await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task SendOnceAsync(CancellationToken cancellationToken)
    {
        var stats = _monitorService.GetSystemStats();
        await _connection.SendAsync(MessageType.SystemStats, stats, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Telemetry sent CPU: {stats.CpuUsagePercent:0.0}% | RAM: {stats.RamUsagePercent:0.0}%");
    }
}
