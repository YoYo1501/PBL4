package vn.networkadmin.network.domain;

import java.util.ArrayList;
import java.util.List;
import java.util.Optional;

/** CIDR validation and host-range calculation matching the .NET scanner's /23 through /30 limits. */
public record ScanSubnet(long networkAddress, int prefixLength) {
    public static final int MAX_HOSTS = 510;
    public static final int DEFAULT_TIMEOUT_MS = 800;
    public static final int MIN_TIMEOUT_MS = 100;
    public static final int MAX_TIMEOUT_MS = 1000;
    public static final String HELP = "Enter an IPv4 unicast CIDR /23 through /30 (maximum 510 hosts), or a three-octet /24 prefix.";

    public static Optional<ScanSubnet> parse(String value) {
        if (value == null || value.isBlank()) return Optional.empty();
        String text = value.trim();
        if (!text.contains("/") && text.split("\\.").length == 3) text += ".0/24";
        String[] parts = text.split("/", -1);
        if (parts.length != 2) return Optional.empty();
        final int prefix;
        try { prefix = Integer.parseInt(parts[1]); } catch (NumberFormatException ex) { return Optional.empty(); }
        if (prefix < 23 || prefix > 30) return Optional.empty();
        String[] octets = parts[0].split("\\.", -1);
        if (octets.length != 4) return Optional.empty();
        long address = 0;
        try {
            for (String octet : octets) {
                if (octet.isEmpty() || !octet.chars().allMatch(Character::isDigit)) return Optional.empty();
                int n = Integer.parseInt(octet);
                if (n < 0 || n > 255) return Optional.empty();
                address = (address << 8) | n;
            }
        } catch (NumberFormatException ex) { return Optional.empty(); }
        int first = (int) ((address >>> 24) & 255);
        if (first == 0 || first == 127 || first >= 224) return Optional.empty();
        long mask = (0xffff_ffffL << (32 - prefix)) & 0xffff_ffffL;
        return Optional.of(new ScanSubnet(address & mask, prefix));
    }

    public List<String> hosts() {
        int count = (1 << (32 - prefixLength)) - 2;
        List<String> hosts = new ArrayList<>(count);
        for (int i = 1; i <= count; i++) hosts.add(format(networkAddress + i));
        return List.copyOf(hosts);
    }

    /** Returns true when the candidate range is wholly inside this network. */
    public boolean contains(ScanSubnet candidate) {
        if (candidate == null || candidate.prefixLength < prefixLength) return false;
        long mask = (0xffff_ffffL << (32 - prefixLength)) & 0xffff_ffffL;
        return (candidate.networkAddress & mask) == networkAddress;
    }

    public static int clampTimeout(int timeoutMs) {
        int value = timeoutMs <= 0 ? DEFAULT_TIMEOUT_MS : timeoutMs;
        return Math.max(MIN_TIMEOUT_MS, Math.min(MAX_TIMEOUT_MS, value));
    }

    @Override public String toString() { return format(networkAddress) + "/" + prefixLength; }

    public static String format(long address) {
        return ((address >>> 24) & 255) + "." + ((address >>> 16) & 255) + "." + ((address >>> 8) & 255) + "." + (address & 255);
    }

    public static long numericAddress(String ip) {
        String[] octets = ip.split("\\.");
        if (octets.length != 4) return Long.MAX_VALUE;
        long value = 0;
        try { for (String part : octets) { int n = Integer.parseInt(part); if (n < 0 || n > 255) return Long.MAX_VALUE; value = (value << 8) | n; } }
        catch (NumberFormatException ex) { return Long.MAX_VALUE; }
        return value;
    }
}
