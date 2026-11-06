using System.Threading.Channels;
using System.Diagnostics.Eventing.Reader;
using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Protocol;

namespace NetworkAdmin.Client.Infrastructure.Security;

internal sealed class SecurityEventCollector : IAsyncDisposable
{
    private readonly SecurityEventCollectorOptions _options;
    private readonly Func<Action<string>, ISecurityEventSource> _sourceFactory;
    private readonly object _sync = new();
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private DateTime? _lastEventTimestampUtc;
    private MonitoringState _state = MonitoringState.Disabled;

    public SecurityEventCollector(SecurityEventCollectorOptions options)
        : this(options, warning => CreateConfiguredSource(options, warning))
    {
    }

    internal SecurityEventCollector(SecurityEventCollectorOptions options, Func<ISecurityEventSource> sourceFactory)
        : this(options, _ => sourceFactory())
    {
    }

    private SecurityEventCollector(SecurityEventCollectorOptions options,
        Func<Action<string>, ISecurityEventSource> sourceFactory)
    {
        if (!RemoteProtocolContract.IsValidIdentifier(options.ClientId))
            throw new ArgumentException("ClientId is invalid.", nameof(options));
        if (!RemoteProtocolContract.IsValidSessionId(options.SessionId))
            throw new ArgumentException("SessionId is invalid.", nameof(options));
        if (options.QueueCapacity <= 0 || options.SourceQueueCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Queue capacities must be positive.");
        if (options.MaxLineBytes <= 0 || options.PollInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Collector limits must be positive.");

        _options = options;
        _sourceFactory = sourceFactory;
    }

    public event Action<SecurityEventMessage>? EventReceived;
    public event Action<MonitoringStatusMessage>? StatusChanged;
    public MonitoringState State => _state;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_runTask is { IsCompleted: false }) return Task.CompletedTask;
            _runCancellation?.Dispose();
            _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _runTask = RunAsync(_runCancellation.Token);
        }
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        Task? task;
        CancellationTokenSource? cancellation;
        lock (_sync)
        {
            task = _runTask;
            cancellation = _runCancellation;
            cancellation?.Cancel();
        }

        if (task is not null)
        {
            try { await task.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        lock (_sync)
        {
            if (ReferenceEquals(task, _runTask))
            {
                _runTask = null;
                _runCancellation = null;
                cancellation?.Dispose();
            }
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var queue = Channel.CreateBounded<SecurityEventMessage>(new BoundedChannelOptions(_options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });

        PublishStatus(MonitoringState.Starting, "Security event collector is starting.");
        var dispatchTask = DispatchAsync(queue.Reader, cancellationToken);
        try
        {
            var source = _sourceFactory(detail => PublishStatus(MonitoringState.Running, detail));
            PublishStatus(MonitoringState.Running, "Security event collector is running.");
            await foreach (var record in source.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var message = ToMessage(record);
                if (message is not null)
                    await queue.Writer.WriteAsync(message, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (EventLogException)
        {
            PublishStatus(MonitoringState.Faulted, "Windows Event Log source is unavailable.");
        }
        catch (UnauthorizedAccessException)
        {
            PublishStatus(MonitoringState.Faulted, "Security event source access was denied.");
        }
        catch (IOException)
        {
            PublishStatus(MonitoringState.Faulted, "Security event source could not be read.");
        }
        catch (Exception)
        {
            // Do not log source data or exception text; it may contain command lines or usernames.
            PublishStatus(MonitoringState.Faulted, "Security event collector stopped unexpectedly.");
        }
        finally
        {
            queue.Writer.TryComplete();
            try { await dispatchTask.ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }

            if (cancellationToken.IsCancellationRequested)
                PublishStatus(MonitoringState.Stopped, "Security event collector stopped.");
            else if (_state != MonitoringState.Faulted)
                PublishStatus(MonitoringState.Stopped, "Security event source completed.");
        }
    }

    private async Task DispatchAsync(ChannelReader<SecurityEventMessage> reader, CancellationToken cancellationToken)
    {
        await foreach (var message in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            _lastEventTimestampUtc = message.DetectionTimestampUtc;
            try { EventReceived?.Invoke(message); }
            catch (Exception)
            {
                // A subscriber must not terminate collection or cause event data to be logged.
                PublishStatus(MonitoringState.Running, "Security event subscriber failed.");
            }
        }
    }

    private SecurityEventMessage? ToMessage(SecurityEventRecord record)
    {
        var message = new SecurityEventMessage
        {
            ClientId = _options.ClientId,
            SessionId = _options.SessionId,
            EventId = NormalizeIdentifier(record.EventId),
            DetectionTimestampUtc = record.DetectionTimestampUtc.Kind == DateTimeKind.Utc
                ? record.DetectionTimestampUtc : record.DetectionTimestampUtc.ToUniversalTime(),
            Source = Limit(record.Source, RemoteProtocolContract.MaxSecurityFieldLength),
            EventCode = record.EventCode,
            UserName = Limit(record.UserName, RemoteProtocolContract.MaxSecurityFieldLength),
            ProcessName = Limit(record.ProcessName, RemoteProtocolContract.MaxSecurityFieldLength),
            ParentProcessName = Limit(record.ParentProcessName, RemoteProtocolContract.MaxSecurityFieldLength),
            ImagePath = Limit(record.ImagePath, RemoteProtocolContract.MaxSecurityFieldLength),
            CommandLine = Limit(record.CommandLine, RemoteProtocolContract.MaxSecurityCommandLineLength),
            DestinationPort = Math.Clamp(record.DestinationPort, 0, 65_535)
        };

        return SecurityMonitoringContractPolicy.ValidateEvent(message) is null ? message : null;
    }

    private void PublishStatus(MonitoringState state, string detail)
    {
        _state = state;
        var status = new MonitoringStatusMessage
        {
            ClientId = _options.ClientId,
            SessionId = _options.SessionId,
            State = state,
            LastEventTimestampUtc = _lastEventTimestampUtc,
            Detail = detail,
            ModelVersion = string.Empty
        };
        try { StatusChanged?.Invoke(status); } catch (Exception) { }
    }

    private static ISecurityEventSource CreateConfiguredSource(SecurityEventCollectorOptions options, Action<string>? warning)
    {
        if (options.SourceKind == SecurityEventSourceKind.Ndjson)
            return new NdjsonSecurityEventSource(options, warning);
        if (options.SourceKind == SecurityEventSourceKind.WindowsEventLog)
            return new WindowsEventLogSecurityEventSource(options, warning);

        var hasNdjson = Directory.Exists(options.NdjsonDirectory) &&
                        Directory.EnumerateFiles(options.NdjsonDirectory, options.NdjsonPattern).Any();
        return hasNdjson
            ? new NdjsonSecurityEventSource(options, warning)
            : new WindowsEventLogSecurityEventSource(options, warning);
    }

    private static string NormalizeIdentifier(string value)
    {
        if (RemoteProtocolContract.IsValidIdentifier(value)) return value;
        var normalized = new string((value ?? string.Empty).Select(c => char.IsWhiteSpace(c) ? '_' : c)
            .Where(c => !char.IsControl(c)).ToArray());
        return RemoteProtocolContract.IsValidIdentifier(normalized)
            ? normalized[..Math.Min(normalized.Length, RemoteProtocolContract.MaxIdentifierLength)]
            : Guid.NewGuid().ToString("N");
    }

    private static string Limit(string? value, int maxLength) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : new string(value.Where(character => !char.IsControl(character)).Take(maxLength).ToArray());

}
