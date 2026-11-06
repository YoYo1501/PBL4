using System.Security.Cryptography;
using System.Text.Json;

namespace NetworkAdmin.Client.Infrastructure.Security;

internal sealed class SecurityPreprocessingArtifacts
{
    private SecurityPreprocessingArtifacts(
        string directory,
        IReadOnlyDictionary<string, IReadOnlyList<string>> labelEncoders,
        IReadOnlyDictionary<string, int> imageVocabulary,
        IReadOnlyList<double> imageIdf,
        IReadOnlyDictionary<string, int> commandLineVocabulary,
        IReadOnlyList<double> commandLineIdf,
        IReadOnlyList<double> scalerMean,
        IReadOnlyList<double> scalerScale,
        IReadOnlySet<string> suspiciousProcesses,
        IReadOnlyList<string> suspiciousCommandLinePatterns,
        IReadOnlySet<(string Parent, string Child)> suspiciousParentChild,
        string inputName,
        IReadOnlyList<string> outputNames,
        int? opsetVersion,
        double? contamination,
        int? estimatorCount,
        string? declaredModelVersion,
        string artifactFingerprint)
    {
        Directory = directory;
        LabelEncoders = labelEncoders;
        ImageVocabulary = imageVocabulary;
        ImageIdf = imageIdf;
        CommandLineVocabulary = commandLineVocabulary;
        CommandLineIdf = commandLineIdf;
        ScalerMean = scalerMean;
        ScalerScale = scalerScale;
        SuspiciousProcesses = suspiciousProcesses;
        SuspiciousCommandLinePatterns = suspiciousCommandLinePatterns;
        SuspiciousParentChild = suspiciousParentChild;
        InputName = inputName;
        OutputNames = outputNames;
        OpsetVersion = opsetVersion;
        Contamination = contamination;
        EstimatorCount = estimatorCount;
        DeclaredModelVersion = declaredModelVersion;
        ArtifactFingerprint = artifactFingerprint;
    }

    public string Directory { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> LabelEncoders { get; }
    public IReadOnlyDictionary<string, int> ImageVocabulary { get; }
    public IReadOnlyList<double> ImageIdf { get; }
    public IReadOnlyDictionary<string, int> CommandLineVocabulary { get; }
    public IReadOnlyList<double> CommandLineIdf { get; }
    public IReadOnlyList<double> ScalerMean { get; }
    public IReadOnlyList<double> ScalerScale { get; }
    public IReadOnlySet<string> SuspiciousProcesses { get; }
    public IReadOnlyList<string> SuspiciousCommandLinePatterns { get; }
    public IReadOnlySet<(string Parent, string Child)> SuspiciousParentChild { get; }
    public string InputName { get; }
    public IReadOnlyList<string> OutputNames { get; }
    public int? OpsetVersion { get; }
    public double? Contamination { get; }
    public int? EstimatorCount { get; }
    public string? DeclaredModelVersion { get; }
    public string ArtifactFingerprint { get; }

    public static SecurityPreprocessingArtifacts Load(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Artifact directory is required.", nameof(directory));
        if (!System.IO.Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);

        var featureNames = ReadStringArray(Path.Combine(directory, "feature_names.json"), "feature_names");
        if (featureNames.Count != SecurityFeatureSchema.ModelFeatureCount ||
            !featureNames.SequenceEqual(SecurityFeatureSchema.ModelFeatureNames))
            throw new InvalidDataException("Feature artifact order/count does not match the Task 10 schema.");

        using var metadata = ReadDocument(Path.Combine(directory, "feature_metadata.json"));
        var labelEncoders = ReadLabelEncoders(Path.Combine(directory, "label_encoders.json"));
        var image = ReadTfidf(Path.Combine(directory, "tfidf_image.json"));
        var commandLine = ReadTfidf(Path.Combine(directory, "tfidf_cmdline.json"));
        var scaler = ReadScaler(Path.Combine(directory, "scaler.json"));
        using var onnx = ReadDocument(Path.Combine(directory, "onnx_metadata.json"));
        var modelPath = Path.Combine(directory, "isolation_forest_model.onnx");
        if (!File.Exists(modelPath)) throw new FileNotFoundException("ONNX model artifact is missing.", modelPath);

        if (scaler.Mean.Count != SecurityFeatureSchema.ModelFeatureCount ||
            scaler.Scale.Count != SecurityFeatureSchema.ModelFeatureCount)
            throw new InvalidDataException("Scaler length does not match the 69-feature model.");
        if (image.Idf.Count != 30 || commandLine.Idf.Count != 20)
            throw new InvalidDataException("TF-IDF artifact lengths do not match the model schema.");

        var onnxFeatureCount = IntProperty(onnx, "n_features");
        if (onnxFeatureCount != SecurityFeatureSchema.ModelFeatureCount)
            throw new InvalidDataException("ONNX metadata feature count does not match the model schema.");

        var inputName = StringProperty(onnx, "input_name") ?? "float_input";
        var outputNames = ReadStringArray(onnx, "output_names");
        if (!outputNames.SequenceEqual(new[] { "label", "scores" }, StringComparer.Ordinal))
            throw new InvalidDataException("ONNX metadata output names must be label and scores.");

        var suspiciousProcesses = ReadLowercaseStringArray(metadata, "suspicious_processes").ToHashSet(StringComparer.Ordinal);
        var suspiciousPatterns = ReadLowercaseStringArray(metadata, "suspicious_cmdline_patterns");
        var suspiciousPairs = new HashSet<(string Parent, string Child)>();
        var pairs = Property(metadata.RootElement, "suspicious_parent_child");
        if (pairs.ValueKind == JsonValueKind.Array)
            foreach (var pair in pairs.EnumerateArray())
                if (pair.ValueKind == JsonValueKind.Array && pair.GetArrayLength() >= 2)
                    suspiciousPairs.Add((pair[0].GetString()?.ToLowerInvariant() ?? string.Empty,
                        pair[1].GetString()?.ToLowerInvariant() ?? string.Empty));

        var declaredVersion = StringProperty(onnx, "model_version");
        var fingerprint = ComputeFingerprint(modelPath);
        return new SecurityPreprocessingArtifacts(
            Path.GetFullPath(directory), labelEncoders, image.Vocabulary, image.Idf,
            commandLine.Vocabulary, commandLine.Idf, scaler.Mean, scaler.Scale,
            suspiciousProcesses, suspiciousPatterns, suspiciousPairs, inputName, outputNames,
            IntProperty(onnx, "opset_version"), NumberProperty(onnx, "contamination"),
            IntProperty(onnx, "n_estimators"), declaredVersion, fingerprint);
    }

    private static JsonDocument ReadDocument(string path) =>
        JsonDocument.Parse(File.ReadAllText(path));

    private static IReadOnlyList<string> ReadStringArray(string path, string property)
    {
        using var document = ReadDocument(path);
        return ReadStringArray(document.RootElement, property);
    }

    private static IReadOnlyList<string> ReadStringArray(JsonDocument document, string property) =>
        ReadStringArray(document.RootElement, property);

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string property)
    {
        var value = Property(root, property);
        return value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? string.Empty).ToArray()
            : Array.Empty<string>();
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ReadLabelEncoders(string path)
    {
        using var document = ReadDocument(path);
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
            result[property.Name] = ReadStringArray(property.Value, "classes");
        return result;
    }

