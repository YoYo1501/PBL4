using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using System.Text;
using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Protocol;

namespace NetworkAdmin.Client.Infrastructure.Networking;

internal sealed class ServerConnection : IAsyncDisposable
{
    private TcpClient? _client;
    private Stream? _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly string? _expectedServerFingerprint;

    public string ClientId { get; }
    public string ClientName { get; }
    public string? SessionId { get; private set; }
    public bool IsTlsAuthenticated => _expectedServerFingerprint is not null && _stream is SslStream;

    public ServerConnection() : this(null) { }

    public ServerConnection(string? expectedServerFingerprint)
    {
        _expectedServerFingerprint = expectedServerFingerprint is null ? null :
            TlsCertificatePin.Normalize(expectedServerFingerprint) ?? throw new ArgumentException("Server SHA-256 fingerprint must contain 64 hexadecimal digits.", nameof(expectedServerFingerprint));
        ClientId = LoadClientId();
        ClientName = Environment.MachineName;
    }

    public async Task<HelloAckMessage> ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        _client = new TcpClient();
        JsonLineProtocol.EnableKeepAlive(_client);
        await _client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
        var networkStream = _client.GetStream();
        if (_expectedServerFingerprint is null)
        {
            _stream = networkStream;
        }
        else
        {
            var tls = new SslStream(networkStream, leaveInnerStreamOpen: false,
                (_, certificate, _, _) => TlsCertificatePin.Matches(certificate, _expectedServerFingerprint));
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }, cancellationToken).ConfigureAwait(false);
            _stream = tls;
        }

        var requestId = Guid.NewGuid().ToString("N");
        await JsonLineProtocol.SendAsync(
            _stream,
            MessageType.Hello,
            requestId,
            new HelloMessage
            {
                ClientId = ClientId,
                ClientName = ClientName
            },
            cancellationToken).ConfigureAwait(false);

        var envelope = await JsonLineProtocol.ReceiveAsync(_stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (envelope is null)
        {
            throw new IOException("Server closed the connection before HelloAck.");
        }

        if (envelope.Type != MessageType.HelloAck || envelope.RequestId != requestId)
        {
            throw new InvalidDataException("Server returned an invalid HelloAck.");
        }

        var ack = JsonLineProtocol.GetPayload<HelloAckMessage>(envelope)
            ?? throw new InvalidDataException("HelloAck payload is missing.");

        if (!ack.Accepted)
        {
            throw new InvalidOperationException(ack.Message);
        }

        SessionId = ack.SessionId;
        return ack;
    }

    public SslStream CreatePinnedTlsStream(Stream networkStream)
    {
        if (_expectedServerFingerprint is null)
            throw new InvalidOperationException("A pinned TLS fingerprint is required for the screen channel.");
        return new SslStream(networkStream, leaveInnerStreamOpen: true,
            (_, certificate, _, _) => TlsCertificatePin.Matches(certificate, _expectedServerFingerprint));
    }

    public Task AuthenticateScreenTlsAsync(SslStream stream, string host, CancellationToken cancellationToken) =>
        stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = host,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck
        }, cancellationToken);

    public Task SendAsync<TPayload>(MessageType type, TPayload payload, CancellationToken cancellationToken)
    {
        if (_stream is null || SessionId is null)
        {
            throw new InvalidOperationException("Server connection is not established.");
        }

        return SendAsync(type, Guid.NewGuid().ToString("N"), payload, cancellationToken);
    }

    public async Task SendAsync<TPayload>(
        MessageType type,
        string requestId,
        TPayload payload,
        CancellationToken cancellationToken)
    {
        if (_stream is null || SessionId is null)
        {
            throw new InvalidOperationException("Server connection is not established.");
        }

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await JsonLineProtocol.SendAsync(
                _stream,
                type,
                requestId,
                payload,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task RunReadLoopAsync(
        Func<MessageEnvelope, CancellationToken, Task> onMessageAsync,
        CancellationToken cancellationToken)
    {
        if (_stream is null || SessionId is null)
        {
            throw new InvalidOperationException("Server connection is not established.");
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var envelope = await JsonLineProtocol.ReceiveAsync(_stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (envelope is null)
            {
                throw new IOException("Server closed the connection.");
            }

            await onMessageAsync(envelope, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task DisconnectAsync()
    {
        _stream?.Dispose();
        _client?.Dispose();
        _stream = null;
        _client = null;
        SessionId = null;
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _writeLock.Dispose();
    }

    private static string LoadClientId()
    {
        var rawId = $"{Environment.MachineName}|{Environment.UserDomainName}|{Environment.UserName}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawId));
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }
}
