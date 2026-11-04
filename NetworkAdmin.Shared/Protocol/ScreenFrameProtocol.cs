using System.Buffers.Binary;

namespace NetworkAdmin.Shared.Protocol;

public readonly record struct ScreenFramePacket(long Sequence, bool IsFullFrame, int X, int Y, int Width, int Height, byte[] Jpeg);

public static class ScreenFrameProtocol
{
    public const int MaxFrameBytes = 12 * 1024 * 1024;
    // JPEG length, sequence, full/delta flag, and the frame rectangle.
    public const int HeaderBytes = sizeof(int) + sizeof(long) + 1 + (sizeof(int) * 4);

    public static Task WriteFrameAsync(Stream stream, long sequence, ReadOnlyMemory<byte> jpeg,
        CancellationToken cancellationToken = default) =>
        WriteFrameAsync(stream, new ScreenFramePacket(sequence, true, 0, 0, 0, 0, jpeg.ToArray()), cancellationToken);

    public static async Task WriteFrameAsync(Stream stream, ScreenFramePacket frame,
        CancellationToken cancellationToken = default)
    {
        var jpeg = frame.Jpeg;
        if (jpeg.Length is <= 0 or > MaxFrameBytes || frame.Sequence < 0 ||
            (frame.IsFullFrame ? (frame.Width < 0 || frame.Height < 0 || (frame.Width == 0) != (frame.Height == 0)) : (frame.Width <= 0 || frame.Height <= 0)) ||
            frame.X < 0 || frame.Y < 0 ||
            frame.X > 3840 || frame.Y > 2160 || frame.Width > 3840 || frame.Height > 2160 ||
            (frame.Width > 0 && frame.X + frame.Width > 3840) ||
            (frame.Height > 0 && frame.Y + frame.Height > 2160) ||
            (frame.IsFullFrame && (frame.X != 0 || frame.Y != 0)))
            throw new InvalidDataException("Invalid screen frame packet.");

        var header = new byte[HeaderBytes];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), jpeg.Length);
        BinaryPrimitives.WriteInt64BigEndian(header.AsSpan(4, 8), frame.Sequence);
        header[12] = frame.IsFullFrame ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(13, 4), frame.X);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(17, 4), frame.Y);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(21, 4), frame.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(25, 4), frame.Height);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(jpeg, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<ScreenFramePacket?> ReadFrameAsync(Stream stream,
        CancellationToken cancellationToken = default)
    {
        var header = new byte[HeaderBytes];
        if (!await ReadExactlyAsync(stream, header, allowEof: true, cancellationToken).ConfigureAwait(false))
            return null;

        var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(0, 4));
        var sequence = BinaryPrimitives.ReadInt64BigEndian(header.AsSpan(4, 8));
        var kind = header[12];
        var x = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(13, 4));
        var y = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(17, 4));
        var width = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(21, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(25, 4));
        if (length <= 0 || length > MaxFrameBytes || sequence < 0 || kind > 1 ||
            x < 0 || y < 0 ||
            (kind == 1 ? (width < 0 || height < 0 || (width == 0) != (height == 0)) : (width <= 0 || height <= 0)) ||
            x > 3840 || y > 2160 || width > 3840 || height > 2160 ||
            (width > 0 && x + width > 3840) || (height > 0 && y + height > 2160) ||
            (kind == 1 && (x != 0 || y != 0)))
            throw new InvalidDataException("Invalid screen frame header.");

        var jpeg = new byte[length];
        await ReadExactlyAsync(stream, jpeg, allowEof: false, cancellationToken).ConfigureAwait(false);
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8 || jpeg[^2] != 0xFF || jpeg[^1] != 0xD9)
            throw new InvalidDataException("Frame is not a complete JPEG image.");
        return new ScreenFramePacket(sequence, kind == 1, x, y, width, height, jpeg);
    }

    private static async Task<bool> ReadExactlyAsync(Stream stream, Memory<byte> buffer, bool allowEof,
        CancellationToken cancellationToken)
    {
        var readTotal = 0;
        while (readTotal < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[readTotal..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (allowEof && readTotal == 0) return false;
                throw new EndOfStreamException("Connection ended in the middle of a screen frame.");
            }
            readTotal += read;
        }
        return true;
    }
}
