package vn.networkadmin.transport.infrastructure;

import org.junit.jupiter.api.Test;
import vn.networkadmin.transport.ports.SystemStatsSampler;

import java.util.Map;

import static org.junit.jupiter.api.Assertions.*;

class JvmSystemStatsSamplerTest {
    @Test
    void reportsFiniteCpuAndRamPercentagesWithinBounds() {
        SystemStatsSampler sampler = new JvmSystemStatsSampler();
        Map<String, Object> sample = sampler.sample();

        assertTrue(sample.get("cpuUsagePercent") instanceof Number);
        assertTrue(sample.get("ramUsagePercent") instanceof Number);
        assertBounded(sample, "cpuUsagePercent");
        assertBounded(sample, "ramUsagePercent");
    }

    private static void assertBounded(Map<String, Object> sample, String key) {
        double value = ((Number) sample.get(key)).doubleValue();
        assertTrue(Double.isFinite(value), key + " must be finite");
        assertTrue(value >= 0 && value <= 100, key + " must be in [0, 100]");
    }
}
