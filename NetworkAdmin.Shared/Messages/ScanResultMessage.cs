using NetworkAdminTool.Models;

namespace NetworkAdmin.Shared.Messages;

public sealed class ScanResultMessage
{
    public bool Success { get; set; }
    public string Subnet { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public List<NetworkDevice> Devices { get; set; } = new();
}
