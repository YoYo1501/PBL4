using System.Net;
using System.Net.Sockets;
using NetworkAdmin.Server;
using NetworkAdmin.Client.Services;
using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Protocol;
using NetworkAdminTool.Models;
using NetworkAdminTool.Services;

var checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception("FAIL: " + description);
    checks++;
    Console.WriteLine("PASS: " + description);
}
async Task Eventually(Func<bool> predicate)
{
    using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    while (!predicate()) await Task.Delay(20, limit.Token);
}
foreach (var subnet in new[] { "224.0.0.0/24", "239.1.2.0/24", "255.255.255.0/24", "0.0.0.0/24", "127.0.0.0/24", "192.168.0.0/16", "bad", "10.0.0.0/31" })
    Check(!ScanSubnet.TryNormalize(subnet, out _), "Reject " + subnet);
Check(ScanSubnet.TryNormalize("192.168.1.129/25", out var normalized) && normalized == "192.168.1.128/25", "Normalize CIDR");
Check(ScanSubnet.TryNormalize("192.168.1", out normalized) && normalized == "192.168.1.0/24", "Legacy subnet prefix");
var ping = await new PingService().PingSeriesAsync("127.0.0.1");
Check(ping.Sent == 4 && ping.Attempts.Count == 4 && ping.Received == 4 && ping.Lost == 0 && ping.AverageResponseTimeMs.HasValue, "Actual ICMP: four loopback probes and statistics");
using (var canceled = new CancellationTokenSource())
{
    canceled.Cancel();
    try { await new PingService().PingSeriesAsync("127.0.0.1", canceled.Token); throw new Exception("Expected cancellation"); }
    catch (OperationCanceledException) { Check(true, "Ping cancellation"); }
}

