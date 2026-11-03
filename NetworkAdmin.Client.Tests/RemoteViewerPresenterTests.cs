using System.Drawing.Imaging;
using NetworkAdmin.Server;
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

    private sealed class FakeViewerSession(IReadOnlyList<(long Sequence, byte[] Jpeg)> frames) : IScreenViewerSession
    {
        private int _index;
        private int _stopCalls;
        public int StopCalls => Volatile.Read(ref _stopCalls);

        public Task<(long Sequence, byte[] Jpeg)?> ReadFrameAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_index >= frames.Count) return Task.FromResult<(long Sequence, byte[] Jpeg)?>(null);
            var frame = frames[_index++];
            return Task.FromResult<(long Sequence, byte[] Jpeg)?>(frame);
        }

        public Task StopAsync(string reason = "Viewer closed.")
        {
            Interlocked.Increment(ref _stopCalls);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
