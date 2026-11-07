package vn.networkadmin.admin.presentation.dashboard;

import org.junit.jupiter.api.Test;
import vn.networkadmin.admin.ports.AdminDashboardUseCases;
import vn.networkadmin.admin.runtime.AdminClientSnapshot;

import java.time.Instant;
import java.util.List;
import java.util.Map;
import java.util.concurrent.TimeUnit;

import static org.junit.jupiter.api.Assertions.*;

class AdminDashboardPresenterTest {
    @Test
    void delegatesRefreshAndCommandsThroughTheDashboardPort() {
        FakeDashboardUseCases useCases = new FakeDashboardUseCases();
        try (AdminDashboardPresenter presenter = new AdminDashboardPresenter(useCases, Runnable::run)) {
            var state = presenter.refresh().join();
            assertTrue(state.listening());
            assertEquals("test-session", state.clients().getFirst().sessionId());
            assertEquals("127.0.0.1", presenter.ping("test-session", "127.0.0.1").join().get("target"));
            assertEquals("192.168.1.0/24", presenter.scan("test-session", "192.168.1.0/24").join().get("subnet"));
            assertEquals(1, useCases.pingCalls);
            assertEquals(1, useCases.scanCalls);
        }
        assertTrue(useCases.closed);
    }

    private static final class FakeDashboardUseCases implements AdminDashboardUseCases {
        private int pingCalls;
        private int scanCalls;
        private boolean closed;
        @Override public boolean isListening() { return true; }
        @Override public List<AdminClientSnapshot> clients() {
            return List.of(new AdminClientSnapshot("test-client", "Test Client", "test-session", "127.0.0.1",
                    Instant.EPOCH, Instant.EPOCH, Map.of(), Map.of(), Map.of()));
        }
        @Override public AdminClientSnapshot client(String sessionId) { return clients().getFirst(); }
        @Override public Map<String, Object> ping(String sessionId, String target, long timeout, TimeUnit unit) {
            pingCalls++; return Map.of("target", target);
        }
        @Override public Map<String, Object> scan(String sessionId, String subnet, int timeoutMs, long timeout, TimeUnit unit) {
            scanCalls++; return Map.of("subnet", subnet);
        }
        @Override public void close() { closed = true; }
    }
}
