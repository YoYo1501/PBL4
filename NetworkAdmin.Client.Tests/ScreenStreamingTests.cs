using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NetworkAdmin.Client.Infrastructure.Runtime;
using NetworkAdmin.Client.Infrastructure.Configuration;
using NetworkAdmin.Client.Infrastructure.Scanning;
using NetworkAdmin.Client.Infrastructure.Monitoring;
using NetworkAdmin.Client.Application.Interfaces;
using NetworkAdmin.Server;
using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Protocol;
using Xunit;

namespace NetworkAdmin.Client.Tests;

public sealed class ScreenStreamingTests
{
    [Fact]
    public async Task View_request_without_server_admin_token_fails_before_contacting_client()
    {
        var options = new TestClientFixture().ServerOptions(IPAddress.Loopback, ReservePort(), adminToken: "");
        var server = new ServerRuntime(options);
        server.Start();
        await Eventually(() => server.IsRunning);
        await using var agent = await TestAgent.ConnectAsync(options.Port);
        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                server.RequestScreenAsync(agent.ClientId, agent.SessionId, CancellationToken.None));

            Assert.Contains("NETWORKADMIN_ADMIN_TOKEN", error.Message, StringComparison.Ordinal);
        }
        finally { await server.StopAsync(); }
    }

    [Fact]
    public async Task Pinned_tls_client_executes_only_consented_session_bound_input_and_releases_on_client_stop()
    {
        using var certificate = CreateTlsCertificate();
        var pin = certificate.GetCertHashString(HashAlgorithmName.SHA256);
        var port = ReservePort();
        var options = new TestClientFixture().ServerOptions(IPAddress.Loopback, port, tlsCertificate: certificate);
        var server = new ServerRuntime(options);
        server.Start();
        await Eventually(() => server.IsRunning);

        var consent = new FakeConsentUi();
        var input = new FakeRemoteInputService();
        var network = new NetworkInfoService();
        var ping = new PingService();
        var client = new ClientRuntime(new ClientOptions
        {
            Host = "127.0.0.1", Port = port, TlsServerSha256 = pin,
            TelemetryInterval = TimeSpan.FromMilliseconds(250), NetworkInfoInterval = TimeSpan.FromSeconds(1)
        }, network, ping, new NetworkScannerService(ping, network), new SystemMonitorService(), consent, input);
        client.Start();
        try
        {
            await Eventually(() => server.GetClients().Any(c => c.Connected));
            var target = server.GetClients().First(c => c.Connected);
            var screenTask = server.RequestScreenAsync(target.ClientId, target.SessionId, CancellationToken.None);
            var viewer = await screenTask.WaitAsync(TimeSpan.FromSeconds(10));
            await using (viewer)
            {
                Assert.False(viewer.IsControlActive);
                Assert.True(await viewer.RequestControlAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)));
                await Eventually(() => consent.ControlActive);
                await viewer.SendInputEventAsync(RemoteInputEventKind.MouseClick, 5000, 5000,
                    RemoteMouseButton.Left, 0, 0, CancellationToken.None);
                await Eventually(() => input.Events.Count == 1);
                Assert.Equal(target.ClientId, input.Events.TryPeek(out var evt) ? evt.ClientId : string.Empty);
                Assert.Equal(target.SessionId, evt!.SessionId);
                Assert.Equal(RemoteInputEventKind.MouseClick, evt.Kind);
                await viewer.SendInputEventAsync(RemoteInputEventKind.KeyDown, 0, 0, 0, 0, 0x41, CancellationToken.None);
                await viewer.SendInputEventAsync(RemoteInputEventKind.KeyUp, 0, 0, 0, 0, 0x41, CancellationToken.None);
                await Eventually(() => input.Events.Count == 3);
                Assert.Equal(RemoteInputEventKind.KeyDown, input.Events.ToArray()[1].Kind);
                Assert.Equal(RemoteInputEventKind.KeyUp, input.Events.ToArray()[2].Kind);
                await viewer.SendInputEventAsync(RemoteInputEventKind.MouseDown, 1000, 1000,
                    RemoteMouseButton.Left, 0, 0, CancellationToken.None);
                await viewer.SendInputEventAsync(RemoteInputEventKind.MouseMove, 2000, 2000,
                    0, 0, 0, CancellationToken.None);
                await viewer.SendInputEventAsync(RemoteInputEventKind.MouseUp, 2000, 2000,
                    RemoteMouseButton.Left, 0, 0, CancellationToken.None);
                await Eventually(() => input.Events.Count == 6);
                Assert.Equal(RemoteInputEventKind.MouseDown, input.Events.ToArray()[3].Kind);
                Assert.Equal(RemoteInputEventKind.MouseMove, input.Events.ToArray()[4].Kind);
                Assert.Equal(RemoteInputEventKind.MouseUp, input.Events.ToArray()[5].Kind);
                await Assert.ThrowsAsync<InvalidOperationException>(() => viewer.SendInputEventAsync(
                    (RemoteInputEventKind)99, 0, 0, 0, 0, 0, CancellationToken.None));

                consent.StopControlFromClient();
                await Eventually(() => !viewer.IsControlActive);
                Assert.True(server.IsScreenSharing(target.ClientId));
                Assert.True(input.ReleaseCalls > 0);
            }
        }
        finally
        {
            await client.StopAsync();
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task Tls_protects_both_agent_control_and_screen_stream_channels()
    {
        using var certificate = CreateTlsCertificate();
        var pin = certificate.GetCertHashString(HashAlgorithmName.SHA256);
        var options = new TestClientFixture().ServerOptions(IPAddress.Loopback, ReservePort(), tlsCertificate: certificate);
        var server = new ServerRuntime(options);
        server.Start();
        await Eventually(() => server.IsRunning);
        await using var agent = await TestAgent.ConnectAsync(options.Port, pin);
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
            await using var rawFrameStream = frameClient.GetStream();
            using var tlsFrameStream = new SslStream(rawFrameStream, leaveInnerStreamOpen: true,
                (_, remoteCertificate, _, _) => TlsCertificatePin.Matches(remoteCertificate, pin));
            await tlsFrameStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "NetworkAdminTool LAN Server",
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            });
            await JsonLineProtocol.SendAsync(tlsFrameStream, MessageType.Hello, startEnvelope.RequestId,
                new ScreenChannelHelloMessage { ClientId = agent.ClientId, SessionId = agent.SessionId,
                    RequestId = startEnvelope.RequestId, Capability = start.Capability });
            Assert.Equal(MessageType.ScreenChannelReady, (await JsonLineProtocol.ReceiveAsync(tlsFrameStream))?.Type);
            await using var viewer = await requestTask.WaitAsync(TimeSpan.FromSeconds(5));

            var jpeg = new byte[] { 0xFF, 0xD8, 7, 8, 0xFF, 0xD9 };
            await ScreenFrameProtocol.WriteFrameAsync(tlsFrameStream, 1, jpeg);
            Assert.Equal(jpeg, (await viewer.ReadFrameAsync(CancellationToken.None))?.Jpeg);
        }
        finally { await server.StopAsync(); }
    }

    [Fact]
    public async Task Remote_control_is_rejected_when_the_server_transport_is_not_tls()
    {
        var options = new TestClientFixture().ServerOptions(IPAddress.Loopback, ReservePort());
        var server = new ServerRuntime(options);
        server.Start();
        await Eventually(() => server.IsRunning);
        await using var agent = await TestAgent.ConnectAsync(options.Port);
        try
        {
            var screenTask = server.RequestScreenAsync(agent.ClientId, agent.SessionId, CancellationToken.None);
            var screenRequest = await agent.ReadAsync();
            await agent.SendAsync(MessageType.ScreenShareConsent, screenRequest.RequestId,
                new ScreenShareConsentMessage { ClientId = agent.ClientId, SessionId = agent.SessionId, Accepted = true });
            var startEnvelope = await agent.ReadAsync();
            var start = JsonLineProtocol.GetPayload<ScreenShareStartMessage>(startEnvelope)!;
            using var frameClient = new TcpClient();
            await frameClient.ConnectAsync(IPAddress.Loopback, start.ScreenPort);
            await using var frameStream = frameClient.GetStream();
            await JsonLineProtocol.SendAsync(frameStream, MessageType.Hello, startEnvelope.RequestId,
                new ScreenChannelHelloMessage { ClientId = agent.ClientId, SessionId = agent.SessionId,
                    RequestId = startEnvelope.RequestId, Capability = start.Capability });
            await JsonLineProtocol.ReceiveAsync(frameStream);
            await using var viewer = await screenTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(viewer.IsControlActive);
            await Assert.ThrowsAsync<InvalidOperationException>(() => viewer.RequestControlAsync(CancellationToken.None));
            Assert.False(viewer.IsControlActive);
        }
        finally { await server.StopAsync(); }
    }

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

    private static X509Certificate2 CreateTlsCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=NetworkAdminTool Test Server", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var temporary = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        const string password = "test-only-pfx-password";
        var pfx = temporary.Export(X509ContentType.Pfx, password);
        try
        {
            return X509CertificateLoader.LoadPkcs12(pfx, password,
                X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet);
        }
        finally { CryptographicOperations.ZeroMemory(pfx); }
    }

    private static async Task Eventually(Func<bool> predicate)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate()) await Task.Delay(20, limit.Token);
    }

    private sealed class TestAgent : IAsyncDisposable
    {
        private readonly TcpClient _client;
        private readonly Stream _stream;
        public string ClientId { get; }
        public string SessionId { get; }
        private TestAgent(TcpClient client, string clientId, string sessionId, Stream stream)
        { _client = client; ClientId = clientId; SessionId = sessionId; _stream = stream; }

        public static async Task<TestAgent> ConnectAsync(int port, string? serverPin = null)
        {
            var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port);
            Stream stream = client.GetStream();
            if (serverPin is not null)
            {
                var tls = new SslStream(stream, leaveInnerStreamOpen: true,
                    (_, remoteCertificate, _, _) => TlsCertificatePin.Matches(remoteCertificate, serverPin));
                await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = "NetworkAdminTool LAN Server",
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                });
                stream = tls;
            }
            const string id = "screen-test-client";
            await JsonLineProtocol.SendAsync(stream, MessageType.Hello, "screen-test-hello",
                new HelloMessage { ClientId = id, ClientName = "Screen Test Client" });
            var ack = await JsonLineProtocol.ReceiveAsync(stream);
            var sessionId = JsonLineProtocol.GetPayload<HelloAckMessage>(ack!)!.SessionId;
            return new TestAgent(client, id, sessionId, stream);
        }
        public Task SendAsync<T>(MessageType type, string requestId, T data) => JsonLineProtocol.SendAsync(_stream, type, requestId, data);
        public async Task<MessageEnvelope> ReadAsync() => await JsonLineProtocol.ReceiveAsync(_stream) ?? throw new IOException("Unexpected EOF.");
        public ValueTask DisposeAsync() { _stream.Dispose(); _client.Dispose(); return ValueTask.CompletedTask; }
    }

    private sealed class FakeConsentUi : IScreenShareConsent
    {
        private Action _stopControl = static () => { };
        private int _controlActive;
        public bool ControlActive => Volatile.Read(ref _controlActive) == 1;
        public Task<bool> RequestConsentAsync(string serverName, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> RequestControlConsentAsync(string serverName, CancellationToken cancellationToken) => Task.FromResult(true);
        public void SetSharing(bool active, Action stop) { }
        public void SetControlState(bool active, Action stop)
        {
            Volatile.Write(ref _controlActive, active ? 1 : 0);
            _stopControl = stop;
        }
        public void StopControlFromClient() => _stopControl();
    }

    private sealed class FakeRemoteInputService : IRemoteInputService
    {
        public System.Collections.Concurrent.ConcurrentQueue<RemoteInputEventMessage> Events { get; } = new();
        private int _releaseCalls;
        public int ReleaseCalls => Volatile.Read(ref _releaseCalls);
        public bool Execute(RemoteInputEventMessage input) { Events.Enqueue(input); return true; }
        public void ReleaseAll() => Interlocked.Increment(ref _releaseCalls);
    }
}
