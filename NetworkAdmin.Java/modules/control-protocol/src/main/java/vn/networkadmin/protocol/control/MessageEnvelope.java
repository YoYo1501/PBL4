package vn.networkadmin.protocol.control;

public record MessageEnvelope(MessageType type, String requestId, Object data) { }
