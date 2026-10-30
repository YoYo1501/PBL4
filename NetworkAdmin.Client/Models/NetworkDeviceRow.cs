namespace NetworkAdminTool.Models;

internal sealed class NetworkDeviceRow
{
    public string IpAddress { get; init; } = string.Empty;
    public string Hostname { get; init; } = "-";
    public string MacAddress { get; init; } = "-";
    public string StatusText { get; init; } = "-";
    public string ResponseTimeText { get; init; } = "-";
    public string Vendor { get; init; } = "Unknown";
    public string LastSeen { get; init; } = "-";
}
