package vn.networkadmin.network.application;

import org.junit.jupiter.api.Test;
import vn.networkadmin.network.domain.NetworkDevice;
import vn.networkadmin.network.domain.PingResult;
import vn.networkadmin.network.ports.NetworkInventoryPort;
import vn.networkadmin.network.ports.PingPort;

import java.util.List;
import java.util.Map;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.CancellationException;
import java.util.concurrent.atomic.AtomicInteger;

import static org.junit.jupiter.api.Assertions.*;

class NetworkScannerTest {
    @Test
    void scanCombinesPingAndArpResultsAndReportsProgress() throws Exception {
        AtomicInteger progressUpdates = new AtomicInteger();
        PingPort ping = new PingPort() {
            @Override public PingResult ping(String host, int timeoutMs, int retryCount) {
                boolean reachable = host.equals("192.168.44.1");
                return new PingResult(host, reachable, reachable ? 7 : 0,
                        reachable ? "Success" : "TimedOut", 1, reachable ? 1 : 0,
                        reachable ? 7.0 : null, List.of());
            }
            @Override public PingResult pingSeries(String host, int timeoutMs, int count) {
                return ping(host, timeoutMs, count);
            }
        };
        NetworkInventoryPort inventory = new NetworkInventoryPort() {
            @Override public List<vn.networkadmin.network.domain.NetworkInterfaceInfo> interfaces() { return List.of(); }
            @Override public Map<String, String> arpEntries() { return Map.of("192.168.44.2", "AA-BB-CC-DD-EE-02"); }
        };

        try (var workers = Executors.newFixedThreadPool(4)) {
            NetworkScanner scanner = new NetworkScanner(ping, inventory, workers);
            List<NetworkDevice> devices = scanner.scan("192.168.44.0/30", 500,
                    ignored -> progressUpdates.incrementAndGet());

            assertEquals(2, devices.size());
            assertEquals("192.168.44.1", devices.get(0).ipAddress());
            assertEquals(7L, devices.get(0).responseTimeMs());
            assertEquals("192.168.44.2", devices.get(1).ipAddress());
            assertEquals("AA-BB-CC-DD-EE-02", devices.get(1).macAddress());
            assertTrue(devices.get(1).online(), "An ARP entry should identify a live host even when ping is blocked.");
            assertEquals(2, progressUpdates.get(), "Progress is emitted for each host in the discovery pass.");
        }
    }

    @Test
    void rejectsSubnetOutsideScannerPolicyBeforeUsingPorts() {
        AtomicInteger pingCalls = new AtomicInteger();
        PingPort ping = new PingPort() {
            @Override public PingResult ping(String host, int timeoutMs, int retryCount) {
                pingCalls.incrementAndGet();
                return new PingResult(host, false, 0, "TimedOut", 1, 0, null, List.of());
            }
            @Override public PingResult pingSeries(String host, int timeoutMs, int count) { return ping(host, timeoutMs, count); }
        };
        NetworkInventoryPort inventory = new NetworkInventoryPort() {
            @Override public List<vn.networkadmin.network.domain.NetworkInterfaceInfo> interfaces() { return List.of(); }
            @Override public Map<String, String> arpEntries() { return Map.of(); }
        };
        try (var workers = Executors.newFixedThreadPool(2)) {
            NetworkScanner scanner = new NetworkScanner(ping, inventory, workers);
            assertThrows(IllegalArgumentException.class, () -> scanner.scan("192.168.44.0/31", 500, null));
            assertEquals(0, pingCalls.get());
        }
    }

    @Test void subnetContainmentAllowsOnlyMoreSpecificLocalRanges() {
        var local = vn.networkadmin.network.domain.ScanSubnet.parse("192.168.4.0/23").orElseThrow();
        assertTrue(local.contains(vn.networkadmin.network.domain.ScanSubnet.parse("192.168.5.0/24").orElseThrow()));
        assertFalse(local.contains(vn.networkadmin.network.domain.ScanSubnet.parse("192.168.6.0/24").orElseThrow()));
        assertFalse(local.contains(new vn.networkadmin.network.domain.ScanSubnet(0xc0a80400L, 22)));
    }

    @Test
    void cancellationInterruptsOutstandingProbesAndStopsProgress() throws Exception {
        CountDownLatch started = new CountDownLatch(1);
        PingPort ping = new PingPort() {
            @Override public PingResult ping(String host, int timeoutMs, int retryCount) throws InterruptedException {
                started.countDown(); new CountDownLatch(1).await();
                return new PingResult(host, false, 0, "Canceled", retryCount, 0, null, List.of());
            }
            @Override public PingResult pingSeries(String host, int timeoutMs, int count) throws InterruptedException {
                return ping(host, timeoutMs, count);
            }
        };
        NetworkInventoryPort inventory = new NetworkInventoryPort() {
            @Override public List<vn.networkadmin.network.domain.NetworkInterfaceInfo> interfaces() { return List.of(); }
            @Override public Map<String, String> arpEntries() { return Map.of(); }
        };
        try (var workers = Executors.newFixedThreadPool(4); var caller = Executors.newSingleThreadExecutor()) {
            NetworkScanner scanner = new NetworkScanner(ping, inventory, workers); ScanCancellation cancellation = new ScanCancellation();
            Future<?> scan = caller.submit(() -> {
                try { scanner.scan("192.168.44.0/24", 500, null, cancellation); fail("Expected scan cancellation."); }
                catch (InterruptedException error) { Thread.currentThread().interrupt(); }
                catch (CancellationException expected) { }
            });
            assertTrue(started.await(2, TimeUnit.SECONDS)); cancellation.cancel();
            assertTimeoutPreemptively(java.time.Duration.ofSeconds(2), () -> scan.get(1, TimeUnit.SECONDS));
        }
    }
}
