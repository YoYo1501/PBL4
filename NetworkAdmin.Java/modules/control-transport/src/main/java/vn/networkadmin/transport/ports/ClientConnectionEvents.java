package vn.networkadmin.transport.ports;

import vn.networkadmin.protocol.control.MessageEnvelope;

public interface ClientConnectionEvents {
    void connected(ClientControlChannel connection);
    void messageReceived(ClientControlChannel connection, MessageEnvelope message);
    void disconnected(ClientControlChannel connection);
    void failed(Exception error);
}
