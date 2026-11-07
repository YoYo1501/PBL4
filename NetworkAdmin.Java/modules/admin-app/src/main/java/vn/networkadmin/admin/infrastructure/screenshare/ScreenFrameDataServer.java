package vn.networkadmin.admin.infrastructure.screenshare;

import vn.networkadmin.protocol.screenshare.ScreenFramePacket;
import vn.networkadmin.protocol.screenshare.ScreenFrameProtocol;
import vn.networkadmin.protocol.screenshare.ScreenFrameDataHandshake;
import vn.networkadmin.admin.application.screenshare.ScreenFrameDataPort;
import vn.networkadmin.admin.application.screenshare.ScreenFrameDataEvents;

import javax.net.ssl.SSLContext;
import javax.net.ssl.SSLServerSocket;
import javax.net.ssl.SSLSocket;
import java.io.DataInputStream;
import java.io.DataOutputStream;
import java.io.IOException;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.util.Objects;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Semaphore;
import java.util.concurrent.atomic.AtomicBoolean;

/** TLS-only, one-use authenticated binary frame channel; separate from JSON control traffic. */
public final class ScreenFrameDataServer implements ScreenFrameDataPort {
    private final SSLContext tls;
    private final InetAddress bindAddress;
    private final int port;
    private final ScreenFrameDataEvents events;
    private final ConcurrentHashMap<String, Authorization> authorizations = new ConcurrentHashMap<>();
    private final ConcurrentHashMap<String, SSLSocket> active = new ConcurrentHashMap<>();
    private final ConcurrentHashMap<SSLSocket, Boolean> pendingSockets = new ConcurrentHashMap<>();
    private final Object lifecycleLock = new Object();
    private final ExecutorService acceptor = Executors.newSingleThreadExecutor(task -> daemon(task, "screen-frame-acceptor"));
    private final ExecutorService readers = Executors.newVirtualThreadPerTaskExecutor();
    private final Semaphore connectionLimit = new Semaphore(64);
    private final AtomicBoolean closed = new AtomicBoolean();
    private volatile SSLServerSocket server;

    public ScreenFrameDataServer(SSLContext tls, InetAddress bindAddress, int port,
                                 ScreenFrameDataEvents events) {
        this.tls = Objects.requireNonNull(tls);
        this.bindAddress = Objects.requireNonNull(bindAddress);
        if (port < 0 || port > 65535) throw new IllegalArgumentException("Screen data port is invalid.");
        this.port = port;
        this.events = Objects.requireNonNull(events);
    }

    @Override public synchronized int start() throws IOException {
        if (closed.get()) throw new IllegalStateException("Screen data server is closed.");
        if (server != null) return server.getLocalPort();
        SSLServerSocket socket = (SSLServerSocket) tls.getServerSocketFactory().createServerSocket();
        socket.setReuseAddress(true);
        socket.setEnabledProtocols(java.util.Arrays.stream(socket.getSupportedProtocols())
                .filter(p -> p.equals("TLSv1.3") || p.equals("TLSv1.2")).toArray(String[]::new));
        socket.bind(new InetSocketAddress(bindAddress, port));
        server = socket;
        acceptor.execute(this::acceptLoop);
        return socket.getLocalPort();
    }

    @Override public int port() { SSLServerSocket s = server; return s == null ? port : s.getLocalPort(); }

    @Override public void authorize(String requestId, String clientId, String sessionId, String token) {
        synchronized (lifecycleLock) {
            if (closed.get()) throw new IllegalStateException("Screen data server is closed.");
            authorizations.put(requestId, new Authorization(clientId, sessionId, token));
        }
    }

    @Override public void revoke(String requestId) {
        synchronized (lifecycleLock) {
            authorizations.remove(requestId);
            SSLSocket socket = active.remove(requestId);
            if (socket != null) try { socket.close(); } catch (IOException ignored) { }
        }
    }

    private void acceptLoop() {
        while (!closed.get()) {
            try {
                SSLSocket socket = (SSLSocket) server.accept();
                if (!connectionLimit.tryAcquire()) { socket.close(); continue; }
                pendingSockets.put(socket, Boolean.TRUE);
                readers.execute(() -> readSession(socket));
            } catch (IOException error) {
                if (!closed.get()) System.getLogger(getClass().getName()).log(System.Logger.Level.WARNING,
                        "Screen data accept failed.", error);
            }
        }
    }

    private void readSession(SSLSocket socket) {
        String requestId = null;
        ScreenFrameDataPort.Identity identity = null;
        boolean authenticated = false;
        try (socket) {
            socket.setSoTimeout(10_000);
            socket.startHandshake();
            DataInputStream input = new DataInputStream(socket.getInputStream());
            ScreenFrameDataHandshake handshake = ScreenFrameDataHandshake.read(input);
            requestId = handshake.requestId();
            String clientId = handshake.clientId();
            String sessionId = handshake.sessionId();
            String token = handshake.token();
            synchronized (lifecycleLock) {
                Authorization expected = authorizations.get(requestId);
                if (closed.get() || expected == null || !expected.matches(clientId, sessionId, token)
                        || !authorizations.remove(requestId, expected)
                        || active.putIfAbsent(requestId, socket) != null)
                    throw new IOException("Screen channel is not authorized.");
            }
            authenticated = true;
            socket.getOutputStream().write(ScreenFrameDataHandshake.ACCEPTED);
            socket.getOutputStream().flush();
            socket.setSoTimeout(0);
            identity = new ScreenFrameDataPort.Identity(requestId, clientId, sessionId);
            while (!closed.get() && !socket.isClosed()) {
                ScreenFramePacket frame = ScreenFrameProtocol.readFrame(input);
                if (frame == null) break;
                events.publish(new ScreenFrameDataEvents.FrameReceived(identity, frame));
            }
        } catch (IOException | RuntimeException error) {
            if (!authenticated && !socket.isClosed()) try {
                socket.getOutputStream().write(ScreenFrameDataHandshake.REJECTED);
                socket.getOutputStream().flush();
            } catch (IOException ignored) { }
            if (!closed.get() && requestId != null) System.getLogger(getClass().getName()).log(System.Logger.Level.DEBUG,
                    "Screen channel ended for request " + requestId, error);
        } finally {
            if (requestId != null) active.remove(requestId, socket);
            pendingSockets.remove(socket);
            if (identity != null) events.publish(new ScreenFrameDataEvents.ChannelClosed(identity));
            connectionLimit.release();
        }
    }

    @Override public void close() {
        synchronized (lifecycleLock) {
            if (!closed.compareAndSet(false, true)) return;
            try { if (server != null) server.close(); } catch (IOException ignored) { }
            active.values().forEach(socket -> { try { socket.close(); } catch (IOException ignored) { } });
            pendingSockets.keySet().forEach(socket -> { try { socket.close(); } catch (IOException ignored) { } });
            active.clear(); authorizations.clear();
            pendingSockets.clear();
        }
        acceptor.shutdownNow(); readers.shutdownNow();
    }

    private static Thread daemon(Runnable action, String name) {
        Thread thread = new Thread(action, name); thread.setDaemon(true); return thread;
    }

    private record Authorization(String clientId, String sessionId, String token) {
        private boolean matches(String client, String session, String suppliedToken) {
            return clientId.equals(client) && sessionId.equals(session) && token.equals(suppliedToken);
        }
    }

}
