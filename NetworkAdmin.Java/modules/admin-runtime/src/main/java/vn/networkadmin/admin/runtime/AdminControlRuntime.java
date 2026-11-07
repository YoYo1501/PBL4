package vn.networkadmin.admin.runtime;

import vn.networkadmin.protocol.control.MessageEnvelope;
import vn.networkadmin.protocol.control.MessageType;
import vn.networkadmin.admin.ports.AdminDashboardUseCases;
import vn.networkadmin.admin.ports.AdminControlMessageHandler;
import vn.networkadmin.admin.ports.screenshare.AdminScreenShareMessageHandler;
import vn.networkadmin.transport.domain.ClientSession;
import vn.networkadmin.transport.ports.ClientSessionStore;
import vn.networkadmin.transport.ports.ControlServerListener;
import vn.networkadmin.transport.ports.ControlServerPort;
import vn.networkadmin.transport.ports.SessionMessageSender;

import java.io.IOException;
import java.time.Instant;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicReference;

/** Admin-side live Client registry and network operation dispatcher. */
public final class AdminControlRuntime implements AdminDashboardUseCases, ControlServerListener {
    private final ClientSessionStore sessions;
    private final ConcurrentHashMap<String, ClientState> clients = new ConcurrentHashMap<>();
    private final ConcurrentHashMap<String, ClientState> knownClients = new ConcurrentHashMap<>();
    private final ConcurrentHashMap<String, PendingResponse> pending = new ConcurrentHashMap<>();
    private final ConcurrentHashMap<String, String> activeScans = new ConcurrentHashMap<>();
    private final ControlServerPort server;
    private final SessionMessageSender sender;
    private final AdminScreenShareMessageHandler screenShareMessages;
    private final AdminControlMessageHandler controlMessages;

    public AdminControlRuntime(ControlServerPort server, ClientSessionStore sessions, SessionMessageSender sender) {
        this(server, sessions, sender, AdminScreenShareMessageHandler.NO_OP, AdminControlMessageHandler.NO_OP);
    }

    public AdminControlRuntime(ControlServerPort server, ClientSessionStore sessions, SessionMessageSender sender,
                               AdminScreenShareMessageHandler screenShareMessages) {
        this(server, sessions, sender, screenShareMessages, AdminControlMessageHandler.NO_OP);
    }

    public AdminControlRuntime(ControlServerPort server, ClientSessionStore sessions, SessionMessageSender sender,
                               AdminScreenShareMessageHandler screenShareMessages,
                               AdminControlMessageHandler controlMessages) {
        this.server = Objects.requireNonNull(server);
        this.sessions = Objects.requireNonNull(sessions);
        this.sender = Objects.requireNonNull(sender);
        this.screenShareMessages = Objects.requireNonNull(screenShareMessages);
        this.controlMessages = Objects.requireNonNull(controlMessages);
    }

    /** Blocks in the TLS listener; call close() from the owning application to stop it. */
    public void run() throws IOException { server.run(this); }
    public boolean isListening() { return server.isListening(); }

    @Override public void connected(ClientSession session) {
        ClientState state = knownClients.compute(session.clientId().toLowerCase(Locale.ROOT), (key, previous) -> {
            if (previous == null) return new ClientState(session);
            previous.connected(session); return previous;
        });
        clients.put(session.sessionId(), state);
    }
    @Override public void disconnected(ClientSession session) {
        screenShareMessages.disconnected(session);
        controlMessages.disconnected(session);
        removeSession(session);
    }
    @Override public void failed(String remoteAddress, Exception error) {
        System.getLogger(getClass().getName()).log(System.Logger.Level.WARNING,
                "Client control connection failed from " + remoteAddress, error);
    }

    public List<AdminClientSnapshot> clients() {
        return knownClients.values().stream().map(ClientState::snapshot)
                .sorted(Comparator.comparing(AdminClientSnapshot::connectedAt)).toList();
    }

    public AdminClientSnapshot client(String sessionId) {
        ClientState state = clients.get(sessionId);
        return state == null ? null : state.snapshot();
    }

    public Map<String, Object> ping(String sessionId, String target, long timeout, TimeUnit unit)
            throws IOException, InterruptedException, TimeoutException {
        if (target == null || target.isBlank()) throw new IllegalArgumentException("Ping target is required.");
        return request(sessionId, MessageType.PING_REQUEST, Map.of("target", target.trim()), timeout, unit, MessageType.PING_RESULT);
    }

    public Map<String, Object> scan(String sessionId, String subnet, int timeoutMs, long timeout, TimeUnit unit)
            throws IOException, InterruptedException, TimeoutException {
        Map<String, Object> payload = new LinkedHashMap<>();
        payload.put("subnet", subnet == null ? "" : subnet.trim()); payload.put("timeoutMs", timeoutMs);
        return request(sessionId, MessageType.SCAN_REQUEST, payload, timeout, unit, MessageType.SCAN_RESULT);
    }

    @Override public void cancelScan(String sessionId) throws IOException {
        String requestId = activeScans.get(sessionId);
        ClientSession session = sessions.get(sessionId);
        if (requestId == null || session == null || !session.isReady()) return;
        sendScanCancellation(sessionId, requestId);
    }

