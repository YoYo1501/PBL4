package vn.networkadmin.admin.application;

import vn.networkadmin.admin.ports.AdminControlMessageHandler;
import vn.networkadmin.protocol.control.MessageEnvelope;
import vn.networkadmin.transport.domain.ClientSession;

import java.util.List;

/** Composition adapter that forwards control-plane messages to independent feature handlers. */
public final class AdminFeatureMessages implements AdminControlMessageHandler {
    private final List<AdminControlMessageHandler> handlers;

    public AdminFeatureMessages(AdminControlMessageHandler... handlers) {
        this.handlers = List.of(handlers);
    }
    @Override public void handle(ClientSession session, MessageEnvelope message) {
        handlers.forEach(handler -> handler.handle(session, message));
    }
    @Override public void disconnected(ClientSession session) {
        handlers.forEach(handler -> handler.disconnected(session));
    }
}
