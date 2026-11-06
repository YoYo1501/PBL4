using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Protocol;

namespace NetworkAdmin.Server.Application.Interfaces;

public interface IScreenViewerSession : IAsyncDisposable
{
    Task<(long Sequence, byte[] Jpeg)?> ReadFrameAsync(CancellationToken cancellationToken);
    async Task<ScreenFramePacket?> ReadScreenFrameAsync(CancellationToken cancellationToken)
    {
        var frame = await ReadFrameAsync(cancellationToken).ConfigureAwait(false);
        return frame is { } value
            ? new ScreenFramePacket(value.Sequence, true, 0, 0, 0, 0, value.Jpeg)
            : null;
    }
    Task StopAsync(string reason = "Viewer closed.");
}
