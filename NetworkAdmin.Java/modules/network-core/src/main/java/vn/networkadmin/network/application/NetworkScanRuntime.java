package vn.networkadmin.network.application;

import vn.networkadmin.network.infrastructure.JavaPingAdapter;
import vn.networkadmin.network.infrastructure.SystemNetworkInventoryAdapter;
import vn.networkadmin.network.ports.NetworkInventoryPort;
import vn.networkadmin.network.ports.PingPort;

import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/** Small composition root for the standalone network feature; desktop apps can inject their own ports. */
public final class NetworkScanRuntime implements AutoCloseable {
    private final ExecutorService workers;
    private final NetworkScanner scanner;

    public NetworkScanRuntime() { this(new JavaPingAdapter(), new SystemNetworkInventoryAdapter()); }

    public NetworkScanRuntime(PingPort ping, NetworkInventoryPort inventory) {
        this.workers = Executors.newFixedThreadPool(32, r -> {
            Thread t = new Thread(r, "network-scan-worker"); t.setDaemon(true); return t;
        });
        this.scanner = new NetworkScanner(ping, inventory, workers);
    }

    public NetworkScanner scanner() { return scanner; }
    @Override public void close() { workers.shutdownNow(); }
}
