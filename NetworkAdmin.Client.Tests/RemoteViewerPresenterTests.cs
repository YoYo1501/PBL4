using System.Drawing.Imaging;
using NetworkAdmin.Server;
using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Protocol;
using Xunit;

namespace NetworkAdmin.Client.Tests;

public sealed class RemoteViewerPresenterTests
{
    [Fact]
    public async Task Latest_frame_queue_keeps_only_the_newest_waiting_frame()
    {
        var queue = new LatestFrameQueue<int>();
        Assert.True(queue.TryWrite(1));
        Assert.True(queue.TryWrite(2));
        Assert.True(queue.TryWrite(3));
        Assert.True(await queue.WaitToReadAsync(CancellationToken.None));
        Assert.True(queue.TryRead(out var frame));
        Assert.Equal(3, frame);
        queue.Complete();
        Assert.False(await queue.WaitToReadAsync(CancellationToken.None));
        Assert.False(queue.TryWrite(4));
    }

    [Fact]
    public void Decoder_rejects_dimensions_above_viewer_limit()
    {
        using var bitmap = new Bitmap(JpegFrameDecoder.MaxWidth + 1, 2);
        using var output = new MemoryStream();
        bitmap.Save(output, ImageFormat.Jpeg);
        Assert.Throws<InvalidDataException>(() => JpegFrameDecoder.Decode(output.ToArray()));
    }

    [Fact]
    public async Task Presenter_composes_delta_inside_full_frame_without_moving_other_pixels()
    {
        var session = new FakePacketViewerSession([
            new ScreenFramePacket(0, true, 0, 0, 32, 24, CreateJpeg(Color.Red, 32, 24)),
            new ScreenFramePacket(1, false, 16, 8, 8, 8, CreateJpeg(Color.Blue, 8, 8))
        ]);
        await using var presenter = new RemoteViewerPresenter(session);
        using var completed = new ManualResetEventSlim();
        Bitmap? latest = null;
        presenter.FrameReceived += frame =>
        {
            Interlocked.Exchange(ref latest, (Bitmap)frame.Image.Clone())?.Dispose();
            frame.Image.Dispose();
            if (frame.Sequence == 1) completed.Set();
        };

        presenter.Start();
        Assert.True(completed.Wait(TimeSpan.FromSeconds(10)));
        using var result = latest;
        Assert.NotNull(result);
        var unchanged = result!.GetPixel(4, 4);
        var patched = result.GetPixel(20, 12);
        Assert.True(unchanged.R > 180 && unchanged.G < 80 && unchanged.B < 80,
            $"A delta changed pixels outside its rectangle: {unchanged}.");
        Assert.True(patched.B > 180 && patched.R < 80 && patched.G < 80,
            $"The delta was not drawn at its declared coordinates: {patched}.");
        await presenter.StopAsync();
    }

    [Fact]
    public async Task Presenter_marks_streaming_then_disconnected_and_stops_session()
    {
        var jpeg = CreateJpeg();
        var session = new FakeViewerSession([
            (0L, jpeg), (1L, jpeg), (2L, jpeg), (3L, jpeg)
        ]);
        await using var presenter = new RemoteViewerPresenter(session);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sawStreaming = 0;
        var frames = 0;
        long lastSequence = -1;
        var states = new System.Collections.Concurrent.ConcurrentQueue<RemoteViewerState>();
        presenter.StateChanged += (state, _) =>
        {
            states.Enqueue(state);
            if (state == RemoteViewerState.Streaming) Interlocked.Exchange(ref sawStreaming, 1);
            if (state == RemoteViewerState.Disconnected) disconnected.TrySetResult();
        };
        presenter.FrameReceived += frame =>
        {
            Interlocked.Increment(ref frames);
            Interlocked.Exchange(ref lastSequence, frame.Sequence);
            frame.Image.Dispose();
        };

        presenter.Start();
        await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, sawStreaming);
        Assert.InRange(frames, 1, 4);
        Assert.Equal(3, lastSequence);
        Assert.Contains(RemoteViewerState.Waiting, states);
        Assert.Contains(RemoteViewerState.Connecting, states);
        Assert.Contains(RemoteViewerState.Streaming, states);
        Assert.Contains(RemoteViewerState.Disconnected, states);
        Assert.True(session.StopCalls > 0);
    }

    [Fact]
    public async Task Presenter_reports_error_and_cleans_session_for_invalid_jpeg()
    {
        var session = new FakeViewerSession([(0L, new byte[] { 1, 2, 3, 4 })]);
        await using var presenter = new RemoteViewerPresenter(session);
        var error = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        presenter.StateChanged += (state, detail) =>
        {
            if (state == RemoteViewerState.Error) error.TrySetResult(detail ?? string.Empty);
        };
        presenter.Start();
        Assert.NotEmpty(await error.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        await presenter.StopAsync();
        Assert.True(session.StopCalls > 0);
    }

    private static byte[] CreateJpeg()
    {
        using var bitmap = new Bitmap(24, 16);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Navy);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Jpeg);
        return stream.ToArray();
    }

    private static byte[] CreateJpeg(Color color, int width, int height)
    {
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(color);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Jpeg);
        return stream.ToArray();
    }

    private sealed class FakePacketViewerSession(IReadOnlyList<ScreenFramePacket> frames) : IScreenViewerSession
    {
        private int _index;
        public bool IsControlActive => false;
        public Task<(long Sequence, byte[] Jpeg)?> ReadFrameAsync(CancellationToken cancellationToken) =>
            Task.FromResult<(long Sequence, byte[] Jpeg)?>(null);
        public Task<ScreenFramePacket?> ReadScreenFrameAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<ScreenFramePacket?>(_index < frames.Count ? frames[_index++] : null);
        }
        public Task<bool> RequestControlAsync(CancellationToken cancellationToken) => Task.FromResult(false);
        public Task SendInputEventAsync(RemoteInputEventKind kind, int x, int y, RemoteMouseButton button,
            int wheelDelta, int virtualKeyCode, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopControlAsync(string reason = "Server stopped control.") => Task.CompletedTask;
        public Task StopAsync(string reason = "Viewer closed.") => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeViewerSession(IReadOnlyList<(long Sequence, byte[] Jpeg)> frames) : IScreenViewerSession
    {
        private int _index;
        private int _stopCalls;
        public int StopCalls => Volatile.Read(ref _stopCalls);
        public bool IsControlActive => false;

        public Task<(long Sequence, byte[] Jpeg)?> ReadFrameAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_index >= frames.Count) return Task.FromResult<(long Sequence, byte[] Jpeg)?>(null);
            var frame = frames[_index++];
            return Task.FromResult<(long Sequence, byte[] Jpeg)?>(frame);
        }

        public Task<bool> RequestControlAsync(CancellationToken cancellationToken) => Task.FromResult(false);
        public Task SendInputEventAsync(NetworkAdmin.Shared.Messages.RemoteInputEventKind kind, int x, int y,
            NetworkAdmin.Shared.Messages.RemoteMouseButton button, int wheelDelta, int virtualKeyCode,
            CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopControlAsync(string reason = "Server stopped control.") => Task.CompletedTask;

        public Task StopAsync(string reason = "Viewer closed.")
        {
            Interlocked.Increment(ref _stopCalls);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
