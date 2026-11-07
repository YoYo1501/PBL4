package vn.networkadmin.protocol.control;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.Socket;
import java.nio.ByteBuffer;
import java.nio.charset.CharacterCodingException;
import java.nio.charset.CodingErrorAction;
import java.nio.charset.StandardCharsets;
import java.util.LinkedHashMap;
import java.util.Map;

/** UTF-8 JSON-lines framing compatible with NetworkAdmin.Shared.Protocol.JsonLineProtocol. */
public final class JsonLineProtocol {
    private JsonLineProtocol() { }
    public static final int DEFAULT_MAX_FRAME_BYTES = 256 * 1024;

    public static void enableKeepAlive(Socket socket) throws IOException { socket.setKeepAlive(true); }

    public static void send(OutputStream stream, MessageType type, String requestId, Object payload) throws IOException {
        if (type == null) throw new IllegalArgumentException("Message type is not recognized.");
        if (!RemoteProtocolContract.isValidRequestId(requestId)) throw new IllegalArgumentException("RequestId is required and must be a valid identifier.");
        if (payload == null) throw new IllegalArgumentException("Message payload is required.");
        Map<String, Object> envelope = new LinkedHashMap<>();
        envelope.put("type", type.wireId()); envelope.put("requestId", requestId); envelope.put("data", payload);
        byte[] bytes = (JsonValueCodec.encode(envelope) + "\n").getBytes(StandardCharsets.UTF_8);
        if (bytes.length > DEFAULT_MAX_FRAME_BYTES) throw new IOException("Frame exceeds the limit of " + DEFAULT_MAX_FRAME_BYTES + " bytes.");
        stream.write(bytes); stream.flush();
    }

    /** Returns null only for EOF before any byte of a new frame. */
    public static MessageEnvelope receive(InputStream stream) throws IOException { return receive(stream, DEFAULT_MAX_FRAME_BYTES); }

    public static MessageEnvelope receive(InputStream stream, int maxFrameBytes) throws IOException {
        if (maxFrameBytes <= 0) throw new IllegalArgumentException("maxFrameBytes must be greater than zero.");
        ByteArrayOutputStream frame = new ByteArrayOutputStream();
        while (true) {
            int next = stream.read();
            if (next < 0) {
                if (frame.size() == 0) return null;
                throw new IOException("Connection closed before frame delimiter.");
            }
            if (next == '\n') break;
            if (frame.size() >= maxFrameBytes) throw new IOException("Frame exceeds the limit of " + maxFrameBytes + " bytes.");
            frame.write(next);
        }
        final String json;
        try { json = StandardCharsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT).onUnmappableCharacter(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(frame.toByteArray())).toString(); }
        catch (CharacterCodingException ex) { throw new IOException("Frame is not valid UTF-8.", ex); }
        final Object parsed;
        try { parsed = JsonValueCodec.decode(json); } catch (IllegalArgumentException ex) { throw new IOException("Invalid JSON message envelope.", ex); }
        if (!(parsed instanceof Map<?, ?> object)) throw new IOException("Message envelope must be a JSON object.");
        Object rawType = object.get("type");
        if (!(rawType instanceof Number number)) throw new IOException("Message type is invalid.");
        final MessageType type;
        try { type = MessageType.fromWireId(new java.math.BigDecimal(number.toString()).intValueExact()); }
        catch (RuntimeException ex) { throw new IOException("Message type is invalid or not recognized.", ex); }
        Object rawRequestId = object.get("requestId");
        String requestId = rawRequestId instanceof String value ? value : null;
        Object data = object.get("data");
        MessageEnvelope envelope = new MessageEnvelope(type, requestId, data);
        String error = RemoteProtocolContract.validateEnvelope(envelope);
        if (error != null) throw new IOException(error);
        return envelope;
    }

    @SuppressWarnings("unchecked") public static Map<String, Object> getObjectPayload(MessageEnvelope envelope) {
        if (!(envelope.data() instanceof Map<?, ?>)) throw new IllegalArgumentException("Message payload must be a JSON object.");
        return (Map<String, Object>) envelope.data();
    }
}
