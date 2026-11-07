package vn.networkadmin.transport.application;

import java.net.InetAddress;
import java.net.UnknownHostException;
import java.time.Duration;

public record ControlServerOptions(InetAddress bindAddress, int port, int maxConnections,
                                  Duration helloTimeout, Duration heartbeatTimeout) {
    public ControlServerOptions {
        if (bindAddress == null || port < 1 || port > 65535) throw new IllegalArgumentException("Bind address and valid port are required.");
        if (maxConnections < 1 || maxConnections > 2048) throw new IllegalArgumentException("maxConnections must be 1..2048.");
        if (helloTimeout == null || helloTimeout.isNegative() || helloTimeout.isZero()) throw new IllegalArgumentException("helloTimeout must be positive.");
        if (heartbeatTimeout == null || heartbeatTimeout.compareTo(Duration.ofSeconds(10)) < 0 || heartbeatTimeout.compareTo(Duration.ofSeconds(300)) > 0)
            throw new IllegalArgumentException("heartbeatTimeout must be 10..300 seconds.");
    }
    public static ControlServerOptions defaults() {
        try { return new ControlServerOptions(InetAddress.getByName("0.0.0.0"), 5000, 128, Duration.ofSeconds(10), Duration.ofSeconds(30)); }
        catch (UnknownHostException impossible) { throw new IllegalStateException(impossible); }
    }
}
