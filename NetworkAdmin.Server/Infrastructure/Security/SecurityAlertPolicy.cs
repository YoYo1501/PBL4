using NetworkAdmin.Shared.Messages;

namespace NetworkAdmin.Server.Infrastructure.Security;

internal sealed class SecurityAlertPolicyOptions
{
    public bool AutoDisconnectEnabled { get; set; }
    public SecuritySeverity AutoDisconnectMinimumSeverity { get; init; } = SecuritySeverity.Critical;
    public double CriticalScoreThreshold { get; init; } = -0.5;
    public double HighScoreThreshold { get; init; } = -0.25;
    public double MediumScoreThreshold { get; init; } = -0.1;
    public double WarningScoreThreshold { get; init; } = 0.0;
    public TimeSpan DedupeWindow { get; init; } = TimeSpan.FromMinutes(1);
    public TimeSpan RateLimitWindow { get; init; } = TimeSpan.FromMinutes(1);
    public int MaxAlertsPerWindow { get; init; } = 30;
    public TimeSpan AutoDismissAfter { get; init; } = TimeSpan.FromMinutes(5);

    public void Validate()
    {
        if (AutoDisconnectMinimumSeverity is < SecuritySeverity.Low or > SecuritySeverity.Critical)
            throw new ArgumentOutOfRangeException(nameof(AutoDisconnectMinimumSeverity));
        if (CriticalScoreThreshold > HighScoreThreshold || HighScoreThreshold > MediumScoreThreshold ||
            MediumScoreThreshold > WarningScoreThreshold)
            throw new ArgumentException("Security anomaly score thresholds must be ordered from critical to warning.");
        if (DedupeWindow <= TimeSpan.Zero || RateLimitWindow <= TimeSpan.Zero || AutoDismissAfter < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(DedupeWindow));
        if (MaxAlertsPerWindow <= 0) throw new ArgumentOutOfRangeException(nameof(MaxAlertsPerWindow));
    }
}

internal static class SecurityAlertPolicy
{
    public static SecuritySeverity Classify(SecurityAlertMessage alert, SecurityAlertPolicyOptions options)
    {
        ArgumentNullException.ThrowIfNull(alert);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (!alert.IsAnomaly) return SecuritySeverity.Normal;
        if (alert.AnomalyScore <= options.CriticalScoreThreshold) return SecuritySeverity.Critical;
        if (alert.AnomalyScore <= options.HighScoreThreshold) return SecuritySeverity.High;
        if (alert.AnomalyScore <= options.MediumScoreThreshold) return SecuritySeverity.Medium;
        return SecuritySeverity.Low;
    }

    public static bool ShouldAutoDisconnect(SecurityAlertMessage alert, SecurityAlertPolicyOptions options) =>
        options.AutoDisconnectEnabled && alert.IsAnomaly &&
        alert.Severity >= options.AutoDisconnectMinimumSeverity;
}
