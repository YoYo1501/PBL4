using System.Diagnostics;
using NetworkAdmin.Client.Infrastructure.Security;
using Xunit;
using Xunit.Abstractions;

namespace NetworkAdmin.Client.Tests;

public sealed class SecurityPreprocessingTests
{
    private readonly ITestOutputHelper _output;

    public SecurityPreprocessingTests(ITestOutputHelper output) => _output = output;

    private static string ArtifactDirectory => Path.Combine(AppContext.BaseDirectory, "SecurityModel");

    [Fact]
    public void Loads_the_real_RemoteX_artifacts_without_changing_model_order()
    {
        var artifacts = SecurityPreprocessingArtifacts.Load(ArtifactDirectory);

        Assert.Equal(69, SecurityFeatureSchema.ModelFeatureCount);
        Assert.Equal(SecurityFeatureSchema.ModelFeatureNames, LoadFeatureNames());
        Assert.Equal("float_input", artifacts.InputName);
        Assert.Equal(new[] { "label", "scores" }, artifacts.OutputNames);
        Assert.Equal(12, artifacts.OpsetVersion);
        Assert.Equal(100, artifacts.EstimatorCount);
        Assert.Equal(0.05, artifacts.Contamination);
        Assert.Null(artifacts.DeclaredModelVersion);
        Assert.Matches("^[0-9a-f]{64}$", artifacts.ArtifactFingerprint);
    }

    [Fact]
    public void Golden_fixture_matches_the_reference_preprocessing_prefix()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "security-feature-golden.json"));
        Assert.True(SecurityFeatureExtractor.TryExtract(json, out var raw));
        Assert.NotNull(raw);

        var artifacts = SecurityPreprocessingArtifacts.Load(ArtifactDirectory);
        var vector = new SecurityPreprocessor(artifacts).Transform(raw);

        // Independent reference calculation from RemoteX's Java/Python pipeline,
        // after StandardScaler, using the local zone (SE Asia Standard Time).
        var expected = new[]
        {
            -0.5292626702f, 23.6422700371f, 0f, 0f, 0f, -1f,
            -9.8157351234f, 38.0453637234f, -2.2204460493e-16f, -2.7755575616e-17f,
            -0.1562123115f, -1.4429874223f, 1.2383064f, -0.1053915142f,
            -0.0447362741f, 0f, -6.4270140406f, -1.2788357779f,
            2.7927293505f
        };

        Assert.Equal(69, vector.Values.Length);
        for (var i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], vector.Values[i], 3);
        Assert.All(vector.Values, value => Assert.False(float.IsNaN(value) || float.IsInfinity(value)));
    }

    [Fact]
    public void Missing_values_produce_a_finite_fixed_width_vector()
    {
        var artifacts = SecurityPreprocessingArtifacts.Load(ArtifactDirectory);
        var vector = new SecurityPreprocessor(artifacts).Transform(new SecurityRawFeatures(
            0, string.Empty, "Unknown", string.Empty, string.Empty, string.Empty, "-", string.Empty, 0));

        Assert.Equal(69, vector.Values.Length);
        Assert.All(vector.Values, value => Assert.False(float.IsNaN(value) || float.IsInfinity(value)));
    }

    [Fact]
    public void Benchmark_transforms_a_realistic_fixture_batch()
    {
        var artifacts = SecurityPreprocessingArtifacts.Load(ArtifactDirectory);
        var preprocessor = new SecurityPreprocessor(artifacts);
        var raw = new SecurityRawFeatures(1, "2025-01-15T14:30:00Z", "FIXTURE\\Analyst",
            @"C:\Program Files\Demo\demo.exe", "demo.exe", @"C:\Windows\explorer.exe",
            "explorer.exe", "demo.exe --mode test", 443);
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < 1_000; i++) _ = preprocessor.Transform(raw);
        stopwatch.Stop();

        // The elapsed value is intentionally diagnostic, not a machine-specific threshold.
        var millisecondsPerEvent = stopwatch.Elapsed.TotalMilliseconds / 1_000;
        _output.WriteLine($"SecurityPreprocessor: 1000 transforms in {stopwatch.Elapsed.TotalMilliseconds:0.###} ms ({millisecondsPerEvent:0.######} ms/event)");
        Assert.True(stopwatch.Elapsed >= TimeSpan.Zero);
    }

    private static IReadOnlyList<string> LoadFeatureNames()
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(ArtifactDirectory, "feature_names.json")));
        return document.RootElement.GetProperty("feature_names").EnumerateArray()
            .Select(value => value.GetString() ?? string.Empty).ToArray();
    }
}
