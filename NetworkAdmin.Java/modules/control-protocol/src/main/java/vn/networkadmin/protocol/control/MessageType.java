package vn.networkadmin.protocol.control;

import java.util.Arrays;

/** Numeric wire IDs are fixed by NetworkAdmin.Shared.Messages.MessageType. */
public enum MessageType {
    HELLO(1), HELLO_ACK(2), SYSTEM_STATS(3), NETWORK_INFO(4), PING_REQUEST(5), PING_RESULT(6),
    SCAN_REQUEST(7), SCAN_RESULT(8), REMOTE_ADMINISTRATION_COMMAND(9), REMOTE_ADMINISTRATION_RESULT(10),
    REMOTE_ADMINISTRATION_CANCEL(11), SCREEN_SHARE_REQUEST(12), SCREEN_SHARE_CONSENT(13), SCREEN_SHARE_STOP(14),
    SCREEN_CHANNEL_READY(15), SCREEN_SHARE_START(16), CONTROL_REQUEST(17), CONTROL_CONSENT(18), CONTROL_STOP(19),
    CONTROL_INPUT_EVENT(20), REMOTE_SESSION_REQUEST(21), REMOTE_SESSION_RESPONSE(22), REMOTE_SESSION_START(23),
    REMOTE_SESSION_STOP(24), REMOTE_SESSION_STATUS(25), REMOTE_SESSION_ERROR(26), REMOTE_SESSION_CANCEL(27),
    REMOTE_CHAT_MESSAGE(28), SECURITY_EVENT(29), SECURITY_ALERT(30), MONITORING_STATUS(31), MONITORING_START(32),
    MONITORING_STOP(33);

    private final int wireId;
    MessageType(int wireId) { this.wireId = wireId; }
    public int wireId() { return wireId; }
    public static MessageType fromWireId(int id) {
        return Arrays.stream(values()).filter(type -> type.wireId == id).findFirst()
                .orElseThrow(() -> new IllegalArgumentException("Message type '" + id + "' is not recognized."));
    }
}
