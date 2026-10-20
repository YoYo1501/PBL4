using System.Net;
using System.Net.Sockets;

namespace NetworkAdmin.Shared.Policies;

// Shared selection rules only; no additional fields are sent over the wire.
public static class NetworkInterfacePolicy
{
    private static readonly string[] VirtualMarkers =
        [
            "vmware", "vethernet", "virtual", "virtualbox", "hyper-v", "loopback",
            "tunnel", "tap-", "vpn", "docker", "wsl", "bluetooth", "pseudo"
        ];

    public static bool IsVirtual(string name, string description) =>
        ContainsVirtualMarker(name) || ContainsVirtualMarker(description);

    public static bool HasUsableIpv4(string address)
    {
        if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = ip.GetAddressBytes();
        var isApipa = bytes[0] == 169 && bytes[1] == 254;
        return bytes[0] is > 0 and < 224 && !isApipa && !IPAddress.IsLoopback(ip);
    }

    private static bool ContainsVirtualMarker(string value) =>
        VirtualMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
