namespace NetworkAdmin.Shared.Messages;

public sealed class ScanRequestMessage
{
    public string Subnet { get; set; } = string.Empty;
    public int TimeoutMs { get; set; } = 800;
}
