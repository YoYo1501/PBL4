namespace NetworkAdminTool.Models;

public sealed class ClientNetworkInfo
{
    public string Hostname { get; set; } = string.Empty;
    public List<NetworkInterfaceInfo> Interfaces { get; set; } = new();
}
