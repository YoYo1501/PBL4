package vn.networkadmin.transport.ports;

import vn.networkadmin.protocol.control.MessageEnvelope;
import vn.networkadmin.transport.domain.ClientSession;

@FunctionalInterface
public interface ClientMessageHandler {
    void handle(ClientSession session, MessageEnvelope message) throws Exception;
}
