using System.Collections.Generic;
using System.Text.Json;
using NetworkAdmin.Shared.Messages;

namespace NetworkAdmin.Shared.Protocol;

public static class RemoteProtocolContract
{
    public const int CurrentVersion = 1;
    public const int MinimumSupportedVersion = 1;
    public const int MaxIdentifierLength = 128;
    public const int MaxRequestIdLength = 128;
    public const int MaxChatTextLength = 4_096;
    public const int MaxChatPayloadBytes = 16 * 1024;
    public const int MaxSecurityFieldLength = 256;
    public const int MaxSecurityCommandLineLength = 4_096;
    public const int MaxSecurityPayloadBytes = 32 * 1024;
    public const int MaxErrorMessageLength = 1_024;
    public const int MaxCapabilityLength = 64;
    public const int MaxCapabilities = 32;
    public const int MaxSessionTimeoutSeconds = 3_600;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static bool IsCompatibleVersion(int version) =>
        version is >= MinimumSupportedVersion and <= CurrentVersion;

    public static bool IsKnownMessageType(MessageType type) =>
        Enum.IsDefined(typeof(MessageType), type);

    public static bool IsValidIdentifier(string? value, int maxLength = MaxIdentifierLength) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= maxLength
        && value.All(c => !char.IsControl(c) && !char.IsWhiteSpace(c));

    public static bool IsValidDisplayName(string? value, int maxLength = MaxIdentifierLength) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= maxLength
        && value == value.Trim()
        && value.All(c => !char.IsControl(c));

    public static bool IsValidRequestId(string? value) =>
        IsValidIdentifier(value, MaxRequestIdLength);

    public static bool IsValidSessionId(string? value) =>
        IsValidIdentifier(value, MaxIdentifierLength);

    public static bool IsValidUtcTimestamp(DateTime value) =>
        value != default && value.Kind == DateTimeKind.Utc;

    public static bool IsValidCapabilities(IReadOnlyCollection<string>? capabilities)
    {
        if (capabilities is null || capabilities.Count > MaxCapabilities)
            return false;

        return capabilities.All(capability =>
            IsValidIdentifier(capability, MaxCapabilityLength));
    }

    public static string? ValidateHello(HelloMessage? hello)
    {
        if (hello is null) return "Hello payload is required.";
        if (!IsValidIdentifier(hello.ClientId)) return "ClientId is invalid.";
        if (!IsValidDisplayName(hello.ClientName)) return "ClientName is invalid.";
        if (!IsCompatibleVersion(hello.ProtocolVersion)) return "ProtocolVersion is not supported.";
        if (!IsValidCapabilities(hello.Capabilities)) return "Capabilities are invalid.";
        return null;
    }

    public static string? ValidateHelloAck(HelloAckMessage? ack)
    {
        if (ack is null) return "HelloAck payload is required.";
        if (!IsCompatibleVersion(ack.ProtocolVersion)) return "ProtocolVersion is not supported.";
        if (!IsValidCapabilities(ack.Capabilities)) return "Capabilities are invalid.";
        if (ack.Accepted && !IsValidSessionId(ack.SessionId)) return "Accepted HelloAck requires a valid SessionId.";
        return null;
    }

    public static string? ValidateEnvelope(MessageEnvelope? envelope)
    {
        if (envelope is null)
            return "Message envelope is required.";
        if (!IsKnownMessageType(envelope.Type))
            return $"Message type '{(int)envelope.Type}' is not recognized.";
        if (!IsValidRequestId(envelope.RequestId))
            return "RequestId is required and must be a valid identifier.";
        if (envelope.Data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return "Message payload is required.";
        return null;
    }

    public static string? ValidatePayloadSize<TPayload>(TPayload payload, int maxBytes)
    {
        if (payload is null)
            return "Message payload is required.";
        if (maxBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));

        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        return bytes.Length <= maxBytes
            ? null
            : $"Message payload exceeds the {maxBytes}-byte limit.";
    }
}

public static class RemoteProtocolCapabilities
{
    public const string RemoteSessionV1 = "remote-session.v1";
    public const string RemoteChatV1 = "remote-chat.v1";
    public const string SecurityMonitoringV1 = "security-monitoring.v1";
}

public sealed class MessageIdLedger
{
    private readonly int _capacity;
    private readonly Queue<string> _order = new();
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    public MessageIdLedger(int capacity = 1_024)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public bool TryRegister(string? messageId)
    {
        if (!RemoteProtocolContract.IsValidIdentifier(messageId))
            return false;

        lock (_sync)
        {
            var normalizedId = messageId!;
            if (!_ids.Add(normalizedId))
                return false;

            _order.Enqueue(normalizedId);
            if (_order.Count > _capacity)
                _ids.Remove(_order.Dequeue());
            return true;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _ids.Clear();
            _order.Clear();
        }
    }
}
