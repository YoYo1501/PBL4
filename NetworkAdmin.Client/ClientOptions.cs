namespace NetworkAdmin.Client;

internal sealed class ClientOptions
{
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 5000;
    public TimeSpan TelemetryInterval { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan? Duration { get; init; }

    public static ClientOptions Load(string[] args)
    {
        var host = GetValue(args, "--host")
            ?? Environment.GetEnvironmentVariable("NETWORKADMIN_SERVER_HOST")
            ?? "127.0.0.1";

        var portText = GetValue(args, "--port")
            ?? Environment.GetEnvironmentVariable("NETWORKADMIN_SERVER_PORT")
            ?? "5000";

        if (!int.TryParse(portText, out var port) || port <= 0 || port > 65535)
        {
            port = 5000;
        }

        var intervalText = GetValue(args, "--telemetry-interval-seconds")
            ?? Environment.GetEnvironmentVariable("NETWORKADMIN_TELEMETRY_INTERVAL_SECONDS");
        var interval = int.TryParse(intervalText, out var intervalSeconds) && intervalSeconds > 0
            ? TimeSpan.FromSeconds(intervalSeconds)
            : TimeSpan.FromSeconds(2);

        var durationText = GetValue(args, "--duration-seconds");
        var duration = int.TryParse(durationText, out var durationSeconds) && durationSeconds > 0
            ? TimeSpan.FromSeconds(durationSeconds)
            : (TimeSpan?)null;

        return new ClientOptions
        {
            Host = host,
            Port = port,
            TelemetryInterval = interval,
            Duration = duration
        };
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
