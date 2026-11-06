using NetworkAdmin.Server.Networking;
using NetworkAdmin.Shared.Messages;

namespace NetworkAdmin.Server.Application.Interfaces;

/// <summary>Independent chat transport module attached to authenticated Client sessions.</summary>
internal interface IRemoteChatModule : IAsyncDisposable
{
    Task HandleClientMessageAsync(ClientSessionInfo session, MessageEnvelope envelope, CancellationToken cancellationToken);
    IRemoteChatSession CreateSession(string clientId, string sessionId);
    void EndSession(string sessionId);
}
