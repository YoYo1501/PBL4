using NetworkAdmin.Shared.Messages;

namespace NetworkAdmin.Server.Application.Interfaces;

public interface IScreenViewerSession : IAsyncDisposable
{
    bool IsControlActive { get; }
    Task<(long Sequence, byte[] Jpeg)?> ReadFrameAsync(CancellationToken cancellationToken);
    Task<bool> RequestControlAsync(CancellationToken cancellationToken);
    Task SendInputEventAsync(RemoteInputEventKind kind, int x, int y, RemoteMouseButton button,
        int wheelDelta, int virtualKeyCode, CancellationToken cancellationToken);
    Task StopControlAsync(string reason = "Server stopped control.");
    Task StopAsync(string reason = "Viewer closed.");
}
