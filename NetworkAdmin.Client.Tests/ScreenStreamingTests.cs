using System.Net;
using System.Net.Sockets;
using NetworkAdmin.Client.Infrastructure.Runtime;
using NetworkAdmin.Server;
using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Protocol;
using Xunit;

namespace NetworkAdmin.Client.Tests;

public sealed class ScreenStreamingTests
{
    [Fact]
    public async Task Accept_creates_capability_channel_streams_frame_and_viewer_close_stops_session()
    {
        var options = new TestClientFixture().ServerOptions(IPAddress.Loopback, ReservePort());
        var server = new ServerRuntime(options);
        server.Start();
        await Eventually(() => server.IsRunning);
        await using var agent = await TestAgent.ConnectAsync(options.Port);
        try
        {
            var requestTask = server.RequestScreenAsync(agent.ClientId, agent.SessionId, CancellationToken.None);
            var requestEnvelope = await agent.ReadAsync();
            Assert.Equal(MessageType.ScreenShareRequest, requestEnvelope.Type);
            var request = JsonLineProtocol.GetPayload<ScreenShareRequestMessage>(requestEnvelope)!;
            Assert.Equal(agent.ClientId, request.ClientId);
            Assert.Equal(agent.SessionId, request.SessionId);

            await agent.SendAsync(MessageType.ScreenShareConsent, requestEnvelope.RequestId,
                new ScreenShareConsentMessage { ClientId = agent.ClientId, SessionId = agent.SessionId, Accepted = true });
            var startEnvelope = await agent.ReadAsync();
            Assert.Equal(MessageType.ScreenShareStart, startEnvelope.Type);
            var start = JsonLineProtocol.GetPayload<ScreenShareStartMessage>(startEnvelope)!;
            Assert.Equal(requestEnvelope.RequestId, startEnvelope.RequestId);
            Assert.Equal(32, Convert.FromBase64String(start.Capability).Length);

            using var frameClient = new TcpClient();
            await frameClient.ConnectAsync(IPAddress.Loopback, start.ScreenPort);
            await using var stream = frameClient.GetStream();
            await JsonLineProtocol.SendAsync(stream, MessageType.Hello, startEnvelope.RequestId,
                new ScreenChannelHelloMessage { ClientId = agent.ClientId, SessionId = agent.SessionId,
                    RequestId = startEnvelope.RequestId, Capability = start.Capability });
            Assert.Equal(MessageType.ScreenChannelReady, (await JsonLineProtocol.ReceiveAsync(stream))?.Type);
            using (var replayClient = new TcpClient())
            {
                await replayClient.ConnectAsync(IPAddress.Loopback, start.ScreenPort);
                await JsonLineProtocol.SendAsync(replayClient.GetStream(), MessageType.Hello, startEnvelope.RequestId,
                    new ScreenChannelHelloMessage { ClientId = agent.ClientId, SessionId = agent.SessionId,
                        RequestId = startEnvelope.RequestId, Capability = start.Capability });
                Assert.Null(await JsonLineProtocol.ReceiveAsync(replayClient.GetStream()).WaitAsync(TimeSpan.FromSeconds(2)));
            }
            await using var viewer = await requestTask.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                server.RequestScreenAsync(agent.ClientId, agent.SessionId, CancellationToken.None));

            var jpeg = new byte[] { 0xFF, 0xD8, 1, 2, 0xFF, 0xD9 };
            await ScreenFrameProtocol.WriteFrameAsync(stream, 8, jpeg);
            var frame = await viewer.ReadFrameAsync(CancellationToken.None);
            Assert.NotNull(frame);
            Assert.Equal(8, frame.Value.Sequence);
            Assert.Equal(jpeg, frame.Value.Jpeg);

            await viewer.StopAsync("Viewer closed.");
            Assert.Equal(MessageType.ScreenShareStop, (await agent.ReadAsync()).Type);
        }
        finally { await server.StopAsync(); }
    }

    [Fact]
    public async Task Reject_never_sends_start_or_accepts_frame_channel()
    {
        var options = new TestClientFixture().ServerOptions(IPAddress.Loopback, ReservePort());
        var server = new ServerRuntime(options);
        server.Start();
        await Eventually(() => server.IsRunning);
        await using var agent = await TestAgent.ConnectAsync(options.Port);
        try
        {
            var requestTask = server.RequestScreenAsync(agent.ClientId, agent.SessionId, CancellationToken.None);
            var envelope = await agent.ReadAsync();
            await agent.SendAsync(MessageType.ScreenShareConsent, envelope.RequestId,
                new ScreenShareConsentMessage { ClientId = agent.ClientId, SessionId = agent.SessionId, Accepted = false });
            await Assert.ThrowsAsync<InvalidOperationException>(() => requestTask);
            Assert.False(server.IsScreenSharing(agent.ClientId));
            using var unauthorized = new TcpClient();
            await unauthorized.ConnectAsync(IPAddress.Loopback, options.ScreenPort);
            await JsonLineProtocol.SendAsync(unauthorized.GetStream(), MessageType.Hello, envelope.RequestId,
                new ScreenChannelHelloMessage { ClientId = agent.ClientId, SessionId = agent.SessionId,
                    RequestId = envelope.RequestId, Capability = Convert.ToBase64String(new byte[32]) });
            Assert.Null(await JsonLineProtocol.ReceiveAsync(unauthorized.GetStream()).WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally { await server.StopAsync(); }
    }

    [Fact]
    public async Task Disconnect_and_reconnect_cleanup_share_and_reject_old_capability()
    {
        var options = new TestClientFixture().ServerOptions(IPAddress.Loopback, ReservePort());
        var server = new ServerRuntime(options);
        server.Start();
        await Eventually(() => server.IsRunning);
        var agent = await TestAgent.ConnectAsync(options.Port);
        try
        {
            var requestTask = server.RequestScreenAsync(agent.ClientId, agent.SessionId, CancellationToken.None);
            var request = await agent.ReadAsync();
            await agent.SendAsync(MessageType.ScreenShareConsent, request.RequestId,
                new ScreenShareConsentMessage { ClientId = agent.ClientId, SessionId = agent.SessionId, Accepted = true });
            var startEnvelope = await agent.ReadAsync();
            var start = JsonLineProtocol.GetPayload<ScreenShareStartMessage>(startEnvelope)!;
            using var frameClient = new TcpClient();
            await frameClient.ConnectAsync(IPAddress.Loopback, start.ScreenPort);
            await using var stream = frameClient.GetStream();
            await JsonLineProtocol.SendAsync(stream, MessageType.Hello, startEnvelope.RequestId,
                new ScreenChannelHelloMessage { ClientId = agent.ClientId, SessionId = agent.SessionId,
                    RequestId = startEnvelope.RequestId, Capability = start.Capability });
            await JsonLineProtocol.ReceiveAsync(stream);
            await using var viewer = await requestTask.WaitAsync(TimeSpan.FromSeconds(5));
            await agent.DisposeAsync();
            await Eventually(() => !server.IsScreenSharing(agent.ClientId));
            await using var reconnected = await TestAgent.ConnectAsync(options.Port);
            Assert.NotEqual(agent.SessionId, reconnected.SessionId);
            Assert.False(server.IsScreenSharing(reconnected.ClientId));
        }
        finally { await agent.DisposeAsync(); await server.StopAsync(); }
    }

    [Fact]
    public async Task Client_stop_message_ends_the_active_viewer_session()
    {
        var options = new TestClientFixture().ServerOptions(IPAddress.Loopback, ReservePort());
        var server = new ServerRuntime(options);
        server.Start();
        await Eventually(() => server.IsRunning);
        await using var agent = await TestAgent.ConnectAsync(options.Port);
        try
        {
            var requestTask = server.RequestScreenAsync(agent.ClientId, agent.SessionId, CancellationToken.None);
            var request = await agent.ReadAsync();
            await agent.SendAsync(MessageType.ScreenShareConsent, request.RequestId,
                new ScreenShareConsentMessage { ClientId = agent.ClientId, SessionId = agent.SessionId, Accepted = true });
            var startEnvelope = await agent.ReadAsync();
            var start = JsonLineProtocol.GetPayload<ScreenShareStartMessage>(startEnvelope)!;
            using var frameClient = new TcpClient();
            await frameClient.ConnectAsync(IPAddress.Loopback, start.ScreenPort);
            await using var stream = frameClient.GetStream();
            await JsonLineProtocol.SendAsync(stream, MessageType.Hello, startEnvelope.RequestId,
                new ScreenChannelHelloMessage { ClientId = agent.ClientId, SessionId = agent.SessionId,
                    RequestId = startEnvelope.RequestId, Capability = start.Capability });
            await JsonLineProtocol.ReceiveAsync(stream);
            await using var viewer = await requestTask.WaitAsync(TimeSpan.FromSeconds(5));

            await agent.SendAsync(MessageType.ScreenShareStop, startEnvelope.RequestId,
                new ScreenShareStopMessage { ClientId = agent.ClientId, SessionId = agent.SessionId, Reason = "Client stopped sharing." });
            await Eventually(() => !server.IsScreenSharing(agent.ClientId));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var error = await Record.ExceptionAsync(() => viewer.ReadFrameAsync(timeout.Token));
            Assert.True(error is null or IOException or ObjectDisposedException,
                $"Unexpected client stop cleanup result: {error}");
        }
        finally { await server.StopAsync(); }
    }

    [Fact]
    public async Task Consent_timeout_releases_client_slot()
    {
        var options = new TestClientFixture().ServerOptions(IPAddress.Loopback, ReservePort(),
            screenConsentTimeout: TimeSpan.FromMilliseconds(100));
        var server = new ServerRuntime(options);
        server.Start();
        await Eventually(() => server.IsRunning);
        await using var agent = await TestAgent.ConnectAsync(options.Port);
        try
        {
            var timedOut = server.RequestScreenAsync(agent.ClientId, agent.SessionId, CancellationToken.None);
            Assert.Equal(MessageType.ScreenShareRequest, (await agent.ReadAsync()).Type);
            await Assert.ThrowsAsync<TimeoutException>(() => timedOut);
            Assert.False(server.IsScreenSharing(agent.ClientId));
        }
        finally { await server.StopAsync(); }
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task Eventually(Func<bool> predicate)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate()) await Task.Delay(20, limit.Token);
    }

    private sealed class TestAgent : IAsyncDisposable
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        public string ClientId { get; }
        public string SessionId { get; }
        private TestAgent(TcpClient client, string clientId, string sessionId, NetworkStream stream)
        { _client = client; ClientId = clientId; SessionId = sessionId; _stream = stream; }

        public static async Task<TestAgent> ConnectAsync(int port)
        {
            var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port);
            var stream = client.GetStream();
            const string id = "screen-test-client";
            await JsonLineProtocol.SendAsync(stream, MessageType.Hello, "screen-test-hello",
                new HelloMessage { ClientId = id, ClientName = "Screen Test Client" });
            var ack = await JsonLineProtocol.ReceiveAsync(stream);
            var sessionId = JsonLineProtocol.GetPayload<HelloAckMessage>(ack!)!.SessionId;
            return new TestAgent(client, id, sessionId, stream);
        }
        public Task SendAsync<T>(MessageType type, string requestId, T data) => JsonLineProtocol.SendAsync(_stream, type, requestId, data);
        public async Task<MessageEnvelope> ReadAsync() => await JsonLineProtocol.ReceiveAsync(_stream) ?? throw new IOException("Unexpected EOF.");
        public ValueTask DisposeAsync() { _client.Dispose(); return ValueTask.CompletedTask; }
    }
}
