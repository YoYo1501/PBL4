namespace NetworkAdmin.Server.Application.Interfaces;

public interface IScreenViewerSession : IAsyncDisposable
{
    Task<(long Sequence, byte[] Jpeg)?> ReadFrameAsync(CancellationToken cancellationToken);
    Task StopAsync(string reason = "Viewer closed.");
}
