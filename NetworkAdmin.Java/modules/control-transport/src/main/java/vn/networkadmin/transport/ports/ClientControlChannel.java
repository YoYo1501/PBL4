package vn.networkadmin.transport.ports;

import vn.networkadmin.protocol.control.MessageEnvelope;
import vn.networkadmin.protocol.control.MessageType;

import java.io.IOException;
import java.util.function.Consumer;

/** Client control-channel contract; socket and TLS details are hidden in its adapter. */
public interface ClientControlChannel extends AutoCloseable {
    void connect() throws IOException;
    String sessionId();
    boolean isConnected();
    void send(MessageType type, Object payload) throws IOException;
    void send(MessageType type, String requestId, Object payload) throws IOException;
    void runReadLoop(Consumer<MessageEnvelope> messageHandler) throws IOException;
    @Override void close();
}
