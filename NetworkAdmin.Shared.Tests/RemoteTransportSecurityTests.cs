using System.Text;
using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Protocol;
using Xunit;

namespace NetworkAdmin.Shared.Tests;

public sealed class RemoteTransportSecurityTests
{
    [Fact]
    public async Task Receive_rejects_unknown_message_type_before_dispatch()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            JsonLineProtocol.ReceiveAsync(new MemoryStream(Encoding.UTF8.GetBytes(
                "{\"type\":999,\"requestId\":\"request-1\",\"data\":{}}\n"))));
    }

    [Fact]
    public async Task Receive_rejects_missing_payload_before_dispatch()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            JsonLineProtocol.ReceiveAsync(new MemoryStream(Encoding.UTF8.GetBytes(
                "{\"type\":30,\"requestId\":\"request-1\",\"data\":null}\n"))));
    }

    [Fact]
    public async Task Send_rejects_invalid_request_id_and_oversized_frame()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => JsonLineProtocol.SendAsync(
            new MemoryStream(), MessageType.RemoteChatMessage, "", new { Content = "x" }));

        await Assert.ThrowsAsync<InvalidDataException>(() => JsonLineProtocol.SendAsync(
            new MemoryStream(), MessageType.RemoteChatMessage, "request-1",
            new { Content = new string('x', JsonLineProtocol.DefaultMaxFrameBytes) }));
    }

    [Fact]
    public void Empty_authorization_tokens_are_rejected_fail_closed()
    {
        var authorization = new RemoteSessionAuthorization
        {
            AdminId = "admin-1",
            AuthorizationToken = string.Empty,
            Permission = RemoteSessionPermission.Control,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(1)
        };

        var error = RemoteSessionAuthorizationPolicy.Validate(
            authorization, string.Empty, RemoteSessionPermission.Control, DateTime.UtcNow);

        Assert.Contains("not configured", error);
    }

    [Fact]
    public void Message_id_ledger_rejects_replay_but_allows_new_id()
    {
        var ledger = new MessageIdLedger(2);

        Assert.True(ledger.TryRegister("message-1"));
        Assert.False(ledger.TryRegister("message-1"));
        Assert.True(ledger.TryRegister("message-2"));
    }
}
