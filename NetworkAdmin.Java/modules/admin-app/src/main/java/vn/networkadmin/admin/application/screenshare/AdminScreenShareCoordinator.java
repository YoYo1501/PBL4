package vn.networkadmin.admin.application.screenshare;

import vn.networkadmin.admin.ports.screenshare.AdminScreenShareMessageHandler;
import vn.networkadmin.protocol.control.MessageEnvelope;
import vn.networkadmin.protocol.control.MessageType;
import vn.networkadmin.screenshare.server.application.ApplyScreenFrame;
import vn.networkadmin.screenshare.server.domain.ScreenCanvas;
import vn.networkadmin.transport.domain.ClientSession;
import vn.networkadmin.transport.ports.ClientSessionStore;
import vn.networkadmin.transport.ports.SessionMessageSender;

import java.io.IOException;
import java.security.SecureRandom;
import java.util.Base64;
import java.util.Map;
import java.util.Objects;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;
import java.time.Duration;
import java.util.concurrent.atomic.AtomicReference;

/** Consent-gated screen-share lifecycle, correlated to one ClientId and SessionId. */
public final class AdminScreenShareCoordinator implements AdminScreenShareUseCases, AdminScreenShareMessageHandler {
    private final ClientSessionStore sessions;
    private final SessionMessageSender sender;
    private final ScreenFrameDataPort dataServer;
    private final ApplyScreenFrame applyFrame;
    private final ScheduledExecutorService scheduler;
    private final String advertisedHost;
    private final int dataPort;
    private final Duration consentTimeout;
    private final AutoCloseable dataEventSubscription;
    private final SecureRandom random = new SecureRandom();
    private final ConcurrentHashMap<String, ActiveShare> bySession = new ConcurrentHashMap<>();
    private volatile boolean closed;

    public AdminScreenShareCoordinator(ClientSessionStore sessions, SessionMessageSender sender,
                                       ScreenFrameDataPort dataServer, ApplyScreenFrame applyFrame,
                                       ScheduledExecutorService scheduler, String advertisedHost, int dataPort) {
        this(sessions, sender, dataServer, applyFrame, scheduler, advertisedHost, dataPort, Duration.ofSeconds(30));
    }

    public AdminScreenShareCoordinator(ClientSessionStore sessions, SessionMessageSender sender,
                                       ScreenFrameDataPort dataServer, ApplyScreenFrame applyFrame,
                                       ScheduledExecutorService scheduler, String advertisedHost, int dataPort,
                                       Duration consentTimeout) {
        this(sessions, sender, dataServer, applyFrame, scheduler, advertisedHost, dataPort, consentTimeout,
                ScreenFrameDataEvents.NO_OP);
    }

    public AdminScreenShareCoordinator(ClientSessionStore sessions, SessionMessageSender sender,
                                       ScreenFrameDataPort dataServer, ApplyScreenFrame applyFrame,
                                       ScheduledExecutorService scheduler, String advertisedHost, int dataPort,
                                       Duration consentTimeout, ScreenFrameDataEvents dataEvents) {
        this.sessions = Objects.requireNonNull(sessions);
        this.sender = Objects.requireNonNull(sender);
        this.dataServer = Objects.requireNonNull(dataServer);
        this.applyFrame = Objects.requireNonNull(applyFrame);
        this.scheduler = Objects.requireNonNull(scheduler);
        if (advertisedHost == null || advertisedHost.isBlank() || dataPort < 1 || dataPort > 65535)
            throw new IllegalArgumentException("Screen data endpoint is invalid.");
        this.advertisedHost = advertisedHost;
        this.dataPort = dataPort;
        if (consentTimeout == null || consentTimeout.isNegative() || consentTimeout.isZero())
            throw new IllegalArgumentException("Consent timeout must be positive.");
        this.consentTimeout = consentTimeout;
        this.dataEventSubscription = Objects.requireNonNull(dataEvents).subscribe(event -> {
            if (event instanceof ScreenFrameDataEvents.FrameReceived frame) frameReceived(frame.identity(), frame.frame());
            else if (event instanceof ScreenFrameDataEvents.ChannelClosed channel) dataChannelClosed(channel.identity());
        });
    }

