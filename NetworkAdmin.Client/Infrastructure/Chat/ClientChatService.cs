using System.Text.Json;
using NetworkAdmin.Client.Application.Interfaces;
using NetworkAdmin.Client.Infrastructure.Networking;
using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Protocol;

namespace NetworkAdmin.Client.Infrastructure.Chat;

internal sealed class ClientChatService : IAsyncDisposable
{
    private readonly ServerConnection _connection;
    private readonly IClientChatUi? _ui;
    private readonly MessageIdLedger _receivedIds = new();
    private readonly RemoteChatRateLimiter _sendLimiter = new();
    private int _disposed;

    public event Action<RemoteChatMessage>? MessageReceived;

    public ClientChatService(ServerConnection connection, IClientChatUi? ui = null)
    {
        _connection = connection;
        _ui = ui;
        _ui?.BindTransport(SendAsync);
        _ui?.SetConnectionState(true);
    }

    public async Task HandleMessageAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        RemoteChatMessage? message;
        try { message = JsonLineProtocol.GetPayload<RemoteChatMessage>(envelope); }
        catch (JsonException) { return; }

        if (RemoteChatMessagePolicy.Validate(message) is not null || message is null ||
            message.MessageId != envelope.RequestId ||
            !string.Equals(message.ClientId, _connection.ClientId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(message.SessionId, _connection.SessionId, StringComparison.OrdinalIgnoreCase) ||
            message.SenderRole != RemoteChatSenderRole.Admin ||
            (!string.IsNullOrWhiteSpace(message.ReceiverId) &&
             !string.Equals(message.ReceiverId, _connection.ClientId, StringComparison.OrdinalIgnoreCase)) ||
            !_receivedIds.TryRegister(message.MessageId))
            return;

        MessageReceived?.Invoke(message);
        _ui?.ShowMessage(message);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public Task SendAsync(string content, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(ClientChatService));
        if (!_sendLimiter.TryAcquire()) throw new InvalidOperationException("Chat message rate limit exceeded.");

        var message = new RemoteChatMessage
        {
            ClientId = _connection.ClientId,
            SessionId = _connection.SessionId ?? string.Empty,
            SenderId = _connection.ClientId,
            SenderRole = RemoteChatSenderRole.Client,
            Content = content,
            MessageId = Guid.NewGuid().ToString("N"),
            TimestampUtc = DateTime.UtcNow
        };
        var error = RemoteChatMessagePolicy.Validate(message);
        if (error is not null) throw new ArgumentException(error, nameof(content));
        return _connection.SendAsync(MessageType.RemoteChatMessage, message.MessageId, message, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _receivedIds.Clear();
            _ui?.SetConnectionState(false, "Chat disconnected; messages are not queued for replay.");
        }
        return ValueTask.CompletedTask;
    }
}
