using NetworkAdmin.Shared.Messages;

namespace NetworkAdmin.Server.Audit;

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

    public void WriteRemoteSession(
        string clientId,
        string sessionId,
        string adminId,
        RemoteSessionState state,
        string detail)
    {
        var safeAdminId = Sanitize(adminId);
        var safeDetail = Sanitize(detail);
        var line = $"{DateTime.UtcNow:O}\tclient={Sanitize(clientId)}\tsession={Sanitize(sessionId)}\tadmin={safeAdminId}\tevent=remote-session\tstate={state}\tdetail={safeDetail}{Environment.NewLine}";
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
            Console.WriteLine($"Remote session audit failed: {ex.Message}");
        }
    }

    public void WriteSecurityAlert(SecurityAlertMessage alert, string outcome)
    {
        var line = $"{DateTime.UtcNow:O}\tclient={Sanitize(alert.ClientId)}\tsession={Sanitize(alert.SessionId)}" +
                   $"\tevent=security-alert\talert={Sanitize(alert.AlertId)}\teventId={Sanitize(alert.EventId)}" +
                   $"\tseverity={alert.Severity}\tscore={alert.AnomalyScore.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                   $"\tprocess={Sanitize(alert.ProcessName)}\treason={Sanitize(alert.DetectionReason)}\toutcome={Sanitize(outcome)}{Environment.NewLine}";
        try
        {
            lock (_sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? AppContext.BaseDirectory);
                File.AppendAllText(_path, line);
            }
        }
        catch (Exception ex) { Console.WriteLine($"Security alert audit failed: {ex.Message}"); }
    }

    public void WriteSecurityPolicyChange(string adminId, string policy, bool enabled)
    {
        var line = $"{DateTime.UtcNow:O}\tadmin={Sanitize(adminId)}\tevent=security-policy\tpolicy={Sanitize(policy)}\tenabled={enabled}{Environment.NewLine}";
        try
        {
            lock (_sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? AppContext.BaseDirectory);
                File.AppendAllText(_path, line);
            }
        }
        catch (Exception ex) { Console.WriteLine($"Security policy audit failed: {ex.Message}"); }
    }

    private static string Sanitize(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
}
