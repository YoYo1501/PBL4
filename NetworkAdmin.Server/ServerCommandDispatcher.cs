using System.Collections.Concurrent;
using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Protocol;
using NetworkAdmin.Shared.Models;

namespace NetworkAdmin.Server;

internal sealed class ServerCommandDispatcher
{
    private readonly ClientManager _clientManager;
    private readonly RemoteAdministrationAuditLog _auditLog;
    private readonly ConcurrentDictionary<string, PendingPingRequest> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PendingScanRequest> _pendingScans = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PendingRemoteCommand> _pendingRemoteCommands = new(StringComparer.OrdinalIgnoreCase);

    public ServerCommandDispatcher(ClientManager clientManager, RemoteAdministrationAuditLog? auditLog = null)
    {
        _clientManager = clientManager;
        _auditLog = auditLog ?? new RemoteAdministrationAuditLog();
    }

    public async Task<PingResult> SendPingAsync(
        string sessionId,
        string target,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new ArgumentException("Ping target is required.", nameof(target));
        }

        var requestId = Guid.NewGuid().ToString("N");
        var pending = new PendingPingRequest(sessionId);
        if (!_pending.TryAdd(requestId, pending))
        {
            throw new InvalidOperationException("Unable to track ping request.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await _clientManager.SendAsync(
                sessionId,
                MessageType.PingRequest,
                requestId,
                new PingRequestMessage { Target = target },
                timeoutCts.Token).ConfigureAwait(false);

            return await pending.Completion.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException("Ping command timed out.");
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    public bool CompletePing(string sessionId, string requestId, PingResult result)
    {
        if (!_pending.TryGetValue(requestId, out var pending) || pending.SessionId != sessionId)
        {
            return false;
        }

        return pending.Completion.TrySetResult(result);
    }

    public async Task<ScanResultMessage> SendScanAsync(
        string sessionId,
        string subnet,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        int timeoutMs = ScanSubnet.DefaultTimeoutMs)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var pending = new PendingScanRequest(sessionId);
        if (!_pendingScans.TryAdd(requestId, pending))
        {
            throw new InvalidOperationException("Unable to track scan request.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        var effectiveTimeoutMs = ScanSubnet.ClampTimeout(timeoutMs);
        try
        {
            await _clientManager.SendAsync(
                sessionId,
                MessageType.ScanRequest,
                requestId,
                new ScanRequestMessage { Subnet = subnet, TimeoutMs = effectiveTimeoutMs },
                timeoutCts.Token).ConfigureAwait(false);

            return await pending.Completion.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException("Scan command timed out.");
        }
        finally
        {
            _pendingScans.TryRemove(requestId, out _);
        }
    }

    public bool CompleteScan(string sessionId, string requestId, ScanResultMessage result)
    {
        if (!_pendingScans.TryGetValue(requestId, out var pending) || pending.SessionId != sessionId)
        {
            return false;
        }

        return pending.Completion.TrySetResult(result);
    }

    public async Task<RemoteAdministrationResultMessage> SendRemoteAdministrationCommandAsync(
        string clientId,
        RemoteAdministrationCommandType command,
        string authorizationToken,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            return RemoteResult(command, RemoteAdministrationCommandStatus.Rejected, "ClientId is required.");

        if (string.IsNullOrWhiteSpace(authorizationToken))
            return RemoteResult(command, RemoteAdministrationCommandStatus.Rejected, "Server admin token is not configured.");

        if (!_clientManager.TryGetActiveSession(clientId, out var session) || session is null)
            return RemoteResult(command, RemoteAdministrationCommandStatus.Rejected, "Client is not connected.");

        var requestId = Guid.NewGuid().ToString("N");
        var pending = new PendingRemoteCommand(session.ClientId, session.SessionId, command);
        if (!_pendingRemoteCommands.TryAdd(requestId, pending))
            return RemoteResult(command, RemoteAdministrationCommandStatus.Failed, "Unable to track remote command.");

        _auditLog.Write(session.ClientId, session.SessionId, command,
            RemoteAdministrationCommandStatus.Accepted, $"requestId={requestId}; dispatched");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await _clientManager.SendAsync(
                session.SessionId,
                MessageType.RemoteAdministrationCommand,
                requestId,
                new RemoteAdministrationCommandMessage
                {
                    Command = command,
                    AuthorizationToken = authorizationToken
                },
                timeoutCts.Token).ConfigureAwait(false);

            return await pending.Completion.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            await TryCancelRemoteCommandAsync(session.SessionId, requestId, authorizationToken).ConfigureAwait(false);
            var result = RemoteResult(command, RemoteAdministrationCommandStatus.Timeout, "Remote command timed out.");
            _auditLog.Write(session.ClientId, session.SessionId, command, result.Status, result.ErrorMessage);
            return result;
        }
        catch (OperationCanceledException)
        {
            await TryCancelRemoteCommandAsync(session.SessionId, requestId, authorizationToken).ConfigureAwait(false);
            var result = RemoteResult(command, RemoteAdministrationCommandStatus.Failed, "Remote command canceled.");
            _auditLog.Write(session.ClientId, session.SessionId, command, result.Status, result.ErrorMessage);
            return result;
        }
        catch (Exception ex)
        {
            var result = RemoteResult(command, RemoteAdministrationCommandStatus.Failed, ex.Message);
            _auditLog.Write(session.ClientId, session.SessionId, command, result.Status, result.ErrorMessage);
            return result;
        }
        finally
        {
            _pendingRemoteCommands.TryRemove(requestId, out _);
        }
    }

    private async Task TryCancelRemoteCommandAsync(string sessionId, string requestId, string authorizationToken)
    {
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await _clientManager.SendAsync(
                sessionId,
                MessageType.RemoteAdministrationCancel,
                requestId,
                new RemoteAdministrationCancelMessage
                {
                    AuthorizationToken = authorizationToken,
                    Reason = "Server command wait ended."
                },
                cancellation.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Remote command cancellation delivery failed: {ex.Message}");
        }
    }

    public bool CompleteRemoteAdministrationCommand(
        string sessionId,
        string requestId,
        RemoteAdministrationResultMessage result)
    {
        if (!_pendingRemoteCommands.TryGetValue(requestId, out var pending)
            || !string.Equals(pending.SessionId, sessionId, StringComparison.OrdinalIgnoreCase)
            || pending.Command != result.Command)
        {
            return false;
        }

        _auditLog.Write(pending.ClientId, sessionId, result.Command, result.Status,
            string.IsNullOrWhiteSpace(result.ErrorMessage) ? result.Result : result.ErrorMessage);

        if (result.Status == RemoteAdministrationCommandStatus.Accepted)
            return true;

        return pending.Completion.TrySetResult(result);
    }

    public void FailPendingForSession(string sessionId, Exception exception)
    {
        foreach (var item in _pending.Where(p => string.Equals(p.Value.SessionId, sessionId, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            if (_pending.TryRemove(item.Key, out var pending))
            {
                pending.Completion.TrySetException(exception);
            }
        }

        foreach (var item in _pendingScans.Where(p => string.Equals(p.Value.SessionId, sessionId, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            if (_pendingScans.TryRemove(item.Key, out var pending))
            {
                pending.Completion.TrySetException(exception);
            }
        }

        foreach (var item in _pendingRemoteCommands.Where(p => string.Equals(p.Value.SessionId, sessionId, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            if (_pendingRemoteCommands.TryRemove(item.Key, out var pending))
            {
                pending.Completion.TrySetException(exception);
            }
        }
    }

    private static RemoteAdministrationResultMessage RemoteResult(
        RemoteAdministrationCommandType command,
        RemoteAdministrationCommandStatus status,
        string errorMessage) => new()
        {
            Command = command,
            Status = status,
            ErrorMessage = errorMessage,
            TimestampUtc = DateTime.UtcNow
        };

    private sealed class PendingPingRequest
    {
        public PendingPingRequest(string sessionId)
        {
            SessionId = sessionId;
        }

        public string SessionId { get; }
        public TaskCompletionSource<PingResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class PendingScanRequest
    {
        public PendingScanRequest(string sessionId)
        {
            SessionId = sessionId;
        }

        public string SessionId { get; }
        public TaskCompletionSource<ScanResultMessage> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class PendingRemoteCommand
    {
        public PendingRemoteCommand(string clientId, string sessionId, RemoteAdministrationCommandType command)
        {
            ClientId = clientId;
            SessionId = sessionId;
            Command = command;
        }

        public string ClientId { get; }
        public string SessionId { get; }
        public RemoteAdministrationCommandType Command { get; }
        public TaskCompletionSource<RemoteAdministrationResultMessage> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
