using NetworkAdmin.Client.Infrastructure.Configuration;
using NetworkAdmin.Client;
using Xunit;

namespace NetworkAdmin.Client.Tests;

public sealed class ScreenStreamConfigurationTests
{
    [Fact]
    public void Screen_stream_defaults_are_bounded_for_lan_demo()
    {
        var options = ClientOptions.Load([]);
        Assert.Equal(8, options.ScreenMaxFps);
        Assert.Equal(58, options.ScreenJpegQuality);
        Assert.Equal(1600, options.ScreenMaxWidth);
        Assert.Equal(1000, options.ScreenMaxHeight);
    }

    [Fact]
    public void Screen_stream_command_line_settings_are_clamped_to_safe_ranges()
    {
        var options = ClientOptions.Load([
            "--screen-fps", "200",
            "--screen-jpeg-quality", "100",
            "--screen-max-width", "9000",
            "--screen-max-height", "10"
        ]);
        Assert.Equal(20, options.ScreenMaxFps);
        Assert.Equal(85, options.ScreenJpegQuality);
        Assert.Equal(2560, options.ScreenMaxWidth);
        Assert.Equal(480, options.ScreenMaxHeight);
    }
}
