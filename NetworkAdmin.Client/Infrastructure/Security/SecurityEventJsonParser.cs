using System.Globalization;
using System.Text.Json;

namespace NetworkAdmin.Client.Infrastructure.Security;

internal static class SecurityEventJsonParser
{
    public static bool TryParse(string json, out SecurityEventRecord? record)
    {
        record = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var eventNode = Property(root, "event");
            var winlogNode = Property(root, "winlog");
            var eventData = Property(winlogNode, "event_data");
            var process = Property(root, "process");
            var user = Property(root, "user");
            var destination = Property(root, "destination");

            var eventId = FirstText(
                Text(winlogNode, "event_id"), Text(eventNode, "id"), Text(eventNode, "code"),
                Text(eventData, "EventId")) ?? Guid.NewGuid().ToString("N");
            var eventCode = FirstInt(Text(winlogNode, "event_id"), Text(eventNode, "code"), Text(eventData, "EventId"));
            var timestamp = ParseTimestamp(Text(root, "@timestamp"))
                ?? DateTime.UtcNow;
            var imagePath = FirstText(Text(eventData, "Image"), Text(process, "executable")) ?? string.Empty;
            var parentImage = Text(eventData, "ParentImage") ?? string.Empty;
            var processName = FirstText(FileName(imagePath), Text(process, "name")) ?? string.Empty;
            var parentProcessName = FirstText(FileName(parentImage), Text(eventData, "ParentProcessName")) ?? string.Empty;

            record = new SecurityEventRecord(
                eventId,
                timestamp,
                FirstText(Text(winlogNode, "provider_name"), Text(eventNode, "provider"), "ndjson")!,
                eventCode,
                FirstText(Text(eventData, "User"), Text(eventData, "UserName"), Text(user, "name"),
                    Text(Property(winlogNode, "user"), "name")) ?? string.Empty,
                processName,
                parentProcessName,
                imagePath,
                FirstText(Text(eventData, "CommandLine"), Text(process, "command_line")) ?? string.Empty,
                FirstInt(Text(eventData, "DestinationPort"), Text(destination, "port")));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static JsonElement Property(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object) return default;
        foreach (var property in parent.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return property.Value;
        return default;
    }

    private static string? Text(JsonElement parent, string name)
    {
        var value = Property(parent, name);
        if (value.ValueKind == JsonValueKind.String) return value.GetString();
        if (value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            return value.ToString();
        return null;
    }

    private static string? FirstText(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static int FirstInt(params string?[] values)
    {
        foreach (var value in values)
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return Math.Clamp(parsed, 0, 65_535);
        return 0;
    }

    private static DateTime? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp)
            ? timestamp.UtcDateTime : null;

    private static string? FileName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return Path.GetFileName(path); }
        catch (ArgumentException) { return null; }
    }
}
