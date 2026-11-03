using System.Buffers.Binary;
using NetworkAdmin.Shared.Protocol;
using Xunit;

namespace NetworkAdmin.Shared.Tests;

public sealed class ScreenFrameProtocolTests
{
    [Fact]
    public async Task Frame_round_trip_preserves_sequence_and_jpeg_bytes()
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 1, 2, 3, 0xFF, 0xD9 };
        using var stream = new MemoryStream();
        await ScreenFrameProtocol.WriteFrameAsync(stream, 42, jpeg);
        stream.Position = 0;
        var frame = await ScreenFrameProtocol.ReadFrameAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal(42, frame.Value.Sequence);
        Assert.Equal(jpeg, frame.Value.Jpeg);
    }

    [Fact]
    public async Task Oversized_frame_is_rejected_before_any_bytes_are_written()
    {
        using var stream = new MemoryStream();
        var oversized = new byte[ScreenFrameProtocol.MaxFrameBytes + 1];
        await Assert.ThrowsAsync<InvalidDataException>(() => ScreenFrameProtocol.WriteFrameAsync(stream, 1, oversized));
        Assert.Equal(0, stream.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ScreenFrameProtocol.MaxFrameBytes + 1)]
    public async Task Invalid_declared_frame_size_is_rejected(int length)
    {
        using var stream = new MemoryStream(new byte[ScreenFrameProtocol.HeaderBytes]);
        var header = new byte[ScreenFrameProtocol.HeaderBytes];
        BinaryPrimitives.WriteInt32BigEndian(header, length);
        BinaryPrimitives.WriteInt64BigEndian(header.AsSpan(sizeof(int)), 1);
        stream.Write(header);
        stream.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => ScreenFrameProtocol.ReadFrameAsync(stream));
    }

    [Fact]
    public async Task Truncated_frame_and_non_jpeg_payload_are_rejected()
    {
        using var truncated = new MemoryStream();
        await ScreenFrameProtocol.WriteFrameAsync(truncated, 1, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
        truncated.SetLength(ScreenFrameProtocol.HeaderBytes + 2);
        truncated.Position = 0;
        await Assert.ThrowsAsync<EndOfStreamException>(() => ScreenFrameProtocol.ReadFrameAsync(truncated));

        using var invalidJpeg = new MemoryStream();
        var header = new byte[ScreenFrameProtocol.HeaderBytes];
        BinaryPrimitives.WriteInt32BigEndian(header, 4);
        BinaryPrimitives.WriteInt64BigEndian(header.AsSpan(sizeof(int)), 1);
        invalidJpeg.Write(header);
        invalidJpeg.Write(new byte[] { 0, 1, 2, 3 });
        invalidJpeg.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => ScreenFrameProtocol.ReadFrameAsync(invalidJpeg));
    }
}
