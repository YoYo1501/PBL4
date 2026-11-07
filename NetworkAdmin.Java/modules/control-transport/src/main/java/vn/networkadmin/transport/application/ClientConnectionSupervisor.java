package vn.networkadmin.transport.application;

import vn.networkadmin.protocol.control.MessageType;
import vn.networkadmin.transport.ports.ClientConnectionEvents;
import vn.networkadmin.transport.ports.ClientConnectionFactory;
import vn.networkadmin.transport.ports.ClientConnectionSupervisorPort;
import vn.networkadmin.transport.ports.ClientControlChannel;
import vn.networkadmin.transport.ports.SystemStatsSampler;

import java.util.Objects;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicReference;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.ScheduledFuture;
import java.util.concurrent.TimeUnit;

/** Reconnects with 1, 2, 4, 8, then 10 second delays; every attempt owns a fresh session/socket. */
public final class ClientConnectionSupervisor implements ClientConnectionSupervisorPort {
    private final ClientConnectionFactory connectionFactory;
    private final AtomicBoolean stopped = new AtomicBoolean();
    private final AtomicReference<ClientControlChannel> active = new AtomicReference<>();
    private final SystemStatsSampler statsSampler;
    private final ScheduledExecutorService heartbeatScheduler;
    private volatile Thread runner;

    public ClientConnectionSupervisor(ClientConnectionFactory connectionFactory, SystemStatsSampler statsSampler,
                                       ScheduledExecutorService heartbeatScheduler) {
        this.connectionFactory = Objects.requireNonNull(connectionFactory);
        this.statsSampler = Objects.requireNonNull(statsSampler);
        this.heartbeatScheduler = Objects.requireNonNull(heartbeatScheduler);
    }

    @Override public void runUntilInterrupted(ClientConnectionEvents events) {
        Objects.requireNonNull(events);
        runner = Thread.currentThread();
        try {
            int retrySeconds = 1;
            while (!stopped.get() && !Thread.currentThread().isInterrupted()) {
                ClientControlChannel connection = null;
                ScheduledFuture<?> heartbeat = null;
                boolean didConnect = false;
                try {
                    connection = connectionFactory.create();
                    active.set(connection);
                    connection.connect(); retrySeconds = 1;
                    didConnect = true;
                    events.connected(connection);
                    ClientControlChannel activeConnection = connection;
                    heartbeat = heartbeatScheduler.scheduleWithFixedDelay(() -> {
                        try { activeConnection.send(MessageType.SYSTEM_STATS, statsSampler.sample()); }
                        catch (Exception sendFailure) { try { safely(() -> events.failed(sendFailure)); } finally { activeConnection.close(); } }
                    }, 0, 2, TimeUnit.SECONDS);
                    ClientControlChannel readConnection = connection;
                    connection.runReadLoop(message -> events.messageReceived(readConnection, message));
                } catch (Exception error) {
                    if (!stopped.get() && !Thread.currentThread().isInterrupted()) safely(() -> events.failed(error));
                } finally {
                    if (heartbeat != null) heartbeat.cancel(true);
                    if (connection != null) {
                        connection.close();
                        active.compareAndSet(connection, null);
                        if (didConnect) {
                            ClientControlChannel disconnected = connection;
                            safely(() -> events.disconnected(disconnected));
                        }
                    }
                }
                if (stopped.get() || Thread.currentThread().isInterrupted()) break;
                try { Thread.sleep(retrySeconds * 1000L); }
                catch (InterruptedException interrupted) { Thread.currentThread().interrupt(); break; }
                retrySeconds = Math.min(retrySeconds * 2, 10);
            }
        } finally { runner = null; }
    }

    @Override public void close() {
        stopped.set(true);
        ClientControlChannel connection = active.getAndSet(null);
        if (connection != null) connection.close();
        Thread thread = runner;
        if (thread != null) thread.interrupt();
    }

    private static void safely(Runnable callback) {
        try { callback.run(); } catch (RuntimeException ignored) { }
    }
}
