package vn.networkadmin.transport.infrastructure;

import vn.networkadmin.protocol.control.HelloAckMessage;
import vn.networkadmin.protocol.control.HelloMessage;
import vn.networkadmin.protocol.control.JsonLineProtocol;
import vn.networkadmin.protocol.control.MessageEnvelope;
import vn.networkadmin.protocol.control.MessageType;
import vn.networkadmin.transport.domain.ClientSession;
import vn.networkadmin.transport.application.ControlServerOptions;
import vn.networkadmin.transport.ports.ClientSessionStore;
import vn.networkadmin.transport.ports.ControlServerListener;
import vn.networkadmin.transport.ports.ControlServerPort;
import vn.networkadmin.transport.ports.SessionMessageSender;

import javax.net.ssl.SSLContext;
import javax.net.ssl.SSLServerSocket;
import javax.net.ssl.SSLSocket;
import java.io.IOException;
import java.net.InetSocketAddress;
import java.net.SocketException;
import java.time.Duration;
import java.time.Instant;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.RejectedExecutionException;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.Semaphore;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.atomic.AtomicBoolean;

/** TLS control listener, Hello handshake, bounded session registry and stale-heartbeat cleanup. */
public final class TcpControlServer implements ControlServerPort, SessionMessageSender {
    private static final System.Logger LOG = System.getLogger(TcpControlServer.class.getName());
    private final SSLContext tlsContext;
    private final ControlServerOptions options;
    private final ClientSessionStore sessions;
    private final Semaphore connectionSlots;
    private final ExecutorService handlers;
    private final ScheduledExecutorService heartbeatMonitor;
    private final ConcurrentHashMap<SSLSocket, Boolean> activeSockets = new ConcurrentHashMap<>();
    private final ConcurrentHashMap<String, SSLSocket> sessionSockets = new ConcurrentHashMap<>();
    private final AtomicBoolean running = new AtomicBoolean();
    private final AtomicBoolean closed = new AtomicBoolean();
    private volatile SSLServerSocket listener;
    private volatile ControlServerListener events;

    public TcpControlServer(SSLContext tlsContext, ControlServerOptions options, ClientSessionStore sessions,
                            ExecutorService handlers, ScheduledExecutorService heartbeatMonitor) {
        this.tlsContext = java.util.Objects.requireNonNull(tlsContext);
        this.options = java.util.Objects.requireNonNull(options);
        this.sessions = java.util.Objects.requireNonNull(sessions);
        this.handlers = java.util.Objects.requireNonNull(handlers);
        this.heartbeatMonitor = java.util.Objects.requireNonNull(heartbeatMonitor);
        this.connectionSlots = new Semaphore(options.maxConnections());
    }

    @Override public void run(ControlServerListener events) throws IOException {
        if (closed.get()) throw new IllegalStateException("Control server has been closed.");
        if (!running.compareAndSet(false, true)) throw new IllegalStateException("Control server is already running.");
        this.events = java.util.Objects.requireNonNull(events);
        try {
            SSLServerSocket server = (SSLServerSocket) tlsContext.getServerSocketFactory().createServerSocket();
            listener = server;
            server.setReuseAddress(true);
            server.setEnabledProtocols(java.util.Arrays.stream(server.getSupportedProtocols())
                    .filter(p -> p.equals("TLSv1.3") || p.equals("TLSv1.2")).toArray(String[]::new));
            server.bind(new InetSocketAddress(options.bindAddress(), options.port()));
            heartbeatMonitor.scheduleAtFixedRate(this::closeStaleSessions, 5, 5, TimeUnit.SECONDS);
            while (running.get()) {
                SSLSocket socket = (SSLSocket) server.accept();
                if (!connectionSlots.tryAcquire()) { socket.close(); continue; }
                activeSockets.put(socket, Boolean.TRUE);
                try { handlers.execute(() -> handle(socket)); }
                catch (RejectedExecutionException rejected) {
                    activeSockets.remove(socket); connectionSlots.release(); socket.close();
                    if (running.get()) throw rejected;
                }
            }
        } catch (SocketException closed) {
            if (running.get()) throw closed;
        } finally { close(); }
    }

    @Override public boolean isListening() {
        SSLServerSocket activeListener = listener;
        return running.get() && activeListener != null && activeListener.isBound() && !activeListener.isClosed();
    }

