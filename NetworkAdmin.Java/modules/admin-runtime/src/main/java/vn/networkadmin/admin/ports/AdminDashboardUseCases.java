package vn.networkadmin.admin.ports;

import vn.networkadmin.admin.runtime.AdminClientSnapshot;

import java.io.IOException;
import java.util.List;
import java.util.Map;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.TimeoutException;

/** Small query/command surface required by the Admin presentation. */
public interface AdminDashboardUseCases extends AutoCloseable {
    boolean isListening();
    List<AdminClientSnapshot> clients();
    AdminClientSnapshot client(String sessionId);
    Map<String, Object> ping(String sessionId, String target, long timeout, TimeUnit unit)
            throws IOException, InterruptedException, TimeoutException;
    Map<String, Object> scan(String sessionId, String subnet, int timeoutMs, long timeout, TimeUnit unit)
            throws IOException, InterruptedException, TimeoutException;
    default void cancelScan(String sessionId) throws IOException { }
    default String remoteAdministration(String sessionId, int command, int countdownSeconds) throws IOException {
        throw new UnsupportedOperationException("Remote administration commands are unavailable.");
    }
    default Map<String, Object> remoteAdministrationResult(String sessionId) { return Map.of(); }
    @Override void close();
}
