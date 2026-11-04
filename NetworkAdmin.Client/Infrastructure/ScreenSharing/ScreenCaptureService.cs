using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.ComponentModel;
using System.Runtime.InteropServices;
using NetworkAdmin.Shared.Protocol;

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

    public ScreenCaptureService(int maxWidth = 3840, int maxHeight = 2160, int jpegQuality = 70)
    {
        _maxWidth = Math.Clamp(maxWidth, 640, 3840);
        _maxHeight = Math.Clamp(maxHeight, 480, 2160);
        _jpegQuality = Math.Clamp(jpegQuality, 30, 85);
    }

    public Bitmap CaptureBitmap()
    {
        // Capture runs on a worker thread. Give that thread the same physical-pixel
        // coordinate space as the interactive desktop before reading bounds/capturing.
        using var dpiContext = DpiAwarenessContext.EnterPerMonitorV2();
        var bounds = GetPrimaryDisplayBounds();
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new InvalidOperationException("No active screen is available for capture.");

        using var source = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        CapturePhysicalDesktop(source, bounds);

        var scale = Math.Min(1d, Math.Min((double)_maxWidth / source.Width, (double)_maxHeight / source.Height));
        if (scale >= 1d) return (Bitmap)source.Clone();
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

        return (Bitmap)resized.Clone();
    }

    public ScreenFramePacket? CreatePacket(Bitmap current, Bitmap? previous, long sequence)
    {
        if (previous is null || previous.Size != current.Size)
            return new ScreenFramePacket(sequence, true, 0, 0, current.Width, current.Height, EncodeJpeg(current));

        var changed = FindChangeBounds(previous, current);
        if (changed.IsEmpty) return null;
        var areaRatio = (double)changed.Width * changed.Height / ((double)current.Width * current.Height);
        if (areaRatio > FullFrameThreshold)
            return new ScreenFramePacket(sequence, true, 0, 0, current.Width, current.Height, EncodeJpeg(current));

        using var patch = current.Clone(changed, PixelFormat.Format24bppRgb);
        return new ScreenFramePacket(sequence, false, changed.X, changed.Y,
            changed.Width, changed.Height, EncodeJpeg(patch));
    }

    private static Rectangle FindChangeBounds(Bitmap previous, Bitmap current)
    {
        var rect = new Rectangle(0, 0, current.Width, current.Height);
        var format = PixelFormat.Format24bppRgb;
        var oldData = previous.LockBits(rect, ImageLockMode.ReadOnly, format);
        var newData = current.LockBits(rect, ImageLockMode.ReadOnly, format);
        var rowBytes = checked(current.Width * 3);
        var oldRow = new byte[rowBytes];
        var newRow = new byte[rowBytes];
        var minX = current.Width;
        var minY = current.Height;
        var maxX = 0;
        var maxY = 0;
        try
        {
            for (var blockY = 0; blockY < current.Height; blockY += BlockSize)
            {
                var endY = Math.Min(blockY + BlockSize, current.Height);
                for (var y = blockY; y < endY; y++)
                {
                    Marshal.Copy(IntPtr.Add(oldData.Scan0, y * oldData.Stride), oldRow, 0, rowBytes);
                    Marshal.Copy(IntPtr.Add(newData.Scan0, y * newData.Stride), newRow, 0, rowBytes);
                    for (var blockX = 0; blockX < current.Width; blockX += BlockSize)
                    {
                        var endX = Math.Min(blockX + BlockSize, current.Width);
                        var offset = blockX * 3;
                        if (oldRow.AsSpan(offset, (endX - blockX) * 3).SequenceEqual(newRow.AsSpan(offset, (endX - blockX) * 3)))
                            continue;
                        minX = Math.Min(minX, blockX);
                        minY = Math.Min(minY, blockY);
                        maxX = Math.Max(maxX, endX);
                        maxY = Math.Max(maxY, endY);
                    }
                }
            }
        }
        finally
        {
            previous.UnlockBits(oldData);
            current.UnlockBits(newData);
        }
        return maxX <= minX || maxY <= minY ? Rectangle.Empty : Rectangle.FromLTRB(minX, minY, maxX, maxY);
    }

    private static Rectangle GetPrimaryDisplayBounds()
    {
        const int CurrentSettings = -1;
        var mode = new DevMode { dmDeviceName = string.Empty, dmFormName = string.Empty };
        mode.dmSize = (ushort)Marshal.SizeOf<DevMode>();
        if (!EnumDisplaySettings(null, CurrentSettings, ref mode) || mode.dmPelsWidth == 0 || mode.dmPelsHeight == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read the active primary display resolution.");

        // RemoteX captures the primary display. EnumDisplaySettings reports its active
        // output mode (physical pixels), avoiding DPI-virtualized virtual-screen metrics.
        return new Rectangle(0, 0, checked((int)mode.dmPelsWidth), checked((int)mode.dmPelsHeight));
    }

    private static void CapturePhysicalDesktop(Bitmap destination, Rectangle bounds)
    {
        const uint SrcCopy = 0x00CC0020;
        const uint CaptureBlt = 0x40000000;
        var desktopDc = GetDC(IntPtr.Zero);
        if (desktopDc == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not access the desktop for screen capture.");

        try
        {
            using var graphics = Graphics.FromImage(destination);
            var destinationDc = graphics.GetHdc();
            try
            {
                if (!BitBlt(destinationDc, 0, 0, bounds.Width, bounds.Height,
                        desktopDc, bounds.X, bounds.Y, SrcCopy | CaptureBlt))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not capture the complete virtual desktop.");
            }
            finally
            {
                graphics.ReleaseHdc(destinationDc);
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, desktopDc);
        }
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNumber, ref DevMode devMode);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(IntPtr destination, int destinationX, int destinationY,
        int width, int height, IntPtr source, int sourceX, int sourceY, uint rasterOperation);

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