    @Override public void requestViewing(String clientId, String sessionId) throws Exception {
        if (closed) throw new IllegalStateException("Screen sharing is stopped.");
        ClientSession session = sessions.get(sessionId);
        if (session == null || !session.isReady() || !session.clientId().equals(clientId))
            throw new IOException("Selected Client session is no longer connected.");
        for (ActiveShare active : bySession.values()) {
            if (active.clientId.equals(clientId) && active.isActive())
                throw new IllegalStateException("This Client already has an active screen-share session.");
        }
        String requestId = randomToken(18);
        ActiveShare share = new ActiveShare(session, requestId, randomToken(32));
        java.util.concurrent.atomic.AtomicBoolean installed = new java.util.concurrent.atomic.AtomicBoolean();
        bySession.compute(sessionId, (key, previous) -> {
            if (previous != null && previous.isActive()) return previous;
            installed.set(true);
            return share;
        });
        if (!installed.get()) throw new IllegalStateException("This session already has a screen-share request.");
        try {
            sender.send(sessionId, MessageType.SCREEN_SHARE_REQUEST, requestId, Map.of(
                    "clientId", clientId, "sessionId", sessionId, "host", advertisedHost,
                    "port", dataPort, "token", share.token));
            scheduler.schedule(() -> expire(share), consentTimeout.toMillis(), TimeUnit.MILLISECONDS);
        } catch (Exception error) {
            bySession.remove(sessionId, share);
            throw error;
        }
    }

    @Override public void handle(ClientSession session, MessageEnvelope message) {
        ActiveShare share = bySession.get(session.sessionId());
        if (share == null || !share.matches(session, message.requestId())) return;
        if (message.type() == MessageType.SCREEN_SHARE_CONSENT) {
            Map<?, ?> data = payload(message);
            if (!session.sessionId().equals(data.get("sessionId")) || !session.clientId().equals(data.get("clientId"))) return;
            synchronized (share) {
                if (!share.status.get().status().equals("Pending")) return;
                if (!Boolean.TRUE.equals(data.get("accepted"))) {
                    share.status.set(new AdminScreenShareUseCases.ScreenShareSnapshot(share.requestId, "Rejected",
                            string(data.get("reason"), "Client rejected the request."), null));
                    dataServer.revoke(share.requestId);
                    return;
                }
                share.status.set(new AdminScreenShareUseCases.ScreenShareSnapshot(share.requestId,
                        "Accepted", "Client accepted; waiting for secure frame channel.", null));
            }
            dataServer.authorize(share.requestId, share.clientId, share.sessionId, share.token);
            scheduler.schedule(() -> {
                if (share.status.get().status().equals("Accepted"))
                    stop(share, "Stopped", "No screen frames arrived after consent.", true);
            }, 15, TimeUnit.SECONDS);
            try {
                sender.send(share.sessionId, MessageType.SCREEN_CHANNEL_READY, share.requestId, Map.of(
                        "clientId", share.clientId, "sessionId", share.sessionId, "host", advertisedHost,
                        "port", dataPort, "token", share.token));
            } catch (IOException error) {
                stop(share, "Stopped", "Could not open the screen frame channel.", false);
            }
        } else if (message.type() == MessageType.SCREEN_SHARE_STOP) {
            stop(share, "Stopped", "Client stopped sharing.", false);
        }
    }

    public void frameReceived(ScreenFrameDataPort.Identity identity,
                              vn.networkadmin.protocol.screenshare.ScreenFramePacket packet) {
        ActiveShare share = bySession.get(identity.sessionId());
        if (share == null || !share.requestId.equals(identity.requestId()) || !share.clientId.equals(identity.clientId())) return;
        synchronized (share) {
            var current = share.status.get();
            if (!current.status().equals("Accepted") && !current.status().equals("Viewing")) return;
            if (packet.sequence() <= share.lastSequence) return;
            try {
                ScreenCanvas canvas = applyFrame.execute(packet, share.canvas.get());
                share.lastSequence = packet.sequence();
                share.canvas.set(canvas);
                share.status.set(new AdminScreenShareUseCases.ScreenShareSnapshot(share.requestId, "Viewing",
                        canvas.width() + " × " + canvas.height(), canvas));
            } catch (RuntimeException error) {
                stop(share, "Stopped", "Invalid screen frame: " + error.getMessage(), true);
            }
        }
    }

