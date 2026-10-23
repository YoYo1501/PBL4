using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Models;

namespace NetworkAdmin.Server.UI;

// UI-thread state. Each asynchronous operation captures its originating session's instance.
internal sealed class SessionCommandState
{
    public string? SessionId { get; set; }
    public PingResult? Ping { get; set; }
    public ScanResultMessage? Scan { get; set; }
    public DateTime? ScanAt { get; set; }
    public string PingStatus { get; set; } = "No ping completed for this session.";
    public string ScanStatus { get; set; } = "No scan completed for this session.";
    public string ScanOrigin { get; set; } = "";
    public bool PingError { get; set; }
    public bool ScanError { get; set; }
    public bool PingPending { get; set; }
    public bool ScanPending { get; set; }
    public string RemoteInfoStatus { get; set; } = "No remote command executed for this session.";
    public bool RemoteInfoError { get; set; }
    public bool RemoteInfoPending { get; set; }
    public string PowerStatus { get; set; } = "No shutdown or restart command executed for this session.";
    public bool PowerError { get; set; }
    public bool PowerPending { get; set; }
}
