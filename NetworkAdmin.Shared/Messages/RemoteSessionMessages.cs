using NetworkAdmin.Shared.Protocol;

namespace NetworkAdmin.Shared.Messages;

public enum RemoteSessionKind
{
    Screen = 1,
    Control = 2,
    Chat = 3,
    Composite = 4
}

public enum RemoteSessionPermission
{
    ViewOnly = 1,
    Control = 2,
    ViewAndControl = 3
}

public enum RemoteSessionState
{
    Requested = 1,
    AwaitingConsent = 2,
    PendingConsent = AwaitingConsent,
    Accepted = 3,
    Starting = 4,
    Active = 5,
    Stopping = 6,
    Stopped = 7,
    Rejected = 8,
    TimedOut = 9,
    Canceled = 10,
    Failed = 11,
    Closed = 12
}

public enum RemoteSessionErrorCode
{
    Unknown = 1,
    InvalidRequest = 2,
    Unauthorized = 3,
    ConsentDenied = 4,
    Timeout = 5,
    Canceled = 6,
    Disconnected = 7,
    ProtocolMismatch = 8,
    Busy = 9
}

public sealed class RemoteSessionRequestMessage
{
    public string ClientId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public RemoteSessionKind Kind { get; set; }
    public RemoteSessionPermission RequestedPermission { get; set; } = RemoteSessionPermission.ViewOnly;
    public string RequestedById { get; set; } = string.Empty;
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public int TimeoutSeconds { get; set; } = 60;
}

