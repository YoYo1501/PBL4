using System.Net.NetworkInformation;
using NetworkAdmin.Shared.Models;
using NetworkAdminTool.Interfaces;
using NetworkAdminTool.Services;

namespace NetworkAdmin.Client.Presentation;

internal sealed class DashboardPresenter
{
    private readonly INetworkInfoService _networkInfoService;
    private readonly ISystemMonitorService _monitorService;
    private readonly NetworkDashboardState _dashboardState;
    private readonly Queue<float> _downloadHistory = new();
    private readonly Queue<float> _uploadHistory = new();
    private long? _lastBytesReceived;
    private long? _lastBytesSent;
    private DateTime _lastTrafficSampleUtc = DateTime.MinValue;

    public DashboardPresenter(INetworkInfoService networkInfoService, ISystemMonitorService monitorService,
        NetworkDashboardState dashboardState)
    {
        _networkInfoService = networkInfoService;
        _monitorService = monitorService;
        _dashboardState = dashboardState;
    }

    public DashboardSnapshot Refresh()
    {
        var adapter = _networkInfoService.GetAvailableNetworkInterfaces().FirstOrDefault();
        var localIp = adapter?.IpAddress ?? string.Empty;
        var subnet = string.IsNullOrWhiteSpace(localIp) ? "-" : _networkInfoService.GetSubnetPrefix(localIp);
        var stats = _monitorService.GetSystemStats();
        var diskUsage = _monitorService.GetDiskUsage();
        var devices = _dashboardState.HasScan
            ? _dashboardState.GetLatestDevices().Select(ToDashboardDevice).ToArray()
            : Array.Empty<DashboardDevice>();

        SampleNetworkTraffic();

        return new DashboardSnapshot(
            adapter,
            subnet,
            stats,
            diskUsage,
            devices,
            _dashboardState.GetRecentAlerts(4),
            _dashboardState.GetUnreadAlertCount(),
            _downloadHistory.ToArray(),
            _uploadHistory.ToArray());
    }

    private static DashboardDevice ToDashboardDevice(NetworkDevice device) => new(
        device.IpAddress.EndsWith(".1", StringComparison.Ordinal) ? "router" : "pc",
        device.IpAddress,
        "-",
        string.IsNullOrWhiteSpace(device.MacAddress) ? "-" : device.MacAddress,
        device.IsOnline,
        device.ResponseTimeMs,
        device.LastSeen?.ToString("HH:mm:ss") ?? "-");

    private void SampleNetworkTraffic()
    {
        var now = DateTime.UtcNow;
        if (_lastTrafficSampleUtc != DateTime.MinValue && (now - _lastTrafficSampleUtc).TotalMilliseconds < 900)
            return;

        try
        {
            long bytesReceived = 0;
            long bytesSent = 0;
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                    networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var statistics = networkInterface.GetIPv4Statistics();
                bytesReceived += statistics.BytesReceived;
                bytesSent += statistics.BytesSent;
            }

            if (_lastBytesReceived.HasValue && _lastBytesSent.HasValue)
            {
                var seconds = Math.Max(0.001, (now - _lastTrafficSampleUtc).TotalSeconds);
                var downloadMbps = (float)Math.Max(0, (bytesReceived - _lastBytesReceived.Value) * 8d / seconds / 1_000_000d);
                var uploadMbps = (float)Math.Max(0, (bytesSent - _lastBytesSent.Value) * 8d / seconds / 1_000_000d);
                EnqueueTrafficSample(downloadMbps, uploadMbps);
            }
            else
            {
                EnqueueTrafficSample(0, 0);
            }

            _lastBytesReceived = bytesReceived;
            _lastBytesSent = bytesSent;
            _lastTrafficSampleUtc = now;
        }
        catch
        {
            EnqueueTrafficSample(0, 0);
            _lastTrafficSampleUtc = now;
        }
    }

    private void EnqueueTrafficSample(float downloadMbps, float uploadMbps)
    {
        _downloadHistory.Enqueue(downloadMbps);
        _uploadHistory.Enqueue(uploadMbps);
        while (_downloadHistory.Count > 60) _downloadHistory.Dequeue();
        while (_uploadHistory.Count > 60) _uploadHistory.Dequeue();
    }
}

internal sealed record DashboardSnapshot(
    NetworkInterfaceInfo? PrimaryAdapter,
    string Subnet,
    SystemStats SystemStats,
    double DiskUsage,
    IReadOnlyList<DashboardDevice> Devices,
    IReadOnlyList<NetworkAlert> RecentAlerts,
    int UnreadAlertCount,
    IReadOnlyList<float> DownloadHistory,
    IReadOnlyList<float> UploadHistory)
{
    public static DashboardSnapshot Empty { get; } = new(null, "-", new(), 0, [], [], 0, [], []);
}

internal sealed record DashboardDevice(
    string Icon,
    string IpAddress,
    string Hostname,
    string MacAddress,
    bool Online,
    long? PingMs,
    string LastSeen);
