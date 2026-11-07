package vn.networkadmin.protocol.control;

import java.util.List;
import java.util.Map;

public record HelloMessage(String clientId, String clientName, int protocolVersion, List<String> capabilities) {
    public HelloMessage { capabilities = capabilities == null ? List.of() : List.copyOf(capabilities); }
    public HelloMessage(String clientId, String clientName) { this(clientId, clientName, RemoteProtocolContract.CURRENT_VERSION, List.of()); }
    public String validate() {
        if (!RemoteProtocolContract.isValidIdentifier(clientId)) return "ClientId is invalid.";
        if (!RemoteProtocolContract.isValidDisplayName(clientName)) return "ClientName is invalid.";
        if (!RemoteProtocolContract.isCompatibleVersion(protocolVersion)) return "ProtocolVersion is not supported.";
        if (!RemoteProtocolContract.isValidCapabilities(capabilities)) return "Capabilities are invalid.";
        return null;
    }
    public Map<String, Object> toPayload() { return Map.of("clientId", clientId, "clientName", clientName, "protocolVersion", protocolVersion, "capabilities", capabilities); }
    public static HelloMessage fromPayload(Map<String, Object> data) {
        String id = string(data.get("clientId"));
        String name = string(data.get("clientName"));
        int version = data.get("protocolVersion") instanceof Number n ? n.intValue() : RemoteProtocolContract.CURRENT_VERSION;
        List<String> caps = data.get("capabilities") instanceof List<?> list ? list.stream().filter(String.class::isInstance).map(String.class::cast).toList() : List.of();
        return new HelloMessage(id, name, version, caps);
    }
    private static String string(Object value) { return value instanceof String s ? s : ""; }
}
