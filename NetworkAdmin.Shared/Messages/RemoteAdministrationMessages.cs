namespace NetworkAdmin.Shared.Messages;

public enum RemoteAdministrationCommandType
{
    GetAgentInfo = 1,
    Shutdown = 2,
    Restart = 3,
    WindowsLock = 4,
    WindowsUnlock = 5,
    ManagedWorkstationLock = 6,
    ManagedWorkstationUnlock = 7
}

public enum RemoteAdministrationCommandStatus
{
    Accepted = 1,
    Rejected = 2,
    Completed = 3,
    Failed = 4,
    Timeout = 5,
    Preparing = 6
}

public sealed class RemoteAdministrationCommandMessage
{
    public RemoteAdministrationCommandType Command { get; set; }
    public string AuthorizationToken { get; set; } = string.Empty;
    public int CountdownSeconds { get; set; } = RemoteAdministrationCommandPolicy.DefaultCountdownSeconds;
}

public sealed class RemoteAdministrationCancelMessage
{
    public string AuthorizationToken { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class RemoteAdministrationResultMessage
{
    public RemoteAdministrationCommandType Command { get; set; }
    public RemoteAdministrationCommandStatus Status { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; }
}
