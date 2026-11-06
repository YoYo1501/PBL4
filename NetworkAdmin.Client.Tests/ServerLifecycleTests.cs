using System.IO;
using NetworkAdmin.Server.Networking;
using Xunit;

namespace NetworkAdmin.Client.Tests;

public sealed class ServerLifecycleTests
{
    [Fact]
    public void Stale_client_session_is_detectable_and_disconnect_releases_registry_entry()
    {
        var manager = new ClientManager();
        using var stream = new MemoryStream();
        var session = manager.Register("client-a", "Client A", "session-a", stream, out _);
        session.IsReady = true;
        session.LastSeen = DateTime.UtcNow.AddMinutes(-2);

        Assert.Single(manager.GetStaleSessions(DateTime.UtcNow, TimeSpan.FromSeconds(30)));
        Assert.True(manager.Disconnect(session.SessionId));
        Assert.False(manager.TryGet(session.SessionId, out _));
    }
}
