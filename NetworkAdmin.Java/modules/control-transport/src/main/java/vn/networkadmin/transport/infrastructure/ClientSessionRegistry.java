package vn.networkadmin.transport.infrastructure;

import vn.networkadmin.transport.domain.ClientSession;
import vn.networkadmin.transport.ports.ClientSessionStore;

import java.time.Duration;
import java.time.Instant;
import java.util.Comparator;
import java.util.List;
import java.util.concurrent.ConcurrentHashMap;

/** Owns sessions by ID and enforces one active connection per stable client identity. */
public final class ClientSessionRegistry implements ClientSessionStore {
    private final ConcurrentHashMap<String, ClientSession> sessions = new ConcurrentHashMap<>();

    @Override public synchronized Registration register(ClientSession candidate) {
        List<ClientSession> replaced = sessions.values().stream()
                .filter(existing -> existing.clientId().equalsIgnoreCase(candidate.clientId())).toList();
        replaced.forEach(existing -> { sessions.remove(existing.sessionId(), existing); existing.markDisconnected(); });
        sessions.put(candidate.sessionId(), candidate);
        return new Registration(candidate, replaced);
    }

    @Override public ClientSession get(String sessionId) { return sessions.get(sessionId); }
    @Override public List<ClientSession> sessions() { return sessions.values().stream().filter(ClientSession::isReady)
            .sorted(Comparator.comparing(ClientSession::connectedAt)).toList(); }
    @Override public List<ClientSession> stale(Instant now, Duration timeout) {
        return sessions().stream().filter(s -> Duration.between(s.lastSeen(), now).compareTo(timeout) > 0).toList();
    }
    @Override public boolean remove(ClientSession session) {
        boolean removed = sessions.remove(session.sessionId(), session);
        if (removed) session.markDisconnected();
        return removed;
    }
    @Override public boolean disconnect(String sessionId) {
        ClientSession session = sessions.remove(sessionId);
        if (session == null) return false;
        session.markDisconnected(); return true;
    }
    @Override public void close() { sessions.values().forEach(ClientSession::markDisconnected); sessions.clear(); }
}
