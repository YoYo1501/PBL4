using System.Diagnostics;
using NetworkAdmin.Shared.Diagnostics;
using NetworkAdmin.Shared.Protocol;

namespace NetworkAdmin.Client.Infrastructure.ScreenSharing.V2;

/// <summary>Coordinates independent capture and send loops over a one-frame mailbox.</summary>
internal sealed class ScreenShareV2Sender
{
    private readonly Func<CancellationToken, Bitmap?> _capture;
    private readonly ScreenShareV2FrameEncoder _encoder;
    private readonly int _maxFps;
    private readonly ScreenSharePerformanceMonitor? _performance;

    public ScreenShareV2Sender(Func<CancellationToken, Bitmap?> capture, int maxFps, int jpegQuality,
        ScreenSharePerformanceMonitor? performance = null)
    {
        _capture = capture;
        _encoder = new ScreenShareV2FrameEncoder(jpegQuality, performance);
        _maxFps = Math.Clamp(maxFps, 1, 20);
        _performance = performance;
    }

    public async Task RunAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var latest = new LatestBitmapSlot();
        using var captureStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var captureTask = Task.Run(() => CaptureLoopAsync(latest, captureStop.Token), CancellationToken.None);
        Bitmap? referenceFrame = null;
        long sequence = 0;
        try
        {
            while (true)
            {
                var current = await latest.TakeAsync(cancellationToken).ConfigureAwait(false);
                if (current is null) break;
                try
                {
                    ScreenFramePacket? packet = null;
                    await Task.Run(() => packet = _encoder.Encode(current, referenceFrame, sequence,
                        forceFull: referenceFrame is null, cancellationToken), cancellationToken).ConfigureAwait(false);

                    referenceFrame?.Dispose();
                    referenceFrame = current;
                    current = null;
                    if (packet is not { } frame) continue;

                    var sendStarted = Stopwatch.GetTimestamp();
                    await ScreenFrameProtocol.WriteFrameAsync(stream, frame, cancellationToken).ConfigureAwait(false);
                    _performance?.Send(Stopwatch.GetTimestamp() - sendStarted, frame.Jpeg.Length);
                    sequence++;
                }
                finally { current?.Dispose(); }
            }
        }
        finally
        {
            captureStop.Cancel();
            try { await captureTask.ConfigureAwait(false); }
            catch (OperationCanceledException) when (captureStop.IsCancellationRequested) { }
            referenceFrame?.Dispose();
            if (_performance is not null)
                for (var i = 0L; i < latest.ReplacedCount; i++) _performance.DroppedFrame();
        }
    }

    private async Task CaptureLoopAsync(LatestBitmapSlot latest, CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(1d / _maxFps);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var cycleStart = Stopwatch.GetTimestamp();
                Bitmap? frame = null;
                try
                {
                    var captureStart = Stopwatch.GetTimestamp();
                    frame = await Task.Run(() => _capture(cancellationToken), CancellationToken.None).ConfigureAwait(false);
                    if (frame is null) break;
                    _performance?.Capture(Stopwatch.GetTimestamp() - captureStart);
                    latest.Publish(frame);
                    frame = null;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                catch
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                }
                finally { frame?.Dispose(); }

                var delay = interval - Stopwatch.GetElapsedTime(cycleStart);
                if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally { latest.Complete(); }
    }
}
