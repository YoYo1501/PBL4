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
        var frames = new LatestFrameQueue<ScreenFrame>();
        using var pipeline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var receiver = ReceiveFramesAsync(frames, pipeline.Token);
        var decoder = DecodeFramesAsync(frames, pipeline.Token);
        try { await Task.WhenAll(receiver, decoder).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            SetState(RemoteViewerState.Error, ex.GetBaseException().Message);
            await pipeline.CancelAsync().ConfigureAwait(false);
            try { await Task.WhenAll(receiver, decoder).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception) { }
        }
        finally { frames.Complete(); }
    }

    private async Task ReceiveFramesAsync(LatestFrameQueue<ScreenFrame> writer, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = await _session!.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
                if (frame is null) break;
                cancellationToken.ThrowIfCancellationRequested();
                writer.TryWrite(new ScreenFrame(frame.Value.Sequence, frame.Value.Jpeg));
            }
        }
        finally { writer.Complete(); }
    }

    private async Task DecodeFramesAsync(LatestFrameQueue<ScreenFrame> reader, CancellationToken cancellationToken)
    {
        var timestamps = new Queue<long>();
        while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!reader.TryRead(out var frame)) continue;
            var image = await Task.Run(() => JpegFrameDecoder.Decode(frame.Jpeg), cancellationToken).ConfigureAwait(false);
            var now = Stopwatch.GetTimestamp();
            timestamps.Enqueue(now);
            while (timestamps.Count > 1 && Stopwatch.GetElapsedTime(timestamps.Peek(), now) > TimeSpan.FromSeconds(1))
                timestamps.Dequeue();
            var fps = timestamps.Count < 2 ? 0 : (timestamps.Count - 1) / Stopwatch.GetElapsedTime(timestamps.Peek(), now).TotalSeconds;
            var update = new RemoteViewerFrame(image, frame.Sequence, fps, image.Width, image.Height);
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

    private sealed record ScreenFrame(long Sequence, byte[] Jpeg);
}

internal static class JpegFrameDecoder
{
    public const int MaxWidth = 2560;
    public const int MaxHeight = 1440;
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

