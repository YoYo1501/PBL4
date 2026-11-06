using System.Diagnostics.Eventing.Reader;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Xml.Linq;
using System.Xml;

namespace NetworkAdmin.Client.Infrastructure.Security;

internal sealed class WindowsEventLogSecurityEventSource : ISecurityEventSource
{
    private readonly SecurityEventCollectorOptions _options;
    private readonly Action<string>? _warning;

    public WindowsEventLogSecurityEventSource(SecurityEventCollectorOptions options, Action<string>? warning = null)
    {
        _options = options;
        _warning = warning;
    }

    public async IAsyncEnumerable<SecurityEventRecord> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var query = new EventLogQuery(_options.EventLogName, PathType.LogName, "*");
        using var watcher = new EventLogWatcher(query);
        var records = Channel.CreateBounded<EventRecord>(new BoundedChannelOptions(_options.SourceQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

        void OnRecordWritten(object? _, EventRecordWrittenEventArgs args)
        {
            if (args.EventRecord is null)
            {
                if (args.EventException is not null) _warning?.Invoke("Windows Event Log reported a source error.");
                return;
            }

            if (!records.Writer.TryWrite(args.EventRecord))
            {
                args.EventRecord.Dispose();
                _warning?.Invoke("Windows Event Log queue is full; an event was dropped.");
            }
        }

        watcher.EventRecordWritten += OnRecordWritten;
        try
        {
            watcher.Enabled = true;
            await foreach (var eventRecord in records.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                using (eventRecord)
                {
                    if (TryParse(eventRecord, out var record) && record is not null)
                        yield return record;
                    else
                        _warning?.Invoke("Windows Event Log record was malformed.");
                }
            }
        }
        finally
        {
            watcher.EventRecordWritten -= OnRecordWritten;
            try { watcher.Enabled = false; } catch (InvalidOperationException) { }
            records.Writer.TryComplete();
            while (records.Reader.TryRead(out var remaining)) remaining.Dispose();
        }
    }

    private static bool TryParse(EventRecord eventRecord, out SecurityEventRecord? record)
    {
        record = null;
        try
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var xml = XDocument.Parse(eventRecord.ToXml(), LoadOptions.PreserveWhitespace);
            foreach (var data in xml.Descendants().Where(element =>
                         string.Equals(element.Name.LocalName, "Data", StringComparison.OrdinalIgnoreCase)))
            {
                var name = data.Attribute("Name")?.Value;
                if (!string.IsNullOrWhiteSpace(name)) values[name] = data.Value;
            }

            var imagePath = Value(values, "Image");
            var parentImage = Value(values, "ParentImage");
            record = new SecurityEventRecord(
                $"event-{eventRecord.RecordId?.ToString() ?? Guid.NewGuid().ToString("N")}",
                eventRecord.TimeCreated?.ToUniversalTime() ?? DateTime.UtcNow,
                eventRecord.ProviderName ?? "WindowsEventLog",
                eventRecord.Id,
                First(values, "User", "UserName"),
                First(values, "ProcessName") ?? FileName(imagePath),
                First(values, "ParentProcessName") ?? FileName(parentImage),
                imagePath,
                Value(values, "CommandLine"),
                ParsePort(Value(values, "DestinationPort")));
            return true;
        }
        catch (Exception ex) when (ex is EventLogException or InvalidOperationException or XmlException)
        {
            return false;
        }
    }

    private static string Value(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? value : string.Empty;

    private static string First(IReadOnlyDictionary<string, string> values, params string[] keys) =>
        keys.Select(key => Value(values, key)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static int ParsePort(string value) =>
        int.TryParse(value, out var port) ? Math.Clamp(port, 0, 65_535) : 0;

    private static string FileName(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try { return Path.GetFileName(path); }
        catch (ArgumentException) { return string.Empty; }
    }
}
