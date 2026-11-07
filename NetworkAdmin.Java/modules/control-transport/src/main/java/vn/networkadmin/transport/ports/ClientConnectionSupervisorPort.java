package vn.networkadmin.transport.ports;

/** Owns reconnect/session lifecycle while reporting transport events through a port. */
public interface ClientConnectionSupervisorPort extends AutoCloseable {
    void runUntilInterrupted(ClientConnectionEvents events);
    @Override void close();
}
