package vn.networkadmin.transport.ports;

/** Inbound control server events consumed by an application service. */
public interface ControlServerListener extends ClientMessageHandler, SessionLifecycleListener { }
