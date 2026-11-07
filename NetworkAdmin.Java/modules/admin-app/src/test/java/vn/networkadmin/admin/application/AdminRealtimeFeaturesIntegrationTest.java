package vn.networkadmin.admin.application;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import vn.networkadmin.admin.application.chat.AdminChatService;
import vn.networkadmin.admin.application.remotecontrol.AdminRemoteControlService;
import vn.networkadmin.admin.ports.screenshare.AdminScreenShareMessageHandler;
import vn.networkadmin.admin.runtime.AdminControlRuntime;
import vn.networkadmin.admin.runtime.AdminClientSnapshot;
import vn.networkadmin.client.application.ClientFeatureMessages;
import vn.networkadmin.client.application.chat.ClientChatService;
import vn.networkadmin.client.application.chat.ClientChatUseCases;
import vn.networkadmin.client.application.remotecontrol.ClientRemoteControlService;
import vn.networkadmin.client.application.remotecontrol.RemoteControlClientView;
import vn.networkadmin.client.application.remotecontrol.RemoteInputPort;
import vn.networkadmin.client.ports.screenshare.ClientScreenShareMessageHandler;
import vn.networkadmin.client.runtime.ClientAgentRuntime;
import vn.networkadmin.network.application.NetworkScanner;
import vn.networkadmin.network.domain.NetworkInterfaceInfo;
import vn.networkadmin.network.domain.PingResult;
import vn.networkadmin.network.ports.NetworkInventoryPort;
import vn.networkadmin.network.ports.PingPort;
import vn.networkadmin.protocol.control.ControlInputEvent;
import vn.networkadmin.transport.application.ClientConnectionSupervisor;
import vn.networkadmin.transport.application.ControlServerOptions;
import vn.networkadmin.transport.domain.ClientSession;
import vn.networkadmin.transport.infrastructure.*;
import vn.networkadmin.transport.ports.ClientControlChannel;
import vn.networkadmin.transport.ports.SessionMessageSender;
import vn.networkadmin.transport.tls.Pkcs12ServerTlsContext;

import javax.net.ssl.SSLContext;
import java.net.InetAddress;
import java.net.ServerSocket;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.MessageDigest;
import java.security.cert.CertificateFactory;
import java.security.cert.X509Certificate;
import java.time.Duration;
import java.time.Instant;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.*;
import java.util.function.Consumer;
import java.util.function.BooleanSupplier;

import static org.junit.jupiter.api.Assertions.*;

class AdminRealtimeFeaturesIntegrationTest {
    private static final String PASSWORD = "realtime-test-password";

