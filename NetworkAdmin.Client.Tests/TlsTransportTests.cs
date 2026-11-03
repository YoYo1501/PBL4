using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NetworkAdmin.Client.Infrastructure.Networking;
using NetworkAdmin.Server;
using NetworkAdmin.Shared.Protocol;
using Xunit;

namespace NetworkAdmin.Client.Tests;

public sealed class TlsTransportTests
{
    [Fact]
    public async Task Main_connection_accepts_the_pinned_server_certificate()
    {
        using var certificate = CreateCertificate();
        var port = ReservePort();
        var options = new TestClientFixture().ServerOptions(IPAddress.Loopback, port, tlsCertificate: certificate);
        var server = new ServerRuntime(options);
        server.Start();
        await Eventually(() => server.IsRunning);
        try
        {
            await using var connection = new ServerConnection(certificate.GetCertHashString(HashAlgorithmName.SHA256));
            await connection.ConnectAsync("127.0.0.1", port, CancellationToken.None);
            Assert.NotNull(connection.SessionId);
            Assert.Contains(server.GetClients(), c => c.Connected && c.SessionId == connection.SessionId);
        }
        finally { await server.StopAsync(); }
    }

    [Fact]
    public async Task Main_connection_rejects_a_server_with_a_different_fingerprint()
    {
        using var certificate = CreateCertificate();
        var port = ReservePort();
        var options = new TestClientFixture().ServerOptions(IPAddress.Loopback, port, tlsCertificate: certificate);
        var server = new ServerRuntime(options);
        server.Start();
        await Eventually(() => server.IsRunning);
        try
        {
            await using var connection = new ServerConnection(new string('0', 64));
            var error = await Record.ExceptionAsync(() => connection.ConnectAsync("127.0.0.1", port, CancellationToken.None));
            Assert.IsAssignableFrom<AuthenticationException>(error);
            Assert.Empty(server.GetClients());
        }
        finally { await server.StopAsync(); }
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=NetworkAdminTool Test Server", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var temporary = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        const string password = "test-only-pfx-password";
        var pfx = temporary.Export(X509ContentType.Pfx, password);
        try
        {
            return X509CertificateLoader.LoadPkcs12(pfx, password,
                X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet);
        }
        finally { CryptographicOperations.ZeroMemory(pfx); }
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
