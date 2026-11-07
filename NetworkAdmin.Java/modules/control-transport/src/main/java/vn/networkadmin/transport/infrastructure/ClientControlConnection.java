package vn.networkadmin.transport.infrastructure;

import vn.networkadmin.protocol.control.HelloAckMessage;
import vn.networkadmin.protocol.control.HelloMessage;
import vn.networkadmin.protocol.control.JsonLineProtocol;
import vn.networkadmin.protocol.control.MessageEnvelope;
import vn.networkadmin.protocol.control.MessageType;
import vn.networkadmin.transport.tls.PinnedTlsClientSocketFactory;
import vn.networkadmin.transport.ports.ClientControlChannel;

import javax.net.ssl.SSLSocket;
import java.io.IOException;
import java.net.SocketException;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.function.Consumer;

/** One Client-to-Admin control connection; owns its TLS socket and Hello session identity. */
public final class ClientControlConnection implements ClientControlChannel {
    private final String host;
    private final int port;
    private final String fingerprint;
    private final String clientId;
    private final String clientName;
    private final List<String> capabilities;
    private final int connectTimeoutMillis;
    private final Object writeLock = new Object();
    private final AtomicBoolean connected = new AtomicBoolean();
    private volatile SSLSocket socket;
    private volatile String sessionId;

    public ClientControlConnection(String host, int port, String fingerprint, String clientId, String clientName,
                                   List<String> capabilities, int connectTimeoutMillis) {
        if (host == null || host.isBlank() || port < 1 || port > 65535) throw new IllegalArgumentException("Server endpoint is invalid.");
        this.host = host; this.port = port; this.fingerprint = fingerprint;
        this.clientId = clientId; this.clientName = clientName;
        this.capabilities = capabilities == null ? List.of() : List.copyOf(capabilities);
        this.connectTimeoutMillis = connectTimeoutMillis > 0 ? connectTimeoutMillis : 10_000;
    }

    @Override public synchronized void connect() throws IOException {
        if (connected.get()) throw new IllegalStateException("Connection is already established.");
        SSLSocket created = new PinnedTlsClientSocketFactory(fingerprint).connect(host, port, connectTimeoutMillis);
        socket = created;
        try {
            created.setKeepAlive(true);
            String requestId = UUID.randomUUID().toString().replace("-", "");
            HelloMessage hello = new HelloMessage(clientId, clientName,
                    vn.networkadmin.protocol.control.RemoteProtocolContract.CURRENT_VERSION, capabilities);
            String helloError = hello.validate();
            if (helloError != null) throw new IOException(helloError);
            JsonLineProtocol.send(created.getOutputStream(), MessageType.HELLO, requestId, hello.toPayload());
            MessageEnvelope response = JsonLineProtocol.receive(created.getInputStream());
            if (response == null) throw new IOException("Server closed the connection before HelloAck.");
            if (response.type() != MessageType.HELLO_ACK || !requestId.equals(response.requestId()))
                throw new IOException("Server returned an invalid HelloAck.");
            if (!(response.data() instanceof Map<?, ?> map)) throw new IOException("HelloAck payload is invalid.");
            HelloAckMessage ack = HelloAckMessage.fromPayload(castMap(map));
            String ackError = ack.validate();
            if (ackError != null) throw new IOException(ackError);
            if (!ack.accepted()) throw new IOException(ack.message());
            sessionId = ack.sessionId(); connected.set(true); created.setSoTimeout(0);
            return;
        } catch (Exception ex) {
            close();
            if (ex instanceof IOException io) throw io;
            throw new IOException("Control handshake failed.", ex);
        }
    }

    @Override public String sessionId() { return sessionId; }
    @Override public boolean isConnected() { return connected.get(); }

    @Override public void send(MessageType type, Object payload) throws IOException {
        SSLSocket active = requireSocket();
        String requestId = UUID.randomUUID().toString().replace("-", "");
        synchronized (writeLock) { JsonLineProtocol.send(active.getOutputStream(), type, requestId, payload); }
    }

    @Override public void send(MessageType type, String requestId, Object payload) throws IOException {
        SSLSocket active = requireSocket();
        synchronized (writeLock) { JsonLineProtocol.send(active.getOutputStream(), type, requestId, payload); }
    }

    @Override public void runReadLoop(Consumer<MessageEnvelope> messageHandler) throws IOException {
        SSLSocket active = requireSocket();
        while (connected.get()) {
            MessageEnvelope envelope = JsonLineProtocol.receive(active.getInputStream());
            if (envelope == null) throw new IOException("Server closed the connection.");
            messageHandler.accept(envelope);
        }
    }

    private SSLSocket requireSocket() throws SocketException {
        SSLSocket active = socket;
        if (!connected.get() || active == null || active.isClosed()) throw new SocketException("Server connection is not established.");
        return active;
    }

    @SuppressWarnings("unchecked") private static Map<String, Object> castMap(Map<?, ?> map) { return (Map<String, Object>) map; }

    @Override public synchronized void close() {
        connected.set(false); sessionId = null;
        SSLSocket active = socket; socket = null;
        if (active != null) try { active.close(); } catch (IOException ignored) { }
    }
}
