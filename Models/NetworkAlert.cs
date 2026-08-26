namespace NetworkAdminTool.Models;

public sealed class NetworkAlert
{
    public string Severity { get; init; } = "Info";
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; } = DateTime.Now;
}
