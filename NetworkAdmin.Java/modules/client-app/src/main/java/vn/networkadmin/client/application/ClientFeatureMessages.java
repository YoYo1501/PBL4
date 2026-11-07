package vn.networkadmin.client.application;

import vn.networkadmin.client.ports.ClientFeatureMessageHandler;
import vn.networkadmin.protocol.control.MessageEnvelope;
import vn.networkadmin.transport.ports.ClientControlChannel;

import java.util.List;

/** Composition adapter that dispatches each control message to its owning feature. */
public final class ClientFeatureMessages implements ClientFeatureMessageHandler {
    private final List<ClientFeatureMessageHandler> handlers;
    public ClientFeatureMessages(ClientFeatureMessageHandler... handlers) {
        this.handlers = List.of(handlers);
    }
    @Override public void connected(ClientControlChannel channel) { handlers.forEach(h -> h.connected(channel)); }
    @Override public void disconnected(ClientControlChannel channel) { handlers.forEach(h -> h.disconnected(channel)); }
    @Override public void handle(ClientControlChannel channel, MessageEnvelope message) {
        handlers.forEach(handler -> handler.handle(channel, message));
    }
}
