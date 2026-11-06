using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using NetworkAdmin.Client.Application.Interfaces;
using NetworkAdmin.Client.Infrastructure.Configuration;
using NetworkAdmin.Client.Infrastructure.Networking;
using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Protocol;

namespace NetworkAdmin.Client.Infrastructure.ScreenSharing;

/// <summary>Consent, capture, and TLS screen transport for one Client connection.</summary>
internal sealed class ClientScreenShareModule : IClientScreenShareModule
{
    private readonly ClientOptions _options;
    private readonly ServerConnection _control;
    private readonly IScreenShareConsent _consentUi;
    private readonly ScreenCaptureService _capture;
    private readonly object _sync = new();
    private readonly ConcurrentDictionary<Task, byte> _tasks = new();
    private string? _pendingRequestId;
    private string? _approvedRequestId;
    private string? _activeRequestId;
    private CancellationTokenSource? _consentCancellation;
    private CancellationTokenSource? _activeShare;
    private int _disposed;

    public ClientScreenShareModule(ClientOptions options, ServerConnection control, IScreenShareConsent consentUi)
    {
        _options = options;
        _control = control;
        _consentUi = consentUi;
        _capture = new ScreenCaptureService(options.ScreenMaxWidth, options.ScreenMaxHeight, options.ScreenJpegQuality);
    }

    public Task HandleAsync(MessageEnvelope envelope, CancellationToken sessionToken)
    {
        if (Volatile.Read(ref _disposed) != 0) return Task.CompletedTask;
        switch (envelope.Type)
        {
            case MessageType.ScreenShareRequest:
                Track(() => HandleRequestAsync(envelope, sessionToken));
                break;
            case MessageType.ScreenShareStart:
                Track(() => HandleStartAsync(envelope, sessionToken));
                break;
            case MessageType.ScreenShareStop:
                HandleStop(envelope);
                break;
        }
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        lock (_sync)
        {
            _consentCancellation?.Cancel();
            _activeShare?.Cancel();
            _approvedRequestId = null;
        }
        while (!_tasks.IsEmpty)
        {
            var tasks = _tasks.Keys.ToArray();
            try { await Task.WhenAll(tasks).ConfigureAwait(false); }
            catch (Exception ex) { Console.WriteLine($"Screen share cleanup: {ex.GetBaseException().Message}"); }
        }
        lock (_sync)
        {
            _consentCancellation?.Dispose();
            _consentCancellation = null;
            _activeShare?.Dispose();
            _activeShare = null;
        }
    }

