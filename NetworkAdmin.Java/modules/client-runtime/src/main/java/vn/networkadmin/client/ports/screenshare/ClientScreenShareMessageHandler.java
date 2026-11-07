package vn.networkadmin.client.ports.screenshare;

import vn.networkadmin.protocol.control.MessageEnvelope;
import vn.networkadmin.transport.ports.ClientControlChannel;

/** Optional screen-share feature boundary for the general client runtime. */
public interface ClientScreenShareMessageHandler {
    void handle(ClientControlChannel source, MessageEnvelope message);
    default void connected(ClientControlChannel source) { }
    default void failed(Exception error) { }
    void disconnected(ClientControlChannel source);

    ClientScreenShareMessageHandler NO_OP = new ClientScreenShareMessageHandler() {
        @Override public void handle(ClientControlChannel source, MessageEnvelope message) { }
        @Override public void disconnected(ClientControlChannel source) { }
    };
}