var reserve = new TcpListener(IPAddress.Loopback, 0); reserve.Start();
var port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
var runtime = new ServerRuntime(new ServerOptions { Host = IPAddress.Loopback, Port = port });
runtime.Start();
await Eventually(() => runtime.IsRunning);
try
{
    using var a = await Agent.Connect(port, "A", "10.20.30.10");
    using var b = await Agent.Connect(port, "B", "10.20.30.11");
    await a.Stats(12, 34); await b.Stats(56, 78);
    await Eventually(() => runtime.GetClients().Count(c => c.Cpu.HasValue) == 2);
    var snapshots = runtime.GetClients();
    Check(snapshots.Count(c => c.Connected) == 2 && a.SessionId != b.SessionId, "Independent client sessions");
    Check(snapshots.Single(c => c.SessionId == a.SessionId).Cpu == 12 && snapshots.Single(c => c.SessionId == b.SessionId).Cpu == 56, "Per-session telemetry");
    Check(snapshots.Single(c => c.SessionId == a.SessionId).IpAddress == "10.20.30.10", "Reported agent network information");
    var command = runtime.PingAsync(a.SessionId, "127.0.0.1", default);
    var request = await a.Read();
    Check(request.Type == MessageType.PingRequest, "Ping routed to selected agent");
    await b.Send(MessageType.PingResult, request.RequestId, ping);
    await Task.Delay(100);
    Check(!command.IsCompleted, "Reject cross-session response");
    await a.Stats(22, 44);
    await Eventually(() => runtime.GetClients().Single(c => c.SessionId == a.SessionId).Cpu == 22);
    Check(!command.IsCompleted, "Telemetry continues during pending Ping");
    await a.Send(MessageType.PingResult, request.RequestId, ping);
    Check((await command).Sent == 4, "Ping statistics survive TCP JSON round trip");
    var scan = runtime.ScanAsync(b.SessionId, "10.20.30.0/24", default);
    var scanRequest = await b.Read();
    Check(scanRequest.Type == MessageType.ScanRequest, "Scan routed to selected agent");
    await b.Stats(66, 88);
    await Eventually(() => runtime.GetClients().Single(c => c.SessionId == b.SessionId).Cpu == 66);
    Check(!scan.IsCompleted, "Telemetry continues during pending Scan");
    var result = new ScanResultMessage { Success = true, Subnet = "10.20.30.0/24", Devices = [
        new() { IpAddress = "10.20.30.10", MacAddress = "AA-BB-CC-DD-EE-01", IsOnline = true },
        new() { IpAddress = "10.20.30.1", IsOnline = true }] };
    await b.Send(MessageType.ScanResult, scanRequest.RequestId, result);
    var rows = ServerRuntime.MatchScan(await scan, runtime.GetClients());
    Check(rows[0].ManagedClient == "Yes" && rows[0].Hostname == "A" && rows[1].ManagedClient == "No", "Managed host matching; unmanaged hosts remain separate");
    using (var cancellation = new CancellationTokenSource())
    {
        var pending = runtime.PingAsync(a.SessionId, "127.0.0.1", cancellation.Token);
        await a.Read(); cancellation.Cancel();
        try { await pending; throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { Check(true, "Dispatcher cancellation preserves cancellation semantics"); }
    }
    var interruptedPing = runtime.PingAsync(a.SessionId, "127.0.0.1", default);
    await a.Read();
    var interruptedScan = runtime.ScanAsync(a.SessionId, "10.20.30.0/24", default);
    await a.Read(); a.Dispose();
    foreach (var pending in new Task[] { interruptedPing, interruptedScan })
    {
        try { await pending; throw new Exception("Expected disconnect failure"); }
        catch (IOException) { Check(true, "Disconnect fails pending command promptly"); }
    }
    await Eventually(() => !runtime.GetClients().Single(c => c.SessionId == a.SessionId).Connected);
    Check(ServerRuntime.MatchScan(result, runtime.GetClients())[0].ManagedClient == "No", "Disconnected agent is no longer managed");
    using var malformed = new TcpClient(); await malformed.ConnectAsync(IPAddress.Loopback, port);
    await malformed.GetStream().WriteAsync("not-json\n"u8.ToArray());
    await b.Stats(70, 90);
    await Eventually(() => runtime.GetClients().Single(c => c.SessionId == b.SessionId).Cpu == 70);
    Check(runtime.IsRunning, "Malformed peer does not stop server or other telemetry");
    await using (var realConnection = new ServerConnection())
    {
        using var stopAgent = new CancellationTokenSource();
        await realConnection.ConnectAsync("127.0.0.1", port, stopAgent.Token);
        var handler = new ClientCommandHandler(new PingService(), realConnection);
        var reader = realConnection.RunReadLoopAsync(handler.HandleAsync, stopAgent.Token);
        try
        {
            var remotePing = await runtime.PingAsync(realConnection.SessionId!, "127.0.0.1", default);
            Check(remotePing.Sent == 4 && remotePing.Received == 4 && remotePing.Attempts.Count == 4,
                "Real Client command handler executes remote four-probe Ping through existing connection");
        }
        finally
        {
            stopAgent.Cancel();
            try { await reader; } catch (OperationCanceledException) { }
        }
    }
    await runtime.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
    Check(!runtime.IsRunning && runtime.GetClients().All(c => !c.Connected), "Shutdown awaits connection cleanup");
}
finally { await runtime.StopAsync(); }
Console.WriteLine($"ALL {checks} CHECKS PASSED");

sealed class Agent : IDisposable
{
    private readonly TcpClient _client = new();
    public string SessionId { get; private set; } = "";
    public static async Task<Agent> Connect(int port, string name, string ip)
    {
        var agent = new Agent();
        await agent._client.ConnectAsync(IPAddress.Loopback, port);
        await agent.Send(MessageType.Hello, "hello", new HelloMessage { ClientId = name, ClientName = name });
        agent.SessionId = JsonLineProtocol.GetPayload<HelloAckMessage>(await agent.Read())!.SessionId;
        await agent.Send(MessageType.NetworkInfo, "network", new ClientNetworkInfo { Hostname = name,
            Interfaces = [new() { Name = "Ethernet", IpAddress = ip, Subnet = "10.20.30.0/24" }] });
        return agent;
    }
    public Task Stats(float cpu, float ram) => Send(MessageType.SystemStats, Guid.NewGuid().ToString(), new SystemStats { CpuUsagePercent = cpu, RamUsagePercent = ram });
    public Task Send<T>(MessageType type, string id, T data) => JsonLineProtocol.SendAsync(_client.GetStream(), type, id, data);
    public async Task<MessageEnvelope> Read()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return await JsonLineProtocol.ReceiveAsync(_client.GetStream(), cancellationToken: timeout.Token) ?? throw new IOException("Unexpected EOF");
    }
    public void Dispose() => _client.Dispose();
}
