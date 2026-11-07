package vn.networkadmin.network.infrastructure;

import vn.networkadmin.network.domain.NetworkInterfaceInfo;
import vn.networkadmin.network.domain.ScanSubnet;
import vn.networkadmin.network.ports.NetworkInventoryPort;

import java.io.IOException;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.TimeUnit;
import java.util.regex.Matcher;
import java.util.regex.Pattern;
import java.util.stream.Collectors;

/** Operating-system network inventory adapter. Discovery never installs services or changes routes. */
public final class SystemNetworkInventoryAdapter implements NetworkInventoryPort {
    private static final String VIRTUAL_MARKERS = "vmware vethernet virtual virtualbox hyper-v loopback tunnel tap- vpn docker wsl bluetooth pseudo";
    private static final Pattern WINDOWS_ARP = Pattern.compile("(?im)^\\s*(\\d{1,3}(?:\\.\\d{1,3}){3})\\s+([0-9a-f]{2}(?:-[0-9a-f]{2}){5})\\s+(?:dynamic|static)\\s*$");
    private static final Pattern UNIX_ARP = Pattern.compile("(?im)^\\S+\\s+\\((\\d{1,3}(?:\\.\\d{1,3}){3})\\)\\s+at\\s+([0-9a-f]{2}(?::[0-9a-f]{2}){5})\\b");

    @Override public List<NetworkInterfaceInfo> interfaces() {
        List<NetworkInterfaceInfo> result = new ArrayList<>();
        try {
            Enumeration<NetworkInterface> all = NetworkInterface.getNetworkInterfaces();
            if (all == null) return List.of();
            while (all.hasMoreElements()) {
                NetworkInterface ni = all.nextElement();
                if (!ni.isUp() || ni.isLoopback() || ni.isVirtual()) continue;
                for (InterfaceAddress address : ni.getInterfaceAddresses()) {
                    InetAddress inet = address.getAddress();
                    if (!(inet instanceof Inet4Address) || !usable(inet.getHostAddress())) continue;
                    String ip = inet.getHostAddress();
                    String subnet = subnet(ip, address.getNetworkPrefixLength());
                    String mac = formatMac(ni.getHardwareAddress());
                    String gateway = defaultGatewayFor(ni);
                    String description = ni.getDisplayName() == null ? ni.getName() : ni.getDisplayName();
                    result.add(new NetworkInterfaceInfo(ni.getName(), description, ip, mac, gateway, subnet,
                            isVirtual(ni.getName() + " " + description)));
                }
            }
        } catch (SocketException ignored) { return List.of(); }
        result.sort(Comparator.comparing(NetworkInterfaceInfo::virtual)
                .thenComparing((NetworkInterfaceInfo n) -> !usable(n.gateway()))
                .thenComparing(NetworkInterfaceInfo::name, String.CASE_INSENSITIVE_ORDER));
        return List.copyOf(result);
    }

    @Override public Map<String, String> arpEntries() throws InterruptedException {
        String os = System.getProperty("os.name", "").toLowerCase(Locale.ROOT);
        List<String> command = os.contains("win") ? List.of("arp", "-a") : List.of("arp", "-an");
        Process process;
        try { process = new ProcessBuilder(command).redirectErrorStream(true).start(); }
        catch (IOException ex) { return Map.of(); }
        try {
            if (!process.waitFor(2, TimeUnit.SECONDS)) { process.destroyForcibly(); return Map.of(); }
            String output = new String(process.getInputStream().readAllBytes(), StandardCharsets.UTF_8);
            Map<String, String> result = new HashMap<>();
            Matcher matcher = WINDOWS_ARP.matcher(output);
            while (matcher.find()) result.put(matcher.group(1), matcher.group(2).replace('-', ':').toUpperCase(Locale.ROOT).replace(':', '-'));
            matcher = UNIX_ARP.matcher(output);
            while (matcher.find()) result.put(matcher.group(1), matcher.group(2).toUpperCase(Locale.ROOT).replace(':', '-'));
            return Map.copyOf(result);
        } catch (InterruptedException ex) { process.destroyForcibly(); throw ex; }
        catch (IOException ex) { return Map.of(); }
    }

