package vn.networkadmin.admin.runtime;

import org.junit.jupiter.api.Test;
import vn.networkadmin.protocol.control.MessageEnvelope;
import vn.networkadmin.protocol.control.MessageType;
import vn.networkadmin.transport.domain.ClientSession;
import vn.networkadmin.transport.infrastructure.ClientSessionRegistry;
import vn.networkadmin.transport.ports.ControlServerListener;
import vn.networkadmin.transport.ports.ControlServerPort;
import vn.networkadmin.transport.ports.SessionMessageSender;
import java.io.IOException;
import java.util.Map;
import static org.junit.jupiter.api.Assertions.*;

class AdminClientRegistryTest {
    @Test void keepsKnownClientOfflineAndReplacesSessionWithoutAcceptingOldTelemetry() {
        var sessions = new ClientSessionRegistry(); var sender = new FakeSender(); var server = new FakeServer();
        var runtime = new AdminControlRuntime(server, sessions, sender);
        ClientSession first = ready("client-stable", "session-first"); sessions.register(first); runtime.connected(first);
        runtime.handle(first, new MessageEnvelope(MessageType.NETWORK_INFO, null, Map.of("hostname", "PC-1")));
        runtime.disconnected(first);
        assertEquals(1, runtime.clients().size()); assertFalse(runtime.clients().getFirst().online());
        assertEquals("PC-1", runtime.clients().getFirst().networkInfo().get("hostname"));

        ClientSession reconnected = ready("client-stable", "session-next"); sessions.register(reconnected); runtime.connected(reconnected);
        runtime.disconnected(first);
        runtime.handle(first, new MessageEnvelope(MessageType.NETWORK_INFO, null, Map.of("hostname", "STALE")));
        assertEquals(1, runtime.clients().size());
        assertTrue(runtime.clients().getFirst().online());
        assertEquals("session-next", runtime.clients().getFirst().sessionId());
        assertEquals("PC-1", runtime.clients().getFirst().networkInfo().get("hostname"));
        runtime.close(); sessions.close();
    }

    private static ClientSession ready(String clientId, String sessionId) {
        ClientSession session = new ClientSession(clientId, "Test Client", sessionId, "localhost"); session.markReady(); return session;
    }
    private static final class FakeServer implements ControlServerPort {
        @Override public void run(ControlServerListener listener) throws IOException { }
        @Override public boolean isListening() { return true; }
        @Override public void close() { }
    }
    private static final class FakeSender implements SessionMessageSender {
        @Override public void send(String sessionId, MessageType type, String requestId, Object payload) { }
        @Override public void closeSession(String sessionId) { }
    }
}
