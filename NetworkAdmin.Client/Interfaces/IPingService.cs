using NetworkAdminTool.Models;

namespace NetworkAdminTool.Interfaces;

public interface IPingService
{
    PingResult Ping(string host, int timeoutMs = 2000, int retryCount = 1);
    Task<PingResult> PingAsync(string host, int timeoutMs = 2000, int retryCount = 1);
    Task<PingResult> PingSeriesAsync(string host, CancellationToken cancellationToken = default);
    List<PingResult> PingMultiple(string[] hosts, int timeoutMs = 2000, int retryCount = 1);
}
