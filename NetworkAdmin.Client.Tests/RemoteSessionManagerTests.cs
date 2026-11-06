using System.Net;
using System.Net.Sockets;
using NetworkAdmin.Server.Networking;
using NetworkAdmin.Server.Runtime;
using NetworkAdmin.Shared.Messages;
using Xunit;

namespace NetworkAdmin.Client.Tests;

public sealed class RemoteSessionManagerTests
{
    [Fact]
    public void Existing_session_owner_enforces_ordered_state_transitions()
    {
        var pending = CreatePending();

        Assert.Equal(RemoteSessionState.Requested, pending.State);
        Assert.True(pending.TryTransition(RemoteSessionState.AwaitingConsent, "test"));
        Assert.True(pending.TryTransition(RemoteSessionState.Accepted, "test"));
        Assert.True(pending.TryTransition(RemoteSessionState.Starting, "test"));
        Assert.True(pending.TryTransition(RemoteSessionState.Active, "test"));
        Assert.False(pending.TryTransition(RemoteSessionState.Closed, "cannot skip stopping"));
        Assert.True(pending.TryTransition(RemoteSessionState.Stopping, "test"));
        Assert.True(pending.TryTransition(RemoteSessionState.Closed, "test"));
        Assert.False(pending.TryTransition(RemoteSessionState.Active, "terminal state"));
    }

    [Fact]
    public async Task Existing_session_owner_allows_only_one_control_request_under_race()
    {
        var pending = CreatePending();
        pending.TryTransition(RemoteSessionState.AwaitingConsent, "test");
        pending.TryTransition(RemoteSessionState.Accepted, "test");
        pending.TryTransition(RemoteSessionState.Starting, "test");
        pending.TryTransition(RemoteSessionState.Active, "test");

        var attempts = Enumerable.Range(0, 16)
            .Select(index => Task.Run(() => pending.TryBeginControl($"control-{index}")))
            .ToArray();
        var results = await Task.WhenAll(attempts);

        Assert.Equal(1, results.Count(accepted => accepted));
        pending.EndControl(pending.ControlRequestId);
        Assert.Null(pending.ControlRequestId);
    }

    private static ScreenStreamingService.PendingScreenShare CreatePending()
    {
        var client = new ClientSessionInfo
        {
            ClientId = "client-a",
            ClientName = "Test Client",
            SessionId = "session-a",
            ConnectedAt = DateTime.UtcNow,
            LastSeen = DateTime.UtcNow,
            RemoteAddress = IPAddress.Loopback.ToString(),
            Stream = new MemoryStream()
        };
        return new ScreenStreamingService.PendingScreenShare(client, "admin-a",
            RemoteSessionPermission.ViewOnly, TimeSpan.FromMinutes(5));
    }
}
