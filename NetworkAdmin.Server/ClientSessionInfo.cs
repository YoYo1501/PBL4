using System.Net.Sockets;
using NetworkAdmin.Shared.Messages;
using NetworkAdminTool.Models;

namespace NetworkAdmin.Server;

internal sealed class ClientSessionInfo
{
    public required string ClientId { get; init; }
    public required string ClientName { get; init; }
    public required string SessionId { get; init; }
    public DateTime ConnectedAt { get; init; }
    public DateTime LastSeen { get; set; }
    public DateTime? LastTelemetryAt { get; set; }
    public string RemoteAddress { get; init; } = string.Empty;
    public volatile bool IsReady;
    public SystemStats? LatestSystemStats { get; set; }
    public ClientNetworkInfo? LatestNetworkInfo { get; set; }
    public ScanResultMessage? LatestScanResult { get; set; }
    public required NetworkStream Stream { get; init; }
    public SemaphoreSlim SendLock { get; } = new(1, 1);
}
