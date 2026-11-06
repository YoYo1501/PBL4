using NetworkAdmin.Shared.Messages;

namespace NetworkAdmin.Client.Application.Interfaces;

/// <summary>Receives screen-view requests independently of chat and remote input.</summary>
internal interface IClientScreenShareModule : IAsyncDisposable
{
    Task HandleAsync(MessageEnvelope envelope, CancellationToken sessionToken);
}
