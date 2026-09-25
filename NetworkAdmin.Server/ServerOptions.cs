using System.Net;

namespace NetworkAdmin.Server;

internal sealed class ServerOptions
{
    public IPAddress Host { get; init; } = IPAddress.Any;
    public int Port { get; init; } = 5000;

    public static ServerOptions Load(string[] args)
    {
        var host = GetValue(args, "--host")
            ?? Environment.GetEnvironmentVariable("NETWORKADMIN_SERVER_HOST")
            ?? "127.0.0.1";

        var portText = GetValue(args, "--port")
            ?? Environment.GetEnvironmentVariable("NETWORKADMIN_SERVER_PORT")
            ?? "5000";

        if (!IPAddress.TryParse(host, out var address))
        {
            address = IPAddress.Loopback;
        }

        if (!int.TryParse(portText, out var port) || port <= 0 || port > 65535)
        {
            port = 5000;
        }

        return new ServerOptions
        {
            Host = address,
            Port = port
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