    @Test void consentBoundControlAndSessionBoundChatRunOverPinnedTlsControlChannel(@TempDir Path temp) throws Exception {
        Path keyStore = temp.resolve("server.p12"), certificate = temp.resolve("server.cer"); createIdentity(keyStore, certificate);
        SSLContext tls = Pkcs12ServerTlsContext.load(keyStore, PASSWORD.toCharArray());
        String fingerprint = fingerprint(certificate); int port = freePort(); String clientId = "realtime-client";
        var sessions = new ClientSessionRegistry();
        var connectionWorkers = Executors.newVirtualThreadPerTaskExecutor();
        var heartbeatMonitor = Executors.newSingleThreadScheduledExecutor();
        var scheduler = Executors.newSingleThreadScheduledExecutor();
        var server = new TcpControlServer(tls, new ControlServerOptions(InetAddress.getLoopbackAddress(), port, 8,
                Duration.ofSeconds(5), Duration.ofSeconds(30)), sessions, connectionWorkers, heartbeatMonitor);
        var remote = new AdminRemoteControlService(sessions, server, scheduler);
        var chat = new AdminChatService(sessions, server);
        var admin = new AdminControlRuntime(server, sessions, server, AdminScreenShareMessageHandler.NO_OP,
                new AdminFeatureMessages(remote, chat));
        Thread listener = new Thread(() -> { try { admin.run(); } catch (Exception error) { throw new RuntimeException(error); } });
        listener.setDaemon(true); listener.start(); await(admin::isListening, Duration.ofSeconds(5));

        var inventory = new NetworkInventoryPort() {
            @Override public List<NetworkInterfaceInfo> interfaces() {
                return List.of(new NetworkInterfaceInfo("test0", "integration", "192.168.66.2", "AA-BB-CC-DD-EE-01",
                        "192.168.66.1", "192.168.66.0/30", false));
            }
            @Override public Map<String, String> arpEntries() { return Map.of(); }
        };
        PingPort ping = new PingPort() {
            @Override public PingResult ping(String host, int timeoutMs, int retries) { return new PingResult(host, false, 0, "timeout", retries, 0, null, List.of()); }
            @Override public PingResult pingSeries(String host, int timeoutMs, int count) { return ping(host, timeoutMs, count); }
        };
        ExecutorService scanWorkers = Executors.newFixedThreadPool(4), commandWorkers = Executors.newVirtualThreadPerTaskExecutor();
        ScheduledExecutorService heartbeat = Executors.newSingleThreadScheduledExecutor(), publisher = Executors.newSingleThreadScheduledExecutor();
        NetworkScanner scanner = new NetworkScanner(ping, inventory, scanWorkers);
        var supervisor = new ClientConnectionSupervisor(() -> new ClientControlConnection("127.0.0.1", port, fingerprint,
                clientId, "Realtime Client", List.of(), 10_000), new JvmSystemStatsSampler(), heartbeat);
        var view = new FakeControlView(); var input = new FakeInput();
        var clientControl = new ClientRemoteControlService(clientId, view, input, Runnable::run);
        CountDownLatch clientChatReceived = new CountDownLatch(1);
        var clientChat = new ClientChatService(clientId, "Realtime Client", message -> clientChatReceived.countDown());
        var featureMessages = new ClientFeatureMessages(clientControl, clientChat);
        var client = new ClientAgentRuntime(clientId, "Realtime Client", inventory, ping, scanner,
                commandWorkers, publisher, supervisor, ClientScreenShareMessageHandler.NO_OP, featureMessages);
        String secondClientId = "realtime-client-2";
        var supervisor2 = new ClientConnectionSupervisor(() -> new ClientControlConnection("127.0.0.1", port, fingerprint,
                secondClientId, "Second Client", List.of(), 10_000), new JvmSystemStatsSampler(), heartbeat);
        var view2 = new FakeControlView(); var input2 = new FakeInput();
        var control2 = new ClientRemoteControlService(secondClientId, view2, input2, Runnable::run);
        CountDownLatch secondChatReceived = new CountDownLatch(1);
        var chat2 = new ClientChatService(secondClientId, "Second Client", message -> secondChatReceived.countDown());
        var client2 = new ClientAgentRuntime(secondClientId, "Second Client", inventory, ping, scanner,
                commandWorkers, publisher, supervisor2, ClientScreenShareMessageHandler.NO_OP,
                new ClientFeatureMessages(control2, chat2));
        try {
            client.start(); client2.start();
            await(() -> admin.clients().size() == 2 && admin.clients().stream().allMatch(AdminClientSnapshot::online), Duration.ofSeconds(15));
            AdminClientSnapshot snapshot = admin.clients().stream().filter(item -> item.clientId().equals(clientId)).findFirst().orElseThrow();
            AdminClientSnapshot snapshot2 = admin.clients().stream().filter(item -> item.clientId().equals(secondClientId)).findFirst().orElseThrow();
            assertEquals(clientId, snapshot.clientId());

            remote.requestControl(clientId, snapshot.sessionId());
            await(() -> remote.state(snapshot.sessionId()).status().equals("Granted"), Duration.ofSeconds(5));
            remote.sendInput(snapshot.sessionId(), new ControlInputEvent(ControlInputEvent.Kind.MOVE, .4, .6, 0, 0, 0, 0));
            assertTrue(input.applied.await(3, TimeUnit.SECONDS));
            assertEquals(0, input2.appliedCount.get(), "Control input must remain isolated to the consented Client session.");
            remote.stopControl(snapshot.sessionId());
            await(() -> !view.controlled.get(), Duration.ofSeconds(3));

            view.accept.set(false);
            remote.requestControl(clientId, snapshot.sessionId());
            await(() -> view.requests.get() >= 2, Duration.ofSeconds(3));
            assertNotEquals("Granted", remote.state(snapshot.sessionId()).status());
            int appliedBefore = input.appliedCount.get();
            try { remote.sendInput(snapshot.sessionId(), new ControlInputEvent(ControlInputEvent.Kind.MOVE, .5, .5, 0, 0, 0, 0)); }
            catch (java.io.IOException expected) { }
            assertEquals(appliedBefore, input.appliedCount.get(), "Input must not run after a rejected consent.");

            chat.send(clientId, snapshot.sessionId(), "Admin hello");
            assertTrue(clientChatReceived.await(3, TimeUnit.SECONDS));
            assertEquals(1, secondChatReceived.getCount(), "Chat must not leak to another connected Client.");
            clientChat.send("Client reply");
            await(() -> chat.messages(snapshot.sessionId()).stream().anyMatch(message -> message.text().equals("Client reply")),
                    Duration.ofSeconds(3));
            assertEquals("Admin hello", chat.messages(snapshot.sessionId()).getFirst().text());
            assertTrue(chat.messages(snapshot2.sessionId()).isEmpty());
            assertThrows(java.io.IOException.class, () -> chat.send("other-client", snapshot.sessionId(), "wrong target"));

            remote.stopControl(snapshot.sessionId());
            await(() -> admin.clients().getFirst().online(), Duration.ofSeconds(1));
        } finally {
            client.close(); client2.close(); remote.close(); admin.close(); scheduler.shutdownNow(); heartbeat.shutdownNow(); publisher.shutdownNow();
            commandWorkers.shutdownNow(); scanWorkers.shutdownNow(); connectionWorkers.shutdownNow(); heartbeatMonitor.shutdownNow(); sessions.close();
        }
    }

