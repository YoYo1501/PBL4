using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using NetworkAdmin.Server.Audit;
using NetworkAdmin.Server.Networking;
using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Protocol;

namespace NetworkAdmin.Server.Infrastructure.Security;

internal sealed class SecurityAlertService : IAsyncDisposable
{
    private sealed record StoredAlert(SecurityAlertMessage Alert, DateTime? ExpiresAtUtc);

    private readonly ClientManager _clients;
    private readonly Func<string, string, CancellationToken, Task> _terminateSessionAsync;
    private readonly RemoteAdministrationAuditLog _auditLog;
    private readonly SecurityAlertPolicyOptions _options;
    private readonly Channel<SecurityAlertMessage> _displayQueue;
    private readonly ConcurrentDictionary<string, MessageIdLedger> _alertIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, StoredAlert> _activeAlerts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _dedupeKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, AlertWindow> _rateWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _disconnectingSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _displayTask;
    private int _disposed;

    public SecurityAlertService(
        ClientManager clients,
        Func<string, string, CancellationToken, Task> terminateSessionAsync,
        SecurityAlertPolicyOptions? options = null,
        RemoteAdministrationAuditLog? auditLog = null)
    {
        _clients = clients;
        _terminateSessionAsync = terminateSessionAsync;
        _auditLog = auditLog ?? new RemoteAdministrationAuditLog();
        _options = options ?? new SecurityAlertPolicyOptions();
        _options.Validate();
        _displayQueue = Channel.CreateBounded<SecurityAlertMessage>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _displayTask = DisplayLoopAsync(_shutdown.Token);
    }

    public event Action<SecurityAlertMessage>? AlertReceived;

    public bool AutoDisconnectEnabled => _options.AutoDisconnectEnabled;

    public bool SetAutoDisconnectEnabled(bool enabled, string adminId)
    {
        if (!RemoteProtocolContract.IsValidIdentifier(adminId))
            return false;

        _options.AutoDisconnectEnabled = enabled;
        _auditLog.WriteSecurityPolicyChange(adminId, "auto-disconnect", enabled);
        return true;
    }

    public async Task HandleClientMessageAsync(ClientSessionInfo session, MessageEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0 || !session.IsReady ||
            envelope.Type != MessageType.SecurityAlert)
            return;

        SecurityAlertMessage? incoming;
        try { incoming = JsonLineProtocol.GetPayload<SecurityAlertMessage>(envelope); }
        catch (JsonException) { return; }
        catch (InvalidOperationException) { return; }

