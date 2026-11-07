package vn.networkadmin.admin.runtime;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import vn.networkadmin.client.runtime.ClientAgentRuntime;
import vn.networkadmin.network.application.NetworkScanner;
import vn.networkadmin.network.domain.NetworkDevice;
import vn.networkadmin.network.domain.NetworkInterfaceInfo;
import vn.networkadmin.network.domain.PingResult;
import vn.networkadmin.network.ports.NetworkInventoryPort;
import vn.networkadmin.network.ports.PingPort;
import vn.networkadmin.transport.application.ControlServerOptions;
import vn.networkadmin.transport.application.ClientConnectionSupervisor;
import vn.networkadmin.transport.infrastructure.ClientControlConnection;
import vn.networkadmin.transport.infrastructure.ClientSessionRegistry;
import vn.networkadmin.transport.infrastructure.TcpControlServer;
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
import java.util.HexFormat;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;
import java.util.function.BooleanSupplier;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.CompletableFuture;

import static org.junit.jupiter.api.Assertions.*;

class AdminClientIntegrationTest {
    private static final String STORE_PASSWORD = "integration-test-password";

    @TempDir Path temporaryDirectory;

    @Test
    void tlsSessionPublishesTelemetryRunsPingAndScanAndReconnects() throws Exception {
        Path keyStore = temporaryDirectory.resolve("test-server.p12");
        Path certificateFile = temporaryDirectory.resolve("test-server.cer");
        createTestIdentity(keyStore, certificateFile);
        SSLContext tls = Pkcs12ServerTlsContext.load(keyStore, STORE_PASSWORD.toCharArray());
        String fingerprint = certificateFingerprint(certificateFile);
        int port = availablePort();
        CountDownLatch cancellationProbeStarted = new CountDownLatch(1);

        NetworkInventoryPort inventory = new NetworkInventoryPort() {
            @Override public List<NetworkInterfaceInfo> interfaces() {
                return List.of(new NetworkInterfaceInfo("test0", "test interface", "192.168.55.10",
                        "AA-BB-CC-DD-EE-10", "192.168.55.1", "192.168.55.0/30", false),
                        new NetworkInterfaceInfo("test1", "managed test interface", "10.9.0.10",
                                "AA-BB-CC-DD-EE-11", "10.9.0.1", "10.9.0.0/23", false));
            }
            @Override public Map<String, String> arpEntries() { return Map.of(); }
        };
        PingPort ping = new PingPort() {
            @Override public PingResult ping(String host, int timeoutMs, int retryCount) {
                if (host.startsWith("10.9.")) {
                    cancellationProbeStarted.countDown();
                    try { new CountDownLatch(1).await(); }
                    catch (InterruptedException error) { Thread.currentThread().interrupt(); }
                }
                return new PingResult(host, true, 3, "Success", 1, 1, 3.0,
                        List.of(new PingResult.Attempt(1, true, 3L, "Success")));
            }
            @Override public PingResult pingSeries(String host, int timeoutMs, int count) {
                return new PingResult(host, true, 3, count + "/" + count + " replies", count, count, 3.0,
                        java.util.stream.IntStream.rangeClosed(1, count)
                                .mapToObj(i -> new PingResult.Attempt(i, true, 3L, "Success")).toList());
            }
        };

        ExecutorService scanWorkers = Executors.newFixedThreadPool(4);
        ExecutorService commandWorkers = Executors.newVirtualThreadPerTaskExecutor();
        ScheduledExecutorService heartbeatScheduler = Executors.newSingleThreadScheduledExecutor();
        ScheduledExecutorService publisherScheduler = Executors.newSingleThreadScheduledExecutor();
        NetworkScanner scanner = new NetworkScanner(ping, inventory, scanWorkers);
        String clientId = "integration-client-id";
        String clientName = "Integration Client";
        var supervisor = new ClientConnectionSupervisor(
                () -> new ClientControlConnection("127.0.0.1", port, fingerprint, clientId, clientName, List.of(), 10_000),
                new vn.networkadmin.transport.infrastructure.JvmSystemStatsSampler(), heartbeatScheduler);
        ClientAgentRuntime client = new ClientAgentRuntime(clientId, clientName, inventory, ping, scanner,
                commandWorkers, publisherScheduler, supervisor);
        AdminControlRuntime firstAdmin = null;
        AdminControlRuntime secondAdmin = null;
        try {
            firstAdmin = startAdmin(tls, port);
            client.start();
            AdminControlRuntime connectedAdmin = firstAdmin;
            await(() -> connectedAdmin.clients().size() == 1
                    && !connectedAdmin.clients().getFirst().networkInfo().isEmpty()
                    && !connectedAdmin.clients().getFirst().systemStats().isEmpty(), Duration.ofSeconds(15));
            assertTrue(connectedAdmin.clients().getFirst().networkInfo().containsKey("osName"));

            AdminClientSnapshot firstSession = firstAdmin.clients().getFirst();
            Map<String, Object> pingResponse = firstAdmin.ping(firstSession.sessionId(), "127.0.0.1", 10, TimeUnit.SECONDS);
            assertEquals("127.0.0.1", pingResponse.get("host"));
            assertEquals(Boolean.TRUE, pingResponse.get("success"));

            Map<String, Object> scanResponse = firstAdmin.scan(firstSession.sessionId(), "192.168.55.0/30",
                    300, 10, TimeUnit.SECONDS);
            assertEquals(Boolean.TRUE, scanResponse.get("success"));
            assertEquals("192.168.55.0/30", scanResponse.get("subnet"));

            Map<String, Object> unauthorizedScan = firstAdmin.scan(firstSession.sessionId(), "8.8.8.0/24",
                    300, 10, TimeUnit.SECONDS);
            assertEquals(Boolean.FALSE, unauthorizedScan.get("success"));
            assertTrue(unauthorizedScan.get("errorMessage").toString().contains("inside a non-virtual"));

            AdminControlRuntime scanAdmin = firstAdmin;
            CompletableFuture<Map<String, Object>> cancelable = CompletableFuture.supplyAsync(() -> {
                try { return scanAdmin.scan(firstSession.sessionId(), "10.9.0.0/23", 300, 20, TimeUnit.SECONDS); }
                catch (Exception error) { throw new java.util.concurrent.CompletionException(error); }
            });
            assertTrue(cancellationProbeStarted.await(10, TimeUnit.SECONDS), "Client scan should reach an authorized local range.");
            firstAdmin.cancelScan(firstSession.sessionId());
            Map<String, Object> canceledScan = cancelable.get(10, TimeUnit.SECONDS);
            assertEquals(Boolean.FALSE, canceledScan.get("success"));
            assertEquals("Scan canceled.", canceledScan.get("errorMessage"));

            AdminControlRuntime disconnectedAdmin = firstAdmin;
            disconnectedAdmin.close();
            await(() -> disconnectedAdmin.clients().size() == 1 && !disconnectedAdmin.clients().getFirst().online(), Duration.ofSeconds(5));
            secondAdmin = startAdmin(tls, port);
            AdminControlRuntime reconnectedAdmin = secondAdmin;
            await(() -> reconnectedAdmin.clients().size() == 1, Duration.ofSeconds(15));
            assertNotEquals(firstSession.sessionId(), secondAdmin.clients().getFirst().sessionId(),
                    "A reconnected Client must receive a fresh session identifier.");
        } finally {
            client.close();
            if (firstAdmin != null) firstAdmin.close();
            if (secondAdmin != null) secondAdmin.close();
            scanWorkers.shutdownNow();
            commandWorkers.shutdownNow();
            heartbeatScheduler.shutdownNow();
            publisherScheduler.shutdownNow();
        }
    }

