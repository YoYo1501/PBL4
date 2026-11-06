using System.Security.Cryptography;
using System.Text;
using NetworkAdmin.Shared.Protocol;

namespace NetworkAdmin.Shared.Messages;

public sealed class RemoteSessionAuthorization
{
    public string AdminId { get; init; } = string.Empty;
    public string AuthorizationToken { get; init; } = string.Empty;
    public RemoteSessionPermission Permission { get; init; } = RemoteSessionPermission.ViewOnly;
    public DateTime ExpiresAtUtc { get; init; }
}

public static class RemoteSessionAuthorizationPolicy
{
    public static string? Validate(
        RemoteSessionAuthorization? authorization,
        string? expectedToken,
        RemoteSessionPermission requiredPermission,
        DateTime nowUtc)
    {
        if (authorization is null) return "Remote session authorization is required.";
        if (!RemoteProtocolContract.IsValidIdentifier(authorization.AdminId)) return "AdminId is invalid.";
        if (!Enum.IsDefined(authorization.Permission)) return "Remote session permission is invalid.";
        if (!Enum.IsDefined(requiredPermission)) return "Required remote session permission is invalid.";
        if (!Allows(authorization.Permission, requiredPermission)) return "Remote session permission is insufficient.";
        if (string.IsNullOrEmpty(expectedToken) || string.IsNullOrEmpty(authorization.AuthorizationToken))
            return "Remote session authorization token is not configured.";
        if (!FixedTimeEquals(authorization.AuthorizationToken, expectedToken))
            return "Remote session authorization token is invalid.";
        if (authorization.ExpiresAtUtc.Kind != DateTimeKind.Utc || authorization.ExpiresAtUtc <= nowUtc)
            return "Remote session authorization has expired.";
        return null;
    }

    public static bool Allows(RemoteSessionPermission granted, RemoteSessionPermission required) =>
        required switch
        {
            RemoteSessionPermission.ViewOnly => granted is RemoteSessionPermission.ViewOnly or RemoteSessionPermission.ViewAndControl,
            RemoteSessionPermission.Control => granted is RemoteSessionPermission.Control or RemoteSessionPermission.ViewAndControl,
            RemoteSessionPermission.ViewAndControl => granted == RemoteSessionPermission.ViewAndControl,
            _ => false
        };

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
