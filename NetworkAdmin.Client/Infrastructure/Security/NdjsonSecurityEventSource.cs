using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace NetworkAdmin.Client.Infrastructure.Security;

internal sealed class NdjsonSecurityEventSource : ISecurityEventSource
{
    private const int ReadChunkBytes = 64 * 1024;
    private const int MaxReadPerPollBytes = 256 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly SecurityEventCollectorOptions _options;
    private readonly Action<string>? _warning;
    private readonly List<byte> _partialLine = new();
    private string? _currentFile;
    private long _offset;
    private string? _lastWarning;
    private DateTime _lastWarningAtUtc;

    public NdjsonSecurityEventSource(SecurityEventCollectorOptions options, Action<string>? warning = null)
    {
        _options = options;
        _warning = warning;
    }

    public async IAsyncEnumerable<SecurityEventRecord> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var latest = FindLatestFile();
            if (latest is null)
            {
                Warn("NDJSON source is unavailable.");
                await Task.Delay(_options.PollInterval, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!string.Equals(_currentFile, latest, StringComparison.OrdinalIgnoreCase))
            {
                _currentFile = latest;
                _partialLine.Clear();
                _offset = LoadCheckpoint(latest);
            }

            var parsedRecords = new List<SecurityEventRecord>();
            try
            {
                var length = new FileInfo(latest).Length;
                if (length < _offset + _partialLine.Count)
                {
                    // The file was truncated or replaced in place.
                    _offset = 0;
                    _partialLine.Clear();
                }

                var bytes = await ReadBytesAsync(latest, _offset + _partialLine.Count, cancellationToken)
                    .ConfigureAwait(false);
                if (bytes.Length > 0)
                    _partialLine.AddRange(bytes);

                while (TryTakeLine(out var lineBytes))
                {
                    _offset += lineBytes.ConsumedBytes;
                    if (lineBytes.Content.Length == 0) continue;

                    string line;
                    try { line = StrictUtf8.GetString(lineBytes.Content).Trim(); }
                    catch (DecoderFallbackException)
                    {
                        Warn("NDJSON source contained invalid UTF-8.");
                        continue;
                    }

                    if (line.Length == 0) continue;
                    if (lineBytes.Content.Length > _options.MaxLineBytes)
                    {
                        Warn("NDJSON source contained an oversized line.");
                        continue;
                    }
                    if (SecurityEventJsonParser.TryParse(line, out var record) && record is not null)
                        parsedRecords.Add(record);
                    else
                        Warn("NDJSON source contained malformed JSON.");
                }

                if (_partialLine.Count > _options.MaxLineBytes)
                {
                    _partialLine.Clear();
                    _offset = length;
                    Warn("NDJSON source contained an oversized line.");
                }

                SaveCheckpoint(latest, _offset);
            }
            catch (IOException)
            {
                Warn("NDJSON source could not be read.");
            }
            catch (UnauthorizedAccessException)
            {
                Warn("NDJSON source access was denied.");
            }

            foreach (var record in parsedRecords)
                yield return record;

            await Task.Delay(_options.PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private string? FindLatestFile()
    {
        try
        {
            if (!Directory.Exists(_options.NdjsonDirectory)) return null;
            return Directory.EnumerateFiles(_options.NdjsonDirectory, _options.NdjsonPattern)
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .ThenByDescending(info => info.Name, StringComparer.OrdinalIgnoreCase)
                .Select(info => info.FullName)
                .FirstOrDefault();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private async Task<byte[]> ReadBytesAsync(string path, long startOffset, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, ReadChunkBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (startOffset >= stream.Length) return [];
        stream.Position = startOffset;

        using var buffer = new MemoryStream();
        var chunk = new byte[ReadChunkBytes];
        var remaining = MaxReadPerPollBytes;
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, remaining)), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0) break;
            buffer.Write(chunk, 0, read);
            remaining -= read;
        }
        return buffer.ToArray();
    }

    private bool TryTakeLine(out (byte[] Content, int ConsumedBytes) line)
    {
        var newline = _partialLine.IndexOf((byte)'\n');
        if (newline < 0)
        {
            line = default;
            return false;
        }

        var length = newline;
        if (length > 0 && _partialLine[length - 1] == (byte)'\r') length--;
        line = (_partialLine.Take(length).ToArray(), newline + 1);
        _partialLine.RemoveRange(0, newline + 1);
        return true;
    }

    private long LoadCheckpoint(string path)
    {
        var checkpointPath = GetCheckpointPath();
        try
        {
            if (!File.Exists(checkpointPath)) return 0;
            var checkpoint = JsonSerializer.Deserialize<NdjsonCheckpoint>(File.ReadAllText(checkpointPath));
            return checkpoint is not null &&
                   string.Equals(checkpoint.FileName, Path.GetFileName(path), StringComparison.OrdinalIgnoreCase) &&
                   checkpoint.Offset >= 0
                ? checkpoint.Offset : 0;
        }
        catch (IOException) { return 0; }
        catch (JsonException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }

    private void SaveCheckpoint(string path, long offset)
    {
        var checkpointPath = GetCheckpointPath();
        try
        {
            var directory = Path.GetDirectoryName(checkpointPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            var tempPath = checkpointPath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(new NdjsonCheckpoint(Path.GetFileName(path), offset)));
            File.Move(tempPath, checkpointPath, true);
        }
        catch (IOException) { Warn("NDJSON checkpoint could not be saved."); }
        catch (UnauthorizedAccessException) { Warn("NDJSON checkpoint access was denied."); }
    }

    private string GetCheckpointPath() => _options.CheckpointFile ??
        Path.Combine(_options.NdjsonDirectory, ".pbl4-security-offset.json");

    private void Warn(string detail)
    {
        var now = DateTime.UtcNow;
        if (string.Equals(detail, _lastWarning, StringComparison.Ordinal) &&
            now - _lastWarningAtUtc < TimeSpan.FromSeconds(5)) return;
        _lastWarning = detail;
        _lastWarningAtUtc = now;
        _warning?.Invoke(detail);
    }

    private sealed record NdjsonCheckpoint(string FileName, long Offset);
}
