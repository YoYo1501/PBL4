using System.Diagnostics;
using NetworkAdmin.Shared.Protocol;

namespace NetworkAdmin.Shared.Messages;

public enum RemoteChatSenderRole
{
    Admin = 1,
    Client = 2,
    System = 3
}

public sealed class RemoteChatMessage
{
    public string ClientId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string SenderId { get; set; } = string.Empty;
    public string ReceiverId { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public RemoteChatSenderRole SenderRole { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}

public static class RemoteChatMessagePolicy
{
    public const int MaxMessagesPerMinute = 60;

    public static string? Validate(RemoteChatMessage? message)
    {
        if (message is null) return "Chat message is required.";
        if (!RemoteProtocolContract.IsValidIdentifier(message.ClientId)) return "ClientId is invalid.";
        if (!RemoteProtocolContract.IsValidSessionId(message.SessionId)) return "SessionId is invalid.";
        if (!RemoteProtocolContract.IsValidIdentifier(message.SenderId)) return "SenderId is invalid.";
        if (!string.IsNullOrEmpty(message.ReceiverId) && !RemoteProtocolContract.IsValidIdentifier(message.ReceiverId))
            return "ReceiverId is invalid.";
        if (!RemoteProtocolContract.IsValidIdentifier(message.MessageId)) return "MessageId is invalid.";
        if (!Enum.IsDefined(message.SenderRole)) return "SenderRole is invalid.";
        if (string.IsNullOrWhiteSpace(message.Content)) return "Chat content is required.";
        if (message.Content.Length > RemoteProtocolContract.MaxChatTextLength) return "Chat content is too long.";
        if (!RemoteProtocolContract.IsValidUtcTimestamp(message.TimestampUtc)) return "TimestampUtc must be UTC.";
        return RemoteProtocolContract.ValidatePayloadSize(message, RemoteProtocolContract.MaxChatPayloadBytes);
    }
}

public sealed class RemoteChatRateLimiter
{
    private readonly object _sync = new();
    private long _windowStarted;
    private int _count;

    public bool TryAcquire()
    {
        var now = Stopwatch.GetTimestamp();
        lock (_sync)
        {
            if (_windowStarted == 0 || Stopwatch.GetElapsedTime(_windowStarted, now) >= TimeSpan.FromMinutes(1))
            {
                _windowStarted = now;
                _count = 0;
            }

            if (_count >= RemoteChatMessagePolicy.MaxMessagesPerMinute) return false;
            _count++;
            return true;
        }
    }
}
