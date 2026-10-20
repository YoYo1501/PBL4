using System.Net;
using System.Net.Sockets;

namespace NetworkAdmin.Shared.Models;

// Shared selection rules only; no additional fields are sent over the wire.
public static class NetworkInterfacePolicy
{
    private static readonly string[] VirtualMarkers =
        ["vmware", "vethernet", "virtual", "hyper-v", "loopback", "tunnel", "tap-", "vpn", "docker", "wsl"];

    public static bool IsVirtual(string name, string description) =>
        VirtualMarkers.Any(marker => $"{name} {description}".Contains(marker, StringComparison.OrdinalIgnoreCase));

    public static bool HasUsableIpv4(string address) =>
        IPAddress.TryParse(address, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork
        && ip.GetAddressBytes()[0] is > 0 and < 224 && !IPAddress.IsLoopback(ip);
}
