namespace NetworkAdmin.Client.Infrastructure.ScreenSharing.V2;

/// <summary>Single-item, ownership-transferring mailbox. Publishing replaces and disposes stale frames.</summary>
internal sealed class LatestBitmapSlot : IDisposable
{
    private readonly object _sync = new();
    private Bitmap? _frame;
    private TaskCompletionSource<bool>? _available;
    private bool _completed;
    private long _replacedCount;

    public long ReplacedCount => Interlocked.Read(ref _replacedCount);

    public void Publish(Bitmap frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Bitmap? replaced;
        TaskCompletionSource<bool>? signal;
        lock (_sync)
        {
            if (_completed) { frame.Dispose(); return; }
            replaced = _frame;
            _frame = frame;
            signal = _available;
            _available = null;
        }
        if (replaced is not null)
        {
            Interlocked.Increment(ref _replacedCount);
            replaced.Dispose();
        }
        signal?.TrySetResult(true);
    }

    public async ValueTask<Bitmap?> TakeAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task wait;
            lock (_sync)
            {
                if (_frame is not null)
                {
                    var result = _frame;
                    _frame = null;
                    return result;
                }
                if (_completed) return null;
                _available ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                wait = _available.Task;
            }
            await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void Complete()
    {
        TaskCompletionSource<bool>? signal;
        lock (_sync)
        {
            if (_completed) return;
            _completed = true;
            signal = _available;
            _available = null;
        }
        signal?.TrySetResult(false);
    }

    public void Dispose()
    {
        Bitmap? remaining;
        TaskCompletionSource<bool>? signal;
        lock (_sync)
        {
            if (_completed && _frame is null) return;
            _completed = true;
            remaining = _frame;
            _frame = null;
            signal = _available;
            _available = null;
        }
        remaining?.Dispose();
        signal?.TrySetResult(false);
    }
}