    private async Task HandleRequestAsync(MessageEnvelope envelope, CancellationToken sessionToken)
    {
        ScreenShareRequestMessage? request;
        try { request = JsonLineProtocol.GetPayload<ScreenShareRequestMessage>(envelope); }
        catch (System.Text.Json.JsonException) { request = null; }
        if (string.IsNullOrWhiteSpace(envelope.RequestId) || request is null ||
            request.ClientId != _control.ClientId || request.SessionId != _control.SessionId)
        {
            await SendConsentAsync(envelope.RequestId, false, "Invalid screen view request.", sessionToken).ConfigureAwait(false);
            return;
        }

        CancellationTokenSource? consentCancellation = null;
        lock (_sync)
        {
            if (_pendingRequestId is not null || _approvedRequestId is not null || _activeShare is not null)
            {
                consentCancellation = null;
            }
            else
            {
                _pendingRequestId = envelope.RequestId;
                consentCancellation = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
                consentCancellation.CancelAfter(TimeSpan.FromSeconds(60));
                _consentCancellation = consentCancellation;
            }
        }
        if (consentCancellation is null)
        {
            await SendConsentAsync(envelope.RequestId, false, "Another screen view is pending or active.", sessionToken).ConfigureAwait(false);
            return;
        }

        var accepted = false;
        try { accepted = await _consentUi.RequestConsentAsync(_options.Host, consentCancellation.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Console.WriteLine($"Screen consent UI failed: {ex.Message}"); }

        lock (_sync)
        {
            if (_pendingRequestId == envelope.RequestId && accepted && !sessionToken.IsCancellationRequested)
                _approvedRequestId = envelope.RequestId;
            else
                accepted = false;
            if (_pendingRequestId == envelope.RequestId) _pendingRequestId = null;
            if (ReferenceEquals(_consentCancellation, consentCancellation)) _consentCancellation = null;
        }
        consentCancellation.Dispose();
        try
        {
            await SendConsentAsync(envelope.RequestId, accepted,
                accepted ? "User accepted screen viewing." : "User declined or consent timed out.", sessionToken).ConfigureAwait(false);
        }
        catch (Exception ex) { Console.WriteLine($"Unable to return screen consent: {ex.Message}"); }
    }

    private async Task HandleStartAsync(MessageEnvelope envelope, CancellationToken sessionToken)
    {
        ScreenShareStartMessage? start;
        try { start = JsonLineProtocol.GetPayload<ScreenShareStartMessage>(envelope); }
        catch (System.Text.Json.JsonException) { return; }
        if (start is null || start.ClientId != _control.ClientId || start.SessionId != _control.SessionId ||
            start.ScreenPort is <= 0 or > 65535 || !IsValidCapability(start.Capability)) return;

        CancellationTokenSource share;
        lock (_sync)
        {
            if (_approvedRequestId != envelope.RequestId || _activeShare is not null) return;
            share = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
            _activeShare = share;
            _activeRequestId = envelope.RequestId;
            _approvedRequestId = null;
        }

        try
        {
            await StreamScreenAsync(envelope.RequestId, start, share).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (share.IsCancellationRequested) { }
        catch (Exception ex) { Console.WriteLine($"Screen transmission ended: {ex.GetBaseException().Message}"); }
        finally
        {
            _consentUi.SetSharing(false, static () => { });
            lock (_sync)
            {
                if (ReferenceEquals(_activeShare, share)) _activeShare = null;
                if (_activeRequestId == envelope.RequestId) _activeRequestId = null;
            }
            share.Dispose();
            if (!sessionToken.IsCancellationRequested)
            {
                try
                {
                    await _control.SendAsync(MessageType.ScreenShareStop, envelope.RequestId,
                        new ScreenShareStopMessage { ClientId = start.ClientId, SessionId = start.SessionId,
                            Reason = "Client stopped sharing or the screen channel closed." }, sessionToken).ConfigureAwait(false);
                }
                catch (Exception ex) { Console.WriteLine($"Unable to report screen share stop: {ex.Message}"); }
            }
        }
    }

    private void HandleStop(MessageEnvelope envelope)
    {
        ScreenShareStopMessage? stop;
        try { stop = JsonLineProtocol.GetPayload<ScreenShareStopMessage>(envelope); }
        catch (System.Text.Json.JsonException) { return; }
        if (stop is null || stop.ClientId != _control.ClientId || stop.SessionId != _control.SessionId) return;
        lock (_sync)
        {
            if (_pendingRequestId == envelope.RequestId) _consentCancellation?.Cancel();
            if (_approvedRequestId == envelope.RequestId) _approvedRequestId = null;
            if (_activeRequestId == envelope.RequestId) _activeShare?.Cancel();
        }
    }

    private async Task StreamScreenAsync(string requestId, ScreenShareStartMessage start, CancellationTokenSource share)
    {
        var token = share.Token;
        using var client = new TcpClient();
        await client.ConnectAsync(_options.Host, start.ScreenPort, token).ConfigureAwait(false);
        await using var network = client.GetStream();
        using var tls = _control.CreatePinnedTlsStream(network);
        await _control.AuthenticateScreenTlsAsync(tls, _options.Host, token).ConfigureAwait(false);
        await JsonLineProtocol.SendAsync(tls, MessageType.Hello, requestId,
            new ScreenChannelHelloMessage
            {
                ClientId = start.ClientId,
                SessionId = start.SessionId,
                RequestId = requestId,
                Capability = start.Capability
            }, token).ConfigureAwait(false);
        var ready = await JsonLineProtocol.ReceiveAsync(tls, maxFrameBytes: 8 * 1024, cancellationToken: token)
            .ConfigureAwait(false);
        if (ready?.Type != MessageType.ScreenChannelReady || ready.RequestId != requestId)
            throw new InvalidDataException("Server rejected the one-time screen capability.");

        _consentUi.SetSharing(true, () => CancelShare(share));
        await CaptureAndSendAsync(tls, token).ConfigureAwait(false);
    }

    private async Task CaptureAndSendAsync(Stream stream, CancellationToken token)
    {
        var frameInterval = TimeSpan.FromSeconds(1d / _options.ScreenMaxFps);
        Bitmap? previous = null;
        long sequence = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var started = Stopwatch.GetTimestamp();
                var current = await Task.Run(() => _capture.Capture(token), token).ConfigureAwait(false);
                try
                {
                    var packet = await Task.Run(() => _capture.CreatePacket(current, previous, sequence, token), token)
                        .ConfigureAwait(false);
                    if (packet is { } frame)
                    {
                        await ScreenFrameProtocol.WriteFrameAsync(stream, frame, token).ConfigureAwait(false);
                        sequence++;
                    }
                    previous?.Dispose();
                    previous = current;
                    current = null!;
                }
                finally { current?.Dispose(); }

                var remaining = frameInterval - Stopwatch.GetElapsedTime(started);
                if (remaining > TimeSpan.Zero) await Task.Delay(remaining, token).ConfigureAwait(false);
            }
        }
        finally { previous?.Dispose(); }
    }

    private async Task SendConsentAsync(string requestId, bool accepted, string message, CancellationToken token) =>
        await _control.SendAsync(MessageType.ScreenShareConsent, requestId,
            new ScreenShareConsentMessage { ClientId = _control.ClientId, SessionId = _control.SessionId ?? string.Empty,
                Accepted = accepted, Message = message }, token).ConfigureAwait(false);

    private static bool IsValidCapability(string capability)
    {
        try { return Convert.FromBase64String(capability).Length == 32; }
        catch (FormatException) { return false; }
    }

    private static void CancelShare(CancellationTokenSource share)
    {
        try { share.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private void Track(Func<Task> work)
    {
        var task = Task.Run(work, CancellationToken.None);
        _tasks.TryAdd(task, 0);
        _ = task.ContinueWith(completed =>
        {
            _tasks.TryRemove(completed, out _);
            if (completed.IsFaulted && completed.Exception is { } error)
                Console.WriteLine($"Screen share task failed: {error.GetBaseException().Message}");
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
