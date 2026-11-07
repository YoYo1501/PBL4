package vn.networkadmin.client.infrastructure.screenshare;

import vn.networkadmin.protocol.screenshare.ScreenFramePacket;
import vn.networkadmin.protocol.screenshare.ScreenFrameProtocol;
import vn.networkadmin.protocol.screenshare.ScreenFrameDataHandshake;
import vn.networkadmin.client.application.screenshare.ScreenFrameSender;
import vn.networkadmin.transport.tls.PinnedTlsClientSocketFactory;

import javax.net.ssl.SSLSocket;
import java.io.DataOutputStream;
import java.io.DataInputStream;
import java.io.IOException;
import java.util.Objects;
import java.util.concurrent.atomic.AtomicBoolean;

/** Client-side pinned TLS connection for the high-bandwidth binary screen stream. */
public final class TlsScreenFrameChannel implements ScreenFrameSender {
    private final SSLSocket socket;
    private final DataOutputStream output;
    private final Object writeLock = new Object();
    private final AtomicBoolean closed = new AtomicBoolean();

    public TlsScreenFrameChannel(String host, int port, String fingerprint, String requestId,
                                 String clientId, String sessionId, String token) throws IOException {
        Objects.requireNonNull(requestId); Objects.requireNonNull(clientId);
        Objects.requireNonNull(sessionId); Objects.requireNonNull(token);
        socket = new PinnedTlsClientSocketFactory(fingerprint).connect(host, port, 10_000);
        boolean initialized = false;
        try {
            DataOutputStream stream = new DataOutputStream(socket.getOutputStream());
            new ScreenFrameDataHandshake(requestId, clientId, sessionId, token).write(stream);
            stream.flush();
            socket.setSoTimeout(10_000);
            int accepted = new DataInputStream(socket.getInputStream()).read();
            if (accepted != ScreenFrameDataHandshake.ACCEPTED)
                throw new IOException("Admin rejected the screen frame channel authorization.");
            socket.setSoTimeout(0);
            output = stream;
            initialized = true;
        } finally { if (!initialized) socket.close(); }
    }

    @Override public void send(ScreenFramePacket frame) throws IOException {
        if (closed.get()) throw new IOException("Screen frame channel is closed.");
        synchronized (writeLock) { ScreenFrameProtocol.writeFrame(output, frame); }
    }

    @Override public void close() {
        if (!closed.compareAndSet(false, true)) return;
        try { socket.close(); } catch (IOException ignored) { }
    }
}
