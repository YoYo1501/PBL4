using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using NetworkAdmin.Shared.Protocol;
using System.Diagnostics;
using NetworkAdmin.Shared.Diagnostics;

namespace NetworkAdmin.Client.Infrastructure.ScreenSharing.V2;

/// <summary>Independent V2 JPEG/full/delta encoder. It shares no V1 packet detection implementation.</summary>
internal sealed class ScreenShareV2FrameEncoder
{
    private const int BlockSize = 16;
    private const double FullFrameAreaThreshold = 0.35;
    private const int KeyFrameInterval = 120;
    private readonly ImageCodecInfo _jpeg = ImageCodecInfo.GetImageEncoders().First(x => x.FormatID == ImageFormat.Jpeg.Guid);
    private readonly long _quality;
    private readonly ScreenSharePerformanceMonitor? _performance;
    private int _framesSinceKeyFrame;

    public ScreenShareV2FrameEncoder(int jpegQuality = 70, ScreenSharePerformanceMonitor? performance = null)
    { _quality = Math.Clamp(jpegQuality, 30, 85); _performance = performance; }

    public ScreenFramePacket? Encode(Bitmap current, Bitmap? previous, long sequence, bool forceFull = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var keyFrame = forceFull || previous is null || previous.Size != current.Size || _framesSinceKeyFrame >= KeyFrameInterval;
        if (keyFrame)
        {
            var full = EncodeJpeg(current);
            _framesSinceKeyFrame = 0;
            return new ScreenFramePacket(sequence, true, 0, 0, current.Width, current.Height, full);
        }

        var deltaStart = Stopwatch.GetTimestamp();
        var changed = GetChangedBounds(previous!, current, cancellationToken);
        _performance?.Delta(Stopwatch.GetTimestamp() - deltaStart);
        if (changed.IsEmpty) return null;
        var ratio = (double)changed.Width * changed.Height / ((double)current.Width * current.Height);
        if (ratio >= FullFrameAreaThreshold)
        {
            _framesSinceKeyFrame = 0;
            return new ScreenFramePacket(sequence, true, 0, 0, current.Width, current.Height, EncodeJpeg(current));
        }

        using var patch = current.Clone(changed, PixelFormat.Format24bppRgb);
        var jpeg = EncodeJpeg(patch);
        _framesSinceKeyFrame++;
        return new ScreenFramePacket(sequence, false, changed.X, changed.Y, changed.Width, changed.Height, jpeg);
    }

    private static Rectangle GetChangedBounds(Bitmap oldFrame, Bitmap newFrame, CancellationToken token)
    {
        var bounds = new Rectangle(0, 0, newFrame.Width, newFrame.Height);
        var format = PixelFormat.Format24bppRgb;
        var bytes = checked(newFrame.Width * 3);
        var oldRow = new byte[bytes];
        var newRow = new byte[bytes];
        var left = newFrame.Width; var top = newFrame.Height; var right = 0; var bottom = 0;
        var oldBits = oldFrame.LockBits(bounds, ImageLockMode.ReadOnly, format);
        try
        {
            var newBits = newFrame.LockBits(bounds, ImageLockMode.ReadOnly, format);
            try
            {
                for (var y = 0; y < newFrame.Height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    Marshal.Copy(IntPtr.Add(oldBits.Scan0, y * oldBits.Stride), oldRow, 0, bytes);
                    Marshal.Copy(IntPtr.Add(newBits.Scan0, y * newBits.Stride), newRow, 0, bytes);
                    for (var x = 0; x < newFrame.Width; x += BlockSize)
                    {
                        var end = Math.Min(x + BlockSize, newFrame.Width);
                        var offset = x * 3;
                        var count = (end - x) * 3;
                        if (oldRow.AsSpan(offset, count).SequenceEqual(newRow.AsSpan(offset, count))) continue;
                        left = Math.Min(left, x); right = Math.Max(right, end);
                        top = Math.Min(top, (y / BlockSize) * BlockSize);
                        bottom = Math.Max(bottom, Math.Min(newFrame.Height, ((y / BlockSize) + 1) * BlockSize));
                    }
                }
            }
            finally { newFrame.UnlockBits(newBits); }
        }
        finally { oldFrame.UnlockBits(oldBits); }
        return right <= left || bottom <= top ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right, bottom);
    }

    private byte[] EncodeJpeg(Image image)
    {
        var started = Stopwatch.GetTimestamp();
        using var output = new MemoryStream();
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, _quality);
        image.Save(output, _jpeg, parameters);
        if (output.Length is <= 0 or > ScreenFrameProtocol.MaxFrameBytes)
            throw new InvalidDataException("V2 JPEG frame exceeds the protocol size limit.");
        var bytes = output.ToArray();
        _performance?.Jpeg(Stopwatch.GetTimestamp() - started);
        return bytes;
    }
}
