using System.Text.Json;
using NetworkAdmin.Client.Infrastructure.Security;
using NetworkAdmin.Shared.Messages;
using Xunit;

namespace NetworkAdmin.Client.Tests;

public sealed class SecurityFeatureExtractorTests
{
    [Fact]
    public void Golden_fixture_matches_RemoteX_raw_feature_extraction()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "security-feature-golden.json");
        var json = File.ReadAllText(path);

        Assert.True(SecurityFeatureExtractor.TryExtract(json, out var features));
        Assert.NotNull(features);
        Assert.Equal(1, features.EventCode);
        Assert.Equal("2025-01-15T14:30:00Z", features.Timestamp);
        Assert.Equal("FIXTURE\\Analyst", features.User);
        Assert.Equal(@"C:\Program Files\Demo\demo.exe", features.ImagePath);
        Assert.Equal("demo.exe", features.ProcessName);
        Assert.Equal(@"C:\Windows\explorer.exe", features.ParentImage);
        Assert.Equal("explorer.exe", features.ParentName);
        Assert.Equal("demo.exe --mode test", features.CommandLine);
        Assert.Equal(443, features.DestinationPort);
    }

    [Fact]
    public void Missing_fields_use_the_same_defaults_as_RemoteX()
    {
        var json = JsonSerializer.Serialize(new
        {
            winlog = new { event_data = new { Image = @"C:\Tools\工具.EXE" } }
        });

        Assert.True(SecurityFeatureExtractor.TryExtract(json, out var features));
        Assert.NotNull(features);
        Assert.Equal(0, features.EventCode);
        Assert.Equal(string.Empty, features.Timestamp);
        Assert.Equal("Unknown", features.User);
        Assert.Equal("工具.exe", features.ProcessName);
        Assert.Equal("-", features.ParentName);
        Assert.Equal(string.Empty, features.CommandLine);
        Assert.Equal(0, features.DestinationPort);
    }

    [Fact]
    public void Malformed_json_is_rejected_without_partial_features()
    {
        Assert.False(SecurityFeatureExtractor.TryExtract("{not-json", out var features));
        Assert.Null(features);
    }

    [Fact]
    public void Model_schema_has_exactly_the_69_RemoteX_features_in_order()
    {
        Assert.Equal(SecurityFeatureSchema.ModelFeatureCount, SecurityFeatureSchema.ModelFeatureNames.Count);
        Assert.Equal("event_code", SecurityFeatureSchema.ModelFeatureNames[0]);
        Assert.Equal("dest_port", SecurityFeatureSchema.ModelFeatureNames[18]);
        Assert.Equal("image_tfidf_0", SecurityFeatureSchema.ModelFeatureNames[19]);
        Assert.Equal("image_tfidf_29", SecurityFeatureSchema.ModelFeatureNames[48]);
        Assert.Equal("cmdline_tfidf_0", SecurityFeatureSchema.ModelFeatureNames[49]);
        Assert.Equal("cmdline_tfidf_19", SecurityFeatureSchema.ModelFeatureNames[68]);
        Assert.Equal(9, SecurityFeatureSchema.RawFeatureNames.Count);
    }

    [Fact]
    public void Security_event_contract_can_be_adapted_without_adding_model_features()
    {
        var message = new SecurityEventMessage
        {
            ClientId = "client-a",
            SessionId = "session-a",
            EventId = "event-1",
            DetectionTimestampUtc = DateTime.Parse("2025-01-15T14:30:00Z").ToUniversalTime(),
            UserName = "FIXTURE\\Analyst",
            ImagePath = @"C:\Program Files\Demo\demo.exe",
            ParentProcessName = "explorer.exe",
            CommandLine = "demo.exe --mode test",
            EventCode = 1,
            DestinationPort = 443
        };

        var features = SecurityFeatureExtractor.FromMessage(message);
        Assert.Equal("demo.exe", features.ProcessName);
        Assert.Equal("explorer.exe", features.ParentName);
        Assert.Equal(9, SecurityFeatureSchema.RawFeatureNames.Count);
    }
}
