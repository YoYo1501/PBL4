using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using NetworkAdminTool.Interfaces;
using NetworkAdminTool.Models;

namespace NetworkAdminTool.Services;

public class NetworkInfoService : INetworkInfoService
{
    private const int MaxConcurrentPings = 32;
    private const int PingTimeoutMs = 600;
    private const int PingRetryCount = 1;

    private readonly IPingService? _pingService;

    public NetworkInfoService()
    {
    }

    public NetworkInfoService(IPingService pingService)
    {
        _pingService = pingService;
    }

    public string GetLocalIpAddress()
    {
        return GetLocalIpAddress(null);
    }

    public string GetLocalIpAddress(string? interfaceName)
    {
        var targetInterface = string.IsNullOrWhiteSpace(interfaceName)
            ? GetPrimaryInterface()
            : FindInterface(interfaceName);

        return targetInterface != null ? GetIpv4Address(targetInterface) : string.Empty;
    }

    public List<NetworkInterfaceInfo> GetAvailableNetworkInterfaces()
    {
        return GetActiveInterfaces()
            .Select(ni => new NetworkInterfaceInfo
            {
                Name = ni.Name,
                Description = ni.Description,
                IpAddress = GetIpv4Address(ni),
                MacAddress = GetMacAddress(ni),
                Gateway = GetDefaultGatewayAddress(ni)
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.IpAddress))
            .OrderByDescending(x => !string.IsNullOrWhiteSpace(x.Gateway))
            .ThenBy(x => x.Name)
            .ToList();
    }

    public string GetDefaultGateway()
    {
        return GetDefaultGateway(null);
    }

    public string GetDefaultGateway(string? interfaceName)
    {
        var interfaces = GetActiveInterfaces();
        if (!string.IsNullOrWhiteSpace(interfaceName))
        {
            interfaces = interfaces.Where(ni => MatchesInterfaceName(ni, interfaceName));
        }

        return interfaces
            .Select(GetDefaultGatewayAddress)
            .FirstOrDefault(gw => !string.IsNullOrWhiteSpace(gw)) ?? string.Empty;
    }

    public NetworkDevice GetDeviceInfo(string ipAddress)
    {
        return GetDeviceInfoAsync(ipAddress).GetAwaiter().GetResult();
    }

