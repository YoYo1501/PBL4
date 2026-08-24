using NetworkAdminTool.Models;

namespace NetworkAdminTool.Interfaces;

public interface IPingService
{
    PingResult Ping(string host, int timeoutMs = 2000, int retryCount = 1);
    Task<PingResult> PingAsync(string host, int timeoutMs = 2000, int retryCount = 1);
    List<PingResult> PingMultiple(string[] hosts, int timeoutMs = 2000, int retryCount = 1);
}
