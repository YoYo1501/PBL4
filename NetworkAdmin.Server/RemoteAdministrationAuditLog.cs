using NetworkAdmin.Shared.Messages;

namespace NetworkAdmin.Server;

internal sealed class RemoteAdministrationAuditLog
{
    private readonly object _sync = new();
    private readonly string _path;

    public RemoteAdministrationAuditLog(string? path = null)
    {
        _path = path ?? Path.Combine(AppContext.BaseDirectory, "Logs", "remote-administration.log");
    }

    public void Write(
        string clientId,
        string sessionId,
        RemoteAdministrationCommandType command,
        RemoteAdministrationCommandStatus status,
        string detail)
    {
        var safeDetail = detail.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        var line = $"{DateTime.UtcNow:O}\tclient={clientId}\tsession={sessionId}\tcommand={command}\tstatus={status}\tdetail={safeDetail}{Environment.NewLine}";
        try
        {
            lock (_sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? AppContext.BaseDirectory);
                File.AppendAllText(_path, line);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Remote administration audit failed: {ex.Message}");
        }
    }
}
