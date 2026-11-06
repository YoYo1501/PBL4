using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NetworkAdmin.Client.Infrastructure.Security;

internal sealed class SecurityPreprocessedVector
{
    public SecurityPreprocessedVector(float[] values, string artifactFingerprint)
    {
        Values = values;
        ArtifactFingerprint = artifactFingerprint;
    }

    public float[] Values { get; }
    public string ArtifactFingerprint { get; }
}

internal sealed class SecurityPreprocessor
{
    private static readonly Regex ImageTokenPattern = new("[A-Za-z0-9_]+", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex CommandTokenPattern = new("\\b[A-Za-z0-9_]+\\b", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex CleanTextPattern = new("[^a-z0-9\\s\\-_\\\\/:.]", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex MultiSpacePattern = new("\\s+", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly SecurityPreprocessingArtifacts _artifacts;
    private readonly ConcurrentDictionary<string, int> _userEventCounts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _processFrequencyCounts = new(StringComparer.Ordinal);

    public SecurityPreprocessor(SecurityPreprocessingArtifacts artifacts) => _artifacts = artifacts;

    public SecurityPreprocessedVector Transform(SecurityRawFeatures raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var vector = new double[SecurityFeatureSchema.ModelFeatureCount];
        var index = 0;
        var localTime = ParseLocalTime(raw.Timestamp);
        var weekday = ((int)localTime.DayOfWeek + 6) % 7; // Java Monday=0, Sunday=6.
        var hour = localTime.Hour;
        vector[index++] = raw.EventCode;
        vector[index++] = hour;
        vector[index++] = weekday;
        vector[index++] = weekday >= 5 ? 1 : 0;
        vector[index++] = hour < 6 || hour > 22 ? 1 : 0;
        vector[index++] = hour >= 9 && hour <= 17 && weekday < 5 ? 1 : 0;
        vector[index++] = Math.Sin(2 * Math.PI * hour / 24);
        vector[index++] = Math.Cos(2 * Math.PI * hour / 24);
        vector[index++] = Math.Sin(2 * Math.PI * weekday / 7);
        vector[index++] = Math.Cos(2 * Math.PI * weekday / 7);

        var user = string.IsNullOrEmpty(raw.User) ? "Unknown" : raw.User;
        var process = raw.ProcessName;
        var parent = raw.ParentName;
        var commandLine = raw.CommandLine;
        vector[index++] = Encode("user", user);
        vector[index++] = Encode("process_name", process);
        vector[index++] = Encode("parent_name", parent);
        vector[index++] = _artifacts.SuspiciousProcesses.Contains(process.ToLowerInvariant()) ? 1 : 0;
        vector[index++] = CountSuspiciousCommandLine(commandLine);
        vector[index++] = _artifacts.SuspiciousParentChild.Contains((parent.ToLowerInvariant(), process.ToLowerInvariant())) ? 1 : 0;
        vector[index++] = _userEventCounts.AddOrUpdate(user, 1, static (_, count) => count + 1);
        vector[index++] = _processFrequencyCounts.AddOrUpdate(process, 1, static (_, count) => count + 1);
        vector[index++] = raw.DestinationPort;

        var imageVector = ComputeTfidf(CleanText(raw.ImagePath), _artifacts.ImageVocabulary, _artifacts.ImageIdf,
            30, ImageTokenPattern);
        foreach (var value in imageVector) vector[index++] = value;
        var commandVector = ComputeTfidf(CleanText(commandLine), _artifacts.CommandLineVocabulary,
            _artifacts.CommandLineIdf, 20, CommandTokenPattern);
        foreach (var value in commandVector) vector[index++] = value;

        if (index != SecurityFeatureSchema.ModelFeatureCount) throw new InvalidDataException("Preprocessor produced an unexpected feature count.");
        for (var i = 0; i < vector.Length; i++)
            if (_artifacts.ScalerScale[i] != 0) vector[i] = (vector[i] - _artifacts.ScalerMean[i]) / _artifacts.ScalerScale[i];
        return new SecurityPreprocessedVector(vector.Select(value => checked((float)value)).ToArray(), _artifacts.ArtifactFingerprint);
    }

    public void ResetCounters()
    {
        _userEventCounts.Clear();
        _processFrequencyCounts.Clear();
    }

    private double Encode(string field, string value)
    {
        if (!_artifacts.LabelEncoders.TryGetValue(field, out var classes)) return 0;
        var index = classes.Select((item, itemIndex) => (item, itemIndex))
            .FirstOrDefault(pair => string.Equals(pair.item, value, StringComparison.Ordinal)).itemIndex;
        return index >= 0 ? index : 0;
    }

    private int CountSuspiciousCommandLine(string commandLine)
    {
        var lower = commandLine.ToLowerInvariant();
        return _artifacts.SuspiciousCommandLinePatterns.Count(pattern => lower.Contains(pattern, StringComparison.Ordinal));
    }

    private static DateTime ParseLocalTime(string timestamp)
    {
        if (DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            return TimeZoneInfo.ConvertTime(parsed, TimeZoneInfo.Local).DateTime;
        return DateTime.Now;
    }

    private static string CleanText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var lowered = text.ToLowerInvariant();
        return MultiSpacePattern.Replace(CleanTextPattern.Replace(lowered, " "), " ").Trim();
    }

    private static double[] ComputeTfidf(string text, IReadOnlyDictionary<string, int> vocabulary,
        IReadOnlyList<double> idf, int maxFeatures, Regex tokenPattern)
    {
        var result = new double[maxFeatures];
        if (string.IsNullOrEmpty(text)) return result;
        var tokens = tokenPattern.Matches(text).Select(match => match.Value).ToArray();
        if (tokens.Length == 0) return result;
        var counts = tokens.GroupBy(token => token, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (double)group.Count(), StringComparer.Ordinal);
        foreach (var token in counts.Keys.ToArray()) counts[token] /= tokens.Length;
        foreach (var pair in counts)
            if (vocabulary.TryGetValue(pair.Key, out var index) && index >= 0 && index < maxFeatures && index < idf.Count)
                result[index] = pair.Value * idf[index];
        var norm = Math.Sqrt(result.Sum(value => value * value));
        if (norm > 0) for (var i = 0; i < result.Length; i++) result[i] /= norm;
        return result;
    }
}
