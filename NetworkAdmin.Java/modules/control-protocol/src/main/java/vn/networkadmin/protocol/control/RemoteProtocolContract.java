package vn.networkadmin.protocol.control;

import java.nio.charset.StandardCharsets;
import java.util.Collection;

public final class RemoteProtocolContract {
    private RemoteProtocolContract() { }
    public static final int CURRENT_VERSION = 1;
    public static final int MINIMUM_SUPPORTED_VERSION = 1;
    public static final int MAX_IDENTIFIER_LENGTH = 128;
    public static final int MAX_REQUEST_ID_LENGTH = 128;
    public static final int MAX_CHAT_TEXT_LENGTH = 4096;
    public static final int MAX_CHAT_PAYLOAD_BYTES = 16 * 1024;
    public static final int MAX_SECURITY_FIELD_LENGTH = 256;
    public static final int MAX_SECURITY_COMMAND_LINE_LENGTH = 4096;
    public static final int MAX_SECURITY_PAYLOAD_BYTES = 32 * 1024;
    public static final int MAX_ERROR_MESSAGE_LENGTH = 1024;
    public static final int MAX_CAPABILITY_LENGTH = 64;
    public static final int MAX_CAPABILITIES = 32;
    public static final int MAX_SESSION_TIMEOUT_SECONDS = 3600;

    public static boolean isCompatibleVersion(int version) { return version >= MINIMUM_SUPPORTED_VERSION && version <= CURRENT_VERSION; }
    public static boolean isValidIdentifier(String value) { return isValidIdentifier(value, MAX_IDENTIFIER_LENGTH); }
    public static boolean isValidIdentifier(String value, int maxLength) {
        return value != null && !value.isBlank() && value.length() <= maxLength
                && value.chars().noneMatch(c -> Character.isISOControl(c) || Character.isWhitespace(c));
    }
    public static boolean isValidDisplayName(String value) {
        return value != null && !value.isBlank() && value.length() <= MAX_IDENTIFIER_LENGTH
                && value.equals(value.trim()) && value.chars().noneMatch(Character::isISOControl);
    }
    public static boolean isValidRequestId(String value) { return isValidIdentifier(value, MAX_REQUEST_ID_LENGTH); }
    public static boolean isValidSessionId(String value) { return isValidIdentifier(value, MAX_IDENTIFIER_LENGTH); }
    public static boolean isValidCapabilities(Collection<String> capabilities) {
        return capabilities != null && capabilities.size() <= MAX_CAPABILITIES
                && capabilities.stream().allMatch(value -> isValidIdentifier(value, MAX_CAPABILITY_LENGTH));
    }
    public static String validateEnvelope(MessageEnvelope envelope) {
        if (envelope == null) return "Message envelope is required.";
        if (envelope.type() == null) return "Message type is not recognized.";
        if (!isValidRequestId(envelope.requestId())) return "RequestId is required and must be a valid identifier.";
        if (envelope.data() == null) return "Message payload is required.";
        return null;
    }
    public static String validatePayloadSize(Object payload, int maxBytes) {
        if (payload == null) return "Message payload is required.";
        if (maxBytes <= 0) throw new IllegalArgumentException("maxBytes must be greater than zero.");
        return JsonValueCodec.encode(payload).getBytes(StandardCharsets.UTF_8).length <= maxBytes
                ? null : "Message payload exceeds the " + maxBytes + "-byte limit.";
    }
}
