namespace NetworkAdmin.Server.Application.Interfaces;

public interface IServerRuntime
{
    event Action<RemoteChatMessage>? ChatMessageReceived;
    event Action<SecurityAlertMessage>? SecurityAlertReceived;
    string Endpoint { get; }
    string SecurityFingerprint { get; }
    string Status { get; }
    bool IsRunning { get; }
    bool AutoDisconnectEnabled { get; }
    void Start();
    Task StopAsync();
    bool SetAutoDisconnectEnabled(bool enabled);
    Task<IScreenViewerSession> RequestScreenAsync(string clientId, string sessionId, CancellationToken token,
        Action? onConnecting = null);
    bool IsScreenSharing(string clientId);
    Task SendChatAsync(string clientId, string sessionId, string content, CancellationToken token);
    Task<bool> RequestControlAsync(string clientId, string sessionId, string screenRequestId, CancellationToken token);
    IReadOnlyList<ClientSnapshot> GetClients();
    Task<PingResult> PingAsync(string sessionId, string target, CancellationToken token);
    Task<ScanResultMessage> ScanAsync(string sessionId, string subnet, CancellationToken token,
        int timeoutMs = 800);
    Task<RemoteAdministrationResultMessage> GetAgentInfoAsync(string clientId, CancellationToken token,
        TimeSpan? timeout = null);
    Task<RemoteAdministrationResultMessage> PowerClientAsync(string clientId, RemoteAdministrationCommandType command,
        int countdownSeconds, CancellationToken token, Action<RemoteAdministrationResultMessage>? progress = null,
        TimeSpan? timeout = null);
    Task<RemoteAdministrationResultMessage> SendRemoteAdministrationAsync(string clientId,
        RemoteAdministrationCommandType command, CancellationToken token,
        Action<RemoteAdministrationResultMessage>? progress = null, TimeSpan? timeout = null);
    IReadOnlyList<ScanHostSnapshot> MapScanResult(ScanResultMessage result, IReadOnlyList<ClientSnapshot> clients);
}
