using System.Globalization;

namespace NetworkAdmin.Client.Infrastructure.Security;

internal enum SecurityEventSourceKind
{
    Auto = 0,
    WindowsEventLog = 1,
    Ndjson = 2
}

internal sealed class SecurityEventCollectorOptions
{
    public string ClientId { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public SecurityEventSourceKind SourceKind { get; init; } = SecurityEventSourceKind.Auto;
    public string EventLogName { get; init; } = "Microsoft-Windows-Sysmon/Operational";
    public string NdjsonDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "YourAppMonitor");
    public string NdjsonPattern { get; init; } = "winlogbeat_output*.ndjson";
    public string? CheckpointFile { get; init; }
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(400);
    public int QueueCapacity { get; init; } = 256;
    public int SourceQueueCapacity { get; init; } = 256;
    public int MaxLineBytes { get; init; } = 64 * 1024;

    public static SecurityEventCollectorOptions Load(string[] args, string clientId, string sessionId)
    {
        var sourceText = GetValue(args, "--security-source")
            ?? Environment.GetEnvironmentVariable("NETWORKADMIN_SECURITY_SOURCE");
        var sourceKind = Enum.TryParse<SecurityEventSourceKind>(sourceText, true, out var parsedSource)
            ? parsedSource
            : SecurityEventSourceKind.Auto;

        var directory = GetValue(args, "--security-ndjson-directory")
            ?? Environment.GetEnvironmentVariable("NETWORKADMIN_SECURITY_NDJSON_DIRECTORY");
        var pattern = GetValue(args, "--security-ndjson-pattern")
            ?? Environment.GetEnvironmentVariable("NETWORKADMIN_SECURITY_NDJSON_PATTERN");
        var checkpoint = GetValue(args, "--security-checkpoint")
            ?? Environment.GetEnvironmentVariable("NETWORKADMIN_SECURITY_CHECKPOINT");
        var eventLog = GetValue(args, "--security-event-log")
            ?? Environment.GetEnvironmentVariable("NETWORKADMIN_SECURITY_EVENT_LOG");

        return new SecurityEventCollectorOptions
        {
            ClientId = clientId,
            SessionId = sessionId,
            SourceKind = sourceKind,
            EventLogName = string.IsNullOrWhiteSpace(eventLog)
                ? "Microsoft-Windows-Sysmon/Operational" : eventLog,
            NdjsonDirectory = string.IsNullOrWhiteSpace(directory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "YourAppMonitor")
                : directory,
            NdjsonPattern = string.IsNullOrWhiteSpace(pattern) ? "winlogbeat_output*.ndjson" : pattern,
            CheckpointFile = checkpoint,
            PollInterval = TimeSpan.FromMilliseconds(GetInt(args, "--security-poll-ms",
                "NETWORKADMIN_SECURITY_POLL_MS", 400, 50, 5_000)),
            QueueCapacity = GetInt(args, "--security-queue-capacity",
                "NETWORKADMIN_SECURITY_QUEUE_CAPACITY", 256, 1, 4_096),
            SourceQueueCapacity = GetInt(args, "--security-source-queue-capacity",
                "NETWORKADMIN_SECURITY_SOURCE_QUEUE_CAPACITY", 256, 16, 2_048),
            MaxLineBytes = GetInt(args, "--security-max-line-bytes",
                "NETWORKADMIN_SECURITY_MAX_LINE_BYTES", 64 * 1024, 1_024, 1_024 * 1_024)
        };
    }

    private static int GetInt(string[] args, string argument, string environmentVariable,
        int fallback, int minimum, int maximum)
    {
        var value = GetValue(args, argument) ?? Environment.GetEnvironmentVariable(environmentVariable);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, minimum, maximum) : fallback;
    }

    private static string? GetValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }
}
