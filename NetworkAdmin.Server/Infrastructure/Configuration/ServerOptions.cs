using System.Net;

namespace NetworkAdmin.Server.Configuration;

internal sealed class ServerOptions
{
    public IPAddress Host { get; init; } = IPAddress.Any;
    public int Port { get; init; } = 5000;
    public string AdminToken { get; init; } = string.Empty;
    public int ScreenPort { get; init; } = 5001;
    public TimeSpan ScreenConsentTimeout { get; init; } = TimeSpan.FromSeconds(60);

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

        return new ServerOptions
        {
            Host = address,
            Port = port,
            AdminToken = adminToken,
            ScreenPort = screenPort
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
