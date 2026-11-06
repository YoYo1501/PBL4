using NetworkAdmin.Server.Networking;
using NetworkAdmin.Shared.Messages;

namespace NetworkAdmin.Server.Application.Interfaces;

/// <summary>View-only screen transport, independent of chat and input-control modules.</summary>
internal interface IScreenShareServerModule : IAsyncDisposable
{
    Task HandleMessageAsync(ClientSessionInfo session, MessageEnvelope envelope, CancellationToken cancellationToken);
    Task<IScreenViewerSession> RequestViewAsync(string clientId, string sessionId,
        RemoteSessionAuthorization authorization, CancellationToken cancellationToken, Action? onConnecting = null);
    bool HasSession(string clientId);
    Task StopForSessionAsync(string sessionId, string reason, bool notifyClient = true);
}
