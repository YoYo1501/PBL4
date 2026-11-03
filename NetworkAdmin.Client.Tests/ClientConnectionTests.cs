using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Authentication;
using System.Text;
using NetworkAdmin.Client.Infrastructure.Runtime;
using NetworkAdmin.Client.Infrastructure.Networking;
using NetworkAdmin.Client.Infrastructure.Scanning;
using NetworkAdmin.Server;
using NetworkAdmin.Shared.Messages;
using Xunit;

namespace NetworkAdmin.Client.Tests;

public sealed class ClientConnectionTests
{
    [Fact]
    public async Task Client_connects_over_tcp_and_reconnects_with_stable_identity_and_new_session()
    {
        var port = ReservePort();
        var fixture = new TestClientFixture();
        var server = new ServerRuntime(fixture.ServerOptions(IPAddress.Loopback, port));
        server.Start();
        await Eventually(() => server.IsRunning);
        try
        {
            await using var first = new ServerConnection();
            await first.ConnectAsync("127.0.0.1", port, CancellationToken.None);
            var clientId = first.ClientId;
            var firstSession = first.SessionId;
            Assert.Equal(CreateExpectedClientId(), clientId);
            Assert.Contains(server.GetClients(), client => client.Connected
                && client.ClientId == clientId && client.SessionId == firstSession);

            await first.DisconnectAsync();
            await Eventually(() => server.GetClients().All(client => !client.Connected));

            await using var second = new ServerConnection();
            await second.ConnectAsync("127.0.0.1", port, CancellationToken.None);
            Assert.Equal(clientId, second.ClientId);
            Assert.NotEqual(firstSession, second.SessionId);
        }
        finally { await server.StopAsync(); }
    }

    [Fact]
    public async Task Client_rejects_sensitive_remote_command_when_admin_token_does_not_match()
    {
        var port = ReservePort();
        var fixture = new TestClientFixture();
        var serverOptions = fixture.ServerOptions(IPAddress.Loopback, port, "wrong-demo-token");
        var server = new ServerRuntime(serverOptions);
        server.Start();
        await Eventually(() => server.IsRunning);
        await using var connection = new ServerConnection();
        using var readCancellation = new CancellationTokenSource();
        var handler = new ClientCommandHandler(new PingService(), connection, "configured-client-token");
        Task? reader = null;
        try
        {
            await connection.ConnectAsync("127.0.0.1", port, CancellationToken.None);
            reader = connection.RunReadLoopAsync(handler.HandleAsync, readCancellation.Token);
            var result = await server.SendRemoteAdministrationAsync(connection.ClientId,
                RemoteAdministrationCommandType.WindowsLock, CancellationToken.None,
                timeout: TimeSpan.FromSeconds(5));
            Assert.Equal(RemoteAdministrationCommandStatus.Rejected, result.Status);
            Assert.Contains("authorization", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await readCancellation.CancelAsync();
            await connection.DisconnectAsync();
            if (reader is not null)
            {
                try { await reader; }
                catch (Exception ex) when (ex is OperationCanceledException or IOException) { }
            }
            await handler.DrainAsync();
            await server.StopAsync();
        }
    }

    private static string CreateExpectedClientId()
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

    private static async Task Eventually(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate()) await Task.Delay(20, timeout.Token);
    }
}