        var validationError = SecurityMonitoringContractPolicy.ValidateAlert(incoming);
        if (validationError is not null || incoming is null ||
            !string.Equals(incoming.AlertId, envelope.RequestId, StringComparison.Ordinal) ||
            !string.Equals(incoming.ClientId, session.ClientId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(incoming.SessionId, session.SessionId, StringComparison.OrdinalIgnoreCase) ||
            !_clients.TryGet(session.SessionId, out var current) || !ReferenceEquals(current, session))
        {
            return;
        }

        var normalized = Clone(incoming);
        normalized.Severity = SecurityAlertPolicy.Classify(normalized, _options);
        normalized.AutoDisconnectEligible = SecurityAlertPolicy.ShouldAutoDisconnect(normalized, _options);

        if (!TryAccept(normalized, out var suppressionReason))
        {
            _auditLog.WriteSecurityAlert(normalized, suppressionReason ?? "suppressed");
            return;
        }

        _auditLog.WriteSecurityAlert(normalized,
            normalized.AutoDisconnectEligible ? "accepted; auto-disconnect scheduled" : "accepted");
        if (!_displayQueue.Writer.TryWrite(normalized))
            _auditLog.WriteSecurityAlert(normalized, "accepted; display queue full");

        if (normalized.AutoDisconnectEligible &&
            _disconnectingSessions.TryAdd(session.SessionId, 0))
        {
            try
            {
                await _terminateSessionAsync(session.SessionId,
                    $"Security alert {normalized.AlertId}: {normalized.DetectionReason}", cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _auditLog.WriteSecurityAlert(normalized, $"auto-disconnect failed: {ex.Message}");
            }
            finally
            {
                _disconnectingSessions.TryRemove(session.SessionId, out _);
            }
        }
    }

    public IReadOnlyList<SecurityAlertMessage> GetActiveAlerts(string? clientId = null, string? sessionId = null)
    {
        var now = DateTime.UtcNow;
        foreach (var pair in _activeAlerts)
        {
            if (pair.Value.ExpiresAtUtc is { } expires && expires <= now)
                _activeAlerts.TryRemove(pair.Key, out _);
        }

        return _activeAlerts.Values
            .Select(value => value.Alert)
            .Where(alert => (clientId is null || string.Equals(alert.ClientId, clientId, StringComparison.OrdinalIgnoreCase)) &&
                            (sessionId is null || string.Equals(alert.SessionId, sessionId, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(alert => alert.DetectionTimestampUtc)
            .ToArray();
    }

    public void EndSession(string sessionId)
    {
        _alertIds.TryRemove(sessionId, out _);
        _rateWindows.TryRemove(sessionId, out _);
        _disconnectingSessions.TryRemove(sessionId, out _);
        foreach (var pair in _activeAlerts.Where(pair => pair.Value.Alert.SessionId == sessionId).ToArray())
            _activeAlerts.TryRemove(pair.Key, out _);
        foreach (var pair in _dedupeKeys.Where(pair => pair.Key.StartsWith(sessionId + "|", StringComparison.OrdinalIgnoreCase)).ToArray())
            _dedupeKeys.TryRemove(pair.Key, out _);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _displayQueue.Writer.TryComplete();
        await _shutdown.CancelAsync().ConfigureAwait(false);
        try { await _displayTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _shutdown.Dispose();
        _alertIds.Clear();
        _activeAlerts.Clear();
        _dedupeKeys.Clear();
        _rateWindows.Clear();
        _disconnectingSessions.Clear();
    }

    private bool TryAccept(SecurityAlertMessage alert, out string? suppressionReason)
    {
        suppressionReason = null;
        var ledger = _alertIds.GetOrAdd(alert.SessionId, static _ => new MessageIdLedger());
        if (!ledger.TryRegister(alert.AlertId))
        {
            suppressionReason = "duplicate alert id";
            return false;
        }

        var now = DateTime.UtcNow;
        var dedupeKey = $"{alert.SessionId}|{alert.EventId}|{alert.ProcessName}|{alert.DetectionReason}";
        if (_dedupeKeys.TryGetValue(dedupeKey, out var previous) && now - previous < _options.DedupeWindow)
        {
            suppressionReason = "duplicate alert fingerprint";
            return false;
        }

        var window = _rateWindows.GetOrAdd(alert.SessionId, static _ => new AlertWindow());
        if (!window.TryAcquire(now, _options.RateLimitWindow, _options.MaxAlertsPerWindow))
        {
            suppressionReason = "alert rate limit";
            return false;
        }

        _dedupeKeys[dedupeKey] = now;
        DateTime? expiresAt = _options.AutoDismissAfter == TimeSpan.Zero
            ? null : now.Add(_options.AutoDismissAfter);
        _activeAlerts[alert.AlertId] = new StoredAlert(alert, expiresAt);
        foreach (var pair in _dedupeKeys)
            if (now - pair.Value >= _options.DedupeWindow)
                _dedupeKeys.TryRemove(pair.Key, out _);
        return true;
    }

    private async Task DisplayLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var alert in _displayQueue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try { AlertReceived?.Invoke(alert); }
                catch (Exception ex) { Console.WriteLine($"Security alert subscriber failed: {ex.Message}"); }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private static SecurityAlertMessage Clone(SecurityAlertMessage source) => new()
    {
        ClientId = source.ClientId,
        SessionId = source.SessionId,
        AlertId = source.AlertId,
        EventId = source.EventId,
        Severity = source.Severity,
        AnomalyScore = source.AnomalyScore,
        Confidence = source.Confidence,
        IsAnomaly = source.IsAnomaly,
        DetectionTimestampUtc = source.DetectionTimestampUtc,
        Title = source.Title,
        Summary = source.Summary,
        DetectionReason = source.DetectionReason,
        ProcessName = source.ProcessName,
        UserName = source.UserName,
        AutoDisconnectEligible = source.AutoDisconnectEligible
    };

    private sealed class AlertWindow
    {
        private readonly object _sync = new();
        private DateTime _startedUtc;
        private int _count;

        public bool TryAcquire(DateTime now, TimeSpan window, int limit)
        {
            lock (_sync)
            {
                if (_startedUtc == default || now - _startedUtc >= window)
                {
                    _startedUtc = now;
                    _count = 0;
                }
                if (_count >= limit) return false;
                _count++;
                return true;
            }
        }
    }
}
