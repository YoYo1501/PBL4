package vn.networkadmin.network.ports;

import vn.networkadmin.network.domain.NetworkInterfaceInfo;
import java.util.List;
import java.util.Map;

public interface NetworkInventoryPort {
    List<NetworkInterfaceInfo> interfaces();
    Map<String, String> arpEntries() throws InterruptedException;
}
