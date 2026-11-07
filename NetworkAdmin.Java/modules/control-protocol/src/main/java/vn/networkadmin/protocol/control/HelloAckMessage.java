package vn.networkadmin.protocol.control;

import java.util.List;
import java.util.Map;

public record HelloAckMessage(boolean accepted, String sessionId, String message, int protocolVersion, List<String> capabilities) {
    public HelloAckMessage { capabilities = capabilities == null ? List.of() : List.copyOf(capabilities); }
    public String validate() {
        if (!RemoteProtocolContract.isCompatibleVersion(protocolVersion)) return "ProtocolVersion is not supported.";
        if (!RemoteProtocolContract.isValidCapabilities(capabilities)) return "Capabilities are invalid.";
        if (accepted && !RemoteProtocolContract.isValidSessionId(sessionId)) return "Accepted HelloAck requires a valid SessionId.";
        return null;
    }
    public Map<String, Object> toPayload() { return Map.of("accepted", accepted, "sessionId", sessionId == null ? "" : sessionId,
            "message", message == null ? "" : message, "protocolVersion", protocolVersion, "capabilities", capabilities); }
    public static HelloAckMessage fromPayload(Map<String, Object> data) {
        boolean accepted = Boolean.TRUE.equals(data.get("accepted"));
        String session = data.get("sessionId") instanceof String s ? s : "";
        String message = data.get("message") instanceof String s ? s : "";
        int version = data.get("protocolVersion") instanceof Number n ? n.intValue() : RemoteProtocolContract.CURRENT_VERSION;
        List<String> caps = data.get("capabilities") instanceof List<?> list ? list.stream().filter(String.class::isInstance).map(String.class::cast).toList() : List.of();
        return new HelloAckMessage(accepted, session, message, version, caps);
    }
}