    public Optional<NetworkInterfaceInfo> primaryInterface() {
        return interfaces().stream().filter(n -> !n.virtual()).filter(n -> usable(n.ipAddress()))
                .sorted(Comparator.comparing((NetworkInterfaceInfo n) -> !usable(n.gateway()))
                        .thenComparing(n -> !(n.name().toLowerCase(Locale.ROOT).contains("wi") || n.name().toLowerCase(Locale.ROOT).contains("eth"))))
                .findFirst();
    }

    public Optional<NetworkInterfaceInfo> findInterface(String name) {
        if (name == null || name.isBlank()) return primaryInterface();
        return interfaces().stream().filter(n -> n.name().equalsIgnoreCase(name) || n.description().equalsIgnoreCase(name)).findFirst();
    }

    public static boolean usable(String ip) {
        try {
            byte[] b = InetAddress.getByName(ip).getAddress();
            return b.length == 4 && (b[0] & 255) > 0 && (b[0] & 255) < 224
                    && !((b[0] & 255) == 169 && (b[1] & 255) == 254) && !ip.startsWith("127.");
        } catch (Exception ex) { return false; }
    }

    private static boolean isVirtual(String value) {
        String lower = value.toLowerCase(Locale.ROOT);
        return Arrays.stream(VIRTUAL_MARKERS.split(" ")).anyMatch(lower::contains);
    }

    private static String subnet(String ip, short prefix) {
        long mask = prefix == 0 ? 0 : (0xffff_ffffL << (32 - prefix)) & 0xffff_ffffL;
        return ScanSubnet.format(ScanSubnet.numericAddress(ip) & mask) + "/" + prefix;
    }

    private static String formatMac(byte[] bytes) {
        if (bytes == null || bytes.length == 0) return "";
        return Arrays.stream(toHex(bytes)).collect(Collectors.joining("-"));
    }
    private static String[] toHex(byte[] bytes) {
        String[] values = new String[bytes.length];
        for (int i = 0; i < bytes.length; i++) values[i] = String.format(Locale.ROOT, "%02X", bytes[i] & 255);
        return values;
    }

    private static String defaultGatewayFor(NetworkInterface ni) {
        try {
            String os = System.getProperty("os.name", "").toLowerCase(Locale.ROOT);
            List<String> command = os.contains("win") ? List.of("route", "print", "-4") : List.of("sh", "-c", "ip -4 route show default");
            Process p = new ProcessBuilder(command).redirectErrorStream(true).start();
            if (!p.waitFor(800, TimeUnit.MILLISECONDS)) { p.destroyForcibly(); return ""; }
            String output = new String(p.getInputStream().readAllBytes(), StandardCharsets.UTF_8);
            if (os.contains("win")) {
                Matcher m = Pattern.compile("(?m)^\\s*0\\.0\\.0\\.0\\s+0\\.0\\.0\\.0\\s+(\\d+\\.\\d+\\.\\d+\\.\\d+)\\s+(\\d+\\.\\d+\\.\\d+\\.\\d+)").matcher(output);
                while (m.find()) if (m.group(2).equalsIgnoreCase(firstIpv4(ni))) return m.group(1);
            } else if (output.contains("dev " + ni.getName())) {
                Matcher m = Pattern.compile("\\bgateway\\s+(\\d+\\.\\d+\\.\\d+\\.\\d+)").matcher(output);
                if (m.find()) return m.group(1);
            }
        } catch (Exception ignored) { }
        return "";
    }

    private static String firstIpv4(NetworkInterface ni) {
        return ni.getInterfaceAddresses().stream().map(InterfaceAddress::getAddress)
                .filter(Inet4Address.class::isInstance).map(InetAddress::getHostAddress).findFirst().orElse("");
    }
}
