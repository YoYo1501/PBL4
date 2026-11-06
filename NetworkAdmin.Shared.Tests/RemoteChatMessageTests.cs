using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Protocol;
using Xunit;

namespace NetworkAdmin.Shared.Tests;

public sealed class RemoteChatMessageTests
{
    [Fact]
    public void Rejects_empty_oversized_or_non_utc_chat_message()
    {
        var message = ValidMessage();
        message.Content = string.Empty;
        Assert.NotNull(RemoteChatMessagePolicy.Validate(message));

        message.Content = new string('x', RemoteProtocolContract.MaxChatTextLength + 1);
        Assert.NotNull(RemoteChatMessagePolicy.Validate(message));

        message.Content = "ok";
        message.TimestampUtc = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Local);
        Assert.NotNull(RemoteChatMessagePolicy.Validate(message));
    }

    [Fact]
    public void Rate_limiter_rejects_a_chat_burst()
    {
        var limiter = new RemoteChatRateLimiter();
        for (var i = 0; i < RemoteChatMessagePolicy.MaxMessagesPerMinute; i++)
            Assert.True(limiter.TryAcquire());
        Assert.False(limiter.TryAcquire());
    }

    private static RemoteChatMessage ValidMessage() => new()
    {
        ClientId = "client-a",
        SessionId = "session-a",
        SenderId = "client-a",
        ReceiverId = "server-admin",
        MessageId = "message-a",
        SenderRole = RemoteChatSenderRole.Client,
        Content = "hello",
        TimestampUtc = DateTime.UtcNow
    };
}
