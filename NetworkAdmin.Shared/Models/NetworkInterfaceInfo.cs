namespace NetworkAdminTool.Models;

public class NetworkInterfaceInfo
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public string Gateway { get; set; } = string.Empty;

    public override string ToString()
    {
        if (!string.IsNullOrWhiteSpace(Description) && !string.IsNullOrWhiteSpace(IpAddress))
        {
            return $"{Description} ({IpAddress})";
        }

        if (!string.IsNullOrWhiteSpace(IpAddress))
        {
            return $"{Name} ({IpAddress})";
        }

        return Name;
    }
}
