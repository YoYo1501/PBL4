namespace NetworkAdmin.Shared.Messages;

public sealed class PingRequestMessage
{
    public string Target { get; set; } = string.Empty;
}
