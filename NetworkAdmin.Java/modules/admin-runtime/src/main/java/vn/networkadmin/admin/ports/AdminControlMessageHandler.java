package vn.networkadmin.admin.ports;

import vn.networkadmin.protocol.control.MessageEnvelope;
import vn.networkadmin.transport.domain.ClientSession;

/** Optional application handler for session-scoped remote-control and chat messages. */
public interface AdminControlMessageHandler {
    AdminControlMessageHandler NO_OP = new AdminControlMessageHandler() { };
    default void handle(ClientSession session, MessageEnvelope message) { }
    default void disconnected(ClientSession session) { }
}
