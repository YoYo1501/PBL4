package vn.networkadmin.network.infrastructure;

import vn.networkadmin.network.domain.PingResult;
import vn.networkadmin.network.ports.PingPort;

import java.net.InetAddress;
import java.util.ArrayList;
import java.util.List;

/** JDK-only reachability adapter. Windows uses the native ICMP implementation when permitted. */
public final class JavaPingAdapter implements PingPort {
    @Override public PingResult ping(String host, int timeoutMs, int retryCount) throws InterruptedException {
        int attempts = Math.max(1, retryCount);
        PingResult last = new PingResult(host, false, 0, "Not started", 0, 0, null, List.of());
        for (int i = 0; i < attempts; i++) {
            long start = System.nanoTime();
            try {
                boolean ok = InetAddress.getByName(host).isReachable(Math.max(1, timeoutMs));
                long elapsed = ok ? Math.max(0, (System.nanoTime() - start) / 1_000_000) : 0;
                last = new PingResult(host, ok, elapsed, ok ? "Success" : "TimedOut", 1, ok ? 1 : 0,
                        ok ? (double) elapsed : null, List.of());
                if (ok) return last;
            } catch (Exception ex) {
                last = new PingResult(host, false, 0, ex.getClass().getSimpleName() + ": " + ex.getMessage(), 1, 0, null, List.of());
            }
        }
        return last;
    }

    @Override public PingResult pingSeries(String host, int timeoutMs, int count) throws InterruptedException {
        int total = Math.max(1, count);
        List<PingResult.Attempt> attempts = new ArrayList<>(total);
        long sum = 0;
        int received = 0;
        String status = "TimedOut";
        for (int i = 1; i <= total; i++) {
            PingResult result = ping(host, timeoutMs, 1);
            status = result.statusMessage();
            Long time = result.success() ? result.roundtripTimeMs() : null;
            attempts.add(new PingResult.Attempt(i, result.success(), time, status));
            if (result.success()) { received++; sum += result.roundtripTimeMs(); }
        }
        Double average = received == 0 ? null : sum / (double) received;
        return new PingResult(host, received > 0, average == null ? 0 : average.longValue(),
                received + "/" + total + " replies; " + String.format(java.util.Locale.ROOT, "%.1f", (total - received) * 100.0 / total) + "% packet loss",
                total, received, average, attempts);
    }
}
