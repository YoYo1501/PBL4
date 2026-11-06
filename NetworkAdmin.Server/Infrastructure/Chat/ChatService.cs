using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetworkAdmin.Server.Application.Interfaces;
using NetworkAdmin.Server.Networking;
using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Protocol;

namespace NetworkAdmin.Server.Infrastructure.Chat;

internal sealed class ChatService : IRemoteChatModule
{
    private readonly ClientManager _clients;
    private readonly string _adminId;
    private readonly string _adminToken;
    private readonly ConcurrentDictionary<string, MessageIdLedger> _receivedIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RemoteChatRateLimiter> _rateLimiters = new(StringComparer.OrdinalIgnoreCase);
    private int _disposed;

    public event Action<RemoteChatMessage>? MessageReceived;

    public ChatService(ClientManager clients, string adminId, string adminToken)
    {
        _clients = clients;
        _adminId = adminId;
        _adminToken = adminToken;
    }

    public IRemoteChatSession CreateSession(string clientId, string sessionId) =>
        new ChatSession(this, clientId, sessionId);

    public Task HandleClientMessageAsync(ClientSessionInfo session, MessageEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0 || !session.IsReady ||
            !string.Equals(envelope.RequestId, ReadMessageId(envelope.Data), StringComparison.Ordinal))
            return Task.CompletedTask;

        RemoteChatMessage? incoming;
        try { incoming = JsonLineProtocol.GetPayload<RemoteChatMessage>(envelope); }
        catch (JsonException) { return Task.CompletedTask; }

        var validationError = RemoteChatMessagePolicy.Validate(incoming);
        if (validationError is not null || incoming is null ||
            incoming.MessageId != envelope.RequestId ||
            !string.Equals(incoming.ClientId, session.ClientId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(incoming.SessionId, session.SessionId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(incoming.SenderId, session.ClientId, StringComparison.OrdinalIgnoreCase) ||
            incoming.SenderRole != RemoteChatSenderRole.Client ||
            (!string.IsNullOrWhiteSpace(incoming.ReceiverId) &&
             !string.Equals(incoming.ReceiverId, _adminId, StringComparison.OrdinalIgnoreCase)))
            return Task.CompletedTask;

        var ledger = _receivedIds.GetOrAdd(session.SessionId, static _ => new MessageIdLedger());
        if (!ledger.TryRegister(incoming.MessageId)) return Task.CompletedTask;
        var limiter = _rateLimiters.GetOrAdd(session.SessionId, static _ => new RemoteChatRateLimiter());
        if (!limiter.TryAcquire()) return Task.CompletedTask;

        // The transport/session identity is authoritative. SenderId and timestamp from the
        // Client are never used to identify the authenticated sender or order the server event.
        var normalized = new RemoteChatMessage
        {
            ClientId = session.ClientId,
            SessionId = session.SessionId,
            SenderId = session.ClientId,
            ReceiverId = _adminId,
            MessageId = incoming.MessageId,
            SenderRole = RemoteChatSenderRole.Client,
            Content = incoming.Content,
            TimestampUtc = DateTime.UtcNow
        };
        try { MessageReceived?.Invoke(normalized); }
        catch (Exception ex) { Console.WriteLine($"Chat subscriber failed: {ex.Message}"); }
        return Task.CompletedTask;
    }

    public async Task SendFromAdminAsync(string clientId, string sessionId,
        string authorizationToken, string content, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(_adminToken) || string.IsNullOrEmpty(authorizationToken) ||
            !FixedTimeEquals(authorizationToken, _adminToken))
            throw new UnauthorizedAccessException("Chat authorization token is not valid.");
        if (!_clients.TryGet(sessionId, out var session) || session is null || !session.IsReady ||
            !string.Equals(session.ClientId, clientId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Client session is not active.");

        var limiter = _rateLimiters.GetOrAdd(sessionId, static _ => new RemoteChatRateLimiter());
        if (!limiter.TryAcquire()) throw new InvalidOperationException("Chat message rate limit exceeded.");

        var message = new RemoteChatMessage
        {
            ClientId = clientId,
            SessionId = sessionId,
            SenderId = _adminId,
            ReceiverId = clientId,
            MessageId = Guid.NewGuid().ToString("N"),
            SenderRole = RemoteChatSenderRole.Admin,
            Content = content,
            TimestampUtc = DateTime.UtcNow
        };
        var validationError = RemoteChatMessagePolicy.Validate(message);
        if (validationError is not null) throw new ArgumentException(validationError, nameof(content));
        await _clients.SendAsync(sessionId, MessageType.RemoteChatMessage, message.MessageId, message, cancellationToken)
            .ConfigureAwait(false);
    }

    public void EndSession(string sessionId)
    {
        _receivedIds.TryRemove(sessionId, out _);
        _rateLimiters.TryRemove(sessionId, out _);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _receivedIds.Clear();
            _rateLimiters.Clear();
        }
        return ValueTask.CompletedTask;
    }

    private static string ReadMessageId(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("messageId", out var property) ||
            property.ValueKind != JsonValueKind.String)
            return string.Empty;

        return property.GetString() ?? string.Empty;
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(ChatService));
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private sealed class ChatSession : IRemoteChatSession
    {
        private readonly ChatService _owner;
        private readonly string _clientId;
        private readonly string _sessionId;
        private int _disposed;

        public ChatSession(ChatService owner, string clientId, string sessionId)
        {
            _owner = owner;
            _clientId = clientId;
            _sessionId = sessionId;
            _owner.MessageReceived += OnMessageReceived;
        }

        public event Action<RemoteChatMessage>? MessageReceived;

        public Task SendAsync(string content, CancellationToken cancellationToken = default) =>
            _owner.SendFromAdminAsync(_clientId, _sessionId, _owner._adminToken, content, cancellationToken);

        private void OnMessageReceived(RemoteChatMessage message)
        {
            if (string.Equals(message.ClientId, _clientId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(message.SessionId, _sessionId, StringComparison.OrdinalIgnoreCase))
                MessageReceived?.Invoke(message);
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _owner.MessageReceived -= OnMessageReceived;
            return ValueTask.CompletedTask;
        }
    }
}
