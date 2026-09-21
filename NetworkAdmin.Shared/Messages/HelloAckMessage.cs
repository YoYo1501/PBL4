namespace NetworkAdmin.Shared.Messages;

public sealed class HelloAckMessage
{
    public bool Accepted { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
