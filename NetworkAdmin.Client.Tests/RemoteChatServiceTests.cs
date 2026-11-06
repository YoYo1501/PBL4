using System.Net;
using System.Net.Sockets;
using NetworkAdmin.Client.Infrastructure.Chat;
using NetworkAdmin.Client.Infrastructure.Networking;
using NetworkAdmin.Server;
using NetworkAdmin.Server.Infrastructure.Chat;
using NetworkAdmin.Server.Networking;
using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Protocol;
using Xunit;

namespace NetworkAdmin.Client.Tests;

public sealed class RemoteChatServiceTests
{
    [Fact]
    public async Task Chat_round_trip_is_session_bound_and_old_session_cannot_send_after_disconnect()
    {
        var fixture = new TestClientFixture();
        var port = ReservePort();
        var server = new ServerRuntime(fixture.ServerOptions(IPAddress.Loopback, port));
        server.Start();
        await Eventually(() => server.IsRunning);
        await using var connection = new ServerConnection();
        await using var chat = new ClientChatService(connection);
        using var readCancellation = new CancellationTokenSource();
        Task? reader = null;
        var receivedByServer = new TaskCompletionSource<RemoteChatMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var receivedByClient = new TaskCompletionSource<RemoteChatMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverMessageCount = 0;
        Action<RemoteChatMessage> serverHandler = message =>
        {
            Interlocked.Increment(ref serverMessageCount);
            receivedByServer.TrySetResult(message);
        };
        Action<RemoteChatMessage> clientHandler = message => receivedByClient.TrySetResult(message);
        server.ChatMessageReceived += serverHandler;
        chat.MessageReceived += clientHandler;

        try
        {
            await connection.ConnectAsync("127.0.0.1", port, CancellationToken.None);
            reader = connection.RunReadLoopAsync((envelope, token) => chat.HandleMessageAsync(envelope, token), readCancellation.Token);

            await chat.SendAsync("hello from client");
            var clientMessage = await receivedByServer.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(connection.ClientId, clientMessage.SenderId);
            Assert.Equal(connection.SessionId, clientMessage.SessionId);
            Assert.Equal(RemoteChatSenderRole.Client, clientMessage.SenderRole);
            Assert.Equal("test-admin", clientMessage.ReceiverId);

            var duplicate = new RemoteChatMessage
            {
                ClientId = connection.ClientId, SessionId = connection.SessionId!, SenderId = connection.ClientId,
                MessageId = clientMessage.MessageId, SenderRole = RemoteChatSenderRole.Client,
                Content = "duplicate", TimestampUtc = DateTime.UtcNow
            };
            await connection.SendAsync(MessageType.RemoteChatMessage, duplicate.MessageId, duplicate, CancellationToken.None);
            var spoof = new RemoteChatMessage
            {
                ClientId = duplicate.ClientId, SessionId = duplicate.SessionId, SenderId = "spoofed-client",
                MessageId = Guid.NewGuid().ToString("N"), SenderRole = duplicate.SenderRole,
                Content = duplicate.Content, TimestampUtc = duplicate.TimestampUtc
            };
            await connection.SendAsync(MessageType.RemoteChatMessage, spoof.MessageId, spoof, CancellationToken.None);
            await Task.Delay(100);
            Assert.Equal(1, Volatile.Read(ref serverMessageCount));

            await server.SendChatAsync(connection.ClientId, connection.SessionId!, "hello from admin", CancellationToken.None);
            var adminMessage = await receivedByClient.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(RemoteChatSenderRole.Admin, adminMessage.SenderRole);
            Assert.Equal(connection.ClientId, adminMessage.ReceiverId);

            await Assert.ThrowsAsync<ArgumentException>(() => chat.SendAsync(
                new string('x', RemoteProtocolContract.MaxChatTextLength + 1)));

            var oldSession = connection.SessionId!;
            await connection.DisconnectAsync();
            await Eventually(() => server.GetClients().All(client => !client.Connected));
            await Assert.ThrowsAsync<InvalidOperationException>(() => server.SendChatAsync(
                connection.ClientId, oldSession, "late message", CancellationToken.None));

            await using var reconnected = new ServerConnection();
            await reconnected.ConnectAsync("127.0.0.1", port, CancellationToken.None);
            Assert.NotEqual(oldSession, reconnected.SessionId);
        }
        finally
        {
            await readCancellation.CancelAsync();
            await connection.DisconnectAsync();
            if (reader is not null)
            {
                try { await reader; }
                catch (Exception ex) when (ex is OperationCanceledException or IOException) { }
            }
            server.ChatMessageReceived -= serverHandler;
            chat.MessageReceived -= clientHandler;
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task Admin_chat_requires_valid_session_authorization()
    {
        var clients = new ClientManager();
        using var stream = new MemoryStream();
        var session = clients.Register("client-a", "Client A", "session-a", stream, out _);
        session.IsReady = true;
        var service = new ChatService(clients, "admin-a", "expected-token");

        var invalidToken = new RemoteSessionAuthorization
        {
            AdminId = "admin-a",
            AuthorizationToken = "wrong-token",
            Permission = RemoteSessionPermission.ViewOnly,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(1)
        };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SendFromAdminAsync(
            "client-a", "session-a", invalidToken, "must be rejected", CancellationToken.None));

        await service.DisposeAsync();
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task Eventually(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate()) await Task.Delay(20, timeout.Token);
    }
}
