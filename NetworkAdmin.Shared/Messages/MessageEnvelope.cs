using System.Text.Json;

namespace NetworkAdmin.Shared.Messages;

public sealed class MessageEnvelope
{
    public MessageType Type { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public JsonElement Data { get; set; }
}
