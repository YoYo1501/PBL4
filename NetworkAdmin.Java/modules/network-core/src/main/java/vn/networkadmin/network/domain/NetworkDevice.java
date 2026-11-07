package vn.networkadmin.network.domain;

public record NetworkDevice(String ipAddress, String macAddress, boolean online, Long responseTimeMs) { }
