namespace NetworkAdmin.Shared.Messages;

public sealed class HelloMessage
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
}