    private void handle(SSLSocket socket) {
        ClientSession session = null;
        String remote = socket.getRemoteSocketAddress() instanceof InetSocketAddress endpoint
                ? endpoint.getAddress().getHostAddress() : String.valueOf(socket.getRemoteSocketAddress());
        try {
            socket.setSoTimeout(Math.toIntExact(options.helloTimeout().toMillis()));
            socket.startHandshake();
            MessageEnvelope first = JsonLineProtocol.receive(socket.getInputStream());
            if (first == null || first.type() != MessageType.HELLO) throw new IOException("Expected Hello as the first control message.");
            if (!(first.data() instanceof Map<?, ?> map)) throw new IOException("Hello payload must be an object.");
            HelloMessage hello = HelloMessage.fromPayload(castMap(map));
            String helloError = hello.validate();
            if (helloError != null) throw new IOException(helloError);

            String sessionId = UUID.randomUUID().toString().replace("-", "");
            session = new ClientSession(hello.clientId(), hello.clientName(), sessionId, remote);
            ClientSessionStore.Registration registration = sessions.register(session);
            registration.replaced().forEach(replaced -> {
                closeSession(replaced.sessionId());
                notifyDisconnected(replaced);
            });
            sessionSockets.put(sessionId, socket);
            send(socket, MessageType.HELLO_ACK, first.requestId(), new HelloAckMessage(true, sessionId,
                    "Hello accepted", hello.protocolVersion(), hello.capabilities()).toPayload());
            session.markReady();
            socket.setSoTimeout(0);
            notifyConnected(session);
            while (running.get() && session.isReady()) {
                MessageEnvelope message = JsonLineProtocol.receive(socket.getInputStream());
                if (message == null) break;
                if (message.type() == MessageType.SYSTEM_STATS || message.type() == MessageType.NETWORK_INFO
                        || message.type() == MessageType.PING_RESULT || message.type() == MessageType.SCAN_RESULT)
                    session.touch();
                try { events.handle(session, message); }
                catch (Exception handlerError) { LOG.log(System.Logger.Level.WARNING, "Control message handler failed for session " + session.sessionId(), handlerError); }
            }
        } catch (Exception error) {
            if (running.get()) notifyFailure(remote, error);
        } finally {
            activeSockets.remove(socket);
            if (session != null) {
                boolean removed = sessions.remove(session);
                sessionSockets.remove(session.sessionId(), socket);
                try { socket.close(); } catch (IOException ignored) { }
                if (removed) notifyDisconnected(session);
            } else try { socket.close(); } catch (IOException ignored) { }
            connectionSlots.release();
        }
    }

    @SuppressWarnings("unchecked") private static Map<String, Object> castMap(Map<?, ?> map) { return (Map<String, Object>) map; }

    private void closeStaleSessions() {
        for (ClientSession stale : sessions.stale(Instant.now(), options.heartbeatTimeout())) {
            if (sessions.disconnect(stale.sessionId())) {
                closeSession(stale.sessionId());
                notifyDisconnected(stale);
            }
        }
    }

    @Override public void send(String sessionId, MessageType type, String requestId, Object payload) throws IOException {
        ClientSession session = sessions.get(sessionId);
        SSLSocket socket = sessionSockets.get(sessionId);
        if (session == null || !session.isReady() || socket == null || socket.isClosed())
            throw new IOException("Client session is not connected.");
        send(socket, type, requestId, payload);
    }

    private static void send(SSLSocket socket, MessageType type, String requestId, Object payload) throws IOException {
        synchronized (socket) { JsonLineProtocol.send(socket.getOutputStream(), type, requestId, payload); }
    }

    @Override public void closeSession(String sessionId) {
        SSLSocket socket = sessionSockets.remove(sessionId);
        if (socket != null) try { socket.close(); } catch (IOException ignored) { }
    }

    @Override public void close() {
        if (!closed.compareAndSet(false, true)) return;
        boolean wasRunning = running.getAndSet(false);
        heartbeatMonitor.shutdownNow();
        try { if (listener != null) listener.close(); } catch (IOException ignored) { }
        var closingSessions = sessions.sessions();
        sessions.close();
        sessionSockets.values().forEach(socket -> { try { socket.close(); } catch (IOException ignored) { } });
        sessionSockets.clear();
        activeSockets.keySet().forEach(socket -> { try { socket.close(); } catch (IOException ignored) { } });
        if (wasRunning) closingSessions.forEach(this::notifyDisconnected);
        handlers.shutdownNow();
        heartbeatMonitor.shutdownNow();
    }

    private void notifyConnected(ClientSession session) {
        try { events.connected(session); }
        catch (RuntimeException ex) { LOG.log(System.Logger.Level.WARNING, "Connected-session listener failed.", ex); }
    }
    private void notifyDisconnected(ClientSession session) {
        try { events.disconnected(session); }
        catch (RuntimeException ex) { LOG.log(System.Logger.Level.WARNING, "Disconnected-session listener failed.", ex); }
    }
    private void notifyFailure(String remote, Exception error) {
        try { events.failed(remote, error); }
        catch (RuntimeException ex) { LOG.log(System.Logger.Level.WARNING, "Connection-failure listener failed.", ex); }
    }
}
