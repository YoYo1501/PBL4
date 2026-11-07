package vn.networkadmin.client.application.screenshare;

import vn.networkadmin.client.ports.screenshare.ClientScreenShareMessageHandler;
import vn.networkadmin.protocol.control.MessageEnvelope;
import vn.networkadmin.protocol.control.MessageType;
import vn.networkadmin.screenshare.application.EncodeScreenFrame;
import vn.networkadmin.screenshare.application.ScreenCaptureLoop;
import vn.networkadmin.screenshare.domain.CapturedFrame;
import vn.networkadmin.screenshare.domain.CapturedFrameSnapshot;
import vn.networkadmin.screenshare.ports.LatestFrameStore;
import vn.networkadmin.screenshare.ports.ScreenCapturePort;
import vn.networkadmin.transport.ports.ClientControlChannel;

import java.io.IOException;
import java.util.Map;
import java.util.Objects;
import java.util.Optional;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.ScheduledFuture;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicReference;

/** Consent lifecycle and capture/encode/send orchestration for one Client session. */
public final class ClientScreenShareService implements ClientScreenShareMessageHandler, AutoCloseable {
    private final String clientId, clientName, fingerprint;
    private final ScreenShareClientView view;
    private final ScreenCapturePort capture;
    private final LatestFrameStore frames;
    private final EncodeScreenFrame encoder;
    private final ScreenFrameSenderFactory senderFactory;
    private final ScheduledExecutorService scheduler;
    private final ExecutorService workers;
    private final AtomicReference<Share> current = new AtomicReference<>();

    public ClientScreenShareService(String clientId, String clientName, String fingerprint,
                                    ScreenShareClientView view, ScreenCapturePort capture,
                                    LatestFrameStore frames, EncodeScreenFrame encoder,
                                    ScreenFrameSenderFactory senderFactory,
                                    ScheduledExecutorService scheduler, ExecutorService workers) {
        this.clientId = Objects.requireNonNull(clientId); this.clientName = Objects.requireNonNull(clientName);
        this.fingerprint = Objects.requireNonNull(fingerprint); this.view = Objects.requireNonNull(view);
        this.view.clientIdentity(clientName, clientId);
        this.capture = Objects.requireNonNull(capture); this.frames = Objects.requireNonNull(frames);
        this.encoder = Objects.requireNonNull(encoder); this.senderFactory = Objects.requireNonNull(senderFactory);
        this.scheduler = Objects.requireNonNull(scheduler); this.workers = Objects.requireNonNull(workers);
    }

    @Override public void handle(ClientControlChannel source, MessageEnvelope message) {
        if (message.type() == MessageType.SCREEN_SHARE_REQUEST) requestConsent(source, message);
        else if (message.type() == MessageType.SCREEN_CHANNEL_READY) startAfterConsent(source, message);
        else if (message.type() == MessageType.SCREEN_SHARE_STOP) stop(source, message.requestId(), "Admin stopped viewing.", false);
    }

    private void requestConsent(ClientControlChannel source, MessageEnvelope message) {
        Map<?, ?> data = object(message);
        String sessionId = string(data.get("sessionId"));
        String requestClientId = string(data.get("clientId"));
        String host = string(data.get("host"));
        int port = integer(data.get("port"));
        String token = string(data.get("token"));
        if (!clientId.equals(requestClientId) || !source.sessionId().equals(sessionId)
                || message.requestId() == null || host.isBlank() || port < 1 || port > 65535 || token.isBlank()) {
            sendConsent(source, message.requestId(), sessionId, false, "Invalid or stale screen request.");
            return;
        }
        Share share = new Share(source, message.requestId(), sessionId, host, port, token);
        Share existing = current.get();
        if (existing != null && !existing.closed.get()) {
            sendConsent(source, message.requestId(), sessionId, false, "Another screen-share session is active.");
            return;
        }
        if (!current.compareAndSet(existing, share)) {
            sendConsent(source, message.requestId(), sessionId, false, "Another screen-share request is pending.");
            return;
        }
        view.requestConsent("Admin").whenCompleteAsync((accepted, error) -> {
            if (current.get() != share || share.closed.get() || !source.isConnected()) return;
            boolean consent = error == null && Boolean.TRUE.equals(accepted);
            share.consent = consent;
            sendConsent(source, share.requestId, share.sessionId, consent,
                    error == null ? "" : "Could not display the consent dialog.");
            if (!consent) {
                current.compareAndSet(share, null);
                view.sharingStopped("Screen-share request rejected.");
            }
        }, workers);
    }

    private void startAfterConsent(ClientControlChannel source, MessageEnvelope message) {
        Share share = current.get();
        Map<?, ?> data = object(message);
        if (share == null || share.closed.get() || !share.consent || share.control != source
                || !share.requestId.equals(message.requestId()) || !share.sessionId.equals(string(data.get("sessionId")))
                || !clientId.equals(string(data.get("clientId"))) || !share.token.equals(string(data.get("token")))) return;
        workers.execute(() -> start(share));
    }

