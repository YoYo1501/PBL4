using System.Text.Json;

namespace NetworkAdmin.Client.Infrastructure.Security;

internal static class SecurityFeatureInputParser
{
    public static bool TryParse(string json, out SecurityRawFeatures? features)
    {
        features = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var winlog = Property(root, "winlog");
            var eventData = Property(winlog, "event_data");

            var imagePath = Text(eventData, "Image");
            var parentImage = Text(eventData, "ParentImage");
            features = new SecurityRawFeatures(
                Integer(Text(winlog, "event_id")),
                Text(root, "@timestamp"),
                Text(eventData, "User", "Unknown"),
                imagePath,
                SecurityProcessName.Normalize(imagePath),
                parentImage,
                SecurityProcessName.NormalizeParent(parentImage),
                Text(eventData, "CommandLine"),
                Integer(Text(eventData, "DestinationPort")));
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

    private static string Text(JsonElement parent, string name, string fallback = "")
    {
        var value = Property(parent, name);
        if (value.ValueKind == JsonValueKind.String) return value.GetString() ?? fallback;
        if (value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            return value.ToString();
        return fallback;
    }

    private static int Integer(string value) =>
        int.TryParse(value, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var number)
            ? Math.Clamp(number, 0, 65_535) : 0;
}

internal static class SecurityFeatureExtractor
{
    public static bool TryExtract(string json, out SecurityRawFeatures? features) =>
        SecurityFeatureInputParser.TryParse(json, out features);

    public static SecurityRawFeatures FromMessage(NetworkAdmin.Shared.Messages.SecurityEventMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new SecurityRawFeatures(
            message.EventCode,
            message.DetectionTimestampUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            string.IsNullOrEmpty(message.UserName) ? "Unknown" : message.UserName,
            message.ImagePath,
            SecurityProcessName.Normalize(message.ImagePath),
            message.ParentProcessName,
            SecurityProcessName.NormalizeParent(message.ParentProcessName),
            message.CommandLine,
            Math.Clamp(message.DestinationPort, 0, 65_535));
    }
}

internal static class SecurityProcessName
{
    public static string Normalize(string? imagePath)
    {
        if (string.IsNullOrEmpty(imagePath)) return string.Empty;
        var normalizedPath = imagePath.Replace('\\', '/').TrimEnd('/');
        var separator = normalizedPath.LastIndexOf('/');
        var name = separator >= 0 ? normalizedPath[(separator + 1)..] : normalizedPath;
        return name.ToLowerInvariant();
    }

    public static string NormalizeParent(string? parentImage)
    {
        var name = Normalize(parentImage);
        return name.Length == 0 ? "-" : name;
    }
}