    private static (IReadOnlyDictionary<string, int> Vocabulary, IReadOnlyList<double> Idf) ReadTfidf(string path)
    {
        using var document = ReadDocument(path);
        var vocabulary = new Dictionary<string, int>(StringComparer.Ordinal);
        var vocabularyNode = Property(document.RootElement, "vocabulary");
        if (vocabularyNode.ValueKind == JsonValueKind.Object)
            foreach (var property in vocabularyNode.EnumerateObject())
                if (property.Value.TryGetInt32(out var index)) vocabulary[property.Name] = index;
        return (vocabulary, ReadNumberArray(document.RootElement, "idf"));
    }

    private static (IReadOnlyList<double> Mean, IReadOnlyList<double> Scale) ReadScaler(string path)
    {
        using var document = ReadDocument(path);
        return (ReadNumberArray(document.RootElement, "mean"), ReadNumberArray(document.RootElement, "scale"));
    }

    private static IReadOnlyList<double> ReadNumberArray(JsonElement root, string property)
    {
        var value = Property(root, property);
        return value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Number)
                .Select(item => item.GetDouble()).ToArray()
            : Array.Empty<double>();
    }

    private static IReadOnlyList<string> ReadLowercaseStringArray(JsonDocument document, string property) =>
        ReadStringArray(document.RootElement, property).Select(value => value.ToLowerInvariant()).ToArray();

    private static JsonElement Property(JsonElement root, string property) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out var value) ? value : default;

    private static string? StringProperty(JsonDocument document, string property) =>
        Property(document.RootElement, property).ValueKind == JsonValueKind.String
            ? Property(document.RootElement, property).GetString() : null;

    private static int? IntProperty(JsonDocument document, string property) =>
        Property(document.RootElement, property).TryGetInt32(out var value) ? value : null;

    private static double? NumberProperty(JsonDocument document, string property) =>
        Property(document.RootElement, property).TryGetDouble(out var value) ? value : null;

    private static string ComputeFingerprint(string modelPath)
    {
        using var stream = File.OpenRead(modelPath);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
