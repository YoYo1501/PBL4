using NetworkAdmin.Client.Application.Interfaces;
using NetworkAdmin.Shared.Messages;

namespace NetworkAdmin.Client.Infrastructure.ScreenSharing;

internal sealed class NullClientScreenShareModule : IClientScreenShareModule
{
    public static NullClientScreenShareModule Instance { get; } = new();
    public Task HandleAsync(MessageEnvelope envelope, CancellationToken sessionToken) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