public sealed class RemoteSessionResponseMessage
{
    public string ClientId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public bool Accepted { get; set; }
    public RemoteSessionState State { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public RemoteSessionPermission GrantedPermission { get; set; } = RemoteSessionPermission.ViewOnly;
}

public sealed class RemoteSessionStartMessage
{
    public string ClientId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public RemoteSessionKind Kind { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class RemoteSessionStopMessage
{
    public string ClientId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string StoppedById { get; set; } = string.Empty;
    public DateTime StoppedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class RemoteSessionStatusMessage
{
    public string ClientId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public RemoteSessionState State { get; set; }
    public string Detail { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool CancellationRequested { get; set; }
    public RemoteSessionPermission GrantedPermission { get; set; } = RemoteSessionPermission.ViewOnly;
}

public sealed class RemoteSessionSnapshot
{
    public string ClientId { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public string AdminId { get; init; } = string.Empty;
    public string RequestId { get; init; } = string.Empty;
    public RemoteSessionKind Kind { get; init; }
    public RemoteSessionState State { get; init; }
    public RemoteSessionPermission GrantedPermission { get; init; }
    public DateTime RequestedAtUtc { get; init; }
    public DateTime ExpiresAtUtc { get; init; }
    public bool IsControlActive { get; init; }
}

public sealed class RemoteSessionErrorMessage
{
    public string ClientId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public RemoteSessionErrorCode Code { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool Retryable { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class RemoteSessionCancelMessage
{
    public string ClientId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string CanceledById { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime CanceledAtUtc { get; set; } = DateTime.UtcNow;
}

public static class RemoteSessionContractPolicy
{
    public static string? ValidateRequest(RemoteSessionRequestMessage? request)
    {
        if (request is null) return "Remote session request is required.";
        if (!RemoteProtocolContract.IsValidIdentifier(request.ClientId)) return "ClientId is invalid.";
        if (!RemoteProtocolContract.IsValidSessionId(request.SessionId)) return "SessionId is invalid.";
        if (!Enum.IsDefined(request.Kind)) return "Remote session kind is invalid.";
        if (!Enum.IsDefined(request.RequestedPermission)) return "Remote session permission is invalid.";
        if (!RemoteProtocolContract.IsValidIdentifier(request.RequestedById)) return "RequestedById is invalid.";
        if (!RemoteProtocolContract.IsValidUtcTimestamp(request.RequestedAtUtc)) return "RequestedAtUtc must be UTC.";
        if (request.TimeoutSeconds is < 1 or > RemoteProtocolContract.MaxSessionTimeoutSeconds)
            return "Session timeout is outside the supported range.";
        return null;
    }

    public static string? ValidateResponse(RemoteSessionResponseMessage? response)
    {
        if (response is null) return "Remote session response is required.";
        if (!RemoteProtocolContract.IsValidIdentifier(response.ClientId)) return "ClientId is invalid.";
        if (!RemoteProtocolContract.IsValidSessionId(response.SessionId)) return "SessionId is invalid.";
        if (!Enum.IsDefined(response.State)) return "Remote session state is invalid.";
        if (!Enum.IsDefined(response.GrantedPermission)) return "Remote session permission is invalid.";
        if (response.Message is { Length: > RemoteProtocolContract.MaxErrorMessageLength }) return "Response message is too long.";
        if (response.ExpiresAtUtc != default && !RemoteProtocolContract.IsValidUtcTimestamp(response.ExpiresAtUtc))
            return "ExpiresAtUtc must be UTC.";
        return null;
    }

    public static string? ValidateStatus(RemoteSessionStatusMessage? status)
    {
        if (status is null) return "Remote session status is required.";
        if (!RemoteProtocolContract.IsValidIdentifier(status.ClientId)) return "ClientId is invalid.";
        if (!RemoteProtocolContract.IsValidSessionId(status.SessionId)) return "SessionId is invalid.";
        if (!Enum.IsDefined(status.State)) return "Remote session state is invalid.";
        if (!Enum.IsDefined(status.GrantedPermission)) return "Remote session permission is invalid.";
        if (status.Detail is { Length: > RemoteProtocolContract.MaxErrorMessageLength }) return "Status detail is too long.";
        if (!RemoteProtocolContract.IsValidUtcTimestamp(status.UpdatedAtUtc)) return "UpdatedAtUtc must be UTC.";
        return null;
    }

    public static string? ValidateStart(RemoteSessionStartMessage? start)
    {
        if (start is null) return "Remote session start is required.";
        var identityError = ValidateIdentity(start.ClientId, start.SessionId);
        if (identityError is not null) return identityError;
        if (!Enum.IsDefined(start.Kind)) return "Remote session kind is invalid.";
        return RemoteProtocolContract.IsValidUtcTimestamp(start.StartedAtUtc) ? null : "StartedAtUtc must be UTC.";
    }

    public static string? ValidateStop(RemoteSessionStopMessage? stop)
    {
        if (stop is null) return "Remote session stop is required.";
        var identityError = ValidateIdentity(stop.ClientId, stop.SessionId);
        if (identityError is not null) return identityError;
        if (!RemoteProtocolContract.IsValidIdentifier(stop.StoppedById)) return "StoppedById is invalid.";
        if (stop.Reason is { Length: > RemoteProtocolContract.MaxErrorMessageLength }) return "Stop reason is too long.";
        return RemoteProtocolContract.IsValidUtcTimestamp(stop.StoppedAtUtc) ? null : "StoppedAtUtc must be UTC.";
    }

    public static string? ValidateError(RemoteSessionErrorMessage? error)
    {
        if (error is null) return "Remote session error is required.";
        var identityError = ValidateIdentity(error.ClientId, error.SessionId);
        if (identityError is not null) return identityError;
        if (!Enum.IsDefined(error.Code)) return "Remote session error code is invalid.";
        if (string.IsNullOrWhiteSpace(error.Message) || error.Message.Length > RemoteProtocolContract.MaxErrorMessageLength)
            return "Remote session error message is invalid.";
        return RemoteProtocolContract.IsValidUtcTimestamp(error.OccurredAtUtc) ? null : "OccurredAtUtc must be UTC.";
    }

    public static string? ValidateCancel(RemoteSessionCancelMessage? cancel)
    {
        if (cancel is null) return "Remote session cancellation is required.";
        var identityError = ValidateIdentity(cancel.ClientId, cancel.SessionId);
        if (identityError is not null) return identityError;
        if (!RemoteProtocolContract.IsValidIdentifier(cancel.CanceledById)) return "CanceledById is invalid.";
        if (cancel.Reason is { Length: > RemoteProtocolContract.MaxErrorMessageLength }) return "Cancellation reason is too long.";
        return RemoteProtocolContract.IsValidUtcTimestamp(cancel.CanceledAtUtc) ? null : "CanceledAtUtc must be UTC.";
    }

    private static string? ValidateIdentity(string? clientId, string? sessionId)
    {
        if (!RemoteProtocolContract.IsValidIdentifier(clientId)) return "ClientId is invalid.";
        return RemoteProtocolContract.IsValidSessionId(sessionId) ? null : "SessionId is invalid.";
    }
}
