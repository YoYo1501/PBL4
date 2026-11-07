package vn.networkadmin.transport.ports;

import java.io.IOException;

/** Lifecycle contract for the inbound control transport. */
public interface ControlServerPort extends AutoCloseable {
    void run(ControlServerListener listener) throws IOException;
    boolean isListening();
    @Override void close();
}
