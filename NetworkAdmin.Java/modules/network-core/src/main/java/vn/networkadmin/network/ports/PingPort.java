package vn.networkadmin.network.ports;

import vn.networkadmin.network.domain.PingResult;

public interface PingPort {
    PingResult ping(String host, int timeoutMs, int retryCount) throws InterruptedException;
    PingResult pingSeries(String host, int timeoutMs, int count) throws InterruptedException;
}
