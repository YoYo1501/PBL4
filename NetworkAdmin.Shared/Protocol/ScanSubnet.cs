using System.Net;
using System.Net.Sockets;

namespace NetworkAdmin.Shared.Protocol;

public static class ScanSubnet
{
    public const string Help = "Enter an IPv4 unicast CIDR /24 through /30 (for example 192.168.1.0/24), or a three-octet /24 prefix.";

    public static bool TryNormalize(string? value, out string subnet)
    {
        subnet = string.Empty;
        var text = value?.Trim() ?? string.Empty;
        if (!text.Contains('/') && text.Split('.').Length == 3) text += ".0/24";
        var parts = text.Split('/');
        if (parts.Length != 2 || parts[0].Split('.').Length != 4 ||
            !IPAddress.TryParse(parts[0], out var ip) || ip.AddressFamily != AddressFamily.InterNetwork ||
            !int.TryParse(parts[1], out var prefix) || prefix is < 24 or > 30) return false;
        var bytes = ip.GetAddressBytes();
        if (bytes[0] is 0 or 127 or >= 224) return false;
        bytes[3] &= (byte)(255 << (32 - prefix));
        subnet = $"{new IPAddress(bytes)}/{prefix}";
        return true;
    }
}