    private void start(Share share) {
        try {
            share.sender = senderFactory.connect(share.host, share.port, share.requestId, clientId, share.sessionId, share.token);
            share.captureLoop = new ScreenCaptureLoop(capture, frames, scheduler, 12,
                    error -> System.getLogger(getClass().getName()).log(System.Logger.Level.WARNING, "Screen capture failed.", error));
            share.captureLoop.start();
            share.senderTask = scheduler.scheduleWithFixedDelay(() -> sendLatest(share), 0, 33, TimeUnit.MILLISECONDS);
            view.sharingStarted(() -> stop(share.control, share.requestId, "Stopped by Client user.", true));
        } catch (Exception error) {
            stop(share.control, share.requestId, "Could not start screen sharing: " + error.getMessage(), true);
        }
    }

    private void sendLatest(Share share) {
        if (share.closed.get() || current.get() != share) return;
        Optional<CapturedFrameSnapshot> snapshot = frames.readAfter(share.captureSequence);
        if (snapshot.isEmpty()) return;
        CapturedFrameSnapshot latest = snapshot.get();
        share.captureSequence = latest.sequence();
        CapturedFrame frame = latest.frame();
        try {
            var packet = encoder.execute(frame, share.previous, share.frameSequence);
            share.previous = frame;
            if (packet.isPresent()) {
                share.sender.send(packet.get());
                share.frameSequence++;
            }
        } catch (Exception error) {
            stop(share.control, share.requestId, "Screen frame transport failed: " + error.getMessage(), true);
        }
    }

    public void connectionChanged(boolean connected) { view.connectionChanged(connected); }

    @Override public void connected(ClientControlChannel source) {
        view.connectionChanged(true);
        view.sessionChanged(source.sessionId());
    }

    @Override public void failed(Exception error) {
        String detail = error == null || error.getMessage() == null || error.getMessage().isBlank()
                ? "Connection attempt failed." : error.getMessage();
        view.connectionFailed(detail);
    }

    @Override public void disconnected(ClientControlChannel source) {
        Share share = current.get();
        if (share != null && share.control == source) stop(source, share.requestId,
                "Admin connection lost. Sharing will not resume automatically.", false);
        view.connectionChanged(false);
        view.sessionChanged(null);
    }

    private void stop(ClientControlChannel source, String requestId, String reason, boolean notifyAdmin) {
        Share share = current.get();
        if (share == null || share.control != source || !share.requestId.equals(requestId)
                || !share.closed.compareAndSet(false, true)) return;
        current.compareAndSet(share, null);
        if (share.senderTask != null) share.senderTask.cancel(true);
        if (share.captureLoop != null) share.captureLoop.close();
        if (share.sender != null) share.sender.close();
        if (notifyAdmin && source.isConnected()) try {
            source.send(MessageType.SCREEN_SHARE_STOP, requestId, Map.of("sessionId", share.sessionId, "reason", reason));
        } catch (IOException ignored) { }
        view.sharingStopped(reason);
    }

    private void sendConsent(ClientControlChannel source, String requestId, String sessionId, boolean accepted, String reason) {
        if (requestId == null || !source.isConnected()) return;
        try { source.send(MessageType.SCREEN_SHARE_CONSENT, requestId, Map.of("clientId", clientId,
                "sessionId", sessionId, "accepted", accepted, "reason", reason)); }
        catch (IOException error) { source.close(); }
    }

    private static Map<?, ?> object(MessageEnvelope message) { return message.data() instanceof Map<?, ?> map ? map : Map.of(); }
    private static String string(Object value) { return value instanceof String text ? text : ""; }
    private static int integer(Object value) { return value instanceof Number number ? number.intValue() : -1; }

    @Override public void close() {
        Share share = current.get();
        if (share != null) stop(share.control, share.requestId, "Client is shutting down.", true);
        scheduler.shutdownNow(); workers.shutdownNow();
    }

    private static final class Share {
        final ClientControlChannel control;
        final String requestId, sessionId, host, token;
        final int port;
        final java.util.concurrent.atomic.AtomicBoolean closed = new java.util.concurrent.atomic.AtomicBoolean();
        volatile boolean consent;
        volatile long captureSequence, frameSequence;
        volatile CapturedFrame previous;
        volatile ScreenFrameSender sender;
        volatile ScreenCaptureLoop captureLoop;
        volatile ScheduledFuture<?> senderTask;
        Share(ClientControlChannel control, String requestId, String sessionId, String host, int port, String token) {
            this.control = control; this.requestId = requestId; this.sessionId = sessionId;
            this.host = host; this.port = port; this.token = token;
        }
    }
}
