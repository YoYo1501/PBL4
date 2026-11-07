package vn.networkadmin.network.domain;

import java.util.List;

public record PingResult(String host, boolean success, long roundtripTimeMs, String statusMessage,
                         int sent, int received, Double averageResponseTimeMs, List<Attempt> attempts) {
    public PingResult { attempts = List.copyOf(attempts); }
    public int lost() { return sent - received; }
    public double packetLossPercent() { return sent == 0 ? 0 : lost() * 100.0 / sent; }
    public record Attempt(int number, boolean success, Long responseTimeMs, String status) { }
}
