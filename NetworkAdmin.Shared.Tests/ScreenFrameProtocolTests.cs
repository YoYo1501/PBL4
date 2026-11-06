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
    public async Task Full_delta_and_full_frames_round_trip_without_changing_wire_shape()
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 1, 2, 0xFF, 0xD9 };
        using var stream = new MemoryStream();
        await ScreenFrameProtocol.WriteFrameAsync(stream,
            new ScreenFramePacket(0, true, 0, 0, 32, 24, jpeg));
        await ScreenFrameProtocol.WriteFrameAsync(stream,
            new ScreenFramePacket(1, false, 8, 4, 8, 8, jpeg));
        await ScreenFrameProtocol.WriteFrameAsync(stream,
            new ScreenFramePacket(2, true, 0, 0, 32, 24, jpeg));
        stream.Position = 0;

        var first = await ScreenFrameProtocol.ReadFrameAsync(stream);
        var delta = await ScreenFrameProtocol.ReadFrameAsync(stream);
        var resync = await ScreenFrameProtocol.ReadFrameAsync(stream);

        Assert.True(first!.Value.IsFullFrame);
        Assert.False(delta!.Value.IsFullFrame);
        Assert.Equal((8, 4, 8, 8), (delta.Value.X, delta.Value.Y, delta.Value.Width, delta.Value.Height));
        Assert.True(resync!.Value.IsFullFrame);
        Assert.Equal(2, resync.Value.Sequence);
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

    [Fact]
    public async Task Invalid_delta_header_is_rejected_before_payload_allocation()
    {
        using var stream = new MemoryStream();
        var header = new byte[ScreenFrameProtocol.HeaderBytes];
        BinaryPrimitives.WriteInt32BigEndian(header, 4);
        BinaryPrimitives.WriteInt64BigEndian(header.AsSpan(sizeof(int)), 1);
        header[12] = 0;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(13), 10);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(17), 10);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(21), 0);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(25), 8);
        stream.Write(header);
        stream.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(() => ScreenFrameProtocol.ReadFrameAsync(stream));
    }

    [Fact]
    public async Task Read_frame_honors_cancellation_before_reading()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var stream = new MemoryStream();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ScreenFrameProtocol.ReadFrameAsync(stream, cancellation.Token));
    }
}
