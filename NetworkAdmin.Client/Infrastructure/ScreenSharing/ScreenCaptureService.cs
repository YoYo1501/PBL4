using System.Drawing.Imaging;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.ComponentModel;
using System.Runtime.InteropServices;
using NetworkAdmin.Shared.Protocol;
using NetworkAdmin.Shared.Diagnostics;

namespace NetworkAdmin.Client.Infrastructure.ScreenSharing;

internal sealed class ScreenCaptureService
{
    private static readonly ImageCodecInfo JpegEncoder = ImageCodecInfo.GetImageEncoders()
        .First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
    private const int BlockSize = 16;
    private const double FullFrameThreshold = 0.35;
    private readonly int _maxWidth;
    private readonly int _maxHeight;
    private readonly long _jpegQuality;
    private readonly ScreenSharePerformanceMonitor? _performance;
    private int _captureInfoLogged;

    public ScreenCaptureService(int maxWidth = 3840, int maxHeight = 2160, int jpegQuality = 70,
        ScreenSharePerformanceMonitor? performance = null)
    {
        _maxWidth = Math.Clamp(maxWidth, 640, 3840);
        _maxHeight = Math.Clamp(maxHeight, 480, 2160);
        _jpegQuality = Math.Clamp(jpegQuality, 30, 85);
        _performance = performance;
    }