    private Map<String, Object> request(String sessionId, MessageType type, Object payload, long timeout, TimeUnit unit,
                                        MessageType expectedResponse)
            throws IOException, InterruptedException, TimeoutException {
        ClientSession session = sessions.get(sessionId);
        if (session == null || !session.isReady()) throw new IOException("Client session is not connected.");
        String requestId = UUID.randomUUID().toString().replace("-", "");
        if (type == MessageType.SCAN_REQUEST && activeScans.putIfAbsent(sessionId, requestId) != null)
            throw new IOException("A LAN scan is already active for this Client session.");
        CompletableFuture<Map<String, Object>> completion = new CompletableFuture<>();
        PendingResponse operation = new PendingResponse(sessionId, expectedResponse, completion);
        if (pending.putIfAbsent(requestId, operation) != null) {
            activeScans.remove(sessionId, requestId);
            throw new IOException("Could not track request.");
        }
        try {
            sender.send(sessionId, type, requestId, payload);
            return completion.get(timeout, unit);
        } catch (ExecutionException ex) {
            Throwable cause = ex.getCause();
            if (cause instanceof IOException io) throw io;
            throw new IOException("Client request failed.", cause);
        } catch (java.util.concurrent.TimeoutException ex) {
            if (type == MessageType.SCAN_REQUEST) sendScanCancellation(sessionId, requestId);
            throw new TimeoutException(type + " request timed out.");
        } catch (InterruptedException interrupted) {
            if (type == MessageType.SCAN_REQUEST) sendScanCancellation(sessionId, requestId);
            Thread.currentThread().interrupt();
            throw interrupted;
        } finally { pending.remove(requestId, operation); activeScans.remove(sessionId, requestId); }
    }

    private void sendScanCancellation(String sessionId, String requestId) {
        try { sender.send(sessionId, MessageType.REMOTE_ADMINISTRATION_CANCEL, requestId,
                Map.of("sessionId", sessionId, "targetRequestId", requestId)); }
        catch (IOException ignored) { }
    }

    @Override public void handle(ClientSession session, MessageEnvelope message) {
        if (message.type() == MessageType.SCREEN_SHARE_CONSENT || message.type() == MessageType.SCREEN_SHARE_STOP) {
            screenShareMessages.handle(session, message);
            return;
        }
        if (message.type() == MessageType.CONTROL_CONSENT || message.type() == MessageType.CONTROL_STOP
                || message.type() == MessageType.REMOTE_CHAT_MESSAGE) {
            controlMessages.handle(session, message);
            return;
        }
        if (!(message.data() instanceof Map<?, ?> raw)) return;
        Map<String, Object> payload = stringKeyMap(raw);
        switch (message.type()) {
            case SYSTEM_STATS -> update(session, state -> state.systemStats.set(payload));
            case NETWORK_INFO -> update(session, state -> state.networkInfo.set(payload));
            case PING_RESULT, SCAN_RESULT -> complete(session, message, payload);
            default -> { /* Other feature handlers are added in their own modules. */ }
        }
    }

    private void update(ClientSession session, java.util.function.Consumer<ClientState> update) {
        ClientState state = clients.get(session.sessionId());
        if (state != null && state.session == session) { update.accept(state); state.lastSeen.set(session.lastSeen()); }
    }

    private void complete(ClientSession session, MessageEnvelope message, Map<String, Object> payload) {
        PendingResponse operation = pending.get(message.requestId());
        if (operation == null || !operation.sessionId.equals(session.sessionId()) || operation.expectedType != message.type()) return;
        ClientState state = clients.get(session.sessionId());
        if (state != null && message.type() == MessageType.SCAN_RESULT) state.lastScan.set(payload);
        if (message.type() == MessageType.SCAN_RESULT && Boolean.TRUE.equals(payload.get("progress"))) return;
        operation.completion.complete(payload);
    }

    private void removeSession(ClientSession session) {
        ClientState state = clients.remove(session.sessionId());
        if (state != null) state.disconnected(session);
        pending.forEach((id, operation) -> {
            if (operation.sessionId.equals(session.sessionId()) && pending.remove(id, operation))
                operation.completion.completeExceptionally(new IOException("Client disconnected: " + session.sessionId()));
        });
    }

    private static Map<String, Object> stringKeyMap(Map<?, ?> raw) {
        Map<String, Object> mapped = new LinkedHashMap<>();
        raw.forEach((key, value) -> { if (key instanceof String text) mapped.put(text, value); });
        return Collections.unmodifiableMap(mapped);
    }

    @Override public void close() { server.close(); }

    private record PendingResponse(String sessionId, MessageType expectedType,
                                   CompletableFuture<Map<String, Object>> completion) { }

    private static final class ClientState {
        private volatile ClientSession session;
        private volatile boolean online = true;
        private final AtomicReference<Instant> lastSeen;
        private final AtomicReference<Map<String, Object>> systemStats = new AtomicReference<>(Map.of());
        private final AtomicReference<Map<String, Object>> networkInfo = new AtomicReference<>(Map.of());
        private final AtomicReference<Map<String, Object>> lastScan = new AtomicReference<>(Map.of());
        private ClientState(ClientSession session) { this.session = session; this.lastSeen = new AtomicReference<>(session.lastSeen()); }
        private synchronized void connected(ClientSession next) {
            session = next; online = true; lastSeen.set(next.lastSeen()); lastScan.set(Map.of());
        }
        private synchronized void disconnected(ClientSession ended) { if (session == ended) online = false; }
        private AdminClientSnapshot snapshot() {
            return new AdminClientSnapshot(session.clientId(), session.clientName(), session.sessionId(), session.remoteAddress(),
                    session.connectedAt(), lastSeen.get(), systemStats.get(), networkInfo.get(), lastScan.get(), online);
        }
    }
}
