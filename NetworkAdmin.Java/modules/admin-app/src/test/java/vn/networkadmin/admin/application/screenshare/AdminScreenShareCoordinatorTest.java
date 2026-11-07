package vn.networkadmin.admin.application.screenshare;

import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.Test;
import vn.networkadmin.admin.infrastructure.screenshare.ScreenFrameDataServer;
import vn.networkadmin.protocol.control.MessageEnvelope;
import vn.networkadmin.protocol.control.MessageType;
import vn.networkadmin.protocol.screenshare.ScreenFramePacket;
import vn.networkadmin.screenshare.server.application.ApplyScreenFrame;
import vn.networkadmin.screenshare.server.domain.DecodedJpeg;
import vn.networkadmin.screenshare.server.ports.JpegDecoderPort;
import vn.networkadmin.transport.domain.ClientSession;
import vn.networkadmin.transport.infrastructure.ClientSessionRegistry;
import vn.networkadmin.transport.ports.SessionMessageSender;

import java.io.IOException;
import java.time.Duration;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;

import static org.junit.jupiter.api.Assertions.*;

class AdminScreenShareCoordinatorTest {
    private final ClientSessionRegistry sessions = new ClientSessionRegistry();
    private final RecordingSender sender = new RecordingSender();
    private final FakeDataPort data = new FakeDataPort();
    private final ScheduledExecutorService scheduler = Executors.newSingleThreadScheduledExecutor();
    private final ClientSession session = new ClientSession("client-1", "Test Client", "session-1", "127.0.0.1");
    private final AdminScreenShareCoordinator coordinator = new AdminScreenShareCoordinator(sessions, sender, data,
            new ApplyScreenFrame(new FakeDecoder()), scheduler, "127.0.0.1", 5001, Duration.ofSeconds(2));

    AdminScreenShareCoordinatorTest() { sessions.register(session); session.markReady(); }

    @AfterEach void close() { coordinator.close(); scheduler.shutdownNow(); sessions.close(); }

    @Test void rejectNeverAuthorizesFrameChannel() throws Exception {
        coordinator.requestViewing("client-1", "session-1");
        String requestId = sender.last().requestId;
        coordinator.handle(session, new MessageEnvelope(MessageType.SCREEN_SHARE_CONSENT, requestId,
                Map.of("clientId", "client-1", "sessionId", "session-1", "accepted", false, "reason", "No")));

        assertEquals("Rejected", coordinator.snapshot("session-1").status());
        assertFalse(data.authorized);
        assertEquals(1, sender.messages.size());
    }

    @Test void acceptsOnlyCurrentSessionAndFramesAfterConsent() throws Exception {
        coordinator.requestViewing("client-1", "session-1");
        String requestId = sender.last().requestId;
        ScreenFrameDataPort.Identity identity = new ScreenFrameDataPort.Identity(requestId, "client-1", "session-1");
        coordinator.frameReceived(identity, frame(0));
        assertEquals("Pending", coordinator.snapshot("session-1").status());

        coordinator.handle(session, consent(requestId, true));
        assertTrue(data.authorized);
        assertEquals(MessageType.SCREEN_CHANNEL_READY, sender.last().type);
        coordinator.frameReceived(new ScreenFrameDataPort.Identity(requestId, "client-1", "old-session"), frame(0));
        assertEquals("Accepted", coordinator.snapshot("session-1").status());

        coordinator.frameReceived(identity, frame(0));
        assertEquals("Viewing", coordinator.snapshot("session-1").status());
        assertEquals(1, coordinator.snapshot("session-1").canvas().width());
    }

    @Test void adminStopRevokesChannelAndSendsStopForSameRequest() throws Exception {
        coordinator.requestViewing("client-1", "session-1");
        String requestId = sender.last().requestId;
        coordinator.handle(session, consent(requestId, true));
        coordinator.stopViewing("session-1");

        assertEquals("Stopped", coordinator.snapshot("session-1").status());
        assertFalse(data.authorized);
        assertEquals(MessageType.SCREEN_SHARE_STOP, sender.last().type);
        assertEquals(requestId, sender.last().requestId);
    }

    @Test void pendingRequestTimesOutAndDisconnectStopsWithoutReauthorization() throws Exception {
        AdminScreenShareCoordinator shortTimeout = new AdminScreenShareCoordinator(sessions, sender, data,
                new ApplyScreenFrame(new FakeDecoder()), scheduler, "127.0.0.1", 5001, Duration.ofMillis(30));
        shortTimeout.requestViewing("client-1", "session-1");
        Thread.sleep(80);
        assertEquals("Stopped", shortTimeout.snapshot("session-1").status());
        shortTimeout.close();

        coordinator.requestViewing("client-1", "session-1");
        coordinator.disconnected(session);
        assertEquals("Stopped", coordinator.snapshot("session-1").status());
        assertFalse(data.authorized);
    }

    private static MessageEnvelope consent(String requestId, boolean accepted) {
        return new MessageEnvelope(MessageType.SCREEN_SHARE_CONSENT, requestId,
                Map.of("clientId", "client-1", "sessionId", "session-1", "accepted", accepted));
    }
    private static ScreenFramePacket frame(long sequence) { return new ScreenFramePacket(sequence, true, 0, 0, 1, 1, new byte[]{1}); }

    private static final class FakeDecoder implements JpegDecoderPort {
        @Override public DecodedJpeg decode(byte[] jpeg) { return new DecodedJpeg(1, 1, new int[]{0xff00ff00}); }
    }
    private static final class FakeDataPort implements ScreenFrameDataPort {
        boolean authorized;
        @Override public int start() { return 5001; }
        @Override public int port() { return 5001; }
        @Override public void authorize(String requestId, String clientId, String sessionId, String token) { authorized = true; }
        @Override public void revoke(String requestId) { authorized = false; }
        @Override public void close() { authorized = false; }
    }
    private static final class RecordingSender implements SessionMessageSender {
        final List<Sent> messages = new ArrayList<>();
        @Override public void send(String sessionId, MessageType type, String requestId, Object payload) { messages.add(new Sent(sessionId, type, requestId, payload)); }
        @Override public void closeSession(String sessionId) { }
        Sent last() { return messages.getLast(); }
    }
    private record Sent(String sessionId, MessageType type, String requestId, Object payload) { }
}
