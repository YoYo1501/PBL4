using NetworkAdmin.Shared.Protocol;

namespace NetworkAdmin.Shared.Messages;

public enum SecuritySeverity
{
    Normal = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum MonitoringState
{
    Disabled = 0,
    Starting = 1,
    Running = 2,
    Stopping = 3,
    Stopped = 4,
    Faulted = 5
}

public sealed class SecurityEventMessage
{
    public string ClientId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public DateTime DetectionTimestampUtc { get; set; } = DateTime.UtcNow;
    public string Source { get; set; } = string.Empty;
    public int EventCode { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string ParentProcessName { get; set; } = string.Empty;
    public string ImagePath { get; set; } = string.Empty;
    public string CommandLine { get; set; } = string.Empty;
    public int DestinationPort { get; set; }
}

public sealed class SecurityAlertMessage
{
    public string ClientId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string AlertId { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public SecuritySeverity Severity { get; set; }
    public double AnomalyScore { get; set; }
    public double? Confidence { get; set; }
    public bool IsAnomaly { get; set; }
    public DateTime DetectionTimestampUtc { get; set; } = DateTime.UtcNow;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string DetectionReason { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public bool AutoDisconnectEligible { get; set; }
}

public sealed class MonitoringStatusMessage
{
    public string ClientId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public MonitoringState State { get; set; }
    public DateTime? LastEventTimestampUtc { get; set; }
    public string Detail { get; set; } = string.Empty;
    public string ModelVersion { get; set; } = string.Empty;
}

public sealed class MonitoringStartRequestMessage
{
    public string ClientId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string RequestedById { get; set; } = string.Empty;
    public string ModelVersion { get; set; } = string.Empty;
    public bool EnableAlerts { get; set; } = true;
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class MonitoringStopRequestMessage
{
    public string ClientId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string RequestedById { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class SecurityMonitoringContractPolicy
{
    public static string? ValidateEvent(SecurityEventMessage? securityEvent)
    {
        if (securityEvent is null) return "Security event is required.";
        if (!RemoteProtocolContract.IsValidIdentifier(securityEvent.ClientId)) return "ClientId is invalid.";
        if (!RemoteProtocolContract.IsValidSessionId(securityEvent.SessionId)) return "SessionId is invalid.";
        if (!RemoteProtocolContract.IsValidIdentifier(securityEvent.EventId)) return "EventId is invalid.";
        if (!RemoteProtocolContract.IsValidUtcTimestamp(securityEvent.DetectionTimestampUtc)) return "DetectionTimestampUtc must be UTC.";
        if (!ValidField(securityEvent.Source) || !ValidField(securityEvent.UserName) ||
            !ValidField(securityEvent.ProcessName) || !ValidField(securityEvent.ParentProcessName) ||
            !ValidField(securityEvent.ImagePath)) return "Security event text field is invalid or too long.";
        if (securityEvent.CommandLine is null || securityEvent.CommandLine.Length > RemoteProtocolContract.MaxSecurityCommandLineLength)
            return "CommandLine is too long.";
        if (securityEvent.DestinationPort is < 0 or > 65_535) return "DestinationPort is invalid.";
        return RemoteProtocolContract.ValidatePayloadSize(securityEvent, RemoteProtocolContract.MaxSecurityPayloadBytes);
    }

    public static string? ValidateAlert(SecurityAlertMessage? alert)
    {
        if (alert is null) return "Security alert is required.";
        if (!RemoteProtocolContract.IsValidIdentifier(alert.ClientId)) return "ClientId is invalid.";
        if (!RemoteProtocolContract.IsValidSessionId(alert.SessionId)) return "SessionId is invalid.";
        if (!RemoteProtocolContract.IsValidIdentifier(alert.AlertId)) return "AlertId is invalid.";
        if (!RemoteProtocolContract.IsValidIdentifier(alert.EventId)) return "EventId is invalid.";
        if (!Enum.IsDefined(alert.Severity)) return "Security severity is invalid.";
        if (double.IsNaN(alert.AnomalyScore) || double.IsInfinity(alert.AnomalyScore)) return "AnomalyScore is invalid.";
        if (alert.Confidence is { } confidence &&
            (double.IsNaN(confidence) || double.IsInfinity(confidence) || confidence is < 0 or > 1))
            return "Confidence must be between 0 and 1.";
        if (!RemoteProtocolContract.IsValidUtcTimestamp(alert.DetectionTimestampUtc)) return "DetectionTimestampUtc must be UTC.";
        if (string.IsNullOrWhiteSpace(alert.DetectionReason)) return "DetectionReason is required.";
        if (!ValidField(alert.Title) || !ValidField(alert.Summary) || !ValidField(alert.DetectionReason) ||
            !ValidField(alert.ProcessName) || !ValidField(alert.UserName))
            return "Security alert text field is invalid or too long.";
        return RemoteProtocolContract.ValidatePayloadSize(alert, RemoteProtocolContract.MaxSecurityPayloadBytes);
    }

    public static string? ValidateMonitoringStatus(MonitoringStatusMessage? status)
    {
        if (status is null) return "Monitoring status is required.";
        if (!RemoteProtocolContract.IsValidIdentifier(status.ClientId)) return "ClientId is invalid.";
        if (!RemoteProtocolContract.IsValidSessionId(status.SessionId)) return "SessionId is invalid.";
        if (!Enum.IsDefined(status.State)) return "Monitoring state is invalid.";
        if (status.LastEventTimestampUtc is { } timestamp && !RemoteProtocolContract.IsValidUtcTimestamp(timestamp))
            return "LastEventTimestampUtc must be UTC.";
        if (!ValidField(status.Detail) || !ValidField(status.ModelVersion)) return "Monitoring status field is invalid.";
        return null;
    }

    public static string? ValidateStartRequest(MonitoringStartRequestMessage? request)
    {
        if (request is null) return "Monitoring start request is required.";
        var identityError = ValidateIdentity(request.ClientId, request.SessionId);
        if (identityError is not null) return identityError;
        if (!RemoteProtocolContract.IsValidIdentifier(request.RequestedById)) return "RequestedById is invalid.";
        if (!ValidField(request.ModelVersion)) return "ModelVersion is invalid.";
        return RemoteProtocolContract.IsValidUtcTimestamp(request.RequestedAtUtc) ? null : "RequestedAtUtc must be UTC.";
    }

    public static string? ValidateStopRequest(MonitoringStopRequestMessage? request)
    {
        if (request is null) return "Monitoring stop request is required.";
        var identityError = ValidateIdentity(request.ClientId, request.SessionId);
        if (identityError is not null) return identityError;
        if (!RemoteProtocolContract.IsValidIdentifier(request.RequestedById)) return "RequestedById is invalid.";
        if (!ValidField(request.Reason)) return "Reason is invalid.";
        return RemoteProtocolContract.IsValidUtcTimestamp(request.RequestedAtUtc) ? null : "RequestedAtUtc must be UTC.";
    }

    private static bool ValidField(string? value) =>
        value is not null
        && value.Length <= RemoteProtocolContract.MaxSecurityFieldLength
        && value.All(c => !char.IsControl(c));

    private static string? ValidateIdentity(string? clientId, string? sessionId)
    {
        if (!RemoteProtocolContract.IsValidIdentifier(clientId)) return "ClientId is invalid.";
        return RemoteProtocolContract.IsValidSessionId(sessionId) ? null : "SessionId is invalid.";
    }
}
