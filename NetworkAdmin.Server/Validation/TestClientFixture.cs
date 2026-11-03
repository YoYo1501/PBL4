using NetworkAdmin.Client.Infrastructure.Configuration;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using NetworkAdmin.Client;
using NetworkAdmin.Server;

internal sealed class TestClientFixture
{
    public ServerOptions ServerOptions(IPAddress host, int port, string adminToken = "", TimeSpan? screenConsentTimeout = null) => new()
    {
        Host = host, Port = port, ScreenPort = ReservePort(),
        AdminToken = adminToken,
        ScreenConsentTimeout = screenConsentTimeout ?? TimeSpan.FromSeconds(60)
    };

    public ClientOptions ClientOptions(string host, int port) => new()
    {
        Host = host,
        Port = port,
        TelemetryInterval = TimeSpan.FromMilliseconds(200)
    };

    public static Task<NetworkStream> ConnectTcpAsync(TcpClient client, string host, int port,
        CancellationToken cancellationToken = default) => client.ConnectAndGetStreamAsync(host, port, cancellationToken);

    public static string GetRuntimeClientId()
    {
        var rawId = $"{Environment.MachineName}|{Environment.UserDomainName}|{Environment.UserName}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawId)))[..32].ToLowerInvariant();
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

internal static class TcpClientTestExtensions
{
    public static async Task<NetworkStream> ConnectAndGetStreamAsync(this TcpClient client, string host, int port,
        CancellationToken cancellationToken)
    {
        await client.ConnectAsync(host, port, cancellationToken);
        return client.GetStream();
    }
}