    public void dataChannelClosed(ScreenFrameDataPort.Identity identity) {
        ActiveShare share = bySession.get(identity.sessionId());
        if (share == null || !share.requestId.equals(identity.requestId()) || !share.clientId.equals(identity.clientId())) return;
        if (share.status.get().status().equals("Viewing") || share.status.get().status().equals("Accepted"))
            stop(share, "Stopped", "Screen frame channel disconnected.", true);
    }

    @Override public void stopViewing(String sessionId) {
        ActiveShare share = bySession.get(sessionId);
        if (share != null) stop(share, "Stopped", "Stopped by Admin.", true);
    }

    @Override public AdminScreenShareUseCases.ScreenShareSnapshot snapshot(String sessionId) {
        ActiveShare share = bySession.get(sessionId);
        return share == null ? new AdminScreenShareUseCases.ScreenShareSnapshot(null, "Stopped", "No active share.", null)
                : share.status.get();
    }

    @Override public void disconnected(ClientSession session) {
        ActiveShare share = bySession.get(session.sessionId());
        if (share != null) stop(share, "Stopped", "Client disconnected; consent is required again after reconnect.", false);
    }

    private void expire(ActiveShare share) {
        if (share.status.get().status().equals("Pending"))
            stop(share, "Stopped", "Client consent request timed out.", true);
    }

    private void stop(ActiveShare share, String status, String detail, boolean notifyClient) {
        synchronized (share) {
            var prior = share.status.get();
            if (prior.status().equals("Stopped") || prior.status().equals("Rejected")) return;
            share.status.set(new AdminScreenShareUseCases.ScreenShareSnapshot(share.requestId, status, detail, null));
            dataServer.revoke(share.requestId);
            if (notifyClient) try {
                sender.send(share.sessionId, MessageType.SCREEN_SHARE_STOP, share.requestId,
                        Map.of("sessionId", share.sessionId, "reason", detail));
            } catch (IOException ignored) { }
        }
    }

    private static Map<?, ?> payload(MessageEnvelope message) {
        return message.data() instanceof Map<?, ?> map ? map : Map.of();
    }
    private static String string(Object value, String fallback) { return value instanceof String text && !text.isBlank() ? text : fallback; }
    private String randomToken(int bytes) {
        byte[] value = new byte[bytes]; random.nextBytes(value);
        return Base64.getUrlEncoder().withoutPadding().encodeToString(value);
    }

    @Override public void close() {
        closed = true;
        bySession.values().forEach(share -> stop(share, "Stopped", "Admin is shutting down.", true));
        dataServer.close();
        try { dataEventSubscription.close(); } catch (Exception ignored) { }
    }

    private static final class ActiveShare {
        final String clientId, sessionId, requestId, token;
        final AtomicReference<AdminScreenShareUseCases.ScreenShareSnapshot> status;
        final AtomicReference<ScreenCanvas> canvas = new AtomicReference<>();
        volatile long lastSequence = -1;
        ActiveShare(ClientSession session, String requestId, String token) {
            this.clientId = session.clientId(); this.sessionId = session.sessionId();
            this.requestId = requestId; this.token = token;
            this.status = new AtomicReference<>(new AdminScreenShareUseCases.ScreenShareSnapshot(requestId,
                    "Pending", "Waiting for Client consent.", null));
        }
        boolean matches(ClientSession session, String request) {
            return clientId.equals(session.clientId()) && sessionId.equals(session.sessionId()) && requestId.equals(request);
        }
        boolean isActive() {
            String current = status.get().status();
            return current.equals("Pending") || current.equals("Accepted") || current.equals("Viewing");
        }
    }
}
