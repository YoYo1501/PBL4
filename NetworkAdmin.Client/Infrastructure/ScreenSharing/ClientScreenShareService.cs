using NetworkAdmin.Client.Infrastructure.Configuration;
using System.Net.Sockets;
using System.IO;
using System.Diagnostics;
using NetworkAdmin.Client.Application.Interfaces;
using NetworkAdmin.Client.Infrastructure.Networking;
using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Protocol;

namespace NetworkAdmin.Client.Infrastructure.ScreenSharing;

internal sealed class ClientScreenShareService
{
    private readonly ClientOptions _options;
    private readonly ServerConnection _control;
    private readonly IScreenShareConsent _ui;
    private readonly ScreenCaptureService _capture;
    private readonly IRemoteInputService _remoteInput;
    private readonly object _sync = new();
    private ApprovedRequest? _approved;
    private CancellationTokenSource? _activeShare;
    private CancellationTokenSource? _consentRequest;
    private CancellationTokenSource? _controlConsentRequest;
    private string? _activeScreenRequestId;
    private string? _controlRequestId;
    private bool _controlActive;
    private long _lastMouseMoveTimestamp;

    public ClientScreenShareService(ClientOptions options, ServerConnection control, IScreenShareConsent ui,
        IRemoteInputService? remoteInput = null)
    {
        _options = options;
        _control = control;
        _ui = ui;
        _capture = new ScreenCaptureService(options.ScreenMaxWidth, options.ScreenMaxHeight, options.ScreenJpegQuality);
        _remoteInput = remoteInput ?? new WindowsRemoteInputService();
    }

