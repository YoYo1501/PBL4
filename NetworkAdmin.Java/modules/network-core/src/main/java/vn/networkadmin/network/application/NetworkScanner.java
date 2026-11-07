package vn.networkadmin.network.application;

import vn.networkadmin.network.domain.NetworkDevice;
import vn.networkadmin.network.domain.PingResult;
import vn.networkadmin.network.domain.ScanSubnet;
import vn.networkadmin.network.ports.NetworkInventoryPort;
import vn.networkadmin.network.ports.PingPort;

import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.function.Consumer;

/** Bounded LAN discovery use case with progress reporting and cooperative cancellation. */
public final class NetworkScanner {
    private static final int MAX_CONCURRENT_PINGS = 32;
    private final PingPort ping;
    private final NetworkInventoryPort inventory;
    private final ExecutorService workers;

    public NetworkScanner(PingPort ping, NetworkInventoryPort inventory, ExecutorService workers) {
        this.ping = Objects.requireNonNull(ping); this.inventory = Objects.requireNonNull(inventory);
        this.workers = Objects.requireNonNull(workers);
    }

    public List<NetworkDevice> scan(String subnetText, int timeoutMs, Consumer<Progress> progress) throws InterruptedException {
        return scan(subnetText, timeoutMs, progress, new ScanCancellation());
    }

    public List<NetworkDevice> scan(String subnetText, int timeoutMs, Consumer<Progress> progress,
                                    ScanCancellation cancellation) throws InterruptedException {
        ScanSubnet subnet = ScanSubnet.parse(subnetText).orElseThrow(() -> new IllegalArgumentException(ScanSubnet.HELP));
        List<String> hosts = subnet.hosts();
        Objects.requireNonNull(cancellation).throwIfCancelled();
        runPass(hosts, 250, 1, null, cancellation); // warm local neighbor cache
        Map<String, PingResult> replies = runPass(hosts, ScanSubnet.clampTimeout(timeoutMs), 2, progress, cancellation);
        Map<String, String> arp = inventory.arpEntries();
        List<NetworkDevice> found = new ArrayList<>();
        for (String host : hosts) {
            cancellation.throwIfCancelled();
            PingResult result = replies.get(host); String mac = arp.getOrDefault(host, "");
            if ((result != null && result.success()) || !mac.isBlank())
                found.add(new NetworkDevice(host, mac, true, result != null && result.success() ? result.roundtripTimeMs() : null));
        }
        found.sort(Comparator.comparingLong(device -> ScanSubnet.numericAddress(device.ipAddress())));
        return List.copyOf(found);
    }

    private Map<String, PingResult> runPass(List<String> hosts, int timeout, int retries,
                                             Consumer<Progress> progress, ScanCancellation cancellation)
            throws InterruptedException {
        ExecutorCompletionService<Map.Entry<String, PingResult>> completion = new ExecutorCompletionService<>(workers);
        List<Future<Map.Entry<String, PingResult>>> tasks = new ArrayList<>(hosts.size());
        ConcurrentMap<String, PingResult> results = new ConcurrentHashMap<>();
        AtomicInteger scanned = new AtomicInteger(), online = new AtomicInteger();
        Semaphore slots = new Semaphore(Math.min(MAX_CONCURRENT_PINGS, Math.max(1, hosts.size())));
        for (String host : hosts) tasks.add(completion.submit(() -> {
            cancellation.throwIfCancelled();
            boolean acquired = false;
            try {
                slots.acquire(); acquired = true; cancellation.throwIfCancelled();
                return Map.entry(host, ping.ping(host, timeout, retries));
            } finally { if (acquired) slots.release(); }
        }));
        try {
            for (int done = 0; done < tasks.size();) {
                if (Thread.currentThread().isInterrupted()) throw new InterruptedException("Scan interrupted.");
                cancellation.throwIfCancelled();
                Future<Map.Entry<String, PingResult>> completed = completion.poll(100, TimeUnit.MILLISECONDS);
                if (completed == null) continue;
                Map.Entry<String, PingResult> entry;
                try { entry = completed.get(); }
                catch (ExecutionException error) {
                    Throwable cause = error.getCause();
                    if (cause instanceof InterruptedException) throw new InterruptedException("Scan interrupted.");
                    if (cause instanceof CancellationException canceled) throw canceled;
                    if (cause instanceof RuntimeException runtime) throw runtime;
                    throw new IllegalStateException("Network scan failed.", cause);
                }
                results.put(entry.getKey(), entry.getValue());
                if (entry.getValue().success()) online.incrementAndGet();
                int scannedCount = scanned.incrementAndGet();
                if (progress != null) progress.accept(new Progress(scannedCount, hosts.size(), online.get(), entry.getKey()));
                done++;
            }
        } catch (InterruptedException | RuntimeException error) {
            cancellation.cancel(); tasks.forEach(task -> task.cancel(true));
            if (error instanceof InterruptedException) Thread.currentThread().interrupt();
            throw error;
        }
        return results;
    }

    public record Progress(int scannedHosts, int totalHosts, int onlineHosts, String currentIpAddress) { }
}
