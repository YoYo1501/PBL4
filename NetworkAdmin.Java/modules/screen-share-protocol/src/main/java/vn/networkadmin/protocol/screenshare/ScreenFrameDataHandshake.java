package vn.networkadmin.protocol.screenshare;

import java.io.DataInput;
import java.io.DataOutput;
import java.io.IOException;
import java.util.Objects;

/** Fixed binary preamble used to bind a TLS frame stream to one consented control session. */
public record ScreenFrameDataHandshake(String requestId, String clientId, String sessionId, String token) {
    public static final int MAGIC = 0x4E415353; // NASS
    public static final int ACCEPTED = 1;
    public static final int REJECTED = 0;

    public ScreenFrameDataHandshake {
        if (blank(requestId) || blank(clientId) || blank(sessionId) || blank(token))
            throw new IllegalArgumentException("Screen data handshake fields are required.");
    }

    public void write(DataOutput output) throws IOException {
        output.writeInt(MAGIC);
        output.writeUTF(requestId); output.writeUTF(clientId); output.writeUTF(sessionId); output.writeUTF(token);
    }

    public static ScreenFrameDataHandshake read(DataInput input) throws IOException {
        if (input.readInt() != MAGIC) throw new IOException("Invalid screen data handshake.");
        try { return new ScreenFrameDataHandshake(input.readUTF(), input.readUTF(), input.readUTF(), input.readUTF()); }
        catch (IllegalArgumentException error) { throw new IOException("Invalid screen data handshake fields.", error); }
    }

    private static boolean blank(String value) { return value == null || value.isBlank() || value.length() > 1024; }
}
