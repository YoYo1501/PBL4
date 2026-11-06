using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Collections.Concurrent;

namespace NetworkAdmin.Client.Infrastructure.Security;

internal sealed class SecurityInferenceEngine : IAsyncDisposable
{
    private readonly SecurityPreprocessingArtifacts _artifacts;
    private readonly InferenceSession _session;
    private readonly SemaphoreSlim _inferenceGate = new(1, 1);
    private readonly ConcurrentDictionary<Task<SecurityInferenceResult>, byte> _activeRuns = new();
    private int _disposed;

    private SecurityInferenceEngine(SecurityPreprocessingArtifacts artifacts, InferenceSession session)
    {
        _artifacts = artifacts;
        _session = session;
    }

    public string ArtifactFingerprint => _artifacts.ArtifactFingerprint;
    public string? DeclaredModelVersion => _artifacts.DeclaredModelVersion;
    public int? OpsetVersion => _artifacts.OpsetVersion;

    public static SecurityInferenceEngine Load(string artifactDirectory)
    {
        var artifacts = SecurityPreprocessingArtifacts.Load(artifactDirectory);
        var modelPath = Path.Combine(artifactDirectory, "isolation_forest_model.onnx");
        var session = new InferenceSession(modelPath);
        try
        {
            ValidateModelContract(session, artifacts);
            return new SecurityInferenceEngine(artifacts, session);
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    public async Task<SecurityInferenceResult> InferAsync(
        SecurityPreprocessedVector vector,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (vector.Values.Length != SecurityFeatureSchema.ModelFeatureCount)
            throw new SecurityModelContractException("Preprocessed vector must contain exactly 69 values.");
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (vector.Values.Any(value => float.IsNaN(value) || float.IsInfinity(value)))
            throw new SecurityModelContractException("Preprocessed vector contains a non-finite value.");

        var task = RunAsync(vector, cancellationToken);
        _activeRuns.TryAdd(task, 0);
        _ = task.ContinueWith(completed => _activeRuns.TryRemove(completed, out _),
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        try
        {
            return await task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new SecurityInferenceTimeoutException($"ONNX inference exceeded {timeout.TotalMilliseconds:0} ms.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        var active = _activeRuns.Keys.ToArray();
        try { await Task.WhenAll(active).ConfigureAwait(false); }
        catch (Exception) { }
        _session.Dispose();
        _inferenceGate.Dispose();
    }

    private async Task<SecurityInferenceResult> RunAsync(SecurityPreprocessedVector vector, CancellationToken cancellationToken)
    {
        await _inferenceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var tensor = new DenseTensor<float>(vector.Values, new[] { 1, SecurityFeatureSchema.ModelFeatureCount });
            var input = NamedOnnxValue.CreateFromTensor(_artifacts.InputName, tensor);
            using var outputs = _session.Run(new[] { input });
            var labelOutput = outputs.FirstOrDefault(output => string.Equals(output.Name, "label", StringComparison.Ordinal));
            var scoreOutput = outputs.FirstOrDefault(output => string.Equals(output.Name, "scores", StringComparison.Ordinal));
            if (labelOutput is null || scoreOutput is null)
                throw new SecurityModelContractException("ONNX outputs label/scores were not returned.");

            var labelTensor = labelOutput.AsTensor<long>();
            var scoreTensor = scoreOutput.AsTensor<float>();
            if (labelTensor.Length != 1 || scoreTensor.Length != 1)
                throw new SecurityModelContractException("ONNX outputs must contain one label and one score.");
            var label = labelTensor.GetValue(0);
            var score = scoreTensor.GetValue(0);
            return new SecurityInferenceResult(label, score, label == -1, null,
                "Not calibrated; anomaly score is not a probability.", vector.ArtifactFingerprint);
        }
        finally
        {
            _inferenceGate.Release();
        }
    }

    private static void ValidateModelContract(InferenceSession session, SecurityPreprocessingArtifacts artifacts)
    {
        var input = session.InputMetadata;
        if (!input.TryGetValue(artifacts.InputName, out var inputMetadata))
            throw new SecurityModelContractException($"ONNX input '{artifacts.InputName}' is missing.");
        if (inputMetadata.ElementType != typeof(float) || inputMetadata.Dimensions.Count() != 2 ||
            inputMetadata.Dimensions[0] > 0 && inputMetadata.Dimensions[0] != 1 ||
            inputMetadata.Dimensions[1] > 0 && inputMetadata.Dimensions[1] != SecurityFeatureSchema.ModelFeatureCount)
            throw new SecurityModelContractException("ONNX input must be float [1,69].");

        var outputs = session.OutputMetadata.Keys.ToHashSet(StringComparer.Ordinal);
        if (!outputs.Contains("label") || !outputs.Contains("scores"))
            throw new SecurityModelContractException("ONNX model must expose label and scores outputs.");
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(SecurityInferenceEngine));
    }
}