    private static AdminControlRuntime startAdmin(SSLContext tls, int port) throws Exception {
        var sessions = new ClientSessionRegistry();
        var connectionWorkers = Executors.newVirtualThreadPerTaskExecutor();
        var heartbeatMonitor = Executors.newSingleThreadScheduledExecutor(task -> {
            Thread thread = new Thread(task, "integration-heartbeat-monitor"); thread.setDaemon(true); return thread;
        });
        var server = new TcpControlServer(tls,
                new ControlServerOptions(InetAddress.getLoopbackAddress(), port, 32,
                        Duration.ofSeconds(5), Duration.ofSeconds(30)), sessions, connectionWorkers, heartbeatMonitor);
        AdminControlRuntime admin = new AdminControlRuntime(server, sessions, server);
        Thread listener = new Thread(() -> {
            try { admin.run(); }
            catch (Exception error) { throw new RuntimeException("Admin listener failed", error); }
        }, "integration-admin-listener");
        listener.setDaemon(true);
        listener.start();
        await(admin::isListening, Duration.ofSeconds(5));
        return admin;
    }

    private void createTestIdentity(Path keyStore, Path certificateFile) throws Exception {
        Path bin = Path.of(System.getProperty("java.home"), "bin");
        Path keytool = bin.resolve(isWindows() ? "keytool.exe" : "keytool");
        assertTrue(Files.isExecutable(keytool), "JDK keytool is required for the TLS integration test.");
        runKeytool(keytool, "-genkeypair", "-alias", "test-server", "-keyalg", "RSA", "-keysize", "2048",
                "-validity", "2", "-dname", "CN=NetworkAdmin Test", "-storetype", "PKCS12",
                "-keystore", keyStore.toString(), "-storepass", STORE_PASSWORD, "-keypass", STORE_PASSWORD);
        runKeytool(keytool, "-exportcert", "-alias", "test-server", "-keystore", keyStore.toString(),
                "-storepass", STORE_PASSWORD, "-file", certificateFile.toString());
    }

    private static void runKeytool(Path executable, String... arguments) throws Exception {
        var command = new java.util.ArrayList<String>();
        command.add(executable.toString()); command.addAll(List.of(arguments));
        Process process = new ProcessBuilder(command).redirectErrorStream(true).start();
        String output = new String(process.getInputStream().readAllBytes(), java.nio.charset.StandardCharsets.UTF_8);
        assertEquals(0, process.waitFor(), "keytool failed: " + output);
    }

    private static String certificateFingerprint(Path certificateFile) throws Exception {
        try (var input = Files.newInputStream(certificateFile)) {
            X509Certificate certificate = (X509Certificate) CertificateFactory.getInstance("X.509").generateCertificate(input);
            return HexFormat.of().withUpperCase().formatHex(MessageDigest.getInstance("SHA-256").digest(certificate.getEncoded()));
        }
    }

    private static int availablePort() throws Exception {
        try (ServerSocket socket = new ServerSocket(0, 1, InetAddress.getLoopbackAddress())) { return socket.getLocalPort(); }
    }

    private static void await(BooleanSupplier condition, Duration timeout) throws Exception {
        long deadline = System.nanoTime() + timeout.toNanos();
        while (System.nanoTime() < deadline) {
            if (condition.getAsBoolean()) return;
            Thread.sleep(25);
        }
        assertTrue(condition.getAsBoolean(), "Condition was not met within " + timeout);
    }

    private static boolean isWindows() { return System.getProperty("os.name").toLowerCase().contains("win"); }
}
