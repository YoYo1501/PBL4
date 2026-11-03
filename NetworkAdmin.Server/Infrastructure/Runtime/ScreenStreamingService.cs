using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Protocol;
using NetworkAdmin.Server.Application.Interfaces;

namespace NetworkAdmin.Server.Runtime;

internal sealed class ScreenStreamingService : IAsyncDisposable
{
    private const int MaxHandshakeBytes = 8 * 1024;
    private readonly ServerOptions _options;
    private readonly ClientManager _clients;
    private readonly TcpListener _listener;
    private readonly ConcurrentDictionary<string, PendingScreenShare> _requests = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PendingScreenShare> _clientSlots = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<TcpClient, byte> _connections = new();
    private readonly ConcurrentDictionary<TcpClient, Task> _handlerTasks = new();
    private CancellationTokenSource? _shutdown;
    private Task? _acceptTask;
    private int _disposed;

    public ScreenStreamingService(ServerOptions options, ClientManager clients)
    {
        _options = options;
        _clients = clients;
        _listener = new TcpListener(options.Host, options.ScreenPort);
    }

    public void Start()
    {
        if (_acceptTask is not null) return;
        _listener.Start();
        _shutdown = new CancellationTokenSource();
        _acceptTask = Task.Run(() => AcceptLoopAsync(_shutdown.Token));
    }

    public bool HasSession(string clientId) => _clientSlots.ContainsKey(clientId);

