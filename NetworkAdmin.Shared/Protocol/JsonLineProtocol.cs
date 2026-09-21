using System.Buffers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NetworkAdmin.Shared.Messages;

namespace NetworkAdmin.Shared.Protocol;

public static class JsonLineProtocol
{
    public const int DefaultMaxFrameBytes = 256 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task SendAsync<TPayload>(
        NetworkStream stream,
        MessageType type,
        string requestId,
        TPayload payload,
        CancellationToken cancellationToken = default)
    {
        var envelope = new
        {
            Type = type,
            RequestId = requestId,
            Data = payload
        };

        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json + "\n");
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<MessageEnvelope?> ReceiveAsync(
        NetworkStream stream,
        int maxFrameBytes = DefaultMaxFrameBytes,
        CancellationToken cancellationToken = default)
    {
        var frame = await ReadFrameAsync(stream, maxFrameBytes, cancellationToken).ConfigureAwait(false);
        if (frame is null)
        {
            return null;
        }

        return JsonSerializer.Deserialize<MessageEnvelope>(frame, JsonOptions);
    }

    public static TPayload? GetPayload<TPayload>(MessageEnvelope envelope)
    {
        return envelope.Data.Deserialize<TPayload>(JsonOptions);
    }

    private static async Task<string?> ReadFrameAsync(
        NetworkStream stream,
        int maxFrameBytes,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(1);
        try
        {
            using var frame = new MemoryStream();
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return frame.Length == 0 ? null : throw new JsonException("Connection closed before frame delimiter.");
                }

                if (buffer[0] == (byte)'\n')
                {
                    return Encoding.UTF8.GetString(frame.ToArray());
                }

                if (frame.Length >= maxFrameBytes)
                {
                    throw new InvalidDataException($"Frame exceeds the limit of {maxFrameBytes} bytes.");
                }

                frame.WriteByte(buffer[0]);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