    public Bitmap CaptureBitmap(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Capture runs on a worker thread. Give that thread the same physical-pixel
        // coordinate space as the interactive desktop before reading bounds/capturing.
        using var dpiContext = DpiAwarenessContext.EnterPerMonitorV2();
        // Use the physical virtual-desktop rectangle so capture includes every
        // attached display and honors negative coordinates in multi-monitor layouts.
        var bounds = GetVirtualDesktopBounds();
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new InvalidOperationException("No active screen is available for capture.");

        cancellationToken.ThrowIfCancellationRequested();
        using var source = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        CapturePhysicalDesktop(source, bounds);
        cancellationToken.ThrowIfCancellationRequested();

        var scale = Math.Min(1d, Math.Min((double)_maxWidth / source.Width, (double)_maxHeight / source.Height));
        var width = scale >= 1d ? source.Width : Math.Max(1, (int)(source.Width * scale));
        var height = scale >= 1d ? source.Height : Math.Max(1, (int)(source.Height * scale));
        if (Interlocked.Exchange(ref _captureInfoLogged, 1) == 0)
            WriteCaptureInfo(bounds, width, height);
        if (scale >= 1d)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return (Bitmap)source.Clone();
        }
        using var resized = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(resized))
        {
            cancellationToken.ThrowIfCancellationRequested();
            graphics.CompositingQuality = CompositingQuality.HighSpeed;
            graphics.InterpolationMode = InterpolationMode.Bilinear;
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return (Bitmap)resized.Clone();
    }

    /// <summary>
    /// Captures one frame for the streaming loop without surfacing the expected
    /// cancellation exception when Stop Sharing cancels the active session.
    /// The original CaptureBitmap method intentionally keeps its cancellation
    /// contract for direct callers and unit tests.
    /// </summary>
    public bool TryCaptureBitmap(CancellationToken cancellationToken, out Bitmap? bitmap)
    {
        try
        {
            bitmap = CaptureBitmap(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            bitmap = null;
            return false;
        }
    }

    public ScreenFramePacket? CreatePacket(Bitmap current, Bitmap? previous, long sequence,
        bool forceFullFrame = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (forceFullFrame || previous is null || previous.Size != current.Size)
        {
            var jpeg = EncodeJpeg(current);
            cancellationToken.ThrowIfCancellationRequested();
            return new ScreenFramePacket(sequence, true, 0, 0, current.Width, current.Height, jpeg);
        }
        if (ReferenceEquals(previous, current)) return null;

        var deltaStarted = Stopwatch.GetTimestamp();
        var changed = FindChangeBounds(previous, current, cancellationToken);
        _performance?.Delta(Stopwatch.GetTimestamp() - deltaStarted);
        if (changed.IsEmpty) return null;
        var areaRatio = (double)changed.Width * changed.Height / ((double)current.Width * current.Height);
        if (areaRatio > FullFrameThreshold)
            return new ScreenFramePacket(sequence, true, 0, 0, current.Width, current.Height, EncodeJpeg(current));

        using var patch = current.Clone(changed, PixelFormat.Format24bppRgb);
        var patchJpeg = EncodeJpeg(patch);
        cancellationToken.ThrowIfCancellationRequested();
        return new ScreenFramePacket(sequence, false, changed.X, changed.Y,
            changed.Width, changed.Height, patchJpeg);
    }

    /// <summary>
    /// Creates one packet for the streaming loop and treats an already-requested
    /// cancellation as a normal stop result instead of an unhandled exception.
    /// </summary>
    public bool TryCreatePacket(Bitmap current, Bitmap? previous, long sequence,
        bool forceFullFrame, CancellationToken cancellationToken, out ScreenFramePacket? packet)
    {
        try
        {
            packet = CreatePacket(current, previous, sequence, forceFullFrame, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            packet = null;
            return false;
        }
    }

    private static Rectangle FindChangeBounds(Bitmap previous, Bitmap current, CancellationToken cancellationToken)
    {
        var rect = new Rectangle(0, 0, current.Width, current.Height);
        var format = PixelFormat.Format24bppRgb;
        var rowBytes = checked(current.Width * 3);
        var oldRow = new byte[rowBytes];
        var newRow = new byte[rowBytes];
        var minX = current.Width;
        var minY = current.Height;
        var maxX = 0;
        var maxY = 0;
        var oldData = previous.LockBits(rect, ImageLockMode.ReadOnly, format);
        try
        {
            var newData = current.LockBits(rect, ImageLockMode.ReadOnly, format);
            try
            {
                for (var blockY = 0; blockY < current.Height; blockY += BlockSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var endY = Math.Min(blockY + BlockSize, current.Height);
                    for (var y = blockY; y < endY; y++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Marshal.Copy(IntPtr.Add(oldData.Scan0, y * oldData.Stride), oldRow, 0, rowBytes);
                        Marshal.Copy(IntPtr.Add(newData.Scan0, y * newData.Stride), newRow, 0, rowBytes);
                        for (var blockX = 0; blockX < current.Width; blockX += BlockSize)
                        {
                            var endX = Math.Min(blockX + BlockSize, current.Width);
                            var offset = blockX * 3;
                            var length = (endX - blockX) * 3;
                            if (oldRow.AsSpan(offset, length).SequenceEqual(newRow.AsSpan(offset, length)))
                                continue;
                            minX = Math.Min(minX, blockX);
                            minY = Math.Min(minY, blockY);
                            maxX = Math.Max(maxX, endX);
                            maxY = Math.Max(maxY, endY);
                        }
                    }
                }
            }
            finally { current.UnlockBits(newData); }
        }
        finally { previous.UnlockBits(oldData); }
        return maxX <= minX || maxY <= minY ? Rectangle.Empty : Rectangle.FromLTRB(minX, minY, maxX, maxY);
    }

    /// <summary>
    /// Returns the physical virtual-desktop rectangle, independent of Windows UI scaling.
    /// </summary>
    private static Rectangle GetVirtualDesktopBounds()
    {
        const int SmXVirtualScreen = 76;
        const int SmYVirtualScreen = 77;
        const int SmCxVirtualScreen = 78;
        const int SmCyVirtualScreen = 79;
        var left = GetSystemMetrics(SmXVirtualScreen);
        var top = GetSystemMetrics(SmYVirtualScreen);
        var width = GetSystemMetrics(SmCxVirtualScreen);
        var height = GetSystemMetrics(SmCyVirtualScreen);
        if (width <= 0 || height <= 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read the physical virtual-desktop bounds.");
        return new Rectangle(left, top, width, height);
    }

    private static void WriteCaptureInfo(Rectangle bounds, int outputWidth, int outputHeight)
    {
        var message = $"[{DateTimeOffset.Now:O}] virtualBounds={bounds.X},{bounds.Y},{bounds.Width}x{bounds.Height}; output={outputWidth}x{outputHeight}{Environment.NewLine}";
        System.Diagnostics.Trace.Write(message);
        try
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "Logs");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "screen-capture.log"), message);
        }
        catch
        {
            // Diagnostics must never interrupt screen capture.
        }
    }

    private static void CapturePhysicalDesktop(Bitmap destination, Rectangle bounds)
    {
        using var graphics = Graphics.FromImage(destination);
        graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size,
            CopyPixelOperation.SourceCopy | CopyPixelOperation.CaptureBlt);
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
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Could not enable per-monitor DPI awareness for screen capture.");
            return new DpiAwarenessContext(previous);
        }

        public void Dispose() => SetThreadDpiAwarenessContext(_previous);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetSystemMetrics(int index);

    private byte[] EncodeJpeg(Image image)
    {
        var started = Stopwatch.GetTimestamp();
        using var output = new MemoryStream();
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, _jpegQuality);
        image.Save(output, JpegEncoder, parameters);
        if (output.Length is <= 0 or > ScreenFrameProtocol.MaxFrameBytes)
            throw new InvalidDataException("Captured JPEG exceeds the screen frame size limit.");
        var bytes = output.ToArray();
        _performance?.Jpeg(Stopwatch.GetTimestamp() - started);
        return bytes;
    }
}
