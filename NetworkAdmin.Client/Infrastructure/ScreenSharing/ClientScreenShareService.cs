using NetworkAdmin.Client.Infrastructure.Configuration;
using System.Net.Sockets;
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
    private readonly object _sync = new();
    private ApprovedRequest? _approved;
    private CancellationTokenSource? _activeShare;
    private CancellationTokenSource? _consentRequest;

    public ClientScreenShareService(ClientOptions options, ServerConnection control, IScreenShareConsent ui)
    {
        _options = options;
        _control = control;
        _ui = ui;
        _capture = new ScreenCaptureService(options.ScreenMaxWidth, options.ScreenMaxHeight, options.ScreenJpegQuality);
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
            lock (_sync)
            {
                if (ReferenceEquals(_activeShare, share)) _activeShare = null;
            }
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
        lock (_sync)
        {
            _approved = null;
            _consentRequest?.Cancel();
            _activeShare?.Cancel();
        }
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
        await using var stream = client.GetStream();
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
