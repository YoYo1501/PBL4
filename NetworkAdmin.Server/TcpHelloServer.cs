using System.Net.Sockets;
using System.Text.Json;
using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Protocol;
using NetworkAdminTool.Models;

namespace NetworkAdmin.Server;

internal sealed class TcpHelloServer
{
    private readonly ServerOptions _options;
    private readonly ClientManager _clientManager;
    private readonly ServerCommandDispatcher _commandDispatcher;

    public TcpHelloServer(
        ServerOptions options,
        ClientManager clientManager,
        ServerCommandDispatcher commandDispatcher)
    {
        _options = options;
        _clientManager = clientManager;
        _commandDispatcher = commandDispatcher;
    }

    private volatile bool _isListening;
    public bool IsListening => _isListening;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var connections = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var listener = new TcpListener(_options.Host, _options.Port);
        listener.Start();
        _isListening = true;
        var handlers = new List<Task>();

        Console.WriteLine($"Listening on {_options.Host}:{_options.Port}");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                handlers.RemoveAll(t => t.IsCompleted);
                handlers.Add(HandleClientAsync(client, connections.Token));
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _isListening = false;
            listener.Stop();
            await connections.CancelAsync();
            await Task.WhenAll(handlers).ConfigureAwait(false);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken serverCancellationToken)
    {
        var remoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        var sessionId = string.Empty;

        try
        {
            using (client)
            await using (var stream = client.GetStream())
            {
                var envelope = await JsonLineProtocol.ReceiveAsync(stream, cancellationToken: serverCancellationToken)
                    .ConfigureAwait(false);

                if (envelope is null)
                {
                    Console.WriteLine($"Client disconnected before hello: {remoteEndPoint}");
                    return;
                }

                if (envelope.Type != MessageType.Hello)
                {
                    Console.WriteLine($"Invalid first message from {remoteEndPoint}: {envelope.Type}");
                    return;
                }

                var hello = JsonLineProtocol.GetPayload<HelloMessage>(envelope);
                if (hello is null || string.IsNullOrWhiteSpace(hello.ClientId) || string.IsNullOrWhiteSpace(hello.ClientName))
                {
                    Console.WriteLine($"Invalid hello from {remoteEndPoint}");
                    return;
                }

                sessionId = Guid.NewGuid().ToString("N");
                var session = _clientManager.Register(hello.ClientId, hello.ClientName, sessionId, stream);
                Console.WriteLine("Client connected");
                Console.WriteLine($"ClientId: {session.ClientId}");
                Console.WriteLine($"ClientName: {session.ClientName}");
                Console.WriteLine($"SessionId: {session.SessionId}");

                await _clientManager.SendAsync(
                    sessionId,
                    MessageType.HelloAck,
                    envelope.RequestId,
                    new HelloAckMessage
                    {
                        Accepted = true,
                        SessionId = sessionId,
                        Message = "Hello accepted"
                    },
                    serverCancellationToken).ConfigureAwait(false);

                session.IsReady = true;

                while (!serverCancellationToken.IsCancellationRequested)
                {
                    var next = await JsonLineProtocol.ReceiveAsync(stream, cancellationToken: serverCancellationToken)
                        .ConfigureAwait(false);

                    if (next is null)
                    {
                        Console.WriteLine($"Client disconnected: {sessionId}");
                        return;
                    }

                    if (next.Type == MessageType.SystemStats)
                    {
                        HandleSystemStats(sessionId, next);
                        continue;
                    }

                    if (next.Type == MessageType.NetworkInfo)
                    {
                        HandleNetworkInfo(sessionId, next);
                        continue;
                    }

                    if (next.Type == MessageType.PingResult)
                    {
                        HandlePingResult(sessionId, next);
                        continue;
                    }

                    if (next.Type == MessageType.ScanResult)
                    {
                        HandleScanResult(sessionId, next);
                        continue;
                    }

                    Console.WriteLine($"Ignoring unsupported message from {sessionId}: {next.Type}");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Malformed JSON from {remoteEndPoint}: {ex.Message}");
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Connection IO error from {remoteEndPoint}: {ex.Message}");
        }
        catch (SocketException ex)
        {
            Console.WriteLine($"Socket error from {remoteEndPoint}: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Client handler error from {remoteEndPoint}: {ex.Message}");
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                _clientManager.Remove(sessionId);
                _commandDispatcher.FailPendingForSession(sessionId, new IOException($"Client disconnected: {sessionId}"));
            }
        }
    }

    private void HandleSystemStats(string sessionId, MessageEnvelope envelope)
    {
        try
        {
            var stats = JsonLineProtocol.GetPayload<SystemStats>(envelope);
            if (stats is null || !IsValid(stats))
            {
                Console.WriteLine($"Malformed SystemStats from {sessionId}");
                return;
            }

            if (!_clientManager.UpdateSystemStats(sessionId, stats, out var session) || session is null)
            {
                Console.WriteLine($"SystemStats from unknown session: {sessionId}");
                return;
            }

            Console.WriteLine($"[{session.ClientName}] CPU: {stats.CpuUsagePercent:0.0}% | RAM: {stats.RamUsagePercent:0.0}%");
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Malformed SystemStats from {sessionId}: {ex.Message}");
        }
    }

    private void HandleNetworkInfo(string sessionId, MessageEnvelope envelope)
    {
        try
        {
            var networkInfo = JsonLineProtocol.GetPayload<ClientNetworkInfo>(envelope);
            if (networkInfo is null || string.IsNullOrWhiteSpace(networkInfo.Hostname))
            {
                Console.WriteLine($"Malformed NetworkInfo from {sessionId}");
                return;
            }

            networkInfo.Interfaces ??= new List<NetworkInterfaceInfo>();
            networkInfo.Interfaces = networkInfo.Interfaces.Where(i => i is not null).ToList();

            if (!_clientManager.UpdateNetworkInfo(sessionId, networkInfo, out var session) || session is null)
            {
                Console.WriteLine($"NetworkInfo from unknown session: {sessionId}");
                return;
            }

            Console.WriteLine($"[{session.ClientName}] NetworkInfo");
            foreach (var item in networkInfo.Interfaces)
            {
                Console.WriteLine($"IPv4: {Display(item.IpAddress)}");
                Console.WriteLine($"MAC: {Display(item.MacAddress)}");
                Console.WriteLine($"Gateway: {Display(item.Gateway)}");
                Console.WriteLine($"Interface: {Display(item.Name)}");
                Console.WriteLine($"Description: {Display(item.Description)}");
            }
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Malformed NetworkInfo from {sessionId}: {ex.Message}");
        }
    }

    private void HandlePingResult(string sessionId, MessageEnvelope envelope)
    {
        try
        {
            var result = JsonLineProtocol.GetPayload<PingResult>(envelope);
            if (result is null)
            {
                Console.WriteLine($"Malformed PingResult from {sessionId}");
                return;
            }

            if (!_clientManager.TryGet(sessionId, out var session) || session is null)
            {
                Console.WriteLine($"PingResult from unknown session: {sessionId}");
                return;
            }

            if (!_commandDispatcher.CompletePing(sessionId, envelope.RequestId, result))
            {
                Console.WriteLine($"Unexpected PingResult from {sessionId}: {envelope.RequestId}");
                return;
            }

            Console.WriteLine("Ping result");
            Console.WriteLine($"Client: {session.ClientName}");
            Console.WriteLine($"Target: {result.Host}");
            Console.WriteLine($"Success: {result.Success}");
            Console.WriteLine($"RoundTripTime: {result.RoundtripTimeMs} ms");
            Console.WriteLine($"Status: {result.StatusMessage}");
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Malformed PingResult from {sessionId}: {ex.Message}");
        }
    }

    private void HandleScanResult(string sessionId, MessageEnvelope envelope)
    {
        try
        {
            var result = JsonLineProtocol.GetPayload<ScanResultMessage>(envelope);
            if (result is null)
            {
                Console.WriteLine($"Malformed ScanResult from {sessionId}");
                return;
            }

            result.Devices ??= new();
            result.Devices = result.Devices.Where(d => d is not null).ToList();
            if (!_clientManager.TryGet(sessionId, out var session) || session is null)
            {
                Console.WriteLine($"ScanResult from unknown session: {sessionId}");
                return;
            }

            if (!_commandDispatcher.CompleteScan(sessionId, envelope.RequestId, result))
            {
                Console.WriteLine($"Unexpected ScanResult from {sessionId}: {envelope.RequestId}");
                return;
            }

            _clientManager.UpdateScanResult(sessionId, result, out _);
            Console.WriteLine("Scan completed");
            Console.WriteLine($"Client: {session.ClientName}");
            Console.WriteLine($"Devices found: {result.Devices.Count}");
            foreach (var device in result.Devices.Take(20))
            {
                Console.WriteLine(device.IpAddress);
                Console.WriteLine($"MAC: {Display(device.MacAddress)}");
                Console.WriteLine($"Status: {(device.IsOnline ? "Online" : "Offline")}");
            }

            if (!result.Success && !string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                Console.WriteLine($"Scan error: {result.ErrorMessage}");
            }
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Malformed ScanResult from {sessionId}: {ex.Message}");
        }
    }

    private static bool IsValid(SystemStats stats)
    {
        return !float.IsNaN(stats.CpuUsagePercent)
            && !float.IsInfinity(stats.CpuUsagePercent)
            && !float.IsNaN(stats.RamUsagePercent)
            && !float.IsInfinity(stats.RamUsagePercent)
            && stats.CpuUsagePercent >= 0
            && stats.RamUsagePercent >= 0;
    }

    private static string Display(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value;
}
