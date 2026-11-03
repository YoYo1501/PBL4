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
