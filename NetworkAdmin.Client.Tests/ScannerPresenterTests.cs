using NetworkAdmin.Client.Presentation;
using NetworkAdmin.Shared.Models;
using NetworkAdminTool.Interfaces;
using NetworkAdminTool.Models;
using NetworkAdminTool.Services;
using Xunit;

namespace NetworkAdmin.Client.Tests;

public sealed class ScannerPresenterTests
{
    [Fact]
    public async Task ScanAsync_PreservesSubnetAndMapsSortedResultsForFiltering()
    {
        var scanner = new FakeScannerService
        {
            Devices =
            [
                new NetworkDevice { IpAddress = "192.168.1.3", IsOnline = true, MacAddress = "30-4A-26-00-00-00", ResponseTimeMs = 12 }
            ]
        };
        var state = new NetworkDashboardState();
        state.UpdateScanResult("192.168.1.0/24", [new NetworkDevice { IpAddress = "192.168.1.20", IsOnline = true }], DateTime.Now.AddMinutes(-1));
        var presenter = new ScannerPresenter(scanner, new FakeNetworkInfoService(), new FakeLogger(), state);
        const string subnet = "192.168.1.0/24";
        presenter.BeginScan(subnet);

        await presenter.ScanAsync(subnet, "Ethernet", CancellationToken.None, null);

        Assert.Equal(subnet, scanner.LastSubnet);
        Assert.Equal(800, scanner.LastTimeout);
        Assert.Equal("Ethernet", scanner.LastInterface);
        Assert.Equal(254, presenter.TotalHosts);
        Assert.Equal(1, presenter.OnlineCount);
        Assert.Equal(1, presenter.OfflineCount);
        Assert.Equal(new[] { "192.168.1.3", "192.168.1.20" }, presenter.FilterRows("").Rows.Select(row => row.IpAddress));

        presenter.SetFilter(ScannerDeviceFilter.Online);
        var result = presenter.FilterRows("intel");
        Assert.Single(result.Rows);
        Assert.Equal("192.168.1.3", result.Rows[0].IpAddress);
    }

    [Theory]
    [InlineData("192.168.1.0/23", true)]
    [InlineData("192.168.1.0/30", true)]
    [InlineData("192.168.1.0/22", false)]
    [InlineData("not-a-subnet", false)]
    public void TryValidateScanSubnet_UsesExistingSupportedRange(string subnet, bool expected)
    {
        var presenter = new ScannerPresenter(new FakeScannerService(), new FakeNetworkInfoService(), new FakeLogger(), new NetworkDashboardState());
        Assert.Equal(expected, presenter.TryValidateScanSubnet(subnet, out _));
    }

    private sealed class FakeScannerService : INetworkScannerService
    {
        public List<NetworkDevice> Devices { get; init; } = [];
        public string? LastSubnet { get; private set; }
        public string? LastInterface { get; private set; }
        public int LastTimeout { get; private set; }
        public List<NetworkDevice> ScanNetwork(string subnet, int timeoutMs = 200, string? interfaceName = null, CancellationToken cancellationToken = default) => Devices;
        public Task<List<NetworkDevice>> ScanNetworkAsync(string subnet, int timeoutMs = 300, string? interfaceName = null, CancellationToken cancellationToken = default, IProgress<NetworkScanProgress>? progress = null)
        {
            LastSubnet = subnet;
            LastInterface = interfaceName;
            LastTimeout = timeoutMs;
            return Task.FromResult(Devices);
        }
        public List<NetworkDevice> ScanLocalNetwork(string? interfaceName = null, CancellationToken cancellationToken = default) => Devices;
    }

    private sealed class FakeNetworkInfoService : INetworkInfoService
    {
        public string GetLocalIpAddress() => "";
        public string GetLocalIpAddress(string interfaceName) => "";
        public List<NetworkInterfaceInfo> GetAvailableNetworkInterfaces() => [];
        public string GetSubnetPrefix(string localIp) => "";
        public string GetDefaultGateway() => "";
        public string GetDefaultGateway(string interfaceName) => "";
        public List<string> GetConnectedDevices() => [];
        public List<string> GetConnectedDevices(string interfaceName) => [];
        public List<string> GetConnectedDevices(string interfaceName, CancellationToken cancellationToken) => [];
        public NetworkDevice GetDeviceInfo(string ipAddress) => new() { IpAddress = ipAddress };
    }

    private sealed class FakeLogger : ILoggerService
    {
        public void Log(string message) { }
        public void LogError(string message) { }
        public void LogWarning(string message) { }
    }
}
