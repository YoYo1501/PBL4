using NetworkAdminTool.Models;

namespace NetworkAdminTool.Interfaces;

public interface INetworkScannerService
{
    List<NetworkDevice> ScanNetwork(string subnet, int timeoutMs = 200, string? interfaceName = null, CancellationToken cancellationToken = default);
    Task<List<NetworkDevice>> ScanNetworkAsync(string subnet, int timeoutMs = 300, string? interfaceName = null, CancellationToken cancellationToken = default, IProgress<NetworkScanProgress>? progress = null);
    List<NetworkDevice> ScanLocalNetwork(string? interfaceName = null, CancellationToken cancellationToken = default);
}
