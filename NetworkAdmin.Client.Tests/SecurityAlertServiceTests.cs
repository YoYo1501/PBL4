using System.Text.Json;
using NetworkAdmin.Server.Infrastructure.Security;
using NetworkAdmin.Server.Networking;
using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Protocol;
using Xunit;

namespace NetworkAdmin.Client.Tests;

public sealed class SecurityAlertServiceTests
{
    [Theory]
    [InlineData(-0.6, true, SecuritySeverity.Critical)]
    [InlineData(-0.3, true, SecuritySeverity.High)]
    [InlineData(-0.15, true, SecuritySeverity.Medium)]
    [InlineData(-0.01, true, SecuritySeverity.Low)]
    [InlineData(-1.0, false, SecuritySeverity.Normal)]
    public void Policy_classifies_score_and_label_without_treating_score_as_confidence(
        double score, bool isAnomaly, SecuritySeverity expected)
    {
        var alert = CreateAlert("alert-policy", score, isAnomaly);
        var options = new SecurityAlertPolicyOptions();

        Assert.Equal(expected, SecurityAlertPolicy.Classify(alert, options));
        Assert.Null(alert.Confidence);
    }

    [Fact]
    public async Task Duplicate_alert_id_and_fingerprint_are_deduplicated()
    {
        var (manager, session) = RegisterSession();
        await using var service = CreateService(manager, (_, _, _) => Task.CompletedTask);
        var received = new TaskCompletionSource<SecurityAlertMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.AlertReceived += alert => received.TrySetResult(alert);

        await service.HandleClientMessageAsync(session, Envelope(CreateAlert("alert-1", -0.1, true)), CancellationToken.None);
        await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.HandleClientMessageAsync(session, Envelope(CreateAlert("alert-1", -0.1, true)), CancellationToken.None);
        await service.HandleClientMessageAsync(session, Envelope(CreateAlert("alert-2", -0.1, true)), CancellationToken.None);

        Assert.Single(service.GetActiveAlerts());
    }

    [Fact]
    public async Task Alert_rate_limit_suppresses_distinct_alerts_after_the_configured_limit()
    {
        var (manager, session) = RegisterSession();
        await using var service = CreateService(manager, (_, _, _) => Task.CompletedTask,
            new SecurityAlertPolicyOptions { MaxAlertsPerWindow = 1, AutoDismissAfter = TimeSpan.Zero });

        await service.HandleClientMessageAsync(session,
            Envelope(CreateAlert("rate-1", -0.1, true, "event-rate-1", "reason-rate-1")), CancellationToken.None);
        await service.HandleClientMessageAsync(session,
            Envelope(CreateAlert("rate-2", -0.1, true, "event-rate-2", "reason-rate-2")), CancellationToken.None);

        Assert.Single(service.GetActiveAlerts());
    }

    [Fact]
    public async Task Auto_disconnect_is_disabled_by_default_and_false_positive_never_disconnects()
    {
        var (manager, session) = RegisterSession();
        var disconnects = 0;
        await using var service = CreateService(manager, (_, _, _) =>
        {
            Interlocked.Increment(ref disconnects);
            return Task.CompletedTask;
        });
        var received = new TaskCompletionSource<SecurityAlertMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.AlertReceived += alert => received.TrySetResult(alert);

        var falsePositive = CreateAlert("false-positive", -1, isAnomaly: false);
        falsePositive.Severity = SecuritySeverity.Critical;
        await service.HandleClientMessageAsync(session, Envelope(falsePositive), CancellationToken.None);
        var normalized = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(SecuritySeverity.Normal, normalized.Severity);
        Assert.False(normalized.AutoDisconnectEligible);
        Assert.Equal(0, disconnects);
    }

    [Fact]
    public async Task Auto_disconnect_policy_can_be_toggled_only_by_a_valid_admin_identity()
    {
        var (manager, _) = RegisterSession();
        await using var service = CreateService(manager, (_, _, _) => Task.CompletedTask);

        Assert.False(service.AutoDisconnectEnabled);
        Assert.False(service.SetAutoDisconnectEnabled(true, string.Empty));
        Assert.False(service.AutoDisconnectEnabled);

        Assert.True(service.SetAutoDisconnectEnabled(true, "admin-a"));
        Assert.True(service.AutoDisconnectEnabled);
        Assert.True(service.SetAutoDisconnectEnabled(false, "admin-a"));
        Assert.False(service.AutoDisconnectEnabled);
    }

