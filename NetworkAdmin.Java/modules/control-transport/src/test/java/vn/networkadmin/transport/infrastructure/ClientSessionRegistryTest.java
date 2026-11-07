package vn.networkadmin.transport.infrastructure;

import org.junit.jupiter.api.Test;
import vn.networkadmin.transport.domain.ClientSession;

import static org.junit.jupiter.api.Assertions.*;

class ClientSessionRegistryTest {
    @Test
    void registeringSameClientIdReplacesOldSessionAndKeepsNewSessionActive() {
        ClientSessionRegistry sessions = new ClientSessionRegistry();
        ClientSession first = session("Managed-Client", "session-first");
        ClientSession second = session("managed-client", "session-second");
        first.markReady();
        second.markReady();
        sessions.register(first);

        var registration = sessions.register(second);

        assertEquals(1, registration.replaced().size());
        assertSame(first, registration.replaced().getFirst());
        assertFalse(first.isReady());
        assertNull(sessions.get(first.sessionId()));
        assertSame(second, sessions.get(second.sessionId()));
        assertEquals(1, sessions.sessions().size());
        assertEquals("managed-client", sessions.sessions().getFirst().clientId());
        sessions.close();
        assertFalse(second.isReady());
    }

    private static ClientSession session(String clientId, String sessionId) {
        return new ClientSession(clientId, "Test Client", sessionId, "127.0.0.1");
    }
}