    public async Task<NetworkDevice> GetDeviceInfoAsync(string ipAddress, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ipAddress) ||
            !IPAddress.TryParse(ipAddress, out var parsedIp) ||
            parsedIp.AddressFamily != AddressFamily.InterNetwork)
        {
            return new NetworkDevice { IpAddress = ipAddress, IsOnline = false };
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (_pingService is null)
        {
            return new NetworkDevice { IpAddress = ipAddress, IsOnline = false };
        }

        var result = await _pingService.PingAsync(ipAddress, timeoutMs: 400, retryCount: 1);

        return new NetworkDevice
        {
            IpAddress = ipAddress,
            IsOnline = result.Success,
            ResponseTimeMs = result.Success ? result.RoundtripTimeMs : null
        };
    }

    public List<string> GetConnectedDevices()
    {
        return GetConnectedDevicesAsync(null).GetAwaiter().GetResult();
    }

    public List<string> GetConnectedDevices(string interfaceName)
    {
        return GetConnectedDevicesAsync(interfaceName).GetAwaiter().GetResult();
    }

    public List<string> GetConnectedDevices(string interfaceName, CancellationToken cancellationToken)
    {
        return GetConnectedDevicesAsync(interfaceName, cancellationToken).GetAwaiter().GetResult();
    }

    public async Task<List<string>> GetConnectedDevicesAsync(string? interfaceName = null, CancellationToken cancellationToken = default)
    {
        var ipAddress = GetLocalIpAddress(interfaceName);
        if (string.IsNullOrWhiteSpace(ipAddress)) return new();

        var subnet = GetSubnetPrefix(ipAddress);
        if (string.IsNullOrWhiteSpace(subnet) || !TryParseCidrSubnet(subnet, out var hostIps)) return new();

        var ipsToScan = hostIps.ToList();
        var onlineIps = new ConcurrentBag<string>();

        using var throttler = new SemaphoreSlim(MaxConcurrentPings);
        var tasks = ipsToScan.Select(async ip =>
        {
            await throttler.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_pingService is null)
                {
                    return;
                }

                var result = await _pingService.PingAsync(ip, PingTimeoutMs, PingRetryCount);
                if (result.Success)
                {
                    onlineIps.Add(ip);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Ignore per-host failures so one bad probe does not stop the scan.
            }
            finally
            {
                throttler.Release();
            }
        });

        await Task.WhenAll(tasks);

        return onlineIps
            .OrderBy(ip => ParseIpToUInt32(IPAddress.Parse(ip)))
            .ToList();
    }

    public string GetSubnetPrefix(string localIp)
    {
        if (!IPAddress.TryParse(localIp, out var localAddress) || localAddress.AddressFamily != AddressFamily.InterNetwork)
            return string.Empty;

        var activeInterface = GetActiveInterfaces().FirstOrDefault(ni =>
            ni.GetIPProperties().UnicastAddresses.Any(u => u.Address.Equals(localAddress)));

        if (activeInterface == null) return string.Empty;

        var ipProp = activeInterface.GetIPProperties().UnicastAddresses
            .FirstOrDefault(u => u.Address.Equals(localAddress));

        if (ipProp?.IPv4Mask == null) return string.Empty;

        var networkAddress = GetNetworkAddress(localAddress, ipProp.IPv4Mask);
        var prefixLength = GetPrefixLength(ipProp.IPv4Mask);

        return $"{networkAddress}/{prefixLength}";
    }

    private static IEnumerable<NetworkInterface> GetActiveInterfaces() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                         ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                         ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel);

    private static NetworkInterface? GetPrimaryInterface() =>
        GetActiveInterfaces()
            .OrderByDescending(ni => ni.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
            .ThenByDescending(ni => ni.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
            .FirstOrDefault();

    private static NetworkInterface? FindInterface(string name) =>
        GetActiveInterfaces().FirstOrDefault(ni => MatchesInterfaceName(ni, name));

    private static bool MatchesInterfaceName(NetworkInterface ni, string name) =>
        ni.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
        ni.Description.Equals(name, StringComparison.OrdinalIgnoreCase);

    private static string GetIpv4Address(NetworkInterface ni) =>
        ni.GetIPProperties().UnicastAddresses
            .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? string.Empty;

    private static string GetDefaultGatewayAddress(NetworkInterface ni) =>
        ni.GetIPProperties().GatewayAddresses
            .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? string.Empty;

    private static string GetMacAddress(NetworkInterface ni)
    {
        try
        {
            var bytes = ni.GetPhysicalAddress().GetAddressBytes();
            return bytes.Length == 0
                ? string.Empty
                : string.Join("-", bytes.Select(b => b.ToString("X2")));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static IPAddress GetNetworkAddress(IPAddress address, IPAddress subnetMask)
    {
        var addrUint = ParseIpToUInt32(address);
        var maskUint = ParseIpToUInt32(subnetMask);
        return UInt32ToIp(addrUint & maskUint);
    }

    private static int GetPrefixLength(IPAddress subnetMask) =>
        System.Numerics.BitOperations.PopCount(ParseIpToUInt32(subnetMask));

    private static bool TryParseCidrSubnet(string subnetWithPrefix, out IEnumerable<string> hosts)
    {
        hosts = Enumerable.Empty<string>();
        var parts = subnetWithPrefix.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !IPAddress.TryParse(parts[0], out var netAddr) ||
            netAddr.AddressFamily != AddressFamily.InterNetwork ||
            !int.TryParse(parts[1], out var prefix) ||
            prefix is < 1 or > 30)
        {
            return false;
        }

        var netUint = ParseIpToUInt32(netAddr);
        var mask = uint.MaxValue << (32 - prefix);
        var network = netUint & mask;
        var broadcast = network | ~mask;

        if (broadcast <= network + 1) return false;

        hosts = EnumerateIpRange(network + 1, broadcast - 1);
        return true;
    }

    private static IEnumerable<string> EnumerateIpRange(uint firstHost, uint lastHost)
    {
        for (var current = firstHost; current <= lastHost; current++)
        {
            yield return UInt32ToIp(current).ToString();
        }
    }

    private static uint ParseIpToUInt32(IPAddress ip) =>
        BinaryPrimitives.ReadUInt32BigEndian(ip.GetAddressBytes());

    private static IPAddress UInt32ToIp(uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return new IPAddress(bytes);
    }
}
