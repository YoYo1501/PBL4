package vn.networkadmin.transport.ports;

import vn.networkadmin.protocol.control.MessageType;

import java.io.IOException;

/** Sends protocol messages through an established control session. */
public interface SessionMessageSender {
    void send(String sessionId, MessageType type, String requestId, Object payload) throws IOException;
    void closeSession(String sessionId);
}
