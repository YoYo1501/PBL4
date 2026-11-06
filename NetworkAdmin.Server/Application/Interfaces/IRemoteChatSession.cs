using NetworkAdmin.Shared.Messages;

namespace NetworkAdmin.Server.Application.Interfaces;

public interface IRemoteChatSession : IAsyncDisposable
{
    event Action<RemoteChatMessage>? MessageReceived;
    Task SendAsync(string content, CancellationToken cancellationToken = default);
}
