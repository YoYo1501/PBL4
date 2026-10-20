using System.Net;
using System.Buffers.Binary;
using System.Globalization;

namespace NetworkAdmin.Shared.Protocol;

public static class ScanSubnet
{
    public const int MaxHosts = 510;
    public const int DefaultTimeoutMs = 800;
    public const int MinTimeoutMs = 100;
    public const int MaxTimeoutMs = 1000;
    public const string Help = "Enter an IPv4 unicast CIDR /23 through /30 (maximum 510 hosts), or a three-octet /24 prefix.";

    public static int ClampTimeout(int timeoutMs) =>
        Math.Clamp(timeoutMs <= 0 ? DefaultTimeoutMs : timeoutMs, MinTimeoutMs, MaxTimeoutMs);

    public static bool TryNormalize(string? value, out string subnet)
    {
        subnet = string.Empty;
        var text = value?.Trim() ?? string.Empty;
        if (!text.Contains('/') && text.Split('.').Length == 3) text += ".0/24";
        var parts = text.Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var prefix)
            || prefix is < 23 or > 30) return false;
        var octets = parts[0].Split('.');
        if (octets.Length != 4) return false;
        var bytes = new byte[4];
        for (var i = 0; i < 4; i++)
            if (!byte.TryParse(octets[i], NumberStyles.None, CultureInfo.InvariantCulture, out bytes[i])) return false;
        if (bytes[0] is 0 or 127 or >= 224) return false;
        var mask = uint.MaxValue << (32 - prefix);
        BinaryPrimitives.WriteUInt32BigEndian(bytes, BinaryPrimitives.ReadUInt32BigEndian(bytes) & mask);
        subnet = $"{new IPAddress(bytes)}/{prefix}";
        return true;
    }

    public static bool TryGetHostRange(string? value, out IReadOnlyList<string> hosts)
    {
        hosts = Array.Empty<string>();
        if (!TryNormalize(value, out var subnet)) return false;
        var parts = subnet.Split('/');
        var network = BinaryPrimitives.ReadUInt32BigEndian(IPAddress.Parse(parts[0]).GetAddressBytes());
        var count = (1 << (32 - int.Parse(parts[1], CultureInfo.InvariantCulture))) - 2;
        var result = new string[count];
        Span<byte> bytes = stackalloc byte[4];
        for (var i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes, network + (uint)i + 1);
            result[i] = new IPAddress(bytes).ToString();
        }
        hosts = result;
        return true;
    }
}