    public async Task<ScreenViewerSession> RequestAsync(string clientId, string sessionId, CancellationToken token,
        Action? onConnecting = null)
    {
        if (!_clients.TryGet(sessionId, out var client) || client is null || !client.IsReady ||
            !string.Equals(client.ClientId, clientId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Selected Client session is no longer connected.");

        var pending = new PendingScreenShare(client);
        if (!_clientSlots.TryAdd(clientId, pending))
            throw new InvalidOperationException("A screen share is already pending or active for this Client.");
        _requests[pending.RequestId] = pending;

        try
        {
            await _clients.SendAsync(sessionId, MessageType.ScreenShareRequest, pending.RequestId,
                new ScreenShareRequestMessage
                {
                    ClientId = clientId,
                    SessionId = sessionId,
                }, token).ConfigureAwait(false);

            var accepted = await pending.Consent.Task.WaitAsync(_options.ScreenConsentTimeout + TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
            if (!accepted)
                throw new InvalidOperationException("The Client user declined the screen sharing request.");

            onConnecting?.Invoke();
            pending.Capability = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            await _clients.SendAsync(sessionId, MessageType.ScreenShareStart, pending.RequestId,
                new ScreenShareStartMessage
                {
                    ClientId = clientId,
                    SessionId = sessionId,
                    Capability = pending.Capability,
                    ScreenPort = _options.ScreenPort
                }, token).ConfigureAwait(false);

            var stream = await pending.Stream.Task.WaitAsync(TimeSpan.FromSeconds(20), token).ConfigureAwait(false);
            pending.SetActiveStream(stream);
            return new ScreenViewerSession(this, pending, stream);
        }
        catch
        {
            await StopPendingAsync(pending, "Screen request failed, was declined, or timed out.", notifyClient: true).ConfigureAwait(false);
            throw;
        }
    }

    public async Task HandleControlMessageAsync(ClientSessionInfo session, MessageEnvelope envelope)
    {
        if (envelope.Type == MessageType.ScreenShareConsent)
        {
            if (!_requests.TryGetValue(envelope.RequestId, out var pending) || pending.Client.SessionId != session.SessionId)
                return;
            var consent = JsonLineProtocol.GetPayload<ScreenShareConsentMessage>(envelope);
            if (consent is null || consent.ClientId != session.ClientId || consent.SessionId != session.SessionId)
                return;
            pending.Consent.TrySetResult(consent.Accepted);
            return;
        }

        if (envelope.Type == MessageType.ScreenShareStop)
        {
            var stop = JsonLineProtocol.GetPayload<ScreenShareStopMessage>(envelope);
            if (stop is null || stop.ClientId != session.ClientId || stop.SessionId != session.SessionId ||
                !_requests.TryGetValue(envelope.RequestId, out var pending) ||
                !ReferenceEquals(pending.Client, session)) return;
            await StopPendingAsync(pending, "Client stopped sharing.", notifyClient: false).ConfigureAwait(false);
        }
    }

    public async Task StopForSessionAsync(string sessionId, string reason, bool notifyClient = true)
    {
        var pending = _clientSlots.Values.FirstOrDefault(p => p.Client.SessionId == sessionId);
        if (pending is not null)
            await StopPendingAsync(pending, reason, notifyClient).ConfigureAwait(false);
    }

    internal async Task StopAsync(PendingScreenShare pending, string reason)
    {
        await StopPendingAsync(pending, reason, notifyClient: true).ConfigureAwait(false);
    }

    private async Task StopPendingAsync(PendingScreenShare pending, string reason, bool notifyClient)
    {
        if (!pending.TryStop()) return;
        _clientSlots.TryRemove(new KeyValuePair<string, PendingScreenShare>(pending.Client.ClientId, pending));
        _requests.TryRemove(new KeyValuePair<string, PendingScreenShare>(pending.RequestId, pending));
        pending.Consent.TrySetResult(false);
        pending.Stream.TrySetCanceled();
        if (notifyClient && _clients.TryGet(pending.Client.SessionId, out var current) && ReferenceEquals(current, pending.Client))
        {
            try
            {
                await _clients.SendAsync(pending.Client.SessionId, MessageType.ScreenShareStop, pending.RequestId,
                    new ScreenShareStopMessage
                    {
                        ClientId = pending.Client.ClientId,
                        SessionId = pending.Client.SessionId,
                        Reason = reason
                    }, CancellationToken.None).ConfigureAwait(false);
            }
            catch { }
        }
        pending.DisposeStream();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                _connections.TryAdd(client, 0);
                var handler = Task.Run(() => HandleScreenClientAsync(client, cancellationToken), CancellationToken.None);
                _handlerTasks[client] = handler;
                _ = handler.ContinueWith(completed => { _handlerTasks.TryRemove(client, out _); },
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (SocketException) when (cancellationToken.IsCancellationRequested) { }
        finally { _listener.Stop(); }
    }

    private async Task HandleScreenClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var transferred = false;
        try
        {
            using (client)
            await using (var network = client.GetStream())
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var hello = await JsonLineProtocol.ReceiveAsync(network, MaxHandshakeBytes, timeout.Token).ConfigureAwait(false);
                if (hello?.Type != MessageType.Hello || !_requests.TryGetValue(hello.RequestId, out var pending) ||
                    !pending.Consent.Task.IsCompletedSuccessfully || !pending.Consent.Task.Result)
                    return;

                var claim = JsonLineProtocol.GetPayload<ScreenChannelHelloMessage>(hello);
                if (claim is null || claim.RequestId != pending.RequestId || claim.SessionId != pending.Client.SessionId ||
                    claim.ClientId != pending.Client.ClientId || !MatchesCapability(claim.Capability, pending.Capability) ||
                    !_clients.TryGet(pending.Client.SessionId, out var current) || !ReferenceEquals(current, pending.Client) ||
                    !SamePeerAddress(client, pending.Client.RemoteAddress))
                    return;

                if (!pending.Stream.TrySetResult(network)) return;
                await JsonLineProtocol.SendAsync(network, MessageType.ScreenChannelReady, pending.RequestId,
                    new { Accepted = true }, timeout.Token).ConfigureAwait(false);
                pending.SetActiveStream(network);
                transferred = true;
                // The Viewer owns the stream and reads frames. The accept handler must not read concurrently.
                await pending.StreamClosed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { Console.WriteLine($"Rejected screen channel: {ex.Message}"); }
        finally
        {
            _connections.TryRemove(client, out _);
            if (!transferred) client.Dispose();
        }
    }

    private static bool SamePeerAddress(TcpClient client, string remoteAddress)
    {
        var actual = (client.Client.RemoteEndPoint as IPEndPoint)?.Address;
        return actual is not null && string.Equals(actual.ToString(), remoteAddress, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesCapability(string provided, string expected)
    {
        try
        {
            var actual = Convert.FromBase64String(provided);
            var secret = Convert.FromBase64String(expected);
            return actual.Length == secret.Length && CryptographicOperations.FixedTimeEquals(actual, secret);
        }
        catch (FormatException) { return false; }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_shutdown is not null) await _shutdown.CancelAsync().ConfigureAwait(false);
        try { _listener.Stop(); } catch { }
        foreach (var connection in _connections.Keys) connection.Dispose();
        foreach (var pending in _clientSlots.Values.ToArray())
            await StopPendingAsync(pending, "Server is shutting down.", notifyClient: false).ConfigureAwait(false);
        var handlers = _handlerTasks.Values.ToArray();
        if (handlers.Length > 0)
        {
            try { await Task.WhenAll(handlers).ConfigureAwait(false); }
            catch { }
        }
        if (_acceptTask is not null)
        {
            try { await _acceptTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _shutdown?.Dispose();
    }

    internal sealed class PendingScreenShare(ClientSessionInfo client)
    {
        public ClientSessionInfo Client { get; } = client;
        public string Capability { get; set; } = string.Empty;
        public string RequestId { get; } = Guid.NewGuid().ToString("N");
        public TaskCompletionSource<bool> Consent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<NetworkStream> Stream { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StreamClosed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private NetworkStream? _activeStream;
        private int _stopped;
        public bool TryStop() => Interlocked.Exchange(ref _stopped, 1) == 0;
        public void SetActiveStream(NetworkStream stream) => _activeStream = stream;
        public void DisposeStream()
        {
            try { _activeStream?.Dispose(); } catch { }
            StreamClosed.TrySetResult();
        }
    }
}

internal sealed class ScreenViewerSession(ScreenStreamingService service,
    ScreenStreamingService.PendingScreenShare pending, NetworkStream stream) : IScreenViewerSession
{
    private int _stopped;

    public async Task<(long Sequence, byte[] Jpeg)?> ReadFrameAsync(CancellationToken cancellationToken)
    {
        try { return await ScreenFrameProtocol.ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false); }
        catch (InvalidDataException) { throw; }
        catch (EndOfStreamException) { throw; }
        catch (IOException) { return null; }
        catch (SocketException) { return null; }
        catch (ObjectDisposedException) { return null; }
    }

    public Task StopAsync(string reason = "Viewer closed.") =>
        Interlocked.Exchange(ref _stopped, 1) == 0 ? service.StopAsync(pending, reason) : Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        pending.StreamClosed.TrySetResult();
    }
}
