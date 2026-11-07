package vn.networkadmin.client.ports;

import vn.networkadmin.protocol.control.MessageEnvelope;
import vn.networkadmin.transport.ports.ClientControlChannel;

/** Optional application handler for independently consented Client features. */
public interface ClientFeatureMessageHandler {
    ClientFeatureMessageHandler NO_OP = new ClientFeatureMessageHandler() { };
    default void connected(ClientControlChannel channel) { }
    default void disconnected(ClientControlChannel channel) { }
    default void handle(ClientControlChannel channel, MessageEnvelope message) { }
}
