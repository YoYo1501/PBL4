using System.Collections.Concurrent;
using System.Diagnostics;
using NetworkAdmin.Shared.Protocol;
using System.Text.RegularExpressions;
using NetworkAdminTool.Interfaces;
using NetworkAdmin.Shared.Models;

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

        public async Task<List<NetworkDevice>> ScanNetworkAsync(
            string subnet,
            int timeoutMs = 300,
            string? interfaceName = null,
            CancellationToken cancellationToken = default,
            IProgress<NetworkAdminTool.Models.NetworkScanProgress>? progress = null)
        {
            if (!ScanSubnet.TryGetHostRange(subnet, out var hostAddresses))
            {
                throw new ArgumentException(ScanSubnet.Help, nameof(subnet));
            }

            var ipsToScan = hostAddresses.ToList();

            // Pass 1 warms ARP/neighbor cache to reduce first-probe misses.
            await ExecutePingPassAsync(ipsToScan, WarmUpTimeoutMs, WarmUpRetryCount, cancellationToken, collectResult: false);

            var effectiveTimeoutMs = Math.Clamp(timeoutMs, MinimumDiscoveryTimeoutMs, 1000);
            var pingResults = await ExecutePingPassAsync(
                ipsToScan,
                effectiveTimeoutMs,
                DiscoveryRetryCount,
                cancellationToken,
                collectResult: true,
                progress);

            var arpEntries = await GetArpEntriesAsync(cancellationToken).ConfigureAwait(false);
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
            if (string.IsNullOrWhiteSpace(subnet)) throw new InvalidOperationException("No IPv4 subnet is available on the selected interface.");
            return ScanNetwork(subnet, interfaceName: interfaceName, cancellationToken: cancellationToken);
        }

        private async Task<Dictionary<string, PingResult>> ExecutePingPassAsync(
            IReadOnlyCollection<string> ips,
            int timeoutMs,
            int retryCount,
            CancellationToken cancellationToken,
            bool collectResult,
            IProgress<NetworkAdminTool.Models.NetworkScanProgress>? progress = null)
        {
            var results = new ConcurrentDictionary<string, PingResult>(StringComparer.OrdinalIgnoreCase);
            using var throttler = new SemaphoreSlim(MaxConcurrentPings);
            var scanned = 0;
            var online = 0;
            var total = ips.Count;

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
                            if (result.Success)
                            {
                                Interlocked.Increment(ref online);
                            }
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                    catch
                    {
                        // Ignore per-host ping failures to keep the scan resilient.
                    }
                    finally
                    {
                        if (collectResult && progress != null)
                        {
                            progress.Report(new NetworkAdminTool.Models.NetworkScanProgress
                            {
                                ScannedHosts = Interlocked.Increment(ref scanned),
                                TotalHosts = total,
                                OnlineHosts = Volatile.Read(ref online),
                                CurrentIpAddress = ip
                            });
                        }
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

        private static async Task<Dictionary<string, string>> GetArpEntriesAsync(CancellationToken cancellationToken)
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
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                string output, error;
                try
                {
                    var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
                    var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
                    await Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync(timeout.Token)).ConfigureAwait(false);
                    output = await outputTask.ConfigureAwait(false);
                    error = await errorTask.ConfigureAwait(false);
                }
                finally
                {
                    if (!process.HasExited) process.Kill();
                }

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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
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
