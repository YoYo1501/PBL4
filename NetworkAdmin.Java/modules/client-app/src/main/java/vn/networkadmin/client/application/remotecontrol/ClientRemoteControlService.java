package vn.networkadmin.client.application.remotecontrol;

import vn.networkadmin.client.ports.ClientFeatureMessageHandler;
import vn.networkadmin.protocol.control.ControlInputEvent;
import vn.networkadmin.protocol.control.MessageEnvelope;
import vn.networkadmin.protocol.control.MessageType;
import vn.networkadmin.transport.ports.ClientControlChannel;

import java.io.IOException;
import java.util.Map;
import java.util.Objects;
import java.util.concurrent.Executor;
import java.util.concurrent.atomic.AtomicReference;

/** Consent-gated remote input handler. Its grant is bound to one live channel and request ID. */
public final class ClientRemoteControlService implements ClientFeatureMessageHandler {
    private static final int MAX_EVENTS_PER_SECOND = 120;
    private final String clientId;
    private final RemoteControlClientView view;
    private final RemoteInputPort input;
    private final Executor inputExecutor;
    private final AtomicReference<Grant> grant = new AtomicReference<>();
    private volatile long rateWindowStart;
    private volatile int rateWindowCount;

    public ClientRemoteControlService(String clientId, RemoteControlClientView view, RemoteInputPort input,
                                      Executor inputExecutor) {
        this.clientId = Objects.requireNonNull(clientId); this.view = Objects.requireNonNull(view);
        this.input = Objects.requireNonNull(input); this.inputExecutor = Objects.requireNonNull(inputExecutor);
    }

    @Override public void handle(ClientControlChannel channel, MessageEnvelope message) {
        switch (message.type()) {
            case CONTROL_REQUEST -> requestConsent(channel, message);
            case CONTROL_INPUT_EVENT -> applyInput(channel, message);
            case CONTROL_STOP -> {
                Map<?, ?> payload = map(message);
                Grant active = grant.get();
                if (active != null && clientId.equals(string(payload.get("clientId")))
                        && active.sessionId.equals(string(payload.get("sessionId"))))
                    stop(channel, message, "Admin stopped remote control.", false);
            }
            default -> { }
        }
    }

    @Override public void disconnected(ClientControlChannel channel) {
        Grant active = grant.get();
        if (active != null && active.channel == channel) stop(active, "Connection closed; remote-control consent was revoked.", false);
    }

    private void requestConsent(ClientControlChannel channel, MessageEnvelope message) {
        Map<?, ?> payload = map(message);
        String session = string(payload.get("sessionId"));
        String requestedClient = string(payload.get("clientId"));
        if (!channel.isConnected() || !channel.sessionId().equals(session) || !clientId.equals(requestedClient)
                || message.requestId() == null || message.requestId().isBlank()) return;
        Grant existing = grant.get();
        if (existing != null && !existing.closed) {
            sendConsent(channel, message.requestId(), session, false, "Another remote-control grant is active."); return;
        }
        Grant request = new Grant(channel, message.requestId(), session);
        if (!grant.compareAndSet(existing, request)) {
            sendConsent(channel, message.requestId(), session, false, "A remote-control request is already pending."); return;
        }
        if (!input.available()) {
            sendConsent(channel, request.requestId, session, false, "Remote input is unavailable on this device.");
            grant.compareAndSet(request, null); return;
        }
        view.requestControlConsent("Admin").whenComplete((accepted, error) -> {
            if (grant.get() != request || request.closed || !channel.isConnected()) return;
            boolean allowed = error == null && Boolean.TRUE.equals(accepted);
            if (allowed) {
                request.allowed = true;
                view.controlStarted(() -> stop(channel, message, "Revoked by Client user.", true));
            } else {
                request.closed = true; grant.compareAndSet(request, null);
                view.controlStopped("Remote-control request rejected.");
            }
            sendConsent(channel, request.requestId, session, allowed, error == null ? "" : "Consent prompt failed.");
        });
    }

    private void applyInput(ClientControlChannel channel, MessageEnvelope message) {
        Grant active = grant.get();
        if (active == null || active.closed || !active.allowed || active.channel != channel
                || !active.requestId.equals(message.requestId()) || !channel.isConnected()) return;
        Map<?, ?> payload = map(message);
        if (!active.sessionId.equals(string(payload.get("sessionId"))) || !withinRateLimit()) return;
        final ControlInputEvent event;
        try { event = ControlInputEvent.fromPayload(payload); }
        catch (IllegalArgumentException invalid) { return; }
        try {
            inputExecutor.execute(() -> {
                if (grant.get() == active && !active.closed && active.allowed && channel.isConnected()) input.apply(event);
            });
        } catch (java.util.concurrent.RejectedExecutionException ignored) { }
    }

    private boolean withinRateLimit() {
        synchronized (this) {
            long now = System.nanoTime();
            if (now - rateWindowStart >= 1_000_000_000L) { rateWindowStart = now; rateWindowCount = 0; }
            return ++rateWindowCount <= MAX_EVENTS_PER_SECOND;
        }
    }

    private void stop(ClientControlChannel channel, MessageEnvelope message, String reason, boolean notifyAdmin) {
        Grant active = grant.get();
        if (active != null && active.channel == channel && active.requestId.equals(message.requestId()))
            stop(active, reason, notifyAdmin);
    }

    private void stop(Grant active, String reason, boolean notifyAdmin) {
        if (!grant.compareAndSet(active, null)) return;
        active.closed = true; active.allowed = false;
        try { inputExecutor.execute(input::releaseAll); }
        catch (java.util.concurrent.RejectedExecutionException rejected) { input.releaseAll(); }
        if (notifyAdmin && active.channel.isConnected()) try {
            active.channel.send(MessageType.CONTROL_STOP, active.requestId,
                    Map.of("clientId", clientId, "sessionId", active.sessionId, "reason", reason));
        } catch (IOException ignored) { active.channel.close(); }
        view.controlStopped(reason);
    }

    private void sendConsent(ClientControlChannel channel, String requestId, String sessionId, boolean accepted, String reason) {
        try { channel.send(MessageType.CONTROL_CONSENT, requestId, Map.of("clientId", clientId,
                "sessionId", sessionId, "accepted", accepted, "reason", reason)); }
        catch (IOException error) { channel.close(); }
    }

    private static Map<?, ?> map(MessageEnvelope message) { return message.data() instanceof Map<?, ?> value ? value : Map.of(); }
    private static String string(Object value) { return value instanceof String text ? text : ""; }

    private static final class Grant {
        private final ClientControlChannel channel; private final String requestId, sessionId;
        private volatile boolean allowed, closed;
        private Grant(ClientControlChannel channel, String requestId, String sessionId) {
            this.channel = channel; this.requestId = requestId; this.sessionId = sessionId;
        }
    }
}
