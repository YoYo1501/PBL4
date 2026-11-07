package vn.networkadmin.admin.infrastructure.screenshare;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import vn.networkadmin.client.infrastructure.screenshare.TlsScreenFrameChannel;
import vn.networkadmin.admin.application.screenshare.ScreenFrameDataEvents;
import vn.networkadmin.admin.application.screenshare.ScreenFrameDataPort;
import vn.networkadmin.admin.infrastructure.screenshare.LocalScreenFrameDataEvents;
import vn.networkadmin.protocol.screenshare.ScreenFramePacket;
import vn.networkadmin.screenshare.server.infrastructure.ImageIoJpegDecoder;
import vn.networkadmin.transport.tls.Pkcs12ServerTlsContext;

import javax.net.ssl.SSLContext;
import java.net.InetAddress;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.MessageDigest;
import java.security.cert.CertificateFactory;
import java.security.cert.X509Certificate;
import java.time.Duration;
import java.util.HexFormat;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicReference;

import static org.junit.jupiter.api.Assertions.*;

class ScreenFrameDataServerIntegrationTest {
    private static final String PASSWORD = "screen-test-password";

    @Test void tlsChannelRequiresOneUseSessionBoundConsentAndCarriesBoundedBinaryFrames(@TempDir Path temp)
            throws Exception {
        Path store = temp.resolve("screen.p12"), certificate = temp.resolve("screen.cer");
        createIdentity(store, certificate);
        SSLContext tls = Pkcs12ServerTlsContext.load(store, PASSWORD.toCharArray());
        String fingerprint = fingerprint(certificate);
        CountDownLatch received = new CountDownLatch(1), disconnected = new CountDownLatch(1);
        AtomicReference<ScreenFrameDataPort.Identity> identity = new AtomicReference<>();
        AtomicReference<ScreenFramePacket> packet = new AtomicReference<>();
        ScreenFrameDataEvents events = new LocalScreenFrameDataEvents();
        events.subscribe(event -> {
            if (event instanceof ScreenFrameDataEvents.FrameReceived frame) {
                identity.set(frame.identity()); packet.set(frame.frame()); received.countDown();
            } else if (event instanceof ScreenFrameDataEvents.ChannelClosed) disconnected.countDown();
        });
        ScreenFrameDataServer server = new ScreenFrameDataServer(tls, InetAddress.getLoopbackAddress(), 0, events);
        try {
            int port = server.start();
            assertThrows(java.io.IOException.class, () -> new TlsScreenFrameChannel("127.0.0.1", port,
                    fingerprint, "req-old", "client-1", "session-1", "not-authorized"));

            server.authorize("req-current", "client-1", "session-2", "consented-token");
            TlsScreenFrameChannel channel = new TlsScreenFrameChannel("127.0.0.1", port, fingerprint,
                    "req-current", "client-1", "session-2", "consented-token");
            ScreenFramePacket frame = new ScreenFramePacket(7, true, 0, 0, 1, 1,
                    new byte[]{(byte) 0xff, (byte) 0xd8, (byte) 0xff, (byte) 0xd9});
            channel.send(frame);
            assertTrue(received.await(3, TimeUnit.SECONDS));
            assertEquals("req-current", identity.get().requestId());
            assertEquals("client-1", identity.get().clientId());
            assertEquals("session-2", identity.get().sessionId());
            assertEquals(7, packet.get().sequence());
            channel.close();
            assertTrue(disconnected.await(3, TimeUnit.SECONDS));
        } finally { server.close(); }
    }

    private static void createIdentity(Path store, Path certificate) throws Exception {
        Path keytool = Path.of(System.getProperty("java.home"), "bin", isWindows() ? "keytool.exe" : "keytool");
        run(keytool, "-genkeypair", "-alias", "screen", "-keyalg", "RSA", "-keysize", "2048", "-validity", "2",
                "-dname", "CN=Screen Test", "-storetype", "PKCS12", "-keystore", store.toString(),
                "-storepass", PASSWORD, "-keypass", PASSWORD);
        run(keytool, "-exportcert", "-alias", "screen", "-keystore", store.toString(), "-storepass", PASSWORD,
                "-file", certificate.toString());
    }
    private static void run(Path executable, String... args) throws Exception {
        var command = new java.util.ArrayList<String>(); command.add(executable.toString()); command.addAll(java.util.List.of(args));
        Process process = new ProcessBuilder(command).redirectErrorStream(true).start();
        String output = new String(process.getInputStream().readAllBytes(), java.nio.charset.StandardCharsets.UTF_8);
        assertEquals(0, process.waitFor(), output);
    }
    private static String fingerprint(Path certificate) throws Exception {
        try (var in = Files.newInputStream(certificate)) {
            X509Certificate cert = (X509Certificate) CertificateFactory.getInstance("X.509").generateCertificate(in);
            return HexFormat.of().withUpperCase().formatHex(MessageDigest.getInstance("SHA-256").digest(cert.getEncoded()));
        }
    }
    private static boolean isWindows() { return System.getProperty("os.name").toLowerCase(java.util.Locale.ROOT).contains("win"); }
}
