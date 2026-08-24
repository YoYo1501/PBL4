using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using NetworkAdminTool.Interfaces;
using NetworkAdminTool.Models;

namespace NetworkAdminTool.Services
{
    public class NetworkScannerService : INetworkScannerService
    {
        private const int MaxConcurrentPings = 32;
        private const int WarmUpTimeoutMs = 250;
        private const int WarmUpRetryCount = 1;
        private const int DiscoveryRetryCount = 2;
        private const int MinimumDiscoveryTimeoutMs = 500;

        private readonly IPingService _pingService;
        private readonly INetworkInfoService _networkInfoService;

        public NetworkScannerService(IPingService pingService, INetworkInfoService networkInfoService)
        {
            _pingService = pingService;
            _networkInfoService = networkInfoService;
        }

        public List<NetworkDevice> ScanNetwork(string subnet, int timeoutMs = 300, string? interfaceName = null, CancellationToken cancellationToken = default)
        {
            return ScanNetworkAsync(subnet, timeoutMs, interfaceName, cancellationToken).GetAwaiter().GetResult();
        }

        public async Task<List<NetworkDevice>> ScanNetworkAsync(string subnet, int timeoutMs = 300, string? interfaceName = null, CancellationToken cancellationToken = default)
        {
            if (!TryGetHostRange(subnet, out var hostAddresses))
            {
                return new List<NetworkDevice>();
            }

            var ipsToScan = hostAddresses.ToList();
            if (ipsToScan.Count == 0)
            {
                return new List<NetworkDevice>();
            }

            // Pass 1 warms ARP/neighbor cache to reduce first-probe misses.
            await ExecutePingPassAsync(ipsToScan, WarmUpTimeoutMs, WarmUpRetryCount, cancellationToken, collectResult: false);

            var effectiveTimeoutMs = Math.Max(timeoutMs, MinimumDiscoveryTimeoutMs);
            var pingResults = await ExecutePingPassAsync(ipsToScan, effectiveTimeoutMs, DiscoveryRetryCount, cancellationToken, collectResult: true);

            var arpEntries = GetArpEntries();
            var onlineDevices = new List<NetworkDevice>();

            foreach (var ip in ipsToScan)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var hasPingSuccess = pingResults.TryGetValue(ip, out var pingResult) && pingResult.Success;
                var hasArpEntry = arpEntries.TryGetValue(ip, out var macAddress) && !string.IsNullOrWhiteSpace(macAddress);

                if (!hasPingSuccess && !hasArpEntry)
                {
                    continue;
                }

                onlineDevices.Add(new NetworkDevice
                {
                    IpAddress = ip,
                    MacAddress = hasArpEntry ? macAddress! : string.Empty,
                    IsOnline = true,
                    ResponseTimeMs = hasPingSuccess ? pingResult!.RoundtripTimeMs : null
                });
            }

            return onlineDevices
                .OrderBy(d => GetIpValue(d.IpAddress))
                .ToList();
        }

        public List<NetworkDevice> ScanLocalNetwork(string? interfaceName = null, CancellationToken cancellationToken = default)
        {
            var localIp = string.IsNullOrWhiteSpace(interfaceName)
                ? _networkInfoService.GetLocalIpAddress()
                : _networkInfoService.GetLocalIpAddress(interfaceName);

            var subnet = _networkInfoService.GetSubnetPrefix(localIp);
            return string.IsNullOrWhiteSpace(subnet)
                ? new List<NetworkDevice>()
                : ScanNetwork(subnet, interfaceName: interfaceName, cancellationToken: cancellationToken);
        }

        private static bool TryGetHostRange(string subnet, out IEnumerable<string> hosts)
        {
            hosts = Enumerable.Empty<string>();

            if (string.IsNullOrWhiteSpace(subnet))
            {
                return false;
            }

            if (TryParseLegacySubnet(subnet, out var legacyHosts))
            {
                hosts = legacyHosts;
                return true;
            }

            if (TryParseCidrSubnet(subnet, out var cidrHosts))
            {
                hosts = cidrHosts;
                return true;
            }

            return false;
        }

