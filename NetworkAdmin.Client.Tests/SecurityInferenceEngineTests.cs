using NetworkAdmin.Client.Infrastructure.Security;
using Xunit;

namespace NetworkAdmin.Client.Tests;

public sealed class SecurityInferenceEngineTests
{
    private static string ArtifactDirectory => Path.Combine(AppContext.BaseDirectory, "SecurityModel");

    [Fact]
    public async Task Real_model_loads_and_returns_label_and_raw_score()
    {
        await using var engine = SecurityInferenceEngine.Load(ArtifactDirectory);
        var artifacts = SecurityPreprocessingArtifacts.Load(ArtifactDirectory);
        var vector = new SecurityPreprocessor(artifacts).Transform(new SecurityRawFeatures(
            1, "2025-01-15T14:30:00Z", "FIXTURE\\Analyst", @"C:\Program Files\Demo\demo.exe",
            "demo.exe", @"C:\Windows\explorer.exe", "explorer.exe", "demo.exe --mode test", 443));

        var result = await engine.InferAsync(vector, TimeSpan.FromSeconds(5));

        Assert.Contains(result.Label, new[] { -1L, 1L });
        Assert.False(float.IsNaN(result.AnomalyScore) || float.IsInfinity(result.AnomalyScore));
        Assert.Equal(result.Label == -1, result.IsAnomaly);
        Assert.Null(result.Confidence);
        Assert.Contains("not a probability", result.ConfidenceStatus, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(vector.ArtifactFingerprint, result.ArtifactFingerprint);
    }

    [Fact]
    public async Task Invalid_vector_is_rejected_before_inference()
    {
        await using var engine = SecurityInferenceEngine.Load(ArtifactDirectory);
        var invalid = new SecurityPreprocessedVector(new float[68], engine.ArtifactFingerprint);

        await Assert.ThrowsAsync<SecurityModelContractException>(() =>
            engine.InferAsync(invalid, TimeSpan.FromSeconds(1)));
    }
}
