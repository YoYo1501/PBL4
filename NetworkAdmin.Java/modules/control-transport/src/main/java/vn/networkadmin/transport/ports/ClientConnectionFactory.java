package vn.networkadmin.transport.ports;

@FunctionalInterface
public interface ClientConnectionFactory {
    ClientControlChannel create();
}
