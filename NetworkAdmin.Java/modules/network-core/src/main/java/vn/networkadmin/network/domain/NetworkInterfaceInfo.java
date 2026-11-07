package vn.networkadmin.network.domain;

public record NetworkInterfaceInfo(String name, String description, String ipAddress,
                                   String macAddress, String gateway, String subnet, boolean virtual) { }
