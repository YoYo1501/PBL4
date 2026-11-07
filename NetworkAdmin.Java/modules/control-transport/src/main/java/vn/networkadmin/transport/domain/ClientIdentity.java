package vn.networkadmin.transport.domain;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.HexFormat;

public final class ClientIdentity {
    private ClientIdentity() { }
    /** Stable 32-hex-character ID using the same machine/domain/user input as the .NET client. */
    public static String stableId(String machineName, String userDomain, String userName) {
        String raw = machineName + "|" + userDomain + "|" + userName;
        try { return HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest(raw.getBytes(StandardCharsets.UTF_8)), 0, 16); }
        catch (Exception ex) { throw new IllegalStateException("SHA-256 is unavailable.", ex); }
    }
}
