using System.Diagnostics;

namespace NetworkAdmin.Shared.Diagnostics;

/// <summary>Low-overhead, opt-in screen-share counters reported once per five-second window.</summary>
public sealed class ScreenSharePerformanceMonitor : IDisposable
{
    private static readonly string[] Names = ["capture", "delta", "jpeg", "send", "decode", "composite", "clone", "ui-update", "paint", "viewer-latency"];
    private readonly long[] _ticks = new long[Names.Length];
    private readonly long[] _samples = new long[Names.Length];
    private readonly Timer? _timer;
    private readonly Process _process = Process.GetCurrentProcess();
    private long _captured, _viewed, _dropped, _sentBytes, _sentFrames, _receivedBytes, _receivedFrames;
    private long _windowStart = Stopwatch.GetTimestamp();
    private TimeSpan _cpuStart;
    private static readonly object LogSync = new();

    public static bool Enabled { get; private set; }

    public static void Configure(string[] args)
    {
        var arg = Array.FindIndex(args, x => string.Equals(x, "--screen-performance", StringComparison.OrdinalIgnoreCase));
        var text = arg >= 0 && arg + 1 < args.Length ? args[arg + 1] : Environment.GetEnvironmentVariable("NETWORKADMIN_SCREEN_PERFORMANCE");
        Enabled = bool.TryParse(text, out var enabled) && enabled;
    }

    public ScreenSharePerformanceMonitor()
    {
        if (!Enabled) return;
        _cpuStart = _process.TotalProcessorTime;
        _timer = new Timer(_ => Report(), null, Timeout.InfiniteTimeSpan, TimeSpan.FromSeconds(5));
    }

    public void Start()
    {
        if (_timer is null) return;
        _cpuStart = _process.TotalProcessorTime;
        Interlocked.Exchange(ref _windowStart, Stopwatch.GetTimestamp());
        _timer.Change(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }
    public void Stop() => _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    public void Capture(long elapsedTicks) { Add(0, elapsedTicks); Interlocked.Increment(ref _captured); }
    public void Delta(long elapsedTicks) => Add(1, elapsedTicks);
    public void Jpeg(long elapsedTicks) => Add(2, elapsedTicks);
    public void Send(long elapsedTicks, int bytes) { Add(3, elapsedTicks); Interlocked.Increment(ref _sentFrames); Interlocked.Add(ref _sentBytes, bytes); }
    public void Decode(long elapsedTicks, int bytes) { Add(4, elapsedTicks); Interlocked.Increment(ref _receivedFrames); Interlocked.Add(ref _receivedBytes, bytes); }
    public void Composite(long elapsedTicks) => Add(5, elapsedTicks);
    public void Clone(long elapsedTicks) => Add(6, elapsedTicks);
    public void UiUpdate(long elapsedTicks) => Add(7, elapsedTicks);
    public void Render(long elapsedTicks) => Add(8, elapsedTicks);
    public void ViewerLatency(long elapsedTicks) => Add(9, elapsedTicks);
    public void ViewerFrame() => Interlocked.Increment(ref _viewed);
    public void DroppedFrame() => Interlocked.Increment(ref _dropped);

    private void Add(int index, long ticks)
    {
        if (Enabled) { Interlocked.Add(ref _ticks[index], ticks); Interlocked.Increment(ref _samples[index]); }
    }

    private void Report()
    {
        if (!Enabled) return;
        try
        {
            var now = Stopwatch.GetTimestamp();
            var seconds = Stopwatch.GetElapsedTime(Interlocked.Exchange(ref _windowStart, now), now).TotalSeconds;
            var captured = Interlocked.Exchange(ref _captured, 0);
            var viewed = Interlocked.Exchange(ref _viewed, 0);
            var dropped = Interlocked.Exchange(ref _dropped, 0);
            var sentBytes = Interlocked.Exchange(ref _sentBytes, 0);
            var sentFrames = Interlocked.Exchange(ref _sentFrames, 0);
            var receivedBytes = Interlocked.Exchange(ref _receivedBytes, 0);
            var receivedFrames = Interlocked.Exchange(ref _receivedFrames, 0);
            var sentKb = sentFrames == 0 ? 0 : sentBytes / 1024d / sentFrames;
            var receivedKb = receivedFrames == 0 ? 0 : receivedBytes / 1024d / receivedFrames;
            var stage = string.Join(" ", Names.Select((name, i) => {
                var count = Interlocked.Exchange(ref _samples[i], 0);
                var ticks = Interlocked.Exchange(ref _ticks[i], 0);
                return count == 0 ? $"{name}=n/a" : $"{name}={ticks * 1000d / Stopwatch.Frequency / count:0.0}ms";
            }));
            _process.Refresh();
            var cpu = (_process.TotalProcessorTime - _cpuStart).TotalMilliseconds / Math.Max(1, seconds * Environment.ProcessorCount) * 100;
            _cpuStart = _process.TotalProcessorTime;
            var line = $"[ScreenPerf] {DateTimeOffset.Now:HH:mm:ss} captureFPS={captured / seconds:0.0} viewerFPS={viewed / seconds:0.0} dropped={dropped} send={sentFrames}frames/{sentKb:0.0}KB avg/{sentBytes / seconds / 125000:0.00}Mbps recv={receivedFrames}frames/{receivedKb:0.0}KB avg/{receivedBytes / seconds / 125000:0.00}Mbps CPU={cpu:0.0}% RAM={_process.WorkingSet64 / 1048576d:0}MB {stage}";
            Console.WriteLine(line);
            lock (LogSync)
            {
                var folder = Path.Combine(AppContext.BaseDirectory, "Logs");
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "screen-performance.log"), line + Environment.NewLine);
            }
        }
        catch (Exception ex) { Console.WriteLine($"[ScreenPerf] metrics unavailable: {ex.Message}"); }
    }

    public void Dispose() => _timer?.Dispose();
}