        private async Task<Dictionary<string, PingResult>> ExecutePingPassAsync(
            IReadOnlyCollection<string> ips,
            int timeoutMs,
            int retryCount,
            CancellationToken cancellationToken,
            bool collectResult)
        {
            var results = new ConcurrentDictionary<string, PingResult>(StringComparer.OrdinalIgnoreCase);
            using var throttler = new SemaphoreSlim(MaxConcurrentPings);

            var tasks = ips.Select(async ip =>
            {
                await throttler.WaitAsync(cancellationToken);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var result = await _pingService.PingAsync(ip, timeoutMs, retryCount);
                        if (collectResult)
                        {
                            results[ip] = result;
                        }
                    }
                    catch
                    {
                        // Ignore per-host ping failures to keep the scan resilient.
                    }
                }
                finally
                {
                    throttler.Release();
                }
            });

            await Task.WhenAll(tasks);
            return results.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
        }

        private static bool TryParseLegacySubnet(string subnet, out IEnumerable<string> hosts)
        {
            hosts = Enumerable.Empty<string>();
            var parts = subnet.Split('.', StringSplitOptions.TrimEntries);
            if (parts.Length != 3)
            {
                return false;
            }

            var octets = new int[3];
            for (var i = 0; i < 3; i++)
            {
                if (!int.TryParse(parts[i], out octets[i]) || octets[i] < 0 || octets[i] > 255)
                {
                    return false;
                }
            }

            hosts = Enumerable.Range(1, 254).Select(host => $"{octets[0]}.{octets[1]}.{octets[2]}.{host}");
            return true;
        }

        private static bool TryParseCidrSubnet(string subnet, out IEnumerable<string> hosts)
        {
            hosts = Enumerable.Empty<string>();
            var parts = subnet.Split('/', StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var baseAddress) || baseAddress.AddressFamily != AddressFamily.InterNetwork)
            {
                return false;
            }

            if (!int.TryParse(parts[1], out var prefixLength) || prefixLength < 1 || prefixLength > 30)
            {
                return false;
            }

            var baseIpValue = ToUInt32(baseAddress);
            var mask = prefixLength == 0 ? 0U : uint.MaxValue << (32 - prefixLength);
            var network = baseIpValue & mask;
            var broadcast = network | ~mask;
            if (broadcast <= network + 1)
            {
                return false;
            }

            var firstHost = network + 1;
            var lastHost = broadcast - 1;
            hosts = EnumerateIpRange(firstHost, lastHost);
            return true;
        }

        private static IEnumerable<string> EnumerateIpRange(uint firstHost, uint lastHost)
        {
            for (var current = firstHost; current <= lastHost; current++)
            {
                yield return ToIpString(current);
            }
        }

        private static uint ToUInt32(IPAddress ipAddress)
        {
            var bytes = ipAddress.GetAddressBytes();
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            return BitConverter.ToUInt32(bytes, 0);
        }

        private static string ToIpString(uint ipValue)
        {
            var bytes = BitConverter.GetBytes(ipValue);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            return new IPAddress(bytes).ToString();
        }

        private static int GetIpValue(string ipAddress)
        {
            var parts = ipAddress.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 4)
            {
                return int.MaxValue;
            }

            var value = 0;
            for (var i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], out var octet) || octet < 0 || octet > 255)
                {
                    return int.MaxValue;
                }

                value = (value << 8) + octet;
            }

            return value;
        }

        private static Dictionary<string, string> GetArpEntries()
        {
            var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "arp",
                    Arguments = "-a",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit(2000);

                if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(output) && !string.IsNullOrWhiteSpace(error))
                {
                    return entries;
                }

                var matches = Regex.Matches(output, @"(?<ip>\d{1,3}(?:\.\d{1,3}){3})\s+(?<mac>(?:[0-9A-Fa-f]{2}[-:]){5}[0-9A-Fa-f]{2})");
                foreach (Match match in matches)
                {
                    var ip = match.Groups["ip"].Value.Trim();
                    var mac = NormalizeMacAddress(match.Groups["mac"].Value.Trim());

                    if (!string.IsNullOrWhiteSpace(ip) && !string.IsNullOrWhiteSpace(mac))
                    {
                        entries[ip] = mac;
                    }
                }
            }
            catch
            {
                // In some environments ARP may not be available or may be blocked.
            }

            return entries;
        }

        private static string NormalizeMacAddress(string macAddress)
        {
            if (string.IsNullOrWhiteSpace(macAddress))
            {
                return string.Empty;
            }

            var cleaned = macAddress.Replace("-", ":").Replace(" ", string.Empty).Trim();
            var parts = cleaned.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 6)
            {
                return string.Empty;
            }

            return string.Join("-", parts.Select(p => p.PadLeft(2, '0')).Select(p => p.ToUpperInvariant()));
        }
    }
}
