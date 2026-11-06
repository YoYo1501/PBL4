using System.Diagnostics;
using System.Drawing.Imaging;
using NetworkAdmin.Shared.Diagnostics;
using NetworkAdmin.Shared.Protocol;

namespace NetworkAdmin.Server.Presentation.V2;

/// <summary>V2 ordered JPEG decoder and delta compositor. Returned bitmaps are caller-owned snapshots.</summary>
internal sealed class ScreenShareV2FrameComposer : IDisposable
{
    private Bitmap? _composite;
    private long _lastSequence = -1;
    private readonly ScreenSharePerformanceMonitor? _performance;

    public ScreenShareV2FrameComposer(ScreenSharePerformanceMonitor? performance = null) => _performance = performance;

    public async Task<Bitmap> ComposeAsync(ScreenFramePacket packet, CancellationToken cancellationToken = default)
    {
        if (packet.Sequence != _lastSequence + 1)
            throw new InvalidDataException($"V2 screen frame sequence gap: expected {_lastSequence + 1}, got {packet.Sequence}.");

        var decodeStart = Stopwatch.GetTimestamp();
        var decoded = await Task.Run(() => Decode(packet.Jpeg), cancellationToken).ConfigureAwait(false);
        _performance?.Decode(Stopwatch.GetTimestamp() - decodeStart, packet.Jpeg.Length);
        try
        {
            if (packet.IsFullFrame)
            {
                if ((packet.Width != 0 && packet.Width != decoded.Width) ||
                    (packet.Height != 0 && packet.Height != decoded.Height))
                    throw new InvalidDataException("V2 full frame dimensions do not match the packet header.");
                _composite?.Dispose();
                _composite = decoded;
                decoded = null!;
            }
            else
            {
                if (_composite is null) throw new InvalidDataException("V2 delta arrived before the initial full frame.");
                if (packet.Width != decoded.Width || packet.Height != decoded.Height || packet.X < 0 || packet.Y < 0 ||
                    packet.X + decoded.Width > _composite.Width || packet.Y + decoded.Height > _composite.Height)
                    throw new InvalidDataException("V2 delta rectangle is outside the composite frame.");

                var updateStart = Stopwatch.GetTimestamp();
                using var graphics = Graphics.FromImage(_composite);
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                graphics.DrawImageUnscaled(decoded, packet.X, packet.Y);
                _performance?.Composite(Stopwatch.GetTimestamp() - updateStart);
            }
            _lastSequence = packet.Sequence;
            var cloneStart = Stopwatch.GetTimestamp();
            var snapshot = await Task.Run(() => (Bitmap)_composite!.Clone(), cancellationToken).ConfigureAwait(false);
            _performance?.Clone(Stopwatch.GetTimestamp() - cloneStart);
            return snapshot;
        }
        finally { decoded?.Dispose(); }
    }

    private static Bitmap Decode(byte[] jpeg)
    {
        using var stream = new MemoryStream(jpeg, writable: false);
        using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
        if (image.Width is <= 0 or > 3840 || image.Height is <= 0 or > 2160 || (long)image.Width * image.Height > 8_294_400)
            throw new InvalidDataException("V2 decoded frame exceeds the viewer dimension limit.");
        var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppPArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.DrawImageUnscaled(image, 0, 0);
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }

    public void Dispose()
    {
        _composite?.Dispose();
        _composite = null;
    }
}
