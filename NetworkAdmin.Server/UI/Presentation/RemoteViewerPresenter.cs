using System.Diagnostics;
using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Server.Application.Interfaces;

namespace NetworkAdmin.Server.Presentation;

internal enum RemoteViewerState { Waiting, Connecting, Streaming, Disconnected, Error }

internal sealed record RemoteViewerFrame(Bitmap Image, long Sequence, double FramesPerSecond,
    int Width, int Height);

// Owns frame I/O and decoding. The view receives only decoded, disposable frames and state updates.
internal sealed class RemoteViewerPresenter : IAsyncDisposable
{
    private readonly Func<CancellationToken, Action, Task<IScreenViewerSession>>? _connect;
    private IScreenViewerSession? _session;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _inputLock = new(1, 1);
    private Task? _runTask;
    private int _started;
    private int _stopped;

    public event Action<RemoteViewerState, string?>? StateChanged;
    public event Action<RemoteViewerFrame>? FrameReceived;
    public RemoteViewerState State { get; private set; } = RemoteViewerState.Waiting;
    public bool IsControlActive => _session?.IsControlActive == true;

    public RemoteViewerPresenter(IScreenViewerSession session) => _session = session;

    public RemoteViewerPresenter(Func<CancellationToken, Action, Task<IScreenViewerSession>> connect) => _connect = connect;

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        _runTask = RunAsync(_stop.Token);
    }

    public Task<bool> RequestControlAsync(CancellationToken cancellationToken) =>
        _session?.RequestControlAsync(cancellationToken) ?? Task.FromResult(false);

    public async Task SendInputEventAsync(RemoteInputEventKind kind, int x, int y, RemoteMouseButton button,
        int wheelDelta, int virtualKeyCode)
    {
        await _inputLock.WaitAsync(_stop.Token).ConfigureAwait(false);
        try
        {
            if (_session?.IsControlActive == true)
                await _session.SendInputEventAsync(kind, x, y, button, wheelDelta, virtualKeyCode, _stop.Token).ConfigureAwait(false);
        }
        finally { _inputLock.Release(); }
    }

    public Task StopControlAsync(string reason = "Server stopped remote control.") =>
        _session?.StopControlAsync(reason) ?? Task.CompletedTask;

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            SetState(RemoteViewerState.Waiting);
            if (_connect is not null)
                _session = await _connect(cancellationToken, () => SetState(RemoteViewerState.Connecting)).ConfigureAwait(false);
            else
                SetState(RemoteViewerState.Connecting);

            cancellationToken.ThrowIfCancellationRequested();
            if (_session is null) throw new InvalidOperationException("Viewer session was not created.");
            await RunFramePipelineAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { SetState(RemoteViewerState.Error, ex.GetBaseException().Message); }
        finally
        {
            await StopSessionAsync("Viewer stopped or screen stream ended.").ConfigureAwait(false);
            if (!cancellationToken.IsCancellationRequested && State is not RemoteViewerState.Error)
                SetState(RemoteViewerState.Disconnected);
        }
    }

    private async Task RunFramePipelineAsync(CancellationToken cancellationToken)
    {
        var timestamps = new Queue<long>();
        Bitmap? composite = null;
        long lastSequence = -1;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = await _session!.ReadScreenFrameAsync(cancellationToken).ConfigureAwait(false);
                if (frame is not { } packet) break;
                if (packet.Sequence <= lastSequence)
                    throw new InvalidDataException("Screen frame sequence is out of order.");

                if (packet.IsFullFrame)
                {
                    var full = await Task.Run(() => JpegFrameDecoder.Decode(packet.Jpeg), cancellationToken).ConfigureAwait(false);
                    if ((packet.Width != 0 && packet.Width != full.Width) ||
                        (packet.Height != 0 && packet.Height != full.Height))
                    {
                        full.Dispose();
                        throw new InvalidDataException("Full screen frame dimensions do not match its header.");
                    }
                    composite?.Dispose();
                    composite = full;
                }
                else
                {
                    if (composite is null)
                        throw new InvalidDataException("Received a screen delta before the first full frame.");
                    var patch = await Task.Run(() => JpegFrameDecoder.Decode(packet.Jpeg), cancellationToken).ConfigureAwait(false);
                    try
                    {
                        if (patch.Width != packet.Width || patch.Height != packet.Height ||
                            packet.X + patch.Width > composite.Width || packet.Y + patch.Height > composite.Height)
                            throw new InvalidDataException("Screen delta rectangle is outside the current frame.");
                        using var graphics = Graphics.FromImage(composite);
                        graphics.DrawImageUnscaled(patch, packet.X, packet.Y);
                    }
                    finally { patch.Dispose(); }
                }

                lastSequence = packet.Sequence;
                var now = Stopwatch.GetTimestamp();
                timestamps.Enqueue(now);
                while (timestamps.Count > 1 && Stopwatch.GetElapsedTime(timestamps.Peek(), now) > TimeSpan.FromSeconds(1))
                    timestamps.Dequeue();
                var fps = timestamps.Count < 2 ? 0 : (timestamps.Count - 1) / Stopwatch.GetElapsedTime(timestamps.Peek(), now).TotalSeconds;
                var image = await Task.Run(() => (Bitmap)composite!.Clone(), cancellationToken).ConfigureAwait(false);
                var update = new RemoteViewerFrame(image, packet.Sequence, fps, composite.Width, composite.Height);
                var handler = FrameReceived;
                if (handler is null) image.Dispose();
                else
                {
                    try { handler(update); }
                    catch { image.Dispose(); throw; }
                }
                SetState(RemoteViewerState.Streaming);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { SetState(RemoteViewerState.Error, ex.GetBaseException().Message); }
        finally { composite?.Dispose(); }
    }

    private void SetState(RemoteViewerState state, string? detail = null)
    {
        State = state;
        StateChanged?.Invoke(state, detail);
    }

    public async Task StopAsync(string reason = "Viewer closed.")
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        await _stop.CancelAsync().ConfigureAwait(false);
        await StopSessionAsync(reason).ConfigureAwait(false);
        if (_runTask is not null)
        {
            try { await _runTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        if (State is not RemoteViewerState.Error)
            SetState(RemoteViewerState.Disconnected);
    }

    private async Task StopSessionAsync(string reason)
    {
        try
        {
            if (_session is not null) await _session.StopAsync(reason).ConfigureAwait(false);
        }
        catch (Exception ex) { SetState(RemoteViewerState.Error, ex.Message); }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _stop.Dispose();
        _inputLock.Dispose();
        if (_session is not null) await _session.DisposeAsync().ConfigureAwait(false);
    }

}

internal static class JpegFrameDecoder
{
    public const int MaxWidth = 3840;
    public const int MaxHeight = 2160;
    public const long MaxPixels = (long)MaxWidth * MaxHeight;

    public static Bitmap Decode(byte[] jpeg)
    {
        using var stream = new MemoryStream(jpeg, writable: false);
        using var decoded = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
        if (decoded.Width is <= 0 or > MaxWidth || decoded.Height is <= 0 or > MaxHeight ||
            (long)decoded.Width * decoded.Height > MaxPixels)
            throw new InvalidDataException($"Decoded frame dimensions exceed the viewer limit ({MaxWidth}×{MaxHeight}).");

        var bitmap = new Bitmap(decoded.Width, decoded.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.DrawImageUnscaled(decoded, 0, 0);
        return bitmap;
    }
}

