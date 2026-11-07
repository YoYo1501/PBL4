package vn.networkadmin.client.infrastructure.monitoring;

import com.sun.management.OperatingSystemMXBean;
import vn.networkadmin.network.domain.NetworkInterfaceInfo;
import vn.networkadmin.network.infrastructure.SystemNetworkInventoryAdapter;
import vn.networkadmin.transport.infrastructure.JvmSystemStatsSampler;

import java.awt.GraphicsEnvironment;
import java.awt.Toolkit;
import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStreamReader;
import java.lang.management.ManagementFactory;
import java.net.InetAddress;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.FileStore;
import java.time.Duration;
import java.time.Instant;
import java.util.Locale;
import java.util.Optional;
import java.util.concurrent.TimeUnit;

/**
 * Reads client-local telemetry without fabricating values. The sampler is intended to run
 * from a background executor; it never touches Swing components.
 */
public final class ClientTelemetrySampler {
    private final JvmSystemStatsSampler systemStats = new JvmSystemStatsSampler();
    private final OperatingSystemMXBean operatingSystem;
    private final SystemNetworkInventoryAdapter inventory = new SystemNetworkInventoryAdapter();
    private long previousReceived;
    private long previousSent;
    private long previousNetworkNanos;
    private boolean networkInitialized;
    private volatile String cachedCpuModel;
    private volatile String cachedScreenResolution;

    public ClientTelemetrySampler() {
        var bean = ManagementFactory.getOperatingSystemMXBean();
        operatingSystem = bean instanceof OperatingSystemMXBean extended ? extended : null;
    }

    public TelemetrySnapshot sample() {
        var stats = systemStats.sample();
        double cpu = number(stats.get("cpuUsagePercent"));
        double ram = number(stats.get("ramUsagePercent"));
        double disk = diskUsage();
        long totalMemory = operatingSystem == null ? 0 : Math.max(0, operatingSystem.getTotalMemorySize());
        long freeMemory = operatingSystem == null ? 0 : Math.max(0, operatingSystem.getFreeMemorySize());
        NetworkRate networkRate = networkRate();
        Optional<NetworkInterfaceInfo> primary = inventory.primaryInterface();
        return new TelemetrySnapshot(cpu, ram, disk, networkRate.downloadMbps(), networkRate.uploadMbps(),
                totalMemory, Math.max(0, totalMemory - freeMemory), machineName(), operatingSystemName(),
                cachedCpuModel(), cachedScreenResolution(), primary.map(NetworkInterfaceInfo::ipAddress).orElse("Unavailable"),
                primary.map(NetworkInterfaceInfo::macAddress).orElse("Unavailable"), Instant.now());
    }

    private double diskUsage() {
        try {
            Path root = Path.of(System.getProperty("user.dir", ".")).toAbsolutePath();
            FileStore store = Files.getFileStore(root);
            long total = store.getTotalSpace();
            long usable = store.getUsableSpace();
            return total <= 0 ? 0 : clamp((total - usable) * 100.0 / total);
        } catch (IOException | SecurityException ignored) {
            return 0;
        }
    }

    private NetworkRate networkRate() {
        long[] counters = readNetworkCounters();
        if (counters == null) return NetworkRate.EMPTY;
        long now = System.nanoTime();
        if (!networkInitialized || now <= previousNetworkNanos) {
            previousReceived = counters[0]; previousSent = counters[1]; previousNetworkNanos = now;
            networkInitialized = true;
            return NetworkRate.EMPTY;
        }
        double seconds = (now - previousNetworkNanos) / 1_000_000_000.0;
        double down = Math.max(0, counters[0] - previousReceived) * 8.0 / seconds / 1_000_000.0;
        double up = Math.max(0, counters[1] - previousSent) * 8.0 / seconds / 1_000_000.0;
        previousReceived = counters[0]; previousSent = counters[1]; previousNetworkNanos = now;
        return new NetworkRate(Math.min(down, 100_000), Math.min(up, 100_000));
    }

    private long[] readNetworkCounters() {
        String os = System.getProperty("os.name", "").toLowerCase(Locale.ROOT);
        if (os.contains("win")) return readWindowsNetworkCounters();
        return readProcNetworkCounters();
    }

