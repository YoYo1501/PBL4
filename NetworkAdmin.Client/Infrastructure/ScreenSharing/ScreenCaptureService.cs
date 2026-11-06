using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using NetworkAdmin.Shared.Protocol;

namespace NetworkAdmin.Client.Infrastructure.ScreenSharing;

/// <summary>Captures the primary desktop and emits RemoteX-style full/delta JPEG frames.</summary>
internal sealed class ScreenCaptureService
{
    private const int BlockSize = 16;
    private const double FullFrameThreshold = 0.35;
    private readonly int _maxWidth;
    private readonly int _maxHeight;
    private readonly long _jpegQuality;
    private byte[]? _oldPixels;
    private byte[]? _newPixels;

    public ScreenCaptureService(int maxWidth = 3840, int maxHeight = 2160, int jpegQuality = 70)
    {
        _maxWidth = Math.Clamp(maxWidth, 640, 3840);
        _maxHeight = Math.Clamp(maxHeight, 480, 2160);
        _jpegQuality = Math.Clamp(jpegQuality, 30, 85);
    }

    public Bitmap Capture(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds
            ?? throw new InvalidOperationException("No primary display is available.");
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new InvalidOperationException("The primary display has invalid dimensions.");

        using var source = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(source))
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
        cancellationToken.ThrowIfCancellationRequested();

        var scale = Math.Min(1d, Math.Min((double)_maxWidth / source.Width, (double)_maxHeight / source.Height));
        if (scale >= 1d) return (Bitmap)source.Clone();
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var resized = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(resized))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return resized;
    }

    public ScreenFramePacket? CreatePacket(Bitmap current, Bitmap? previous, long sequence, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (previous is null || previous.Size != current.Size)
            return FullFrame(current, sequence, token);

        var changed = FindChangedBounds(previous, current, token);
        if (changed.IsEmpty) return null;
        var ratio = (double)changed.Width * changed.Height / ((double)current.Width * current.Height);
        if (ratio > FullFrameThreshold) return FullFrame(current, sequence, token);
        using var patch = current.Clone(changed, PixelFormat.Format24bppRgb);
        return new ScreenFramePacket(sequence, false, changed.X, changed.Y,
            changed.Width, changed.Height, EncodeJpeg(patch, token));
    }

    private ScreenFramePacket FullFrame(Bitmap bitmap, long sequence, CancellationToken token) =>
        new(sequence, true, 0, 0, bitmap.Width, bitmap.Height, EncodeJpeg(bitmap, token));

    private byte[] EncodeJpeg(Bitmap bitmap, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, _jpegQuality);
        using var output = new MemoryStream();
        bitmap.Save(output, codec, parameters);
        token.ThrowIfCancellationRequested();
        return output.ToArray();
    }

    private Rectangle FindChangedBounds(Bitmap oldBitmap, Bitmap newBitmap, CancellationToken token)
    {
        var rect = new Rectangle(0, 0, newBitmap.Width, newBitmap.Height);
        var oldData = oldBitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        var newData = newBitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var length = Math.Abs(newData.Stride) * newBitmap.Height;
            _oldPixels ??= new byte[length];
            _newPixels ??= new byte[length];
            if (_oldPixels.Length != length || _newPixels.Length != length)
            {
                _oldPixels = new byte[length];
                _newPixels = new byte[length];
            }
            Marshal.Copy(oldData.Scan0, _oldPixels, 0, length);
            Marshal.Copy(newData.Scan0, _newPixels, 0, length);
            var minX = newBitmap.Width;
            var minY = newBitmap.Height;
            var maxX = -1;
            var maxY = -1;
            for (var by = 0; by < newBitmap.Height; by += BlockSize)
            {
                token.ThrowIfCancellationRequested();
                var endY = Math.Min(by + BlockSize, newBitmap.Height);
                for (var bx = 0; bx < newBitmap.Width; bx += BlockSize)
                {
                    var endX = Math.Min(bx + BlockSize, newBitmap.Width);
                    var different = false;
                    for (var y = by; y < endY && !different; y++)
                    {
                        var offset = y * newData.Stride + bx * 3;
                        var end = y * newData.Stride + endX * 3;
                        for (var i = offset; i < end; i++)
                            if (_oldPixels[i] != _newPixels[i]) { different = true; break; }
                    }
                    if (!different) continue;
                    minX = Math.Min(minX, bx); minY = Math.Min(minY, by);
                    maxX = Math.Max(maxX, endX); maxY = Math.Max(maxY, endY);
                }
            }
            return maxX < 0 ? Rectangle.Empty : Rectangle.FromLTRB(minX, minY, maxX, maxY);
        }
        finally
        {
            oldBitmap.UnlockBits(oldData);
            newBitmap.UnlockBits(newData);
        }
    }

}