    [Fact]
    public async Task Enabled_critical_policy_terminates_only_the_matching_session_once_under_race()
    {
        var (manager, session) = RegisterSession();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnects = 0;
        await using var service = CreateService(manager, async (sessionId, _, _) =>
        {
            Assert.Equal(session.SessionId, sessionId);
            Interlocked.Increment(ref disconnects);
            await release.Task;
        }, new SecurityAlertPolicyOptions { AutoDisconnectEnabled = true, AutoDismissAfter = TimeSpan.Zero });

        var sends = Enumerable.Range(1, 5).Select(index =>
            service.HandleClientMessageAsync(session,
                Envelope(CreateAlert($"race-{index}", -0.6, true, $"event-{index}", $"reason-{index}")),
                CancellationToken.None)).ToArray();
        await Task.Delay(100);
        Assert.Equal(1, disconnects);
        release.SetResult();
        await Task.WhenAll(sends);
    }

    [Fact]
    public async Task Alert_for_another_authenticated_session_is_rejected()
    {
        var (manager, session) = RegisterSession();
        await using var service = CreateService(manager, (_, _, _) => Task.CompletedTask);
        var alert = CreateAlert("spoofed", -0.6, true);
        alert.SessionId = "other-session";

        await service.HandleClientMessageAsync(session, Envelope(alert), CancellationToken.None);

        Assert.Empty(service.GetActiveAlerts());
    }

    [Fact]
    public async Task Active_alerts_are_auto_dismissed_after_configured_window()
    {
        var (manager, session) = RegisterSession();
        await using var service = CreateService(manager, (_, _, _) => Task.CompletedTask,
            new SecurityAlertPolicyOptions { AutoDismissAfter = TimeSpan.FromMilliseconds(200) });

        await service.HandleClientMessageAsync(session, Envelope(CreateAlert("dismiss-me", -0.1, true)), CancellationToken.None);
        Assert.Single(service.GetActiveAlerts());
        await Task.Delay(300);

        Assert.Empty(service.GetActiveAlerts());
    }

    private static SecurityAlertService CreateService(ClientManager manager,
        Func<string, string, CancellationToken, Task> terminate,
        SecurityAlertPolicyOptions? options = null) =>
        new(manager, terminate, options, new NetworkAdmin.Server.Audit.RemoteAdministrationAuditLog(
            Path.Combine(Path.GetTempPath(), $"security-alert-{Guid.NewGuid():N}.log")));

    private static (ClientManager Manager, ClientSessionInfo Session) RegisterSession()
    {
        var manager = new ClientManager();
        var session = manager.Register("client-a", "Security Test Client", "session-a", new MemoryStream(),
            out _, remoteAddress: "127.0.0.1");
        session.IsReady = true;
        return (manager, session);
    }

    private static MessageEnvelope Envelope(SecurityAlertMessage alert)
    {
        var json = JsonSerializer.SerializeToElement(alert, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return new MessageEnvelope
        {
            Type = MessageType.SecurityAlert,
            RequestId = alert.AlertId,
            Data = json
        };
    }

    private static SecurityAlertMessage CreateAlert(string alertId, double score, bool isAnomaly,
        string eventId = "event-1", string reason = "suspicious process") => new()
    {
        ClientId = "client-a",
        SessionId = "session-a",
        AlertId = alertId,
        EventId = eventId,
        Severity = SecuritySeverity.Low,
        AnomalyScore = score,
        Confidence = null,
        IsAnomaly = isAnomaly,
        DetectionTimestampUtc = DateTime.UtcNow,
        Title = "Security anomaly",
        Summary = "Test anomaly",
        DetectionReason = reason,
        ProcessName = "demo.exe",
        UserName = "test-user"
    };
}
