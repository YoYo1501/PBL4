using System.Net.NetworkInformation;
using NetworkAdminTool.Interfaces;
using NetworkAdminTool.Models;

namespace NetworkAdminTool.Services
{
    public class PingService : IPingService
    {
        public PingResult Ping(string host, int timeoutMs = 2000, int retryCount = 1)
        {
            return PingAsync(host, timeoutMs, retryCount).GetAwaiter().GetResult();
        }

        public async Task<PingResult> PingAsync(string host, int timeoutMs = 2000, int retryCount = 1)
        {
            var attempts = Math.Max(1, retryCount);
            var lastResult = new PingResult
            {
                Host = host,
                Success = false,
                RoundtripTimeMs = 0,
                StatusMessage = "Not started"
            };

            for (var i = 0; i < attempts; i++)
            {
                try
                {
                    using var ping = new Ping();
                    var reply = await ping.SendPingAsync(host, timeoutMs);

                    lastResult = new PingResult
                    {
                        Host = host,
                        Success = reply.Status == IPStatus.Success,
                        RoundtripTimeMs = reply.Status == IPStatus.Success ? reply.RoundtripTime : 0,
                        StatusMessage = reply.Status.ToString()
                    };

                    if (lastResult.Success)
                    {
                        return lastResult;
                    }
                }
                catch (Exception ex)
                {
                    lastResult = new PingResult
                    {
                        Host = host,
                        Success = false,
                        RoundtripTimeMs = 0,
                        StatusMessage = $"Loi: {ex.Message}"
                    };
                }
            }

            return lastResult;
        }

        public List<PingResult> PingMultiple(string[] hosts, int timeoutMs = 2000, int retryCount = 1)
        {
            return hosts.Select(host => Ping(host, timeoutMs, retryCount)).ToList();
        }

        public Task<PingResult> PingHostAsync(string ipOrHostname, int timeoutMs = 1000, int retryCount = 1)
        {
            return PingAsync(ipOrHostname, timeoutMs, retryCount);
        }

        public PingResult PingHost(string ipOrHostname, int timeoutMs = 1000, int retryCount = 1)
        {
            return Ping(ipOrHostname, timeoutMs, retryCount);
        }
    }
}