    public async Task HandleRequestAsync(MessageEnvelope envelope, CancellationToken sessionToken)
    {
        var request = JsonLineProtocol.GetPayload<ScreenShareRequestMessage>(envelope);
        if (string.IsNullOrWhiteSpace(envelope.RequestId) || request is null ||
            request.ClientId != _control.ClientId || request.SessionId != _control.SessionId)
        {
            await SendConsentAsync(envelope.RequestId, false, "Screen request is invalid or belongs to another session.", sessionToken)
                .ConfigureAwait(false);
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var busy = false;
        lock (_sync)
        {
            if (_approved is not null || _activeShare is not null)
                busy = true;
            else
            {
                _approved = new ApprovedRequest(envelope.RequestId, request.ClientId, request.SessionId);
                _consentRequest = timeout;
            }
        }
        if (busy)
        {
            await SendConsentAsync(envelope.RequestId, false, "A screen share is already pending or active.", sessionToken)
                .ConfigureAwait(false);
            return;
        }

        try
        {
            bool accepted;
            try { accepted = await _ui.RequestConsentAsync(_options.Host, timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { accepted = false; }

            if (!accepted || sessionToken.IsCancellationRequested)
            {
                ClearApproval(envelope.RequestId);
                await SendConsentAsync(envelope.RequestId, false, "User declined or consent timed out.", sessionToken)
                    .ConfigureAwait(false);
                return;
            }

            await SendConsentAsync(envelope.RequestId, true, "User accepted screen sharing.", sessionToken)
                .ConfigureAwait(false);
            // Keep the approved request reserved until Start arrives or this TCP session ends.
            lock (_sync)
                if (ReferenceEquals(_consentRequest, timeout)) _consentRequest = null;
        }
        catch (OperationCanceledException) when (sessionToken.IsCancellationRequested)
        {
            ClearApproval(envelope.RequestId);
        }
        catch (Exception ex)
        {
            ClearApproval(envelope.RequestId);
            Console.WriteLine($"Screen consent failed: {ex.Message}");
        }
    }

    public async Task HandleStartAsync(MessageEnvelope envelope, CancellationToken sessionToken)
    {
        var start = JsonLineProtocol.GetPayload<ScreenShareStartMessage>(envelope);
        if (start is null || start.ClientId != _control.ClientId || start.SessionId != _control.SessionId ||
            start.ScreenPort is <= 0 or > 65535 || !IsCapabilityValid(start.Capability))
            return;

        CancellationTokenSource share;
        lock (_sync)
        {
            if (_approved is not { } approved || approved.RequestId != envelope.RequestId ||
                approved.ClientId != start.ClientId || approved.SessionId != start.SessionId || _activeShare is not null)
                return;
            share = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
            _activeShare = share;
            _approved = null;
            _activeScreenRequestId = envelope.RequestId;
        }

        try
        {
            await StreamScreenAsync(envelope.RequestId, start, share,
                () => _ui.SetSharing(true, () => CancelShare(share))).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (share.IsCancellationRequested) { }
        catch (Exception ex) { Console.WriteLine($"Screen stream ended: {ex.Message}"); }
        finally
        {
            _ui.SetSharing(false, static () => { });
            _ui.SetControlState(false, static () => { });
            lock (_sync)
            {
                if (ReferenceEquals(_activeShare, share)) _activeShare = null;
                if (_activeScreenRequestId == envelope.RequestId) _activeScreenRequestId = null;
                _controlRequestId = null;
                _controlActive = false;
            }
            _remoteInput.ReleaseAll();
            share.Dispose();
            if (!sessionToken.IsCancellationRequested)
            {
                try
                {
                    await _control.SendAsync(MessageType.ScreenShareStop, envelope.RequestId,
                        new ScreenShareStopMessage { ClientId = start.ClientId, SessionId = start.SessionId,
                            Reason = "Client stopped or screen channel closed." }, sessionToken).ConfigureAwait(false);
                }
                catch (Exception ex) { Console.WriteLine($"Unable to report screen share stop: {ex.Message}"); }
            }
        }
    }

    public void Stop()
    {
        string? controlRequestId;
        string? screenRequestId;
        lock (_sync)
        {
            _approved = null;
            _consentRequest?.Cancel();
            _controlConsentRequest?.Cancel();
            _activeShare?.Cancel();
            controlRequestId = _controlRequestId;
            screenRequestId = _activeScreenRequestId;
            _controlRequestId = null;
            _activeScreenRequestId = null;
            _controlActive = false;
        }
        _remoteInput.ReleaseAll();
        _ui.SetControlState(false, static () => { });
        if (controlRequestId is not null && screenRequestId is not null)
            _ = SendControlStopBestEffortAsync(controlRequestId, screenRequestId, "Screen session ended.");
    }

    public async Task HandleControlRequestAsync(MessageEnvelope envelope, CancellationToken sessionToken)
    {
        var request = JsonLineProtocol.GetPayload<RemoteControlRequestMessage>(envelope);
        if (string.IsNullOrWhiteSpace(envelope.RequestId) || request is null ||
            request.ClientId != _control.ClientId || request.SessionId != _control.SessionId)
            return;
        lock (_sync)
        {
            if (_activeShare is null || _activeShare.IsCancellationRequested ||
                _activeScreenRequestId != request.ScreenRequestId || _controlRequestId is not null)
                return;
            _controlRequestId = envelope.RequestId;
            _controlConsentRequest = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
        }
        CancellationTokenSource consentCancellation;
        lock (_sync) consentCancellation = _controlConsentRequest!;
        bool accepted;
        try { accepted = await _ui.RequestControlConsentAsync(_options.Host, consentCancellation.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { accepted = false; }
        lock (_sync)
        {
            if (_controlRequestId != envelope.RequestId || _activeScreenRequestId != request.ScreenRequestId)
                accepted = false;
            if (!accepted && _controlRequestId == envelope.RequestId) _controlRequestId = null;
            _controlActive = accepted;
            if (ReferenceEquals(_controlConsentRequest, consentCancellation)) _controlConsentRequest = null;
        }
        consentCancellation.Dispose();
        await _control.SendAsync(MessageType.ControlConsent, envelope.RequestId,
            new RemoteControlConsentMessage
            {
                ClientId = request.ClientId, SessionId = request.SessionId,
                ScreenRequestId = request.ScreenRequestId, Accepted = accepted,
                Message = accepted ? "Client consented; control is simulation-only." : "Client rejected or session ended."
            }, sessionToken).ConfigureAwait(false);
        if (accepted) _ui.SetControlState(true, () => _ = StopControlFromClientAsync(envelope.RequestId, request.ScreenRequestId));
    }

    public void HandleControlStop(MessageEnvelope envelope)
    {
        var stop = JsonLineProtocol.GetPayload<RemoteControlStopMessage>(envelope);
        if (stop is null || stop.ClientId != _control.ClientId || stop.SessionId != _control.SessionId) return;
        lock (_sync)
        {
            if (_controlRequestId != envelope.RequestId || _activeScreenRequestId != stop.ScreenRequestId) return;
            _controlConsentRequest?.Cancel();
            _controlRequestId = null;
            _controlActive = false;
        }
        _remoteInput.ReleaseAll();
        _ui.SetControlState(false, static () => { });
    }

    public void HandleInputEvent(MessageEnvelope envelope)
    {
        if (!_control.IsTlsAuthenticated) return;
        var input = JsonLineProtocol.GetPayload<RemoteInputEventMessage>(envelope);
        lock (_sync)
        {
            if (!_controlActive || _controlRequestId is null || _activeScreenRequestId is null ||
                envelope.RequestId != _controlRequestId ||
                !RemoteInputEventPolicy.IsValid(input, _control.ClientId, _control.SessionId ?? string.Empty,
                    _activeScreenRequestId, _controlRequestId))
                return;
            if (input!.Kind == RemoteInputEventKind.MouseMove)
            {
                var now = Stopwatch.GetTimestamp();
                if (_lastMouseMoveTimestamp != 0 && Stopwatch.GetElapsedTime(_lastMouseMoveTimestamp, now) < TimeSpan.FromMilliseconds(16))
                    return;
                _lastMouseMoveTimestamp = now;
            }
            try { _remoteInput.Execute(input); }
            catch (Exception ex) { Console.WriteLine($"Remote input event rejected: {ex.Message}"); }
        }
    }

    private async Task StopControlFromClientAsync(string controlRequestId, string screenRequestId)
    {
        lock (_sync)
        {
            if (_controlRequestId != controlRequestId || _activeScreenRequestId != screenRequestId) return;
            _controlRequestId = null;
            _controlActive = false;
        }
        _remoteInput.ReleaseAll();
        _ui.SetControlState(false, static () => { });
        await SendControlStopBestEffortAsync(controlRequestId, screenRequestId, "Client user stopped remote control.").ConfigureAwait(false);
    }

    private Task SendControlStopAsync(string controlRequestId, string screenRequestId, string reason) =>
        _control.SendAsync(MessageType.ControlStop, controlRequestId,
            new RemoteControlStopMessage { ClientId = _control.ClientId, SessionId = _control.SessionId ?? string.Empty,
                ScreenRequestId = screenRequestId, Reason = reason }, CancellationToken.None);

    private async Task SendControlStopBestEffortAsync(string controlRequestId, string screenRequestId, string reason)
    {
        try { await SendControlStopAsync(controlRequestId, screenRequestId, reason).ConfigureAwait(false); }
        catch (Exception ex) { Console.WriteLine($"Unable to send ControlStop: {ex.Message}"); }
    }

    private static void CancelShare(CancellationTokenSource share)
    {
        try { share.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private async Task StreamScreenAsync(string requestId, ScreenShareStartMessage start,
        CancellationTokenSource share, Action onConnected)
    {
        var cancellationToken = share.Token;
        using var client = new TcpClient();
        await client.ConnectAsync(_options.Host, start.ScreenPort, cancellationToken).ConfigureAwait(false);
        await using var rawStream = client.GetStream();
        using var tlsStream = _control.CreatePinnedTlsStream(rawStream);
        await _control.AuthenticateScreenTlsAsync(tlsStream, _options.Host, cancellationToken).ConfigureAwait(false);
        Stream stream = tlsStream;
        await JsonLineProtocol.SendAsync(stream, MessageType.Hello, requestId,
            new ScreenChannelHelloMessage
            {
                ClientId = start.ClientId,
                SessionId = start.SessionId,
                RequestId = requestId,
                Capability = start.Capability
            }, cancellationToken).ConfigureAwait(false);
        var ready = await JsonLineProtocol.ReceiveAsync(stream, maxFrameBytes: 8 * 1024,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (ready?.Type != MessageType.ScreenChannelReady || ready.RequestId != requestId)
            throw new InvalidDataException("Server did not accept the one-time screen capability.");

        onConnected();
        long sequence = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1d / _options.ScreenMaxFps));
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var jpeg = await Task.Run(_capture.CaptureJpeg, cancellationToken).ConfigureAwait(false);
            await ScreenFrameProtocol.WriteFrameAsync(stream, sequence++, jpeg, cancellationToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
    }

    private Task SendConsentAsync(string requestId, bool accepted, string message, CancellationToken cancellationToken) =>
        _control.SendAsync(MessageType.ScreenShareConsent, requestId,
            new ScreenShareConsentMessage
            {
                ClientId = _control.ClientId,
                SessionId = _control.SessionId ?? string.Empty,
                Accepted = accepted,
                Message = message
            }, cancellationToken);

    private void ClearApproval(string requestId)
    {
        lock (_sync)
        {
            if (_approved?.RequestId == requestId) _approved = null;
            _consentRequest = null;
        }
    }

    private static bool IsCapabilityValid(string capability)
    {
        try { return Convert.FromBase64String(capability).Length == 32; }
        catch (FormatException) { return false; }
    }

    private sealed record ApprovedRequest(string RequestId, string ClientId, string SessionId);
}
