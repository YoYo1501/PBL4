namespace NetworkAdmin.Client.Infrastructure.Security;

internal sealed record SecurityInferenceResult(
    long Label,
    float AnomalyScore,
    bool IsAnomaly,
    double? Confidence,
    string ConfidenceStatus,
    string ArtifactFingerprint);
