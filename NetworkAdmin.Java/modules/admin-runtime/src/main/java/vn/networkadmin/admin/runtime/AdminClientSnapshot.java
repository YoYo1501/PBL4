package vn.networkadmin.admin.runtime;

import java.time.Instant;
import java.util.Map;

public record AdminClientSnapshot(String clientId, String clientName, String sessionId, String remoteAddress,
                                  Instant connectedAt, Instant lastSeen, Map<String, Object> systemStats,
                                  Map<String, Object> networkInfo, Map<String, Object> lastScan, boolean online) {
    public AdminClientSnapshot(String clientId, String clientName, String sessionId, String remoteAddress,
                               Instant connectedAt, Instant lastSeen, Map<String, Object> systemStats,
                               Map<String, Object> networkInfo, Map<String, Object> lastScan) {
        this(clientId, clientName, sessionId, remoteAddress, connectedAt, lastSeen, systemStats, networkInfo, lastScan, true);
    }
}
