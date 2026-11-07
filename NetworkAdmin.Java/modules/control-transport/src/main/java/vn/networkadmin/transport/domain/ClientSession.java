package vn.networkadmin.transport.domain;

import vn.networkadmin.protocol.control.RemoteProtocolContract;

import java.time.Instant;
import java.util.Objects;
import java.util.concurrent.atomic.AtomicBoolean;

/** Transport-independent identity and lifecycle metadata for an authenticated Client session. */
public final class ClientSession {
    private final String clientId;
    private final String clientName;
    private final String sessionId;
    private final String remoteAddress;
    private final AtomicBoolean ready = new AtomicBoolean();
    private final Instant connectedAt = Instant.now();
    private volatile Instant lastSeen = connectedAt;

    public ClientSession(String clientId, String clientName, String sessionId, String remoteAddress) {
        if (!RemoteProtocolContract.isValidIdentifier(clientId) || !RemoteProtocolContract.isValidDisplayName(clientName)
                || !RemoteProtocolContract.isValidSessionId(sessionId)) throw new IllegalArgumentException("Client session identity is invalid.");
        this.clientId = clientId; this.clientName = clientName; this.sessionId = sessionId;
        this.remoteAddress = Objects.requireNonNullElse(remoteAddress, "");
    }

    public String clientId() { return clientId; }
    public String clientName() { return clientName; }
    public String sessionId() { return sessionId; }
    public String remoteAddress() { return remoteAddress; }
    public Instant connectedAt() { return connectedAt; }
    public Instant lastSeen() { return lastSeen; }
    public boolean isReady() { return ready.get(); }
    public void markReady() { ready.set(true); touch(); }
    public void touch() { lastSeen = Instant.now(); }
    public void markDisconnected() { ready.set(false); }
}
