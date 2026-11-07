package vn.networkadmin.admin.application.remotecontrol;

import vn.networkadmin.admin.ports.AdminControlMessageHandler;
import vn.networkadmin.protocol.control.ControlInputEvent;
import vn.networkadmin.protocol.control.MessageEnvelope;
import vn.networkadmin.protocol.control.MessageType;
import vn.networkadmin.transport.domain.ClientSession;
import vn.networkadmin.transport.ports.ClientSessionStore;
import vn.networkadmin.transport.ports.SessionMessageSender;

import java.io.IOException;
import java.util.Map;
import java.util.Objects;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicReference;

/** Admin control authorization is independent from screen-view consent and expires on timeout/disconnect. */
public final class AdminRemoteControlService implements AdminRemoteControlUseCases, AdminControlMessageHandler {
    private final ClientSessionStore sessions;
    private final SessionMessageSender sender;
    private final ScheduledExecutorService scheduler;
    private final ConcurrentHashMap<String, Grant> grants = new ConcurrentHashMap<>();
    private volatile boolean closed;

    public AdminRemoteControlService(ClientSessionStore sessions, SessionMessageSender sender,
                                     ScheduledExecutorService scheduler) {
        this.sessions = Objects.requireNonNull(sessions); this.sender = Objects.requireNonNull(sender);
        this.scheduler = Objects.requireNonNull(scheduler);
    }

    @Override public void requestControl(String clientId, String sessionId) throws IOException {
        if (closed) throw new IOException("Remote control is shutting down.");
        ClientSession session = requireSession(clientId, sessionId);
        String requestId = UUID.randomUUID().toString().replace("-", "");
        Grant grant = new Grant(session, requestId);
        Grant prior = grants.putIfAbsent(sessionId, grant);
        if (prior != null && !prior.state.get().status().equals("Stopped") && !prior.state.get().status().equals("Rejected"))
            throw new IOException("A remote-control request is already active for this Client.");
        if (prior != null) grants.replace(sessionId, prior, grant);
        try {
            sender.send(sessionId, MessageType.CONTROL_REQUEST, requestId,
                    Map.of("clientId", clientId, "sessionId", sessionId));
            scheduler.schedule(() -> expire(grant), 30, TimeUnit.SECONDS);
        } catch (IOException error) { grants.remove(sessionId, grant); throw error; }
    }

    @Override public void handle(ClientSession session, MessageEnvelope message) {
        Grant grant = grants.get(session.sessionId());
        if (grant == null || grant.session != session || sessions.get(session.sessionId()) != session
                || !session.isReady() || !grant.requestId.equals(message.requestId())) return;
        Map<?, ?> payload = map(message);
        if (!grant.clientId.equals(string(payload.get("clientId")))
                || !grant.sessionId.equals(string(payload.get("sessionId")))) return;
        if (message.type() == MessageType.CONTROL_CONSENT) {
            boolean accepted = Boolean.TRUE.equals(payload.get("accepted"));
            grant.state.set(new ControlState(grant.requestId, accepted ? "Granted" : "Rejected",
                    string(payload.get("reason"))));
            if (!accepted) grants.remove(grant.sessionId, grant);
        } else if (message.type() == MessageType.CONTROL_STOP) {
            grant.state.set(new ControlState(grant.requestId, "Stopped", string(payload.get("reason"))));
            grants.remove(grant.sessionId, grant);
        }
    }

    @Override public void disconnected(ClientSession session) {
        Grant grant = grants.remove(session.sessionId());
        if (grant != null && grant.session == session)
            grant.state.set(new ControlState(grant.requestId, "Stopped", "Client disconnected; consent revoked."));
    }

    @Override public void stopControl(String sessionId) {
        Grant grant = grants.remove(sessionId);
        if (grant == null) return;
        grant.state.set(new ControlState(grant.requestId, "Stopped", "Stopped by Admin."));
        if (sessions.get(sessionId) == grant.session) try {
            sender.send(sessionId, MessageType.CONTROL_STOP, grant.requestId,
                    Map.of("clientId", grant.clientId, "sessionId", sessionId, "reason", "Stopped by Admin."));
        } catch (IOException ignored) { }
    }

    @Override public void sendInput(String sessionId, ControlInputEvent event) throws IOException {
        Grant grant = grants.get(sessionId);
        if (grant == null || !grant.state.get().status().equals("Granted") || sessions.get(sessionId) != grant.session)
            throw new IOException("Remote control has not been accepted for the current Client session.");
        sender.send(sessionId, MessageType.CONTROL_INPUT_EVENT, grant.requestId, event.toPayload(sessionId));
    }

    @Override public ControlState state(String sessionId) {
        Grant grant = grants.get(sessionId);
        return grant == null ? new ControlState(null, "Stopped", "Remote control is not active.") : grant.state.get();
    }

    private ClientSession requireSession(String clientId, String sessionId) throws IOException {
        ClientSession session = sessions.get(sessionId);
        if (session == null || !session.isReady() || !session.clientId().equals(clientId))
            throw new IOException("Selected Client session is no longer active.");
        return session;
    }

    private void expire(Grant grant) {
        if (grant.state.get().status().equals("Pending") && grants.remove(grant.sessionId, grant)) {
            grant.state.set(new ControlState(grant.requestId, "Rejected", "Client consent request timed out."));
            if (sessions.get(grant.sessionId) == grant.session) try {
                sender.send(grant.sessionId, MessageType.CONTROL_STOP, grant.requestId,
                        Map.of("clientId", grant.clientId, "sessionId", grant.sessionId, "reason", "Consent request timed out."));
            } catch (IOException ignored) { }
        }
    }

    @Override public void close() {
        closed = true;
        for (String session : grants.keySet()) stopControl(session);
    }

    private static Map<?, ?> map(MessageEnvelope message) { return message.data() instanceof Map<?, ?> value ? value : Map.of(); }
    private static String string(Object value) { return value instanceof String text ? text : ""; }

    private static final class Grant {
        private final String clientId, sessionId, requestId;
        private final ClientSession session;
        private final AtomicReference<ControlState> state;
        private Grant(ClientSession session, String requestId) {
            this.clientId = session.clientId(); this.sessionId = session.sessionId(); this.requestId = requestId;
            this.session = session; this.state = new AtomicReference<>(new ControlState(requestId, "Pending", "Waiting for Client consent."));
        }
    }
}
