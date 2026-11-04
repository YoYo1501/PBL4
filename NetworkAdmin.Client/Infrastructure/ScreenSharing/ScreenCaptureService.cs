using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using NetworkAdmin.Shared.Protocol;

namespace NetworkAdmin.Client.Infrastructure.ScreenSharing;

internal sealed class ScreenCaptureService
{
    private static readonly ImageCodecInfo JpegEncoder = ImageCodecInfo.GetImageEncoders()
        .First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
    private readonly int _maxWidth;
    private readonly int _maxHeight;
    private readonly long _jpegQuality;

    public ScreenCaptureService(int maxWidth = 3840, int maxHeight = 2160, int jpegQuality = 70)
    {
        _maxWidth = Math.Clamp(maxWidth, 640, 3840);
        _maxHeight = Math.Clamp(maxHeight, 480, 2160);
        _jpegQuality = Math.Clamp(jpegQuality, 30, 85);
    }

    public byte[] CaptureJpeg()
    {
        // CaptureJpeg runs on a worker thread. Give that thread the same physical-pixel
        // coordinate space as the interactive desktop before reading bounds/capturing.
        using var dpiContext = DpiAwarenessContext.EnterPerMonitorV2();
        var bounds = SystemInformation.VirtualScreen;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new InvalidOperationException("No active screen is available for capture.");

        using var source = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(source))
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);

        var scale = Math.Min(1d, Math.Min((double)_maxWidth / source.Width, (double)_maxHeight / source.Height));
        if (scale >= 1d) return EncodeJpeg(source);
        var width = Math.Max(1, (int)(source.Width * scale));
        var height = Math.Max(1, (int)(source.Height * scale));
        using var resized = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(resized))
        {
            graphics.CompositingQuality = CompositingQuality.HighSpeed;
            graphics.InterpolationMode = InterpolationMode.Bilinear;
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        }

        return EncodeJpeg(resized);
    }

    private sealed class DpiAwarenessContext : IDisposable
    {
        private static readonly IntPtr PerMonitorAwareV2 = new(-4);
        private readonly IntPtr _previous;

        private DpiAwarenessContext(IntPtr previous) => _previous = previous;

        public static DpiAwarenessContext EnterPerMonitorV2()
        {
            var previous = SetThreadDpiAwarenessContext(PerMonitorAwareV2);
            if (previous == IntPtr.Zero)
                throw new InvalidOperationException("Could not enable per-monitor DPI awareness for screen capture.",
                    Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
            return new DpiAwarenessContext(previous);
        }

        public void Dispose() => SetThreadDpiAwarenessContext(_previous);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);
    }

    private byte[] EncodeJpeg(Image image)
    {
        using var output = new MemoryStream();
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, _jpegQuality);
        image.Save(output, JpegEncoder, parameters);
        if (output.Length is <= 0 or > ScreenFrameProtocol.MaxFrameBytes)
            throw new InvalidDataException("Captured JPEG exceeds the screen frame size limit.");
        return output.ToArray();
    }
}
