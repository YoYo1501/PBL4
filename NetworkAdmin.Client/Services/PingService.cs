using System.Net.NetworkInformation;
using NetworkAdminTool.Interfaces;
using NetworkAdminTool.Models;

namespace NetworkAdminTool.Services
{
    public class PingService : IPingService
    {
        // An administration ping always sends four probes; discovery keeps its existing retry semantics.
        public async Task<PingResult> PingSeriesAsync(string host, CancellationToken cancellationToken = default)
        {
            var result = new PingResult { Host = host };
            for (var i = 1; i <= 4; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attempt = new PingAttempt { Number = i };
                try
                {
                    using var ping = new Ping();
                    var reply = await ping.SendPingAsync(host, TimeSpan.FromSeconds(1),
                        new byte[32], null, cancellationToken).ConfigureAwait(false);
                    attempt.Success = reply.Status == IPStatus.Success;
                    attempt.ResponseTimeMs = attempt.Success ? reply.RoundtripTime : null;
                    attempt.Status = reply.Status.ToString();
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { attempt.Status = ex.Message; }
                result.Attempts.Add(attempt);
            }
            result.Sent = result.Attempts.Count;
            result.Received = result.Attempts.Count(a => a.Success);
            result.Success = result.Received > 0;
            result.AverageResponseTimeMs = result.Success
                ? result.Attempts.Where(a => a.Success).Average(a => (double)a.ResponseTimeMs!.Value) : null;
            result.RoundtripTimeMs = (long)(result.AverageResponseTimeMs ?? 0);
            result.StatusMessage = $"{result.Received}/{result.Sent} replies; {result.PacketLossPercent:0.#}% packet loss";
            return result;
        }

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
