using System.Text.Json;
using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Protocol;
using Xunit;

namespace NetworkAdmin.Shared.Tests;

public sealed class RemoteProtocolContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void New_message_ids_are_unique_and_do_not_change_existing_ids()
    {
        var values = Enum.GetValues<MessageType>().Select(type => (int)type).ToArray();

        Assert.Equal(values.Length, values.Distinct().Count());
        Assert.Equal(20, (int)MessageType.ControlInputEvent);
        Assert.All(values.Where(value => value > 20), value => Assert.InRange(value, 21, 33));
        Assert.Equal(33, values.Max());
    }

    [Fact]
    public void Chat_message_serializes_and_deserializes_with_contract_fields()
    {
        var original = new RemoteChatMessage
        {
            ClientId = "client-a",
            SessionId = "session-a",
            SenderId = "admin-a",
            ReceiverId = "client-a",
            MessageId = "message-a",
            SenderRole = RemoteChatSenderRole.Admin,
            Content = "hello",
            TimestampUtc = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var copy = JsonSerializer.Deserialize<RemoteChatMessage>(json, JsonOptions);

        Assert.NotNull(copy);
        Assert.Null(RemoteChatMessagePolicy.Validate(copy));
        Assert.Equal(original.MessageId, copy!.MessageId);
        Assert.Equal(original.Content, copy.Content);
        Assert.Equal(original.TimestampUtc, copy.TimestampUtc);
    }

    [Fact]
    public void Invalid_message_type_is_rejected_by_envelope_validation()
    {
        using var document = JsonDocument.Parse("{}");
        var envelope = new MessageEnvelope
        {
            Type = (MessageType)999,
            RequestId = "request-1",
            Data = document.RootElement.Clone()
        };

        Assert.Contains("not recognized", RemoteProtocolContract.ValidateEnvelope(envelope));
    }

    [Fact]
    public void Null_or_empty_payload_is_rejected()
    {
        var emptyEnvelope = new MessageEnvelope
        {
            Type = MessageType.RemoteChatMessage,
            RequestId = "request-1"
        };
        Assert.Contains("payload", RemoteProtocolContract.ValidateEnvelope(emptyEnvelope));

        Assert.Contains("required", RemoteChatMessagePolicy.Validate(null));
        Assert.Contains("required", RemoteChatMessagePolicy.Validate(new RemoteChatMessage
        {
            ClientId = "client-a",
            SessionId = "session-a",
            SenderId = "admin-a",
            MessageId = "message-a",
            SenderRole = RemoteChatSenderRole.Admin,
            TimestampUtc = DateTime.UtcNow
        }));
    }

    [Fact]
    public void Oversized_chat_payload_is_rejected()
    {
        var message = new RemoteChatMessage
        {
            ClientId = "client-a",
            SessionId = "session-a",
            SenderId = "admin-a",
            MessageId = "message-a",
            SenderRole = RemoteChatSenderRole.Admin,
            Content = new string('x', RemoteProtocolContract.MaxChatTextLength + 1),
            TimestampUtc = DateTime.UtcNow
        };

        Assert.Contains("too long", RemoteChatMessagePolicy.Validate(message));
    }

    [Fact]
    public void Duplicate_message_id_is_rejected_by_ledger()
    {
        var ledger = new MessageIdLedger(capacity: 2);

        Assert.True(ledger.TryRegister("message-1"));
        Assert.False(ledger.TryRegister("message-1"));
        Assert.True(ledger.TryRegister("message-2"));
        Assert.True(ledger.TryRegister("message-3"));
        Assert.True(ledger.TryRegister("message-1"));
    }

    [Fact]
    public void Invalid_session_id_is_rejected()
    {
        var request = new RemoteSessionRequestMessage
        {
            ClientId = "client-a",
            SessionId = " ",
            Kind = RemoteSessionKind.Chat,
            RequestedById = "admin-a",
            RequestedAtUtc = DateTime.UtcNow,
            TimeoutSeconds = 60
        };

        Assert.Contains("SessionId", RemoteSessionContractPolicy.ValidateRequest(request));
    }

    [Fact]
    public void Security_alert_keeps_anomaly_score_and_confidence_distinct()
    {
        var alert = new SecurityAlertMessage
        {
            ClientId = "client-a",
            SessionId = "session-a",
            AlertId = "alert-a",
            EventId = "event-a",
            Severity = SecuritySeverity.High,
            AnomalyScore = -0.035,
            Confidence = 0.87,
            DetectionTimestampUtc = DateTime.UtcNow,
            Title = "Suspicious process",
            Summary = "Anomaly detected",
            DetectionReason = "Suspicious process behavior",
            ProcessName = "powershell.exe",
            UserName = "user-a"
        };

        Assert.Null(SecurityMonitoringContractPolicy.ValidateAlert(alert));
        Assert.NotEqual(alert.AnomalyScore, alert.Confidence);
    }

    [Fact]
    public void Legacy_hello_payload_without_protocol_fields_remains_compatible()
    {
        const string legacyJson = "{\"clientId\":\"client-a\",\"clientName\":\"Client A\"}";

        var hello = JsonSerializer.Deserialize<HelloMessage>(legacyJson, JsonOptions);

        Assert.NotNull(hello);
        Assert.Equal("client-a", hello!.ClientId);
        Assert.Equal(RemoteProtocolContract.CurrentVersion, hello.ProtocolVersion);
        Assert.Empty(hello.Capabilities);
    }

    [Fact]
    public void Hello_accepts_human_readable_client_names_without_relaxing_identifier_rules()
    {
        var hello = new HelloMessage
        {
            ClientId = "client-a",
            ClientName = "Screen Test Client"
        };

        Assert.Null(RemoteProtocolContract.ValidateHello(hello));
        Assert.False(RemoteProtocolContract.IsValidIdentifier(hello.ClientName));
    }

    [Fact]
    public void Existing_remote_administration_contract_still_round_trips()
    {
        var original = new RemoteAdministrationCommandMessage
        {
            Command = RemoteAdministrationCommandType.GetAgentInfo,
            AuthorizationToken = "test-token",
            CountdownSeconds = RemoteAdministrationCommandPolicy.DefaultCountdownSeconds
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var copy = JsonSerializer.Deserialize<RemoteAdministrationCommandMessage>(json, JsonOptions);

        Assert.NotNull(copy);
        Assert.Equal(original.Command, copy!.Command);
        Assert.Equal(original.AuthorizationToken, copy.AuthorizationToken);
    }

    [Fact]
    public void Remote_session_authorization_requires_matching_admin_and_permission()
    {
        var authorization = new RemoteSessionAuthorization
        {
            AdminId = "admin-a",
            AuthorizationToken = "secret-token",
            Permission = RemoteSessionPermission.ViewAndControl,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5)
        };

        Assert.Null(RemoteSessionAuthorizationPolicy.Validate(
            authorization, "secret-token", RemoteSessionPermission.Control, DateTime.UtcNow));
        Assert.Contains("invalid", RemoteSessionAuthorizationPolicy.Validate(
            new RemoteSessionAuthorization
            {
                AdminId = authorization.AdminId,
                AuthorizationToken = "wrong-token",
                Permission = authorization.Permission,
                ExpiresAtUtc = authorization.ExpiresAtUtc
            }, "secret-token", RemoteSessionPermission.Control, DateTime.UtcNow));
        Assert.Contains("insufficient", RemoteSessionAuthorizationPolicy.Validate(
            new RemoteSessionAuthorization
            {
                AdminId = authorization.AdminId,
                AuthorizationToken = authorization.AuthorizationToken,
                Permission = RemoteSessionPermission.ViewOnly,
                ExpiresAtUtc = authorization.ExpiresAtUtc
            }, "secret-token", RemoteSessionPermission.Control, DateTime.UtcNow));
    }

    [Fact]
    public void Expired_remote_session_authorization_is_rejected()
    {
        var authorization = new RemoteSessionAuthorization
        {
            AdminId = "admin-a",
            AuthorizationToken = "secret-token",
            Permission = RemoteSessionPermission.ViewOnly,
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1)
        };

        Assert.Contains("expired", RemoteSessionAuthorizationPolicy.Validate(
            authorization, "secret-token", RemoteSessionPermission.ViewOnly, DateTime.UtcNow));
    }
}
