using NetworkAdmin.Shared.Models;

namespace NetworkAdmin.Client.Application.Interfaces;

public interface INetworkInfoService
{
    string GetLocalIpAddress();
    string GetLocalIpAddress(string interfaceName);
    List<NetworkInterfaceInfo> GetAvailableNetworkInterfaces();
    string GetSubnetPrefix(string localIp);
    string GetDefaultGateway();
    string GetDefaultGateway(string interfaceName);
    List<string> GetConnectedDevices();
    List<string> GetConnectedDevices(string interfaceName);
    List<string> GetConnectedDevices(string interfaceName, CancellationToken cancellationToken);
    NetworkDevice GetDeviceInfo(string ipAddress);
}
