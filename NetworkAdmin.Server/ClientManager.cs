using System.Collections.Concurrent;
using System.Net.Sockets;
using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Protocol;
using NetworkAdminTool.Models;

namespace NetworkAdmin.Server;

internal sealed class ClientManager
{
    private readonly ConcurrentDictionary<string, ClientSessionInfo> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public ClientSessionInfo Register(string clientId, string clientName, string sessionId, NetworkStream stream)
    {
        var now = DateTime.UtcNow;
        var session = new ClientSessionInfo
        {
            ClientId = clientId,
            ClientName = clientName,
            SessionId = sessionId,
            Stream = stream,
            ConnectedAt = now,
            LastSeen = now,
            RemoteAddress = (stream.Socket.RemoteEndPoint as System.Net.IPEndPoint)?.Address.ToString() ?? string.Empty
        };

        _sessions[sessionId] = session;
        return session;
    }

    public bool UpdateSystemStats(string sessionId, SystemStats stats, out ClientSessionInfo? session)
    {
        if (!_sessions.TryGetValue(sessionId, out session))
        {
            return false;
        }

        lock (session)
        {
            session.LastSeen = DateTime.UtcNow;
            session.LatestSystemStats = stats;
            session.LastTelemetryAt = session.LastSeen;
        }

        return true;
    }

    public void Remove(string sessionId)
    {
        // Writers may still hold or await the semaphore. Let it be collected with the session.
        _sessions.TryRemove(sessionId, out _);
    }

    public bool UpdateNetworkInfo(string sessionId, ClientNetworkInfo networkInfo, out ClientSessionInfo? session)
    {
        if (!_sessions.TryGetValue(sessionId, out session))
        {
            return false;
        }

        lock (session)
        {
            session.LastSeen = DateTime.UtcNow;
            session.LatestNetworkInfo = networkInfo;
        }

        return true;
    }

    public bool UpdateScanResult(string sessionId, ScanResultMessage scanResult, out ClientSessionInfo? session)
    {
        if (!_sessions.TryGetValue(sessionId, out session))
        {
            return false;
        }

        lock (session)
        {
            session.LastSeen = DateTime.UtcNow;
            session.LatestScanResult = scanResult;
        }

        return true;
    }

    public bool TryGet(string sessionId, out ClientSessionInfo? session) =>
        _sessions.TryGetValue(sessionId, out session);

    public IReadOnlyList<ClientSessionInfo> GetSessions() =>
        _sessions.Values.Where(s => s.IsReady).OrderBy(s => s.ConnectedAt).ToList();

    public async Task SendAsync<TPayload>(
        string sessionId,
        MessageType type,
        string requestId,
        TPayload payload,
        CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new InvalidOperationException($"Session not found: {sessionId}");
        }

        await session.SendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await JsonLineProtocol.SendAsync(session.Stream, type, requestId, payload, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            session.SendLock.Release();
        }
    }
}
