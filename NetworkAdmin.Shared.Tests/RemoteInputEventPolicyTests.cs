using NetworkAdmin.Shared.Messages;
using Xunit;

namespace NetworkAdmin.Shared.Tests;

public sealed class RemoteInputEventPolicyTests
{
    private const string Client = "client-a", Session = "session-a", ScreenRequest = "screen-request-a", ControlRequest = "control-request-a";

    [Theory]
    [InlineData(RemoteInputEventKind.MouseMove, 0, 10_000, 0, 0, 0, true)]
    [InlineData(RemoteInputEventKind.MouseClick, 400, 800, (int)RemoteMouseButton.Left, 0, 0, true)]
    [InlineData(RemoteInputEventKind.MouseDown, 400, 800, (int)RemoteMouseButton.Left, 0, 0, true)]
    [InlineData(RemoteInputEventKind.MouseUp, 400, 800, (int)RemoteMouseButton.Left, 0, 0, true)]
    [InlineData(RemoteInputEventKind.MouseWheel, 400, 800, 0, -1200, 0, true)]
    [InlineData(RemoteInputEventKind.KeyDown, 0, 0, 0, 0, 255, true)]
    [InlineData(RemoteInputEventKind.MouseMove, 10_001, 0, 0, 0, 0, false)]
    [InlineData(RemoteInputEventKind.MouseClick, 0, 0, 9, 0, 0, false)]
    [InlineData(RemoteInputEventKind.MouseDown, 0, 0, 9, 0, 0, false)]
    [InlineData(RemoteInputEventKind.MouseWheel, 0, 0, 0, 1201, 0, false)]
    [InlineData(RemoteInputEventKind.KeyUp, 0, 0, 0, 0, 256, false)]
    public void Validates_event_fields(RemoteInputEventKind kind, int x, int y, int button, int wheel, int key, bool expected)
    {
        var input = new RemoteInputEventMessage
        {
            ClientId = Client, SessionId = Session, ScreenRequestId = ScreenRequest, ControlRequestId = ControlRequest,
            Kind = kind, X = x, Y = y, Button = (RemoteMouseButton)button,
            WheelDelta = wheel, VirtualKeyCode = key
        };
        Assert.Equal(expected, RemoteInputEventPolicy.IsValid(input, Client, Session, ScreenRequest, ControlRequest));
    }

    [Fact]
    public void Rejects_identity_or_screen_request_mismatch()
    {
        var input = new RemoteInputEventMessage
        {
            ClientId = Client, SessionId = Session, ScreenRequestId = ScreenRequest, ControlRequestId = ControlRequest,
            Kind = RemoteInputEventKind.MouseMove, X = 50, Y = 50
        };
        Assert.False(RemoteInputEventPolicy.IsValid(input, "other-client", Session, ScreenRequest, ControlRequest));
        Assert.False(RemoteInputEventPolicy.IsValid(input, Client, "other-session", ScreenRequest, ControlRequest));
        Assert.False(RemoteInputEventPolicy.IsValid(input, Client, Session, "other-request", ControlRequest));
        Assert.False(RemoteInputEventPolicy.IsValid(input, Client, Session, ScreenRequest, "other-control-request"));
    }

    [Fact]
    public void Rate_limiter_bounds_input_burst()
    {
        var limiter = new RemoteInputRateLimiter();
        for (var i = 0; i < RemoteInputEventPolicy.MaxEventsPerSecond; i++)
            Assert.True(limiter.TryAcquire());

        Assert.False(limiter.TryAcquire());
    }

    [Fact]
    public void Existing_input_event_ids_remain_unchanged()
    {
        Assert.Equal(1, (int)RemoteInputEventKind.MouseMove);
        Assert.Equal(2, (int)RemoteInputEventKind.MouseClick);
        Assert.Equal(3, (int)RemoteInputEventKind.MouseWheel);
        Assert.Equal(4, (int)RemoteInputEventKind.KeyDown);
        Assert.Equal(5, (int)RemoteInputEventKind.KeyUp);
        Assert.Equal(6, (int)RemoteInputEventKind.MouseDown);
        Assert.Equal(7, (int)RemoteInputEventKind.MouseUp);
    }
}
