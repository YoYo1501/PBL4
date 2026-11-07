package vn.networkadmin.transport.infrastructure;

import vn.networkadmin.transport.ports.SystemStatsSampler;

import java.lang.management.ManagementFactory;
import java.util.Map;

/** Samples system CPU and physical RAM using the JDK management bean, with safe bounded values. */
public final class JvmSystemStatsSampler implements SystemStatsSampler {
    private final com.sun.management.OperatingSystemMXBean bean;
    public JvmSystemStatsSampler() {
        var osBean = ManagementFactory.getOperatingSystemMXBean();
        this.bean = osBean instanceof com.sun.management.OperatingSystemMXBean extended ? extended : null;
    }
    @Override public Map<String, Object> sample() {
        double cpu = bean == null ? 0 : bean.getCpuLoad() * 100.0;
        if (!Double.isFinite(cpu) || cpu < 0) cpu = 0;
        cpu = Math.min(cpu, 100);
        long total = bean == null ? 0 : bean.getTotalMemorySize();
        long free = bean == null ? 0 : bean.getFreeMemorySize();
        double ram = total <= 0 ? 0 : Math.max(0, Math.min(100, (total - free) * 100.0 / total));
        return Map.of("cpuUsagePercent", cpu, "ramUsagePercent", ram);
    }
}
