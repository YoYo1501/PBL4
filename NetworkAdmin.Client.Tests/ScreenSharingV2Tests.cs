using System.Drawing.Imaging;
using NetworkAdmin.Client.Infrastructure.ScreenSharing.V2;
using NetworkAdmin.Client.Infrastructure.ScreenSharing;
using NetworkAdmin.Server.Presentation.V2;
using NetworkAdmin.Server.Presentation;
using NetworkAdmin.Server.Application.Interfaces;
using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Protocol;
using Xunit;

namespace NetworkAdmin.Client.Tests;

public sealed class ScreenSharingV2Tests
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    [InlineData(null, false)]
    [InlineData("invalid", false)]
    public void Version_flag_parses_safely(string? value, bool expected) =>
        Assert.Equal(expected, ScreenShareV2Configuration.Parse(value));

    [Fact]
    public void Encoder_emits_initial_full_skips_unchanged_and_encodes_delta()
    {
        var encoder = new ScreenShareV2FrameEncoder(85);
        using var before = Solid(Color.DarkSlateBlue, 64, 48);
        using var same = Solid(Color.DarkSlateBlue, 64, 48);
        using var changed = Solid(Color.DarkSlateBlue, 64, 48);
        using (var graphics = Graphics.FromImage(changed))
            graphics.FillRectangle(Brushes.OrangeRed, 4, 5, 5, 6);

        var full = encoder.Encode(before, null, 0);
        var noChange = encoder.Encode(same, before, 1);
        var delta = encoder.Encode(changed, before, 1);

        Assert.True(full!.Value.IsFullFrame);
        Assert.Null(noChange);
        Assert.False(delta!.Value.IsFullFrame);
        Assert.Equal(0, delta.Value.X);
        Assert.Equal(0, delta.Value.Y);
        Assert.Equal(16, delta.Value.Width);
        Assert.Equal(16, delta.Value.Height);
    }

    [Fact]
    public void Encoder_emits_periodic_full_keyframe_after_delta_run()
    {
        var encoder = new ScreenShareV2FrameEncoder(60);
        using var previous = Solid(Color.Black, 32, 32);
        using var current = Solid(Color.Black, 32, 32);
        Assert.True(encoder.Encode(previous, null, 0)!.Value.IsFullFrame);

        for (var sequence = 1; sequence <= 120; sequence++)
        {
            using var graphics = Graphics.FromImage(current);
            graphics.FillRectangle(Brushes.White, 2, 2, 4, 4);
            var packet = encoder.Encode(current, previous, sequence);
            Assert.NotNull(packet);
        }
        var keyFrame = encoder.Encode(current, previous, 121);
        Assert.True(keyFrame!.Value.IsFullFrame);
    }

    [Fact]
    public async Task Composer_applies_delta_and_rejects_sequence_gap()
    {
        var encoder = new ScreenShareV2FrameEncoder(90);
        using var before = Solid(Color.DarkSlateBlue, 64, 48);
        using var changed = Solid(Color.DarkSlateBlue, 64, 48);
        using (var graphics = Graphics.FromImage(changed))
            graphics.FillRectangle(Brushes.LimeGreen, 4, 5, 5, 6);
        var full = encoder.Encode(before, null, 0)!.Value;
        var delta = encoder.Encode(changed, before, 1)!.Value;

        using var composer = new ScreenShareV2FrameComposer();
        using var initial = await composer.ComposeAsync(full);
        using var updated = await composer.ComposeAsync(delta);
        Assert.Equal(initial.Size, updated.Size);
        var pixel = updated.GetPixel(5, 6);
        Assert.InRange(Math.Abs(pixel.R - Color.LimeGreen.R), 0, 30);
        Assert.InRange(Math.Abs(pixel.G - Color.LimeGreen.G), 0, 30);
        Assert.InRange(Math.Abs(pixel.B - Color.LimeGreen.B), 0, 30);
        await Assert.ThrowsAsync<InvalidDataException>(() => composer.ComposeAsync(
            new ScreenFramePacket(3, true, 0, 0, 64, 48, full.Jpeg)));
    }

    [Fact]
    public async Task Latest_slot_replaces_stale_frame_and_completes_cleanly()
    {
        using var slot = new LatestBitmapSlot();
        slot.Publish(Solid(Color.Red, 10, 10));
        slot.Publish(Solid(Color.Blue, 10, 10));
        Assert.Equal(1, slot.ReplacedCount);
        using var latest = await slot.TakeAsync(CancellationToken.None);
        Assert.Equal(Color.Blue.ToArgb(), latest!.GetPixel(0, 0).ToArgb());
        slot.Dispose();
        Assert.Null(await slot.TakeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task V1_encoder_packets_are_accepted_by_V2_composer()
    {
        var v1 = new ScreenCaptureService(jpegQuality: 85);
        using var before = Solid(Color.DarkSlateBlue, 64, 48);
        using var changed = Solid(Color.DarkSlateBlue, 64, 48);
        using (var graphics = Graphics.FromImage(changed)) graphics.FillRectangle(Brushes.OrangeRed, 4, 5, 5, 6);
        var full = v1.CreatePacket(before, null, 0)!.Value;
        var delta = v1.CreatePacket(changed, before, 1)!.Value;
        using var composer = new ScreenShareV2FrameComposer();
        using var initial = await composer.ComposeAsync(full);
        using var updated = await composer.ComposeAsync(delta);
        Assert.Equal(initial.Size, updated.Size);
    }

    [Fact]
    public async Task V2_encoder_packets_are_accepted_by_V1_presenter()
    {
        var encoder = new ScreenShareV2FrameEncoder(85);
        using var before = Solid(Color.DarkSlateBlue, 64, 48);
        using var changed = Solid(Color.DarkSlateBlue, 64, 48);
        using (var graphics = Graphics.FromImage(changed)) graphics.FillRectangle(Brushes.OrangeRed, 4, 5, 5, 6);
        var packets = new[] { encoder.Encode(before, null, 0)!.Value, encoder.Encode(changed, before, 1)!.Value };
        await using var presenter = new RemoteViewerPresenter(new PacketSession(packets));
        using var received = new ManualResetEventSlim();
        presenter.FrameReceived += frame =>
        {
            frame.Image.Dispose();
            if (frame.Sequence == 1) received.Set();
        };
        presenter.Start();
        Assert.True(received.Wait(TimeSpan.FromSeconds(5)));
        await presenter.StopAsync();
    }

    [Fact]
    public async Task V2_presenter_path_uses_composer_and_stops_session()
    {
        var encoder = new ScreenShareV2FrameEncoder(85);
        using var before = Solid(Color.DarkSlateBlue, 64, 48);
        using var changed = Solid(Color.DarkSlateBlue, 64, 48);
        using (var graphics = Graphics.FromImage(changed)) graphics.FillRectangle(Brushes.OrangeRed, 4, 5, 5, 6);
        var packets = new[] { encoder.Encode(before, null, 0)!.Value, encoder.Encode(changed, before, 1)!.Value };
        var session = new PacketSession(packets);
        await using var presenter = new RemoteViewerPresenter(session, screenV2Enabled: true);
        using var received = new ManualResetEventSlim();
        presenter.FrameReceived += frame =>
        {
            frame.Image.Dispose();
            if (frame.Sequence == 1) received.Set();
        };
        presenter.Start();
        Assert.True(received.Wait(TimeSpan.FromSeconds(5)));
        await presenter.StopAsync();
        Assert.True(session.WasStopped);
    }

    private static Bitmap Solid(Color color, int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(color);
        return bitmap;
    }

    private sealed class PacketSession(IReadOnlyList<ScreenFramePacket> packets) : IScreenViewerSession
    {
        private int _index;
        public bool WasStopped { get; private set; }
        public bool IsControlActive => false;
        public Task<(long Sequence, byte[] Jpeg)?> ReadFrameAsync(CancellationToken cancellationToken) => Task.FromResult<(long, byte[])?>(null);
        public Task<ScreenFramePacket?> ReadScreenFrameAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ScreenFramePacket?>(_index < packets.Count ? packets[_index++] : null);
        public Task<bool> RequestControlAsync(CancellationToken cancellationToken) => Task.FromResult(false);
        public Task SendInputEventAsync(RemoteInputEventKind kind, int x, int y, RemoteMouseButton button, int wheelDelta, int virtualKeyCode, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopControlAsync(string reason = "Server stopped control.") => Task.CompletedTask;
        public Task StopAsync(string reason = "Viewer closed.") { WasStopped = true; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
