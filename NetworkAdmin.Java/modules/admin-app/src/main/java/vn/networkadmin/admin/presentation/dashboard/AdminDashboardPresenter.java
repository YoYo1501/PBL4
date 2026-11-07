package vn.networkadmin.admin.presentation.dashboard;

import vn.networkadmin.admin.ports.AdminDashboardUseCases;
import vn.networkadmin.admin.runtime.AdminClientSnapshot;

import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.Executor;
import java.util.concurrent.TimeUnit;

/** Presentation coordinator; executes dashboard use cases off the Swing event thread. */
public final class AdminDashboardPresenter implements AutoCloseable {
    private final AdminDashboardUseCases useCases;
    private final Executor backgroundExecutor;

    public AdminDashboardPresenter(AdminDashboardUseCases useCases, Executor backgroundExecutor) {
        this.useCases = Objects.requireNonNull(useCases);
        this.backgroundExecutor = Objects.requireNonNull(backgroundExecutor);
    }

    public CompletableFuture<DashboardState> refresh() {
        return CompletableFuture.supplyAsync(() -> new DashboardState(useCases.isListening(), useCases.clients()), backgroundExecutor);
    }

    public CompletableFuture<Map<String, Object>> ping(String sessionId, String target) {
        return CompletableFuture.supplyAsync(() -> {
            try { return useCases.ping(sessionId, target, 20, TimeUnit.SECONDS); }
            catch (Exception error) { throw new java.util.concurrent.CompletionException(error); }
        }, backgroundExecutor);
    }

    public CompletableFuture<Map<String, Object>> scan(String sessionId, String subnet) {
        return CompletableFuture.supplyAsync(() -> {
            try { return useCases.scan(sessionId, subnet, 800, 90, TimeUnit.SECONDS); }
            catch (Exception error) { throw new java.util.concurrent.CompletionException(error); }
        }, backgroundExecutor);
    }

    public CompletableFuture<Void> cancelScan(String sessionId) {
        return CompletableFuture.runAsync(() -> {
            try { useCases.cancelScan(sessionId); }
            catch (Exception error) { throw new java.util.concurrent.CompletionException(error); }
        }, backgroundExecutor);
    }

    @Override public void close() { useCases.close(); }

    public record DashboardState(boolean listening, List<AdminClientSnapshot> clients) {
        public DashboardState { clients = List.copyOf(clients); }
    }
}
