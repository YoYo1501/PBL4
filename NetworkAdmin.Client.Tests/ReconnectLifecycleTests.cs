using System.Net;
using System.Net.Sockets;
using NetworkAdmin.Client.Infrastructure.Networking;
using NetworkAdmin.Server;
using NetworkAdmin.Server.Networking;
using Xunit;

namespace NetworkAdmin.Client.Tests;

public sealed class ReconnectLifecycleTests
{
    [Fact]
    public async Task Server_restart_requires_a_new_session_and_preserves_client_identity()
    {
        var fixture = new TestClientFixture();
        var port = ReservePort();
        var firstServer = new ServerRuntime(fixture.ServerOptions(IPAddress.Loopback, port));
        ServerRuntime? secondServer = null;
        var firstServerStopped = false;

        await using var firstConnection = new ServerConnection();
        try
        {
            firstServer.Start();
            await EventuallyAsync(() => firstServer.IsRunning);
            using (var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                await firstConnection.ConnectAsync("127.0.0.1", port, connectTimeout.Token);

            var clientId = firstConnection.ClientId;
            var firstSessionId = firstConnection.SessionId;
            await firstConnection.DisconnectAsync();
            await EventuallyAsync(() => firstServer.GetClients().All(client => !client.Connected));
            await firstServer.StopAsync();
            firstServerStopped = true;

            secondServer = new ServerRuntime(fixture.ServerOptions(IPAddress.Loopback, port));
            secondServer.Start();
            await EventuallyAsync(() => secondServer.IsRunning);

            await using var secondConnection = new ServerConnection();
            using var reconnectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await secondConnection.ConnectAsync("127.0.0.1", port, reconnectTimeout.Token);

            Assert.Equal(clientId, secondConnection.ClientId);
            Assert.NotEqual(firstSessionId, secondConnection.SessionId);
        }
        finally
        {
            await firstConnection.DisconnectAsync();
            if (!firstServerStopped) await firstServer.StopAsync();
            if (secondServer is not null) await secondServer.StopAsync();
        }
    }

    [Fact]
    public async Task Concurrent_disconnect_is_idempotent_and_removes_the_session()
    {
        var manager = new ClientManager();
        using var stream = new MemoryStream();
        var session = manager.Register("client-a", "Client A", "session-a", stream, out _);
        session.IsReady = true;

        var results = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() => manager.Disconnect(session.SessionId))));

        Assert.Single(results, result => result);
        Assert.False(manager.TryGet(session.SessionId, out _));
        Assert.False(session.IsReady);
    }

    [Fact]
    public void Replacing_a_client_session_closes_and_deactivates_the_previous_session()
    {
        var manager = new ClientManager();
        using var oldStream = new MemoryStream();
        var oldSession = manager.Register("client-a", "Client A", "session-old", oldStream, out _);
        oldSession.IsReady = true;

        using var newStream = new MemoryStream();
        var newSession = manager.Register("client-a", "Client A", "session-new", newStream, out var replaced);

        Assert.Same(oldSession, replaced);
        Assert.False(oldSession.IsReady);
        Assert.True(manager.TryGet(newSession.SessionId, out var active));
        Assert.Same(newSession, active);
        Assert.False(manager.TryGet(oldSession.SessionId, out _));
    }

    [Fact]
    public void Heartbeat_stale_filter_ignores_unready_sessions()
    {
        var manager = new ClientManager();
        using var stream = new MemoryStream();
        var session = manager.Register("client-a", "Client A", "session-a", stream, out _);
        session.LastSeen = DateTime.UtcNow.AddMinutes(-2);

        Assert.Empty(manager.GetStaleSessions(DateTime.UtcNow, TimeSpan.FromSeconds(30)));

        session.IsReady = true;
        Assert.Single(manager.GetStaleSessions(DateTime.UtcNow, TimeSpan.FromSeconds(30)));
    }

    private static async Task EventuallyAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate()) await Task.Delay(20, timeout.Token);
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
