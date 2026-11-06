using System.Net;
using System.Security.Cryptography.X509Certificates;
using NetworkAdmin.Server.Security;
using NetworkAdmin.Server.Infrastructure.Security;
using NetworkAdmin.Shared.Messages;

namespace NetworkAdmin.Server.Configuration;

internal sealed class ServerOptions
{
    public IPAddress Host { get; init; } = IPAddress.Any;
    public int Port { get; init; } = 5000;
    public string AdminToken { get; init; } = string.Empty;
    public string AdminId { get; init; } = "server-admin";
    public int ScreenPort { get; init; } = 5001;
    public TimeSpan ScreenConsentTimeout { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan RemoteSessionTimeout { get; init; } = TimeSpan.FromMinutes(30);
    public TimeSpan HeartbeatTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public int MaxClientConnections { get; init; } = 128;
    public int MaxScreenConnections { get; init; } = 64;
    public SecurityAlertPolicyOptions SecurityAlertPolicy { get; init; } = new();
    public X509Certificate2? TlsCertificate { get; init; }

    public static ServerOptions Load(string[] args)
    {
        var host = GetValue(args, "--host")
            ?? Environment.GetEnvironmentVariable("NETWORKADMIN_SERVER_HOST")
            ?? IPAddress.Any.ToString();

        var portText = GetValue(args, "--port")
            ?? Environment.GetEnvironmentVariable("NETWORKADMIN_SERVER_PORT")
            ?? "5000";

        var adminToken = GetValue(args, "--admin-token")
            ?? Environment.GetEnvironmentVariable("NETWORKADMIN_ADMIN_TOKEN")
            ?? string.Empty;

        var adminId = GetValue(args, "--admin-id")
            ?? Environment.GetEnvironmentVariable("NETWORKADMIN_ADMIN_ID")
            ?? "server-admin";

        var screenPortText = GetValue(args, "--screen-port")
            ?? Environment.GetEnvironmentVariable("NETWORKADMIN_SCREEN_PORT")
            ?? "5001";

        if (!IPAddress.TryParse(host, out var address))
        {
            Console.WriteLine($"Warning: invalid server host '{host}'. Listening on {IPAddress.Any} instead.");
            address = IPAddress.Any;
        }

        if (!int.TryParse(portText, out var port) || port <= 0 || port > 65535)
        {
            port = 5000;
        }

        if (!int.TryParse(screenPortText, out var screenPort) || screenPort <= 0 || screenPort > 65535 || screenPort == port)
            screenPort = port == 5001 ? 5002 : 5001;

        var sessionTimeoutText = GetValue(args, "--remote-session-timeout-seconds")
            ?? Environment.GetEnvironmentVariable("NETWORKADMIN_REMOTE_SESSION_TIMEOUT_SECONDS")
            ?? "1800";
        if (!int.TryParse(sessionTimeoutText, out var sessionTimeoutSeconds)) sessionTimeoutSeconds = 1800;
        sessionTimeoutSeconds = Math.Clamp(sessionTimeoutSeconds, 60, 24 * 60 * 60);

        var heartbeatText = GetValue(args, "--heartbeat-timeout-seconds")
            ?? Environment.GetEnvironmentVariable("NETWORKADMIN_HEARTBEAT_TIMEOUT_SECONDS")
            ?? "30";
        if (!int.TryParse(heartbeatText, out var heartbeatSeconds)) heartbeatSeconds = 30;
        heartbeatSeconds = Math.Clamp(heartbeatSeconds, 10, 300);

        var maxClientConnections = ParseInt(args, "--max-client-connections", "NETWORKADMIN_MAX_CLIENT_CONNECTIONS", 128, 1, 2_048);
        var maxScreenConnections = ParseInt(args, "--max-screen-connections", "NETWORKADMIN_MAX_SCREEN_CONNECTIONS", 64, 1, 512);

        var autoDisconnect = bool.TryParse(
            GetValue(args, "--security-auto-disconnect") ??
            Environment.GetEnvironmentVariable("NETWORKADMIN_SECURITY_AUTO_DISCONNECT"), out var parsedAutoDisconnect)
            && parsedAutoDisconnect;
        var autoDismissSeconds = ParseInt(args, "--security-alert-auto-dismiss-seconds", "NETWORKADMIN_SECURITY_ALERT_AUTO_DISMISS_SECONDS", 300, 0, 86_400);
        var dedupeSeconds = ParseInt(args, "--security-alert-dedupe-seconds", "NETWORKADMIN_SECURITY_ALERT_DEDUPE_SECONDS", 60, 1, 86_400);
        var rateLimit = ParseInt(args, "--security-alert-rate-limit", "NETWORKADMIN_SECURITY_ALERT_RATE_LIMIT", 30, 1, 10_000);

        return new ServerOptions
        {
            Host = address,
            Port = port,
            AdminToken = adminToken,
            AdminId = adminId,
            ScreenPort = screenPort,
            RemoteSessionTimeout = TimeSpan.FromSeconds(sessionTimeoutSeconds),
            HeartbeatTimeout = TimeSpan.FromSeconds(heartbeatSeconds),
            MaxClientConnections = maxClientConnections,
            MaxScreenConnections = maxScreenConnections,
            SecurityAlertPolicy = new SecurityAlertPolicyOptions
            {
                AutoDisconnectEnabled = autoDisconnect,
                AutoDismissAfter = TimeSpan.FromSeconds(autoDismissSeconds),
                DedupeWindow = TimeSpan.FromSeconds(dedupeSeconds),
                MaxAlertsPerWindow = rateLimit
            },
            TlsCertificate = ServerTlsCertificateProvider.LoadOrCreate()
        };
    }

    private static int ParseInt(string[] args, string argument, string environmentVariable,
        int fallback, int minimum, int maximum)
    {
        var value = GetValue(args, argument) ?? Environment.GetEnvironmentVariable(environmentVariable);
        return int.TryParse(value, out var parsed) ? Math.Clamp(parsed, minimum, maximum) : fallback;
    }

    private static string? GetValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
