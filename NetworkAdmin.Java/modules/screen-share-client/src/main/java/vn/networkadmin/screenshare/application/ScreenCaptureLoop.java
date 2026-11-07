package vn.networkadmin.screenshare.application;

import java.util.Objects;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.ScheduledFuture;
import java.util.concurrent.TimeUnit;
import java.util.function.Consumer;
import vn.networkadmin.screenshare.ports.LatestFrameStore;
import vn.networkadmin.screenshare.ports.ScreenCapturePort;

/** Captures independently from the sender and keeps the latest complete frame available. */
public final class ScreenCaptureLoop implements AutoCloseable {
    private final ScreenCapturePort capturePort;
    private final LatestFrameStore latestFrameStore;
    private final ScheduledExecutorService scheduler;
    private final Consumer<RuntimeException> errorHandler;
    private final long intervalNanos;
    private ScheduledFuture<?> captureTask;
    private boolean closed;

    public ScreenCaptureLoop(ScreenCapturePort capturePort, LatestFrameStore latestFrameStore,
                             ScheduledExecutorService scheduler, int framesPerSecond,
                             Consumer<RuntimeException> errorHandler) {
        this.capturePort = Objects.requireNonNull(capturePort, "capturePort");
        this.latestFrameStore = Objects.requireNonNull(latestFrameStore, "latestFrameStore");
        this.scheduler = Objects.requireNonNull(scheduler, "scheduler");
        this.errorHandler = Objects.requireNonNull(errorHandler, "errorHandler");
        if (framesPerSecond < 1 || framesPerSecond > 60) {
            throw new IllegalArgumentException("Capture FPS must be in the range 1 to 60.");
        }
        this.intervalNanos = TimeUnit.SECONDS.toNanos(1) / framesPerSecond;
    }

    public synchronized void start() {
        if (closed) throw new IllegalStateException("Capture loop has already been closed.");
        if (captureTask != null && !captureTask.isCancelled()) return;
        captureTask = scheduler.scheduleAtFixedRate(this::captureLatest, 0, intervalNanos,
                TimeUnit.NANOSECONDS);
    }

    private void captureLatest() {
        try {
            latestFrameStore.publish(capturePort.capture());
        } catch (RuntimeException failure) {
            try { errorHandler.accept(failure); }
            catch (RuntimeException ignored) { /* A logging failure must not stop future captures. */ }
        }
    }

    @Override
    public synchronized void close() {
        if (closed) return;
        closed = true;
        if (captureTask != null) captureTask.cancel(true);
    }
}
