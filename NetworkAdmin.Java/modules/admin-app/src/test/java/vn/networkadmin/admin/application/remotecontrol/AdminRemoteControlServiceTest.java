package vn.networkadmin.admin.application.remotecontrol;

import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.Test;
import vn.networkadmin.protocol.control.MessageEnvelope;
import vn.networkadmin.protocol.control.MessageType;
import vn.networkadmin.transport.domain.ClientSession;
import vn.networkadmin.transport.infrastructure.ClientSessionRegistry;
import vn.networkadmin.transport.ports.SessionMessageSender;
import java.io.IOException;
import java.time.Duration;
import java.time.Instant;
import java.util.*;
import java.util.concurrent.*;
import static org.junit.jupiter.api.Assertions.*;

class AdminRemoteControlServiceTest {
    private final ClientSessionRegistry sessions = new ClientSessionRegistry();
    private final ClientSession session = ready("session-1");
    private final RecordingSender sender = new RecordingSender();
    private final ScheduledExecutorService scheduler = Executors.newSingleThreadScheduledExecutor();
    private final AdminRemoteControlService service = new AdminRemoteControlService(sessions, sender, scheduler);
    AdminRemoteControlServiceTest() { sessions.register(session); session.markReady(); }
    @AfterEach void close() { service.close(); scheduler.shutdownNow(); sessions.close(); }

    @Test void requiresCurrentSessionConsentBeforeSendingInputAndStopsOnRevoke() throws Exception {
        service.requestControl("client-1", "session-1");
        String request = sender.last().requestId;
        assertEquals("Pending", service.state("session-1").status());
        assertThrows(IOException.class, () -> service.sendInput("session-1", move()));
        service.handle(session, consent(request, true, "session-1"));
        service.sendInput("session-1", move());
        assertEquals(MessageType.CONTROL_INPUT_EVENT, sender.last().type);
        service.handle(session, new MessageEnvelope(MessageType.CONTROL_STOP, request,
                Map.of("clientId", "client-1", "sessionId", "session-1", "reason", "revoke")));
        assertEquals("Stopped", service.state("session-1").status());
        assertThrows(IOException.class, () -> service.sendInput("session-1", move()));
    }

    @Test void ignoresOldSessionConsentAndDisconnectRevokesGrant() throws Exception {
        service.requestControl("client-1", "session-1"); String request = sender.last().requestId;
        ClientSession replacement = ready("session-2"); sessions.register(replacement); replacement.markReady();
        service.handle(session, consent(request, true, "session-1"));
        assertThrows(IOException.class, () -> service.sendInput("session-1", move()));
        service.disconnected(session);
        assertEquals("Stopped", service.state("session-1").status());
    }

    private MessageEnvelope consent(String request, boolean allow, String sid) {
        return new MessageEnvelope(MessageType.CONTROL_CONSENT, request,
                Map.of("clientId", "client-1", "sessionId", sid, "accepted", allow));
    }
    private static vn.networkadmin.protocol.control.ControlInputEvent move() {
        return new vn.networkadmin.protocol.control.ControlInputEvent(
                vn.networkadmin.protocol.control.ControlInputEvent.Kind.MOVE, .4, .5, 0, 0, 0, 0);
    }
    private ClientSession ready(String id) { ClientSession value = new ClientSession("client-1", "Client", id, "localhost"); return value; }
    private static final class RecordingSender implements SessionMessageSender {
        final List<Sent> sent = new CopyOnWriteArrayList<>();
        @Override public void send(String sid, MessageType type, String id, Object data) { sent.add(new Sent(sid,type,id,data)); }
        @Override public void closeSession(String sid) { }
        Sent last() { return sent.getLast(); }
    }
    private record Sent(String sessionId, MessageType type, String requestId, Object payload) { }
}
