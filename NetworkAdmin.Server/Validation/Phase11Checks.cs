using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using NetworkAdmin.Client;
using NetworkAdmin.Client.Services;
using NetworkAdmin.Server;
using NetworkAdmin.Server.UI;
using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Protocol;
using NetworkAdminTool.Interfaces;
using NetworkAdmin.Shared.Models;
using NetworkAdminTool.Services;

internal static class Phase11Checks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        check(ScanSubnet.TryNormalize("172.31.99.116/23", out var subnet) && subnet == "172.31.98.0/23", "Phase 11: /23 normalization across third octet");
        check(ScanSubnet.TryGetHostRange(subnet, out var hosts) && hosts.Count == 510
            && hosts[0] == "172.31.98.1" && hosts[^1] == "172.31.99.254"
            && hosts.Contains("172.31.98.255") && hosts.Contains("172.31.99.0")
            && !hosts.Contains("172.31.98.0") && !hosts.Contains("172.31.99.255"), "Phase 11: /23 usable hosts include both /24 halves and exclude only true network/broadcast");
        check(ScanSubnet.TryGetHostRange("192.168.1.129/24", out hosts) && hosts.Count == 254
            && hosts[0] == "192.168.1.1" && hosts[^1] == "192.168.1.254", "Phase 11: /24 range retained");
        check(ScanSubnet.TryGetHostRange("10.0.0.7/30", out hosts) && hosts.SequenceEqual(new[] { "10.0.0.5", "10.0.0.6" }), "Phase 11: /30 range retained");
        foreach (var invalid in new[] { "172.31.98.0/22", "10.0.0.1/0", "10.0.0.1/32", "10.0.0.1/-1", "10.0.0.1/33", "10.0.0.256/24", "10.0/24", "::1/24", "1.2.3.4/24/25", "10.0.0.1/+24", "0x0A.0.0.1/24" })
            check(!ScanSubnet.TryGetHostRange(invalid, out _), "Phase 11: reject " + invalid);

        var now = DateTime.UtcNow;
        var snapshot = new ClientSnapshot("s", "c", "host", "10.0.0.10", true, now, now, now, 1, 2,
            [new("VMware", "192.168.1.1", "", "192.168.1.2", ""), new("vEthernet", "172.16.0.1", "", "172.16.0.2", ""),
             new("Ethernet", "::1", "", "10.0.0.1", ""), new("Wi-Fi", "10.0.0.10", "", "", "10.0.0.0/24")]);
        check(snapshot.PrimaryInterface?.Name == "Wi-Fi" && snapshot.Interfaces.Count == 4, "Phase 11: physical IPv4 without gateway beats virtual gateway; all adapters retained");
        check(snapshot.HasFreshTelemetry(now) && !snapshot.HasFreshTelemetry(now.AddSeconds(11))
            && !(snapshot with { Connected = false }).HasFreshTelemetry(now)
            && !(snapshot with { LastTelemetryAt = null }).HasFreshTelemetry(now), "Phase 11: fresh, stale, disconnected and missing telemetry states");

        var probes = new RecordingPing();
        var network = new NetworkInfoService();
        var scanner = new NetworkScannerService(probes, network);
        var devices = await scanner.ScanNetworkAsync("172.31.99.116/23");
        check(devices.Count == 510 && probes.Addresses.Count == 510 && probes.Calls == 1020 && probes.Peak <= 32,
            "Phase 11: real scanner visits all 510 hosts in two passes with at most 32 active probes (fake ICMP)");
        try { await scanner.ScanNetworkAsync("10.0.0.0/8"); throw new Exception("Expected oversized scan rejection"); }
        catch (ArgumentException) { check(probes.Calls == 1020, "Phase 11: oversized direct scan rejected before probes"); }
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            try { await scanner.ScanNetworkAsync("10.0.0.0/23", cancellationToken: canceled.Token); throw new Exception("Expected cancellation"); }
            catch (OperationCanceledException) { check(probes.Calls == 1020, "Phase 11: canceled scan does not start probes"); }
        }
        await CheckReconnectAsync(check, network);
        CheckDashboardPresenterSessionState(check);
        await CheckTimeoutAsync(check);
        await CheckShutdownClosesIdleClientAsync(check);
    }

    private static async Task CheckReconnectAsync(Action<bool, string> check, INetworkInfoService network)
    {
        var port = ReservePort();
        var options = new ServerOptions { Host = IPAddress.Loopback, Port = port };
        var server = new ServerRuntime(options);
        var blocking = new BlockingScanner();
        var client = new ClientRuntime(new ClientOptions { Host = "127.0.0.1", Port = port, TelemetryInterval = TimeSpan.FromMilliseconds(200) },
            network, new PingService(), blocking, new FixedMonitor());
        ServerRuntime? restarted = null;
        client.Start(); // Deliberately start before the listener: first attempt must recover.
        try
        {
            await Task.Delay(500);
            server.Start();
            await Eventually(() => server.GetClients().Any(c => c.Connected && c.Cpu == 17));
            var first = server.GetClients().Single(c => c.Connected);
            check(first.Interfaces.Count > 0, "Phase 11: actual ClientRuntime retries initial failure and sends network info/telemetry");
            var dashboard = new ServerDashboardPresenter(server);
            dashboard.ObserveClients(server.GetClients());
            await dashboard.PingAsync(first, "127.0.0.1", default, () => false, () => { }, () => { });
            var dashboardState = dashboard.CommandsFor(first.ClientId);
            check(dashboardState.Ping is { Sent: 4, Received: 4 } && !dashboardState.PingPending,
                "Phase 11: dashboard presenter completes Ping for the captured client session");
            await dashboard.GetAgentInfoAsync(first, default, () => false, () => { }, () => { });
            check(dashboardState.RemoteInfoError && dashboardState.RemoteInfoStatus.Contains("Rejected"),
                "Phase 11: dashboard presenter surfaces rejected Remote Administration result");
            await dashboard.PowerAsync(first, RemoteAdministrationCommandType.Shutdown,
                RemoteAdministrationCommandPolicy.MinimumCountdownSeconds, default, () => false,
                () => { }, () => { }, (_, _, _, _) => { });
            check(dashboardState.PowerError && !dashboardState.PowerPending
                && dashboardState.PowerStatus.Contains("not configured", StringComparison.OrdinalIgnoreCase),
                "Phase 11: dashboard presenter surfaces rejected power command without scheduling it");
            var oldSession = first.SessionId;
            var manager = (ClientManager)typeof(ServerRuntime).GetField("_clients", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(server)!;
            manager.TryGet(oldSession, out var connected);
            connected!.Stream.Socket.LingerState = new LingerOption(true, 0);
            connected.Stream.Dispose(); // Abrupt socket reset, without a graceful application shutdown.
            await Eventually(() => server.GetClients().Any(c => c.Connected && c.SessionId != oldSession && c.Cpu == 17));
            first = server.GetClients().Single(c => c.Connected);
            check(server.GetClients().Count(c => c.ClientId == connected.ClientId) == 1
                && first.SessionId != oldSession && first.ClientId == connected.ClientId,
                "Phase 11: actual ClientRuntime recovers from unexpected socket reset without duplicate client rows");
            for (var reconnect = 1; reconnect <= 3; reconnect++)
            {
                var staleSessionId = first.SessionId;
                manager.TryGet(staleSessionId, out var staleSession);
                staleSession!.Stream.Socket.LingerState = new LingerOption(true, 0);
                staleSession.Stream.Dispose();
                await Eventually(() => server.GetClients().Any(c => c.Connected
                    && c.ClientId == first.ClientId && c.SessionId != staleSessionId && c.Cpu == 17));

                // Simulate the old handler's delayed finally block after its replacement is active.
                manager.Remove(staleSessionId);
                first = server.GetClients().Single(c => c.Connected && c.ClientId == connected.ClientId);
                dashboard.ObserveClients(server.GetClients());
                check(first.SessionId != staleSessionId
                    && manager.TryGet(first.SessionId, out var activeSession) && activeSession is { IsReady: true }
                    && server.GetClients().Count(c => c.ClientId == connected.ClientId) == 1,
                    $"Phase 11: reconnect {reconnect} keeps one active client when stale session cleanup arrives late");
            }
            var pendingScan = server.ScanAsync(first.SessionId, "172.31.99.116/23", default);
            await blocking.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await dashboard.ScanAsync(first, "172.31.98.0/23", ScanSubnet.DefaultTimeoutMs, default,
                () => false, () => { }, () => { });
            check(dashboardState.ScanError && dashboardState.ScanStatus.Contains("already running", StringComparison.OrdinalIgnoreCase),
                "Phase 11: dashboard presenter surfaces an active-scan rejection");
            var secondScan = await server.ScanAsync(first.SessionId, "172.31.98.0/23", default);
            check(!secondScan.Success && secondScan.ErrorMessage.Contains("already running") && blocking.LastSubnet == "172.31.98.0/23",
                "Phase 11: /23 normalized through real remote command path; overlapping scans rejected");
            await server.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            try { await pendingScan; throw new Exception("Expected interrupted scan"); }
            catch (Exception ex) when (ex is OperationCanceledException or IOException) { check(true, "Phase 11: server shutdown interrupts pending scan"); }
            await blocking.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            check(server.GetClients().All(c => !c.Connected), "Phase 11: stopped server retains Offline snapshots; old scan canceled");

            restarted = new ServerRuntime(options);
            restarted.Start();
            await Eventually(() => restarted.GetClients().Any(c => c.Connected && c.Cpu == 17));
            var second = restarted.GetClients().Single(c => c.Connected);
            check(second.ClientId == first.ClientId && second.SessionId != first.SessionId, "Phase 11: automatic reconnect keeps ClientId and receives new SessionId");
            var ping = await restarted.PingAsync(second.SessionId, "127.0.0.1", default);
            check(ping.Sent == 4 && ping.Received == 4, "Phase 11: reconnected session accepts real Ping commands");
            await client.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await Eventually(() => restarted.GetClients().All(c => !c.Connected));
            check(true, "Phase 11: client shutdown stops retries and marks session Offline");
        }
        finally
        {
            await client.StopAsync();
            await server.StopAsync();
            if (restarted is not null) await restarted.StopAsync();
        }
    }

    private static void CheckDashboardPresenterSessionState(Action<bool, string> check)
    {
        var now = DateTime.UtcNow;
        var presenter = new ServerDashboardPresenter(new ServerRuntime(new ServerOptions { Host = IPAddress.Loopback }));
        var first = new ClientSnapshot("session-1", "client-1", "agent", "127.0.0.1", true,
            now, now, now, 12, 34, []);
        presenter.ObserveClients([first]);
        var state = presenter.CommandsFor(first.ClientId);
        state.PingPending = state.ScanPending = state.PowerPending = true;
        presenter.ObserveClients([first with { SessionId = "session-2", LastSeen = now.AddSeconds(1),
            LastTelemetryAt = now.AddSeconds(1), Cpu = 20 }]);
        check(state.SessionId == "session-2" && !state.PingPending && !state.ScanPending && !state.PowerPending
            && state.PingStatus.Contains("interrupted") && state.ScanStatus.Contains("interrupted")
            && state.PowerStatus.Contains("interrupted") && presenter.TelemetryFor(first.ClientId).Count == 2,
            "Phase 11: dashboard presenter resets pending commands on session replacement and retains telemetry samples");
        presenter.ObserveClients([]);
        check(!presenter.Commands.ContainsKey(first.ClientId) && presenter.TelemetryFor(first.ClientId).Count == 0,
            "Phase 11: dashboard presenter releases state and telemetry for removed clients");
    }

    private static async Task CheckTimeoutAsync(Action<bool, string> check)
    {
        var manager = new ClientManager();
        var dispatcher = new ServerCommandDispatcher(manager);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var peer = new TcpClient();
        await peer.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using var accepted = await listener.AcceptTcpClientAsync();
        var session = manager.Register("id", "name", "session", accepted.GetStream(), out _);
        // Hold the writer lock: timeout must cover sending as well as waiting for a response.
        await session.SendLock.WaitAsync();
        try
        {
            foreach (var operation in new Func<Task>[] {
                () => dispatcher.SendPingAsync("session", "127.0.0.1", TimeSpan.FromMilliseconds(100), default),
                () => dispatcher.SendScanAsync("session", "10.0.0.0/24", TimeSpan.FromMilliseconds(100), default) })
            {
                try { await operation().WaitAsync(TimeSpan.FromSeconds(3)); throw new Exception("Expected timeout"); }
                catch (TimeoutException ex) { check(ex.Message.Contains("command timed out"), "Phase 11: command deadline covers waiting for send lock"); }
            }
        }
        finally { session.SendLock.Release(); }
    }

    private static async Task CheckShutdownClosesIdleClientAsync(Action<bool, string> check)
    {
        var port = ReservePort();
        var server = new ServerRuntime(new ServerOptions { Host = IPAddress.Loopback, Port = port });
        server.Start();
        await Eventually(() => server.IsRunning);

        using var peer = new TcpClient();
        await peer.ConnectAsync(IPAddress.Loopback, port);
        // A partial frame leaves the handler blocked in its hello read until shutdown.
        await peer.GetStream().WriteAsync("{"u8.ToArray());
        await Task.Delay(100);

        await server.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var read = await peer.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        check(read == 0 && !server.IsRunning,
            "Phase 11: shutdown closes a client blocked in the hello frame and awaits its handler");
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task Eventually(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!predicate()) await Task.Delay(25, timeout.Token);
    }

    private sealed class RecordingPing : IPingService
    {
        public ConcurrentDictionary<string, byte> Addresses { get; } = new();
        public int Calls, Peak;
        private int _active;
        public async Task<PingResult> PingAsync(string host, int timeoutMs = 2000, int retryCount = 1)
        {
            Addresses[host] = 0; Interlocked.Increment(ref Calls);
            var active = Interlocked.Increment(ref _active);
            int previous;
            do { previous = Peak; } while (active > previous && Interlocked.CompareExchange(ref Peak, active, previous) != previous);
            try { await Task.Delay(2); return new() { Host = host, Success = true }; }
            finally { Interlocked.Decrement(ref _active); }
        }
        public PingResult Ping(string host, int timeoutMs = 2000, int retryCount = 1) => throw new NotSupportedException();
        public Task<PingResult> PingSeriesAsync(string host, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public List<PingResult> PingMultiple(string[] hosts, int timeoutMs = 2000, int retryCount = 1) => throw new NotSupportedException();
    }

    private sealed class BlockingScanner : INetworkScannerService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? LastSubnet { get; private set; }
        public async Task<List<NetworkDevice>> ScanNetworkAsync(string subnet, int timeoutMs = 300, string? interfaceName = null,
            CancellationToken cancellationToken = default, IProgress<NetworkAdminTool.Models.NetworkScanProgress>? progress = null)
        {
            LastSubnet = subnet; Started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, cancellationToken); return []; }
            finally { Canceled.TrySetResult(); }
        }
        public List<NetworkDevice> ScanNetwork(string subnet, int timeoutMs = 200, string? interfaceName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public List<NetworkDevice> ScanLocalNetwork(string? interfaceName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedMonitor : ISystemMonitorService
    {
        public SystemStats GetSystemStats() => new() { CpuUsagePercent = 17, RamUsagePercent = 23 };
        public double GetCpuUsage() => 17;
        public double GetMemoryUsage() => 23;
        public double GetDiskUsage() => 0;
        public List<SystemStats> GetHistory(int sampleCount = 10) => [];
    }
}
