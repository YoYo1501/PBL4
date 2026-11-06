using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using NetworkAdmin.Server.Configuration;
using NetworkAdmin.Server.Infrastructure.Security;
using NetworkAdmin.Server.Runtime;
using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Protocol;
using Xunit;

namespace NetworkAdmin.Client.Tests;

public sealed class SecurityAlertIntegrationTests
{
    [Fact]
    public async Task Critical_alert_over_control_channel_stops_the_matching_screen_session()
    {
        var options = new ServerOptions
        {
            Host = IPAddress.Loopback,
            Port = ReservePort(),
            ScreenPort = ReservePort(),
            AdminToken = "test-admin-token",
            AdminId = "test-admin",
            TlsCertificate = null,
            SecurityAlertPolicy = new SecurityAlertPolicyOptions
            {
                AutoDisconnectEnabled = true,
                AutoDismissAfter = TimeSpan.Zero
            }
        };
        var server = new ServerRuntime(options);
        server.Start();
        await Eventually(() => server.IsRunning);
        await using var agent = await TestAgent.ConnectAsync(options.Port);

        try
        {
            var screenTask = server.RequestScreenAsync(agent.ClientId, agent.SessionId, CancellationToken.None);
            var request = await agent.ReadAsync();
            await agent.SendAsync(MessageType.ScreenShareConsent, request.RequestId,
                new ScreenShareConsentMessage { ClientId = agent.ClientId, SessionId = agent.SessionId, Accepted = true });
            var startEnvelope = await agent.ReadAsync();
            var start = JsonLineProtocol.GetPayload<ScreenShareStartMessage>(startEnvelope)!;

            using var frameClient = new TcpClient();
            await frameClient.ConnectAsync(IPAddress.Loopback, start.ScreenPort);
            await using var frameStream = frameClient.GetStream();
            await JsonLineProtocol.SendAsync(frameStream, MessageType.Hello, startEnvelope.RequestId,
                new ScreenChannelHelloMessage
                {
                    ClientId = agent.ClientId,
                    SessionId = agent.SessionId,
                    RequestId = startEnvelope.RequestId,
                    Capability = start.Capability
                });
            Assert.Equal(MessageType.ScreenChannelReady, (await JsonLineProtocol.ReceiveAsync(frameStream))?.Type);
            await using var viewer = await screenTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(server.IsScreenSharing(agent.ClientId));

            var alert = new SecurityAlertMessage
            {
                ClientId = agent.ClientId,
                SessionId = agent.SessionId,
                AlertId = "critical-alert-1",
                EventId = "event-1",
                Severity = SecuritySeverity.Low,
                AnomalyScore = -0.6,
                IsAnomaly = true,
                DetectionTimestampUtc = DateTime.UtcNow,
                Title = "Critical security anomaly",
                Summary = "Critical process behavior detected.",
                DetectionReason = "critical isolation forest score",
                ProcessName = "demo.exe",
                UserName = "test-user"
            };
            await agent.SendAsync(MessageType.SecurityAlert, alert.AlertId, alert);

            await Eventually(() => !server.IsScreenSharing(agent.ClientId));
            Assert.False(server.IsScreenSharing(agent.ClientId));
        }
        finally
        {
            await server.StopAsync();
        }
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task Eventually(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!predicate()) await Task.Delay(20, timeout.Token);
    }

    private sealed class TestAgent : IAsyncDisposable
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;

        private TestAgent(TcpClient client, NetworkStream stream, string clientId, string sessionId)
        {
            _client = client;
            _stream = stream;
            ClientId = clientId;
            SessionId = sessionId;
        }

        public string ClientId { get; }
        public string SessionId { get; }

        public static async Task<TestAgent> ConnectAsync(int port)
        {
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            var stream = client.GetStream();
            const string clientId = "security-alert-client";
            await JsonLineProtocol.SendAsync(stream, MessageType.Hello, "security-alert-hello",
                new HelloMessage { ClientId = clientId, ClientName = "Security Alert Test Client" });
            var ack = await JsonLineProtocol.ReceiveAsync(stream) ?? throw new IOException("Missing HelloAck.");
            var payload = JsonLineProtocol.GetPayload<HelloAckMessage>(ack) ?? throw new IOException("Missing HelloAck payload.");
            return new TestAgent(client, stream, clientId, payload.SessionId);
        }

        public Task SendAsync<T>(MessageType type, string requestId, T payload) =>
            JsonLineProtocol.SendAsync(_stream, type, requestId, payload);

        public async Task<MessageEnvelope> ReadAsync() =>
            await JsonLineProtocol.ReceiveAsync(_stream) ?? throw new IOException("Unexpected EOF.");

        public ValueTask DisposeAsync()
        {
            _stream.Dispose();
            _client.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
