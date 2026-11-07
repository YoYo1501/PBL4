package vn.networkadmin.network.application;

import java.util.concurrent.atomic.AtomicBoolean;

/** Cooperative cancellation flag shared by a scan request and its host probes. */
public final class ScanCancellation {
    private final AtomicBoolean cancelled = new AtomicBoolean();
    public boolean cancel() { return cancelled.compareAndSet(false, true); }
    public boolean isCancelled() { return cancelled.get(); }
    public void throwIfCancelled() {
        if (cancelled.get()) throw new java.util.concurrent.CancellationException("Scan canceled.");
    }
}
