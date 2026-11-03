using System.Threading.Channels;

namespace NetworkAdmin.Server.Networking;

// A single-slot bounded queue deliberately discards stale frames under load.
internal sealed class LatestFrameQueue<T>
{
    private readonly Channel<T> _channel = Channel.CreateBounded<T>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = true,
        AllowSynchronousContinuations = false
    });

    public bool TryWrite(T item) => _channel.Writer.TryWrite(item);
    public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken) => _channel.Reader.WaitToReadAsync(cancellationToken);
    public bool TryRead(out T item) => _channel.Reader.TryRead(out item!);
    public void Complete(Exception? error = null) => _channel.Writer.TryComplete(error);
}
