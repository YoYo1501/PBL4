namespace NetworkAdminTool.Models;

public sealed class NetworkScanProgress
{
    public int ScannedHosts { get; init; }
    public int TotalHosts { get; init; }
    public int OnlineHosts { get; init; }
    public string CurrentIpAddress { get; init; } = string.Empty;
}
