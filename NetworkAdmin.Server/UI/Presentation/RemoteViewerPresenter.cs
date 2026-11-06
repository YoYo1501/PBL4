using System.Diagnostics;
using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Server.Application.Interfaces;
using NetworkAdmin.Shared.Diagnostics;
using NetworkAdmin.Server.Presentation.V2;

namespace NetworkAdmin.Server.Presentation;

internal enum RemoteViewerState { Waiting, Connecting, Streaming, Disconnected, Error }

internal sealed record RemoteViewerFrame(Bitmap Image, long Sequence, double FramesPerSecond,
    int Width, int Height, long ReceivedAtTimestamp);

// Owns frame I/O and decoding. The view receives only decoded, disposable frames and state updates.
internal sealed class RemoteViewerPresenter : IAsyncDisposable
{
    private readonly Func<CancellationToken, Action, Task<IScreenViewerSession>>? _connect;
    private IScreenViewerSession? _session;
    private readonly CancellationTokenSource _stop = new();
    private readonly ScreenSharePerformanceMonitor? _performance;
    private readonly bool _screenV2Enabled;
    private Task? _runTask;
    private int _started;
    private int _stopped;

    public event Action<RemoteViewerState, string?>? StateChanged;
    public event Action<RemoteViewerFrame>? FrameReceived;
    public RemoteViewerState State { get; private set; } = RemoteViewerState.Waiting;

    public RemoteViewerPresenter(IScreenViewerSession session, ScreenSharePerformanceMonitor? performance = null,
        bool screenV2Enabled = false)
    { _session = session; _performance = performance; _screenV2Enabled = screenV2Enabled; }

    public RemoteViewerPresenter(Func<CancellationToken, Action, Task<IScreenViewerSession>> connect,
        ScreenSharePerformanceMonitor? performance = null, bool screenV2Enabled = false)
    { _connect = connect; _performance = performance; _screenV2Enabled = screenV2Enabled; }

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        _runTask = RunAsync(_stop.Token);
    }

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
        _performance?.Start();
        var timestamps = new Queue<long>();
        Bitmap? composite = null;
        using var v2Composer = _screenV2Enabled ? new ScreenShareV2FrameComposer(_performance) : null;
        long lastSequence = -1;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = await _session!.ReadScreenFrameAsync(cancellationToken).ConfigureAwait(false);
                if (frame is not { } packet) break;
                var receivedAt = Stopwatch.GetTimestamp();
                if (!_screenV2Enabled && packet.Sequence != lastSequence + 1)
                    throw new InvalidDataException($"Screen frame sequence gap: expected {lastSequence + 1}, received {packet.Sequence}.");

                Bitmap image;
                if (v2Composer is not null)
                {
                    image = await v2Composer.ComposeAsync(packet, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    if (packet.IsFullFrame)
                    {
                        var decodeStarted = Stopwatch.GetTimestamp();
                        var full = await Task.Run(() => JpegFrameDecoder.Decode(packet.Jpeg), cancellationToken).ConfigureAwait(false);
                        _performance?.Decode(Stopwatch.GetTimestamp() - decodeStarted, packet.Jpeg.Length);
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
                        var decodeStarted = Stopwatch.GetTimestamp();
                        var patch = await Task.Run(() => JpegFrameDecoder.Decode(packet.Jpeg), cancellationToken).ConfigureAwait(false);
                        _performance?.Decode(Stopwatch.GetTimestamp() - decodeStarted, packet.Jpeg.Length);
                        try
                        {
                            if (patch.Width != packet.Width || patch.Height != packet.Height ||
                                packet.X + patch.Width > composite.Width || packet.Y + patch.Height > composite.Height)
                                throw new InvalidDataException("Screen delta rectangle is outside the current frame.");
                            var compositeStarted = Stopwatch.GetTimestamp();
                            using var graphics = Graphics.FromImage(composite);
                            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                            graphics.DrawImageUnscaled(patch, packet.X, packet.Y);
                            _performance?.Composite(Stopwatch.GetTimestamp() - compositeStarted);
                        }
                        finally { patch.Dispose(); }
                    }
                    lastSequence = packet.Sequence;

                    var cloneStarted = Stopwatch.GetTimestamp();
                    image = await Task.Run(() => (Bitmap)composite!.Clone(), cancellationToken).ConfigureAwait(false);
                    _performance?.Clone(Stopwatch.GetTimestamp() - cloneStarted);
                }

                var now = Stopwatch.GetTimestamp();
                timestamps.Enqueue(now);
                while (timestamps.Count > 1 && Stopwatch.GetElapsedTime(timestamps.Peek(), now) > TimeSpan.FromSeconds(1))
                    timestamps.Dequeue();
                var fps = timestamps.Count < 2 ? 0 : (timestamps.Count - 1) / Stopwatch.GetElapsedTime(timestamps.Peek(), now).TotalSeconds;
                var update = new RemoteViewerFrame(image, packet.Sequence, fps, image.Width, image.Height, receivedAt);
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
        finally { composite?.Dispose(); _performance?.Stop(); }
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

