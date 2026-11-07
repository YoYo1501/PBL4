package vn.networkadmin.client.application.screenshare;

import org.junit.jupiter.api.Test;
import vn.networkadmin.protocol.control.MessageEnvelope;
import vn.networkadmin.protocol.control.MessageType;
import vn.networkadmin.protocol.screenshare.ScreenFramePacket;
import vn.networkadmin.screenshare.application.EncodeScreenFrame;
import vn.networkadmin.screenshare.domain.CapturedFrame;
import vn.networkadmin.screenshare.infrastructure.AtomicLatestFrameStore;
import vn.networkadmin.screenshare.infrastructure.ImageIoJpegEncoder;
import vn.networkadmin.transport.ports.ClientControlChannel;

import java.io.IOException;
import java.util.List;
import java.util.Map;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.function.Consumer;

import static org.junit.jupiter.api.Assertions.*;

class ClientScreenShareServiceTest {
    @Test void rejectNeverCapturesOrOpensDataChannel() throws Exception {
        Harness h = new Harness(false);
        try {
            h.service.handle(h.control, h.request());
            assertTrue(h.control.consentSent.await(2, TimeUnit.SECONDS));
            assertEquals(Boolean.FALSE, h.control.consentPayload.get("accepted"));
            assertEquals(0, h.captures.get());
            assertNull(h.sender);
        } finally { h.close(); }
    }

    @Test void acceptsThenStreamsAndClientCanRevokeImmediately() throws Exception {
        Harness h = new Harness(true);
        try {
            h.service.handle(h.control, h.request());
            assertTrue(h.control.consentSent.await(2, TimeUnit.SECONDS));
            assertEquals(Boolean.TRUE, h.control.consentPayload.get("accepted"));
            h.service.handle(h.control, new MessageEnvelope(MessageType.SCREEN_CHANNEL_READY, "request-1",
                    Map.of("clientId", "client-1", "sessionId", "session-1", "token", "one-time",
                            "host", "127.0.0.1", "port", 5001)));

            assertTrue(h.frameSent.await(3, TimeUnit.SECONDS));
            assertNotNull(h.sender.lastFrame);
            assertTrue(h.view.stopAction != null);
            h.view.stopAction.run();
            assertTrue(h.sender.closed.await(2, TimeUnit.SECONDS));
            assertEquals("Stopped by Client user.", h.view.lastStopped);
            assertTrue(h.control.messages.stream().anyMatch(m -> m.type == MessageType.SCREEN_SHARE_STOP));
        } finally { h.close(); }
    }

    @Test void staleChannelReadyDoesNotStartCapture() throws Exception {
        Harness h = new Harness(true);
        try {
            h.service.handle(h.control, new MessageEnvelope(MessageType.SCREEN_CHANNEL_READY, "old-request",
                    Map.of("clientId", "client-1", "sessionId", "session-1", "token", "one-time",
                            "host", "127.0.0.1", "port", 5001)));
            Thread.sleep(80);
            assertEquals(0, h.captures.get());
            assertNull(h.sender);
        } finally { h.close(); }
    }

    @Test void reportsConnectionFailuresToClientView() throws Exception {
        Harness h = new Harness(false);
        try {
            h.service.failed(new IOException("Admin certificate pin mismatch"));
            assertEquals("Admin certificate pin mismatch", h.view.connectionError);
            assertEquals("Test Client", h.view.clientName);
            assertEquals("client-1", h.view.clientId);
        } finally { h.close(); }
    }

    private static final class Harness {
        final FakeControl control = new FakeControl();
        final FakeView view;
        final AtomicInteger captures = new AtomicInteger();
        final CountDownLatch frameSent = new CountDownLatch(1);
        final ExecutorService workers = Executors.newSingleThreadExecutor();
        final ScheduledExecutorService scheduler = Executors.newScheduledThreadPool(2);
        final ClientScreenShareService service;
        volatile FakeSender sender;
        Harness(boolean consent) {
            view = new FakeView(consent);
            service = new ClientScreenShareService("client-1", "Test Client", "unused-fingerprint", view,
                    () -> { captures.incrementAndGet(); return new CapturedFrame(2, 2,
                            new int[]{0xffff0000, 0xff00ff00, 0xff0000ff, 0xffffffff}); },
                    new AtomicLatestFrameStore(), new EncodeScreenFrame(new ImageIoJpegEncoder(), 0.7f),
                    (host, port, request, client, session, token) -> {
                        sender = new FakeSender(frameSent); return sender;
                    }, scheduler, workers);
        }
        MessageEnvelope request() {
            return new MessageEnvelope(MessageType.SCREEN_SHARE_REQUEST, "request-1",
                    Map.of("clientId", "client-1", "sessionId", "session-1", "host", "127.0.0.1",
                            "port", 5001, "token", "one-time"));
        }
        void close() { service.close(); }
    }

    private static final class FakeView implements ScreenShareClientView {
        final boolean consent;
        volatile Runnable stopAction;
        volatile String lastStopped;
        volatile String connectionError;
        volatile String clientName, clientId;
        FakeView(boolean consent) { this.consent = consent; }
        @Override public CompletionStage<Boolean> requestConsent(String adminName) { return CompletableFuture.completedFuture(consent); }
        @Override public void sharingStarted(Runnable stopAction) { this.stopAction = stopAction; }
        @Override public void sharingStopped(String reason) { lastStopped = reason; }
        @Override public void connectionChanged(boolean connected) { }
        @Override public void connectionFailed(String reason) { connectionError = reason; }
        @Override public void clientIdentity(String name, String id) { clientName = name; clientId = id; }
    }

    private static final class FakeSender implements ScreenFrameSender {
        final CountDownLatch closed = new CountDownLatch(1);
        final CountDownLatch frameSent;
        volatile ScreenFramePacket lastFrame;
        FakeSender(CountDownLatch frameSent) { this.frameSent = frameSent; }
        @Override public void send(ScreenFramePacket frame) { lastFrame = frame; frameSent.countDown(); }
        @Override public void close() { closed.countDown(); }
    }

    private static final class FakeControl implements ClientControlChannel {
        final List<Sent> messages = new CopyOnWriteArrayList<>();
        final CountDownLatch consentSent = new CountDownLatch(1);
        volatile Map<?, ?> consentPayload;
        @Override public void connect() { }
        @Override public String sessionId() { return "session-1"; }
        @Override public boolean isConnected() { return true; }
        @Override public void send(MessageType type, Object payload) { }
        @Override public void send(MessageType type, String requestId, Object payload) {
            messages.add(new Sent(type, requestId, payload));
            if (type == MessageType.SCREEN_SHARE_CONSENT) {
                consentPayload = (Map<?, ?>) payload; consentSent.countDown();
            }
        }
        @Override public void runReadLoop(Consumer<MessageEnvelope> handler) { }
        @Override public void close() { }
    }
    private record Sent(MessageType type, String requestId, Object payload) { }
}
