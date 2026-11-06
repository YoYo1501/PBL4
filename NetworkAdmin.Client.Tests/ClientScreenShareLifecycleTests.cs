using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using NetworkAdmin.Client.Application.Interfaces;
using NetworkAdmin.Client.Infrastructure.Configuration;
using NetworkAdmin.Client.Infrastructure.Networking;
using NetworkAdmin.Client.Infrastructure.ScreenSharing;
using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Protocol;
using Xunit;

namespace NetworkAdmin.Client.Tests;

public sealed class ClientScreenShareLifecycleTests
{
    [Fact]
    public async Task Canceled_consent_releases_pending_capture_approval()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        await using var connection = new ServerConnection();
        var connectTask = connection.ConnectAsync("127.0.0.1", port, CancellationToken.None);
        using var serverClient = await listener.AcceptTcpClientAsync();
        await using var serverStream = serverClient.GetStream();
        var hello = await JsonLineProtocol.ReceiveAsync(serverStream);
        Assert.NotNull(hello);
        await JsonLineProtocol.SendAsync(serverStream, MessageType.HelloAck, hello!.RequestId,
            new HelloAckMessage { Accepted = true, SessionId = "session-capture-test" });
        await connectTask;

        var ui = new ConsentUiThatCancelsOnce();
        var options = ClientOptions.Load(["--tls-sha256", new string('A', 64)]);
        var service = new ClientScreenShareService(options, connection, ui);
        using var canceledSession = new CancellationTokenSource();
        var firstRequest = service.HandleRequestAsync(CreateRequest(connection, "request-canceled"), canceledSession.Token);

        await ui.FirstConsentStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await canceledSession.CancelAsync();
        await firstRequest.WaitAsync(TimeSpan.FromSeconds(2));

        var secondRequest = service.HandleRequestAsync(CreateRequest(connection, "request-after-cancel"), CancellationToken.None);
        var response = await JsonLineProtocol.ReceiveAsync(serverStream).WaitAsync(TimeSpan.FromSeconds(2));
        await secondRequest;

        Assert.NotNull(response);
        Assert.Equal(MessageType.ScreenShareConsent, response!.Type);
        Assert.Equal("request-after-cancel", response.RequestId);
        Assert.False(JsonLineProtocol.GetPayload<ScreenShareConsentMessage>(response)!.Accepted);
    }

    private static MessageEnvelope CreateRequest(ServerConnection connection, string requestId) => new()
    {
        Type = MessageType.ScreenShareRequest,
        RequestId = requestId,
        Data = JsonSerializer.SerializeToElement(new ScreenShareRequestMessage
        {
            ClientId = connection.ClientId,
            SessionId = connection.SessionId ?? string.Empty
        })
    };

    private sealed class ConsentUiThatCancelsOnce : IScreenShareConsent
    {
        private int _requestCount;
        public TaskCompletionSource FirstConsentStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<bool> RequestConsentAsync(string serverName, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _requestCount) == 1)
            {
                FirstConsentStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return false;
        }

        public Task<bool> RequestControlConsentAsync(string serverName, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public void SetSharing(bool active, Action stop) { }
        public void SetControlState(bool active, Action stop) { }
    }
}