    private long[] readWindowsNetworkCounters() {
        Process process = null;
        try {
            String script = "$s=Get-NetAdapterStatistics -ErrorAction SilentlyContinue; "
                    + "if($s){($s|Measure-Object -Property ReceivedBytes -Sum).Sum;($s|Measure-Object -Property SentBytes -Sum).Sum}";
            process = new ProcessBuilder("powershell.exe", "-NoProfile", "-NonInteractive", "-Command", script)
                    .redirectErrorStream(true).start();
            if (!process.waitFor(1200, TimeUnit.MILLISECONDS)) { process.destroyForcibly(); return null; }
            try (BufferedReader reader = new BufferedReader(new InputStreamReader(process.getInputStream(), StandardCharsets.UTF_8))) {
                long[] values = reader.lines().map(String::strip).filter(s -> s.matches("\\d+"))
                        .mapToLong(Long::parseLong).toArray();
                return values.length >= 2 ? new long[]{values[0], values[1]} : null;
            }
        } catch (Exception ignored) {
            if (process != null) process.destroyForcibly();
            return null;
        }
    }

    private long[] readProcNetworkCounters() {
        Path proc = Path.of("/proc/net/dev");
        if (!Files.isReadable(proc)) return null;
        long received = 0, sent = 0;
        try {
            for (String line : Files.readAllLines(proc, StandardCharsets.UTF_8)) {
                int colon = line.indexOf(':');
                if (colon < 0 || line.substring(0, colon).trim().equals("lo")) continue;
                String[] columns = line.substring(colon + 1).trim().split("\\s+");
                if (columns.length >= 9) { received += Long.parseLong(columns[0]); sent += Long.parseLong(columns[8]); }
            }
            return new long[]{received, sent};
        } catch (Exception ignored) { return null; }
    }

    private static String machineName() {
        try { return InetAddress.getLocalHost().getHostName(); }
        catch (Exception ignored) { return System.getenv().getOrDefault("COMPUTERNAME", "Unknown"); }
    }

    private static String operatingSystemName() {
        return System.getProperty("os.name", "Unknown") + " " + System.getProperty("os.version", "");
    }

    private static String cpuModel() {
        String os = System.getProperty("os.name", "").toLowerCase(Locale.ROOT);
        if (os.contains("win")) {
            String output = command("wmic", "cpu", "get", "name", "/value");
            for (String line : output.split("\\R")) if (line.startsWith("Name=")) return line.substring(5).trim();
            output = command("powershell.exe", "-NoProfile", "-NonInteractive", "-Command", "(Get-CimInstance Win32_Processor).Name");
            if (!output.isBlank()) return output.lines().findFirst().orElse(output).trim();
        } else {
            try {
                for (String line : Files.readAllLines(Path.of("/proc/cpuinfo"), StandardCharsets.UTF_8))
                    if (line.toLowerCase(Locale.ROOT).startsWith("model name")) return line.substring(line.indexOf(':') + 1).trim();
            } catch (Exception ignored) { }
        }
        return System.getProperty("os.arch", "Unknown");
    }

    private String cachedCpuModel() {
        String current = cachedCpuModel;
        if (current == null) { current = cpuModel(); cachedCpuModel = current; }
        return current;
    }

    private static String screenResolution() {
        if (GraphicsEnvironment.isHeadless()) return "Unavailable";
        try {
            var bounds = GraphicsEnvironment.getLocalGraphicsEnvironment().getDefaultScreenDevice().getDefaultConfiguration().getBounds();
            double scale = Toolkit.getDefaultToolkit().getScreenResolution() / 96.0;
            return bounds.width + " × " + bounds.height + " (" + Math.round(scale * 100) + "%)";
        } catch (Exception ignored) { return "Unavailable"; }
    }

    private String cachedScreenResolution() {
        String current = cachedScreenResolution;
        if (current == null) { current = screenResolution(); cachedScreenResolution = current; }
        return current;
    }

    private static String command(String... command) {
        try {
            Process p = new ProcessBuilder(command).redirectErrorStream(true).start();
            if (!p.waitFor(1500, TimeUnit.MILLISECONDS)) { p.destroyForcibly(); return ""; }
            return new String(p.getInputStream().readAllBytes(), StandardCharsets.UTF_8).strip();
        } catch (Exception ignored) { return ""; }
    }

    private static double number(Object value) { return value instanceof Number n ? clamp(n.doubleValue()) : 0; }
    private static double clamp(double value) { return !Double.isFinite(value) ? 0 : Math.max(0, Math.min(100, value)); }

    public record TelemetrySnapshot(double cpuPercent, double ramPercent, double diskPercent,
                                    double downloadMbps, double uploadMbps, long totalMemoryBytes,
                                    long usedMemoryBytes, String machineName, String operatingSystem,
                                    String cpuModel, String screenResolution, String ipAddress,
                                    String macAddress, Instant sampledAt) { }

    private record NetworkRate(double downloadMbps, double uploadMbps) { private static final NetworkRate EMPTY = new NetworkRate(0, 0); }
}