    private static final class FakeControlView implements RemoteControlClientView {
        final AtomicBoolean accept = new AtomicBoolean(true), controlled = new AtomicBoolean();
        final AtomicInteger requests = new AtomicInteger();
        @Override public CompletionStage<Boolean> requestControlConsent(String admin) { requests.incrementAndGet(); return CompletableFuture.completedFuture(accept.get()); }
        @Override public void controlStarted(Runnable revoke) { controlled.set(true); }
        @Override public void controlStopped(String reason) { controlled.set(false); }
    }
    private static final class FakeInput implements RemoteInputPort {
        final CountDownLatch applied = new CountDownLatch(1); final AtomicInteger appliedCount = new AtomicInteger();
        @Override public boolean available() { return true; }
        @Override public void apply(ControlInputEvent event) { appliedCount.incrementAndGet(); applied.countDown(); }
        @Override public void releaseAll() { }
    }
    private static void createIdentity(Path store, Path certificate) throws Exception {
        Path keytool = Path.of(System.getProperty("java.home"), "bin", System.getProperty("os.name").toLowerCase().contains("win") ? "keytool.exe" : "keytool");
        runKeytool(keytool, "-genkeypair", "-alias", "realtime", "-keyalg", "RSA", "-keysize", "2048", "-validity", "2",
                "-dname", "CN=NetworkAdmin Realtime Test", "-storetype", "PKCS12", "-keystore", store.toString(), "-storepass", PASSWORD, "-keypass", PASSWORD);
        runKeytool(keytool, "-exportcert", "-alias", "realtime", "-keystore", store.toString(), "-storepass", PASSWORD, "-file", certificate.toString());
    }
    private static void runKeytool(Path executable, String... args) throws Exception {
        List<String> command = new ArrayList<>(); command.add(executable.toString()); command.addAll(List.of(args));
        Process process = new ProcessBuilder(command).redirectErrorStream(true).start();
        String output = new String(process.getInputStream().readAllBytes(), java.nio.charset.StandardCharsets.UTF_8);
        assertEquals(0, process.waitFor(), output);
    }
    private static String fingerprint(Path certificate) throws Exception {
        try (var stream = Files.newInputStream(certificate)) {
            X509Certificate cert = (X509Certificate) CertificateFactory.getInstance("X.509").generateCertificate(stream);
            return HexFormat.of().withUpperCase().formatHex(MessageDigest.getInstance("SHA-256").digest(cert.getEncoded()));
        }
    }
    private static int freePort() throws Exception { try (ServerSocket socket = new ServerSocket(0)) { return socket.getLocalPort(); } }
    private static void await(BooleanSupplier condition, Duration timeout) throws Exception {
        long deadline = System.nanoTime() + timeout.toNanos();
        while (System.nanoTime() < deadline) { if (condition.getAsBoolean()) return; Thread.sleep(20); }
        assertTrue(condition.getAsBoolean(), "Condition was not met within " + timeout);
    }
}
