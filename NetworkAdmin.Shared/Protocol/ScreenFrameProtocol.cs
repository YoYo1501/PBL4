using System.Buffers.Binary;

namespace NetworkAdmin.Shared.Protocol;

public static class ScreenFrameProtocol
{
    public const int MaxFrameBytes = 12 * 1024 * 1024;
    public const int HeaderBytes = sizeof(int) + sizeof(long);

    public static async Task WriteFrameAsync(Stream stream, long sequence, ReadOnlyMemory<byte> jpeg,
        CancellationToken cancellationToken = default)
    {
        if (jpeg.IsEmpty || jpeg.Length > MaxFrameBytes)
            throw new InvalidDataException($"Screen frame size must be between 1 and {MaxFrameBytes} bytes.");

        var header = new byte[HeaderBytes];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, sizeof(int)), jpeg.Length);
        BinaryPrimitives.WriteInt64BigEndian(header.AsSpan(sizeof(int), sizeof(long)), sequence);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(jpeg, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<(long Sequence, byte[] Jpeg)?> ReadFrameAsync(Stream stream,
        CancellationToken cancellationToken = default)
    {
        var header = new byte[HeaderBytes];
        if (!await ReadExactlyAsync(stream, header, allowEof: true, cancellationToken).ConfigureAwait(false))
            return null;

        var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(0, sizeof(int)));
        var sequence = BinaryPrimitives.ReadInt64BigEndian(header.AsSpan(sizeof(int), sizeof(long)));
        if (length <= 0 || length > MaxFrameBytes || sequence < 0)
            throw new InvalidDataException("Invalid screen frame header.");

        var jpeg = new byte[length];
        await ReadExactlyAsync(stream, jpeg, allowEof: false, cancellationToken).ConfigureAwait(false);
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8 || jpeg[^2] != 0xFF || jpeg[^1] != 0xD9)
            throw new InvalidDataException("Frame is not a complete JPEG image.");
        return (sequence, jpeg);
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
