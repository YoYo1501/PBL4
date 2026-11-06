using System.Collections.Concurrent;
using NetworkAdmin.Client.Application.Interfaces;
using NetworkAdmin.Client.Infrastructure.Networking;
using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Protocol;

namespace NetworkAdmin.Client.Infrastructure.Chat;

/// <summary>Chat transport and policy for a single authenticated ServerConnection.</summary>
internal sealed class ClientChatSession : IClientChatSession
{
    private readonly ServerConnection _connection;
    private readonly RemoteChatRateLimiter _outgoingLimiter = new();
    private readonly RemoteChatRateLimiter _incomingLimiter = new();
    private readonly ConcurrentDictionary<string, byte> _messageIds = new(StringComparer.Ordinal);
    private int _disposed;

    public ClientChatSession(ServerConnection connection) => _connection = connection;

    public event Action<RemoteChatMessage>? MessageReceived;

    public Task HandleIncomingAsync(RemoteChatMessage? message, string requestId, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0 || message is null ||
            !string.Equals(requestId, message.MessageId, StringComparison.Ordinal) ||
            message.SenderRole != RemoteChatSenderRole.Admin ||
            !string.Equals(message.ClientId, _connection.ClientId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(message.SessionId, _connection.SessionId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(message.ReceiverId, _connection.ClientId, StringComparison.OrdinalIgnoreCase) ||
            RemoteChatMessagePolicy.Validate(message) is not null ||
            !_messageIds.TryAdd(message.MessageId, 0) || !_incomingLimiter.TryAcquire())
            return Task.CompletedTask;

        try { MessageReceived?.Invoke(message); }
        catch (Exception ex) { Console.WriteLine($"Client chat subscriber failed: {ex.Message}"); }
        return Task.CompletedTask;
    }

    public async Task SendAsync(string content, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!_outgoingLimiter.TryAcquire()) throw new InvalidOperationException("Chat message rate limit exceeded.");
        var message = new RemoteChatMessage
        {
            ClientId = _connection.ClientId,
            SessionId = _connection.SessionId ?? string.Empty,
            SenderId = _connection.ClientId,
            SenderRole = RemoteChatSenderRole.Client,
            MessageId = Guid.NewGuid().ToString("N"),
            Content = content,
            TimestampUtc = DateTime.UtcNow
        };
        var error = RemoteChatMessagePolicy.Validate(message);
        if (error is not null) throw new ArgumentException(error, nameof(content));
        await _connection.SendAsync(MessageType.RemoteChatMessage, message.MessageId, message, cancellationToken)
            .ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _messageIds.Clear();
            MessageReceived = null;
        }
        return ValueTask.CompletedTask;
    }
}
