using System.Collections.Concurrent;
using NetworkAdmin.Shared.Messages;
using NetworkAdminTool.Models;

namespace NetworkAdmin.Server;

internal sealed class ServerCommandDispatcher
{
    private readonly ClientManager _clientManager;
    private readonly ConcurrentDictionary<string, PendingPingRequest> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PendingScanRequest> _pendingScans = new(StringComparer.OrdinalIgnoreCase);

    public ServerCommandDispatcher(ClientManager clientManager)
    {
        _clientManager = clientManager;
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
        try
        {
            await _clientManager.SendAsync(
                sessionId,
                MessageType.PingRequest,
                requestId,
                new PingRequestMessage { Target = target },
                timeoutCts.Token).ConfigureAwait(false);

            return await pending.Completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
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

        pending.Completion.TrySetResult(result);
        return true;
    }

    public async Task<ScanResultMessage> SendScanAsync(
        string sessionId,
        string subnet,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var pending = new PendingScanRequest(sessionId);
        if (!_pendingScans.TryAdd(requestId, pending))
        {
            throw new InvalidOperationException("Unable to track scan request.");
        }

        try
        {
            await _clientManager.SendAsync(
                sessionId,
                MessageType.ScanRequest,
                requestId,
                new ScanRequestMessage { Subnet = subnet, TimeoutMs = 800 },
                cancellationToken).ConfigureAwait(false);

            return await pending.Completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
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

        pending.Completion.TrySetResult(result);
        return true;
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
    }

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
}
