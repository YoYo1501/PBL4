using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
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

    internal async Task StopControlAsync(PendingScreenShare pending, string reason)
    {
        var controlRequestId = pending.ControlRequestId;
        if (controlRequestId is null) return;
        pending.EndControl(controlRequestId);
        if (_clients.TryGet(pending.Client.SessionId, out var current) && ReferenceEquals(current, pending.Client))
        {
            try { await _clients.SendAsync(pending.Client.SessionId, MessageType.ControlStop, controlRequestId,
                new RemoteControlStopMessage { ClientId = pending.Client.ClientId, SessionId = pending.Client.SessionId,
                    ScreenRequestId = pending.RequestId, Reason = reason }, CancellationToken.None).ConfigureAwait(false); }
            catch { }
        }
    }

    public async Task<bool> RequestControlAsync(string clientId, string sessionId, string screenRequestId, CancellationToken token)
    {
        if (_options.TlsCertificate is null)
            throw new InvalidOperationException("Remote control requires an authenticated TLS Server certificate.");
        if (!_clientSlots.TryGetValue(clientId, out var pending) || pending.Client.SessionId != sessionId ||
            pending.RequestId != screenRequestId || pending.IsStopped || !pending.Stream.Task.IsCompletedSuccessfully)
            throw new InvalidOperationException("The matching screen session is not active.");
        var controlRequestId = Guid.NewGuid().ToString("N");
        if (!pending.TryBeginControl(controlRequestId)) throw new InvalidOperationException("A control request is already pending or active.");
        try
        {
            await _clients.SendAsync(sessionId, MessageType.ControlRequest, controlRequestId,
                new RemoteControlRequestMessage { ClientId = clientId, SessionId = sessionId, ScreenRequestId = screenRequestId }, token).ConfigureAwait(false);
            return await pending.ControlConsent.Task.WaitAsync(TimeSpan.FromSeconds(60), token).ConfigureAwait(false);
        }
        catch
        {
            await StopControlAsync(pending, "Control request was canceled or timed out.").ConfigureAwait(false);
            throw;
        }
    }

    internal Task SendInputEventAsync(PendingScreenShare pending, RemoteInputEventKind kind, int x, int y,
        RemoteMouseButton button, int wheelDelta, int virtualKeyCode, CancellationToken token)
    {
        if (_options.TlsCertificate is null)
            throw new InvalidOperationException("Remote input is disabled because this Server has no TLS certificate.");
        var controlRequestId = pending.ControlRequestId;
        var input = new RemoteInputEventMessage
        {
            ClientId = pending.Client.ClientId, SessionId = pending.Client.SessionId,
            ScreenRequestId = pending.RequestId, ControlRequestId = controlRequestId ?? string.Empty,
            Kind = kind, X = x, Y = y, Button = button, WheelDelta = wheelDelta, VirtualKeyCode = virtualKeyCode
        };
        if (!pending.IsControlActive || controlRequestId is null || pending.IsStopped ||
            !_clients.TryGet(pending.Client.SessionId, out var current) || !ReferenceEquals(current, pending.Client) ||
            !RemoteInputEventPolicy.IsValid(input, pending.Client.ClientId, pending.Client.SessionId, pending.RequestId, controlRequestId))
            throw new InvalidOperationException("Control is not active for this Client session or the input event is invalid.");
        return _clients.SendAsync(pending.Client.SessionId, MessageType.ControlInputEvent, controlRequestId, input, token);
    }

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
            return;
        }

        if (envelope.Type == MessageType.ControlConsent)
        {
            var consent = JsonLineProtocol.GetPayload<RemoteControlConsentMessage>(envelope);
            if (consent is null || !_requests.TryGetValue(consent.ScreenRequestId, out var pending) ||
                !ReferenceEquals(pending.Client, session) || consent.ClientId != session.ClientId ||
                consent.SessionId != session.SessionId || pending.ControlRequestId != envelope.RequestId)
                return;
            pending.SetControlConsent(envelope.RequestId, consent.Accepted);
            return;
        }

        if (envelope.Type == MessageType.ControlStop)
        {
            var stop = JsonLineProtocol.GetPayload<RemoteControlStopMessage>(envelope);
            if (stop is null || !_requests.TryGetValue(stop.ScreenRequestId, out var pending) ||
                !ReferenceEquals(pending.Client, session) || stop.ClientId != session.ClientId ||
                stop.SessionId != session.SessionId || pending.ControlRequestId != envelope.RequestId)
                return;
            pending.EndControl(envelope.RequestId);
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
        if (pending.ControlRequestId is { } controlRequestId && notifyClient &&
            _clients.TryGet(pending.Client.SessionId, out var controlClient) && ReferenceEquals(controlClient, pending.Client))
        {
            try { await _clients.SendAsync(pending.Client.SessionId, MessageType.ControlStop, controlRequestId,
                new RemoteControlStopMessage { ClientId = pending.Client.ClientId, SessionId = pending.Client.SessionId,
                    ScreenRequestId = pending.RequestId, Reason = reason }, CancellationToken.None).ConfigureAwait(false); }
            catch { }
        }
        pending.EndControl(pending.ControlRequestId);
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
            await using (var rawNetwork = client.GetStream())
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                using var tlsNetwork = _options.TlsCertificate is null ? null : new SslStream(rawNetwork, leaveInnerStreamOpen: true);
                if (tlsNetwork is not null)
                    await tlsNetwork.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                    {
                        ServerCertificate = _options.TlsCertificate,
                        ClientCertificateRequired = false,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                        CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                    }, timeout.Token).ConfigureAwait(false);
                Stream network = (Stream?)tlsNetwork ?? rawNetwork;
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
        public TaskCompletionSource<Stream> Stream { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StreamClosed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ControlConsent { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? ControlRequestId { get; private set; }
        public bool IsControlActive { get; private set; }
        public bool IsStopped => Volatile.Read(ref _stopped) != 0;
        private Stream? _activeStream;
        private int _stopped;
        public bool TryBeginControl(string requestId)
        {
            lock (this)
            {
                if (_stopped != 0 || ControlRequestId is not null) return false;
                ControlRequestId = requestId;
                ControlConsent = new(TaskCreationOptions.RunContinuationsAsynchronously);
                return true;
            }
        }
        public void EndControl(string? requestId)
        {
            lock (this)
            {
                if (requestId is null || ControlRequestId != requestId) return;
                ControlConsent.TrySetResult(false);
                IsControlActive = false;
                ControlRequestId = null;
            }
        }
        public void SetControlConsent(string requestId, bool accepted)
        {
            lock (this)
            {
                if (ControlRequestId != requestId) return;
                IsControlActive = accepted;
                ControlConsent.TrySetResult(accepted);
            }
        }
        public bool TryStop() => Interlocked.Exchange(ref _stopped, 1) == 0;
        public void SetActiveStream(Stream stream) => _activeStream = stream;
        public void DisposeStream()
        {
            try { _activeStream?.Dispose(); } catch { }
            StreamClosed.TrySetResult();
        }
    }
}

internal sealed class ScreenViewerSession(ScreenStreamingService service,
    ScreenStreamingService.PendingScreenShare pending, Stream stream) : IScreenViewerSession
{
    public bool IsControlActive => pending.IsControlActive;
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

    public Task<bool> RequestControlAsync(CancellationToken cancellationToken) =>
        service.RequestControlAsync(pending.Client.ClientId, pending.Client.SessionId, pending.RequestId, cancellationToken);
    public Task SendInputEventAsync(RemoteInputEventKind kind, int x, int y, RemoteMouseButton button,
        int wheelDelta, int virtualKeyCode, CancellationToken cancellationToken) =>
        service.SendInputEventAsync(pending, kind, x, y, button, wheelDelta, virtualKeyCode, cancellationToken);
    public Task StopControlAsync(string reason = "Server stopped control.") =>
        service.StopControlAsync(pending, reason);

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        pending.StreamClosed.TrySetResult();
    }
}
