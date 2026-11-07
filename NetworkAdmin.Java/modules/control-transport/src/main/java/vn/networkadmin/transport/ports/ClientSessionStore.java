package vn.networkadmin.transport.ports;

import vn.networkadmin.transport.domain.ClientSession;

import java.time.Duration;
import java.time.Instant;
import java.util.List;

/** Application-facing session registry contract used by Admin and the TCP adapter. */
public interface ClientSessionStore extends AutoCloseable {
    Registration register(ClientSession session);
    ClientSession get(String sessionId);
    List<ClientSession> sessions();
    List<ClientSession> stale(Instant now, Duration timeout);
    boolean remove(ClientSession session);
    boolean disconnect(String sessionId);
    @Override void close();

    record Registration(ClientSession current, List<ClientSession> replaced) {
        public Registration { replaced = List.copyOf(replaced); }
    }
}
