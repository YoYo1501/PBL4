package vn.networkadmin.client.infrastructure.monitoring;

/**
 * Converts monotonically increasing network byte counters into Mbps samples.
 * The calculation mirrors the legacy C# client: the first sample is zero and
 * counter decreases are clamped to zero before the new counters become the
 * baseline for the next sample.
 */
public final class NetworkTrafficRateCalculator {
    private static final double BITS_PER_MEGABIT = 1_000_000.0;

    private long previousReceived;
    private long previousSent;
    private long previousNanos;
    private boolean initialized;

    public Rate sample(Long receivedBytes, Long sentBytes, long nowNanos) {
        if (receivedBytes == null || sentBytes == null || receivedBytes < 0 || sentBytes < 0) {
            return Rate.ZERO;
        }

        if (!initialized || nowNanos <= previousNanos) {
            updateBaseline(receivedBytes, sentBytes, nowNanos);
            return Rate.ZERO;
        }

        double seconds = Math.max(0.001, (nowNanos - previousNanos) / 1_000_000_000.0);
        double download = toMbps(Math.max(0L, receivedBytes - previousReceived), seconds);
        double upload = toMbps(Math.max(0L, sentBytes - previousSent), seconds);
        updateBaseline(receivedBytes, sentBytes, nowNanos);
        return new Rate(download, upload);
    }

    public void reset() {
        initialized = false;
        previousReceived = 0;
        previousSent = 0;
        previousNanos = 0;
    }

    private void updateBaseline(long receivedBytes, long sentBytes, long nowNanos) {
        previousReceived = receivedBytes;
        previousSent = sentBytes;
        previousNanos = nowNanos;
        initialized = true;
    }

    private static double toMbps(long byteDelta, double seconds) {
        return byteDelta * 8.0 / seconds / BITS_PER_MEGABIT;
    }

    public record Rate(double downloadMbps, double uploadMbps) {
        public static final Rate ZERO = new Rate(0, 0);
    }
}
