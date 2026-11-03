namespace NetworkAdmin.Client.Infrastructure.Configuration;

internal sealed class ClientOptions
{
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 5000;
    public TimeSpan TelemetryInterval { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan NetworkInfoInterval { get; init; } = TimeSpan.FromSeconds(15);
    public string AdminToken { get; init; } = string.Empty;
    public int ScreenMaxFps { get; init; } = 8;
    public int ScreenJpegQuality { get; init; } = 58;
    public int ScreenMaxWidth { get; init; } = 1600;
    public int ScreenMaxHeight { get; init; } = 1000;

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

        var networkInfoIntervalText = GetValue(args, "--network-info-interval-seconds")
            ?? Environment.GetEnvironmentVariable("NETWORKADMIN_NETWORK_INFO_INTERVAL_SECONDS");
        var networkInfoInterval = int.TryParse(networkInfoIntervalText, out var networkInfoIntervalSeconds)
            && networkInfoIntervalSeconds > 0
            ? TimeSpan.FromSeconds(networkInfoIntervalSeconds)
            : TimeSpan.FromSeconds(15);

        var adminToken = GetValue(args, "--admin-token")
            ?? Environment.GetEnvironmentVariable("NETWORKADMIN_ADMIN_TOKEN")
            ?? string.Empty;

        var screenMaxFps = GetBoundedInt(args, "--screen-fps", "NETWORKADMIN_SCREEN_FPS", 8, 1, 20);
        var screenJpegQuality = GetBoundedInt(args, "--screen-jpeg-quality", "NETWORKADMIN_SCREEN_JPEG_QUALITY", 58, 30, 85);
        var screenMaxWidth = GetBoundedInt(args, "--screen-max-width", "NETWORKADMIN_SCREEN_MAX_WIDTH", 1600, 640, 2560);
        var screenMaxHeight = GetBoundedInt(args, "--screen-max-height", "NETWORKADMIN_SCREEN_MAX_HEIGHT", 1000, 480, 1440);

        return new ClientOptions
        {
            Host = host,
            Port = port,
            TelemetryInterval = interval,
            NetworkInfoInterval = networkInfoInterval,
            AdminToken = adminToken,
            ScreenMaxFps = screenMaxFps,
            ScreenJpegQuality = screenJpegQuality,
            ScreenMaxWidth = screenMaxWidth,
            ScreenMaxHeight = screenMaxHeight
        };
    }

    private static int GetBoundedInt(string[] args, string argument, string environmentVariable,
        int fallback, int minimum, int maximum)
    {
        var text = GetValue(args, argument) ?? Environment.GetEnvironmentVariable(environmentVariable);
        return int.TryParse(text, out var value) ? Math.Clamp(value, minimum, maximum) : fallback;
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
