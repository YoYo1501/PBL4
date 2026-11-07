package vn.networkadmin.admin.ports.screenshare;

import vn.networkadmin.protocol.control.MessageEnvelope;
import vn.networkadmin.transport.domain.ClientSession;

/** Feature hook for screen-share control messages; keeps the generic runtime independent of UI/adapters. */
public interface AdminScreenShareMessageHandler {
    void handle(ClientSession session, MessageEnvelope message);
    void disconnected(ClientSession session);

    AdminScreenShareMessageHandler NO_OP = new AdminScreenShareMessageHandler() {
        @Override public void handle(ClientSession session, MessageEnvelope message) { }
        @Override public void disconnected(ClientSession session) { }
    };
}
