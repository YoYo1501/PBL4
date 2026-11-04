using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Protocol;

namespace NetworkAdmin.Server.Application.Interfaces;

public interface IScreenViewerSession : IAsyncDisposable
{
    bool IsControlActive { get; }
    Task<(long Sequence, byte[] Jpeg)?> ReadFrameAsync(CancellationToken cancellationToken);
    async Task<ScreenFramePacket?> ReadScreenFrameAsync(CancellationToken cancellationToken)
    {
        var frame = await ReadFrameAsync(cancellationToken).ConfigureAwait(false);
        return frame is { } value
            ? new ScreenFramePacket(value.Sequence, true, 0, 0, 0, 0, value.Jpeg)
            : null;
    }
    Task<bool> RequestControlAsync(CancellationToken cancellationToken);
    Task SendInputEventAsync(RemoteInputEventKind kind, int x, int y, RemoteMouseButton button,
        int wheelDelta, int virtualKeyCode, CancellationToken cancellationToken);
    Task StopControlAsync(string reason = "Server stopped control.");
    Task StopAsync(string reason = "Viewer closed.");
}
