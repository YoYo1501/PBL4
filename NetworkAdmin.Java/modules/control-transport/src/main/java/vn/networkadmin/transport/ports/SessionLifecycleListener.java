package vn.networkadmin.transport.ports;

import vn.networkadmin.transport.domain.ClientSession;

public interface SessionLifecycleListener {
    default void connected(ClientSession session) { }
    default void disconnected(ClientSession session) { }
    default void failed(String remoteAddress, Exception error) { }
}
