using NetworkAdmin.Shared.Messages;

namespace NetworkAdmin.Client.Application.Interfaces;

/// <summary>Chat operations scoped to one authenticated Client connection.</summary>
internal interface IClientChatSession : IAsyncDisposable
{
    event Action<RemoteChatMessage>? MessageReceived;
    Task HandleIncomingAsync(RemoteChatMessage? message, string requestId, CancellationToken cancellationToken);
    Task SendAsync(string content, CancellationToken cancellationToken = default);
}
