package vn.networkadmin.client.infrastructure.monitoring;

import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Deque;
import java.util.List;

/** Bounded traffic history used by the dashboard chart. */
public final class NetworkTrafficHistory {
    private final int capacity;
    private final Deque<Sample> samples = new ArrayDeque<>();

    public NetworkTrafficHistory(int capacity) {
        if (capacity <= 0) throw new IllegalArgumentException("capacity must be positive");
        this.capacity = capacity;
    }

    public void add(double downloadMbps, double uploadMbps) {
        samples.addLast(new Sample(safe(downloadMbps), safe(uploadMbps)));
        while (samples.size() > capacity) samples.removeFirst();
    }

    public List<Sample> snapshot() {
        return List.copyOf(new ArrayList<>(samples));
    }

    public int size() { return samples.size(); }

    private static double safe(double value) { return Double.isFinite(value) ? Math.max(0, value) : 0; }

    public record Sample(double downloadMbps, double uploadMbps) { }
}
