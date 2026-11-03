namespace NetworkAdmin.Shared.Messages;

public sealed class ScreenShareRequestMessage
{
    public string ClientId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
}

public sealed class ScreenShareStartMessage
{
    public string ClientId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string Capability { get; set; } = string.Empty;
    public int ScreenPort { get; set; }
}

public sealed class ScreenShareConsentMessage
{
    public string ClientId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public bool Accepted { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class ScreenShareStopMessage
{
    public string ClientId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class ScreenChannelHelloMessage
{
    public string ClientId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public string Capability { get; set; } = string.Empty;
}

public sealed class RemoteControlRequestMessage
{
    public string ClientId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string ScreenRequestId { get; set; } = string.Empty;
}

public sealed class RemoteControlConsentMessage
{
    public string ClientId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string ScreenRequestId { get; set; } = string.Empty;
    public bool Accepted { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class RemoteControlStopMessage
{
    public string ClientId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string ScreenRequestId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public enum RemoteInputEventKind { MouseMove = 1, MouseClick = 2, MouseWheel = 3, KeyDown = 4, KeyUp = 5 }
public enum RemoteMouseButton { Left = 1, Right = 2, Middle = 3 }

// Coordinates are normalized to 0..10,000 and bound to the active consented screen/control requests.
public sealed class RemoteInputEventMessage
{
    public string ClientId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string ScreenRequestId { get; set; } = string.Empty;
    public string ControlRequestId { get; set; } = string.Empty;
    public RemoteInputEventKind Kind { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public RemoteMouseButton Button { get; set; }
    public int WheelDelta { get; set; }
    public int VirtualKeyCode { get; set; }
}
