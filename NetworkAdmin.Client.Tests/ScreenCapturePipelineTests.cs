using NetworkAdmin.Client.Infrastructure.ScreenSharing;
using Xunit;

namespace NetworkAdmin.Client.Tests;

public sealed class ScreenCapturePipelineTests
{
    [Fact]
    public void Capture_diff_produces_bounded_delta_and_periodic_keyframe_can_resync_unchanged_screen()
    {
        var capture = new ScreenCaptureService(jpegQuality: 85);
        using var previous = new Bitmap(64, 64);
        using var current = new Bitmap(64, 64);
        using (var graphics = Graphics.FromImage(previous)) graphics.Clear(Color.Navy);
        using (var graphics = Graphics.FromImage(current)) graphics.Clear(Color.Navy);
        using (var graphics = Graphics.FromImage(current)) graphics.FillRectangle(Brushes.Orange, 20, 30, 5, 5);

        var delta = capture.CreatePacket(current, previous, sequence: 1);
        Assert.NotNull(delta);
        Assert.False(delta.Value.IsFullFrame);
        Assert.InRange(delta.Value.X, 16, 20);
        Assert.InRange(delta.Value.Y, 16, 30);
        Assert.True(delta.Value.X + delta.Value.Width >= 25);
        Assert.True(delta.Value.Y + delta.Value.Height >= 35);

        Assert.Null(capture.CreatePacket(previous, previous, sequence: 2));
        var keyframe = capture.CreatePacket(previous, previous, sequence: 2, forceFullFrame: true);
        Assert.NotNull(keyframe);
        Assert.True(keyframe.Value.IsFullFrame);
        Assert.Equal((64, 64), (keyframe.Value.Width, keyframe.Value.Height));
    }
}
