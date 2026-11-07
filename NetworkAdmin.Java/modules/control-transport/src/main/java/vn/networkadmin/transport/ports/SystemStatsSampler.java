package vn.networkadmin.transport.ports;

import java.util.Map;

@FunctionalInterface
public interface SystemStatsSampler {
    Map<String, Object> sample();
}
