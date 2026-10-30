using System.Net;
using System.Net.NetworkInformation;
using NetworkAdmin.Shared.Models;
using NetworkAdmin.Shared.Protocol;
using NetworkAdminTool.Interfaces;
using NetworkAdminTool.Models;
using NetworkAdminTool.Services;

namespace NetworkAdmin.Client.Presentation;

internal sealed class ScannerPresenter
{
    private readonly INetworkScannerService _scannerService;
    private readonly INetworkInfoService _networkInfoService;
    private readonly ILoggerService _logger;
    private readonly NetworkDashboardState _dashboardState;
    private List<NetworkDeviceRow> _allRows = [];
    private ScannerDeviceFilter _deviceFilter = ScannerDeviceFilter.All;

    public ScannerPresenter(INetworkScannerService scannerService, INetworkInfoService networkInfoService,
        ILoggerService logger, NetworkDashboardState dashboardState)
    {
        _scannerService = scannerService;
        _networkInfoService = networkInfoService;
        _logger = logger;
        _dashboardState = dashboardState;
    }

    public int TotalHosts { get; private set; }
    public int OnlineCount => _allRows.Count(row => row.StatusText == "Online");
    public int OfflineCount => _allRows.Count(row => row.StatusText == "Offline");
    public int KnownOfflineCount => _dashboardState.GetLatestDevices().Count(device => !device.IsOnline);
    public int ScannedCount => TotalHosts > 0 ? TotalHosts : _allRows.Count;
    public DateTime? LastScanAt => _dashboardState.LastScanAt;

    public List<NetworkInterfaceInfo> GetAvailableInterfaces() => _networkInfoService.GetAvailableNetworkInterfaces();

    public ScannerInterfaceDetails GetInterfaceDetails(NetworkInterfaceInfo selected)
    {
        var cidr = _networkInfoService.GetSubnetPrefix(selected.IpAddress);
        return new ScannerInterfaceDetails(
            ToRangeText(cidr),
            selected.Description,
            selected.IpAddress,
            string.IsNullOrWhiteSpace(selected.Gateway) ? "-" : selected.Gateway,
            TryGetMaskFromCidr(cidr, out var mask) ? mask : "-",
            TryGetLocalMac(selected.Name, out var mac) ? mac : "-");
    }

    public bool TryValidateScanSubnet(string subnet, out string message)
    {
        var valid = ScanSubnet.TryNormalize(subnet, out _);
        message = valid ? string.Empty : "Subnet kh\u00f4ng h\u1ee3p l\u1ec7. Nh\u1eadp IPv4 CIDR /23 \u0111\u1ebfn /30 (t\u1ed1i \u0111a 510 host).";
        return valid;
    }

    public string ToCidrText(string text)
    {
        if (text.Contains('/'))
            return text;

        var parts = text.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var firstAddress))
            return text;

        var first = ToUInt32(firstAddress);
        var network = first & 0xFFFFFF00;
        return $"{ToIpString(network)}/24";
    }

    public void BeginScan(string subnet)
    {
        _allRows = [];
        TotalHosts = CountHosts(subnet);
    }

    public async Task ScanAsync(string subnet, string? interfaceName, CancellationToken cancellationToken,
        IProgress<NetworkScanProgress>? progress)
    {
        var devices = await _scannerService.ScanNetworkAsync(
            subnet,
            800,
            interfaceName: interfaceName,
            cancellationToken: cancellationToken,
            progress: progress);

        var scanAt = DateTime.Now;
        _dashboardState.UpdateScanResult(subnet, devices, scanAt);
        _allRows = _dashboardState.GetLatestDevices()
            .Select(device => CreateDeviceRow(device, scanAt))
            .OrderBy(row => GetIpValue(row.IpAddress))
            .ToList();

        _logger.Log($"IP scanner completed: {subnet}, online={OnlineCount}, known={_allRows.Count}");
    }

    public bool LoadLastScanState()
    {
        if (!_dashboardState.HasScan)
            return false;

        TotalHosts = string.IsNullOrWhiteSpace(_dashboardState.LastSubnet) ? 0 : CountHosts(_dashboardState.LastSubnet);
        _allRows = _dashboardState.GetLatestDevices()
            .Select(device => CreateDeviceRow(device, _dashboardState.LastScanAt ?? DateTime.Now))
            .OrderBy(row => GetIpValue(row.IpAddress))
            .ToList();
        return true;
    }

    public void SetFilter(ScannerDeviceFilter filter) => _deviceFilter = filter;

    public void LogCancelled(string subnet) => _logger.Log($"IP scanner cancelled: {subnet}");

    public void LogFailure(string message) => _logger.Log($"IP scanner failed: {message}");

    public ScannerFilterResult FilterRows(string searchText)
    {
        var keyword = searchText.Trim();
        var rows = _allRows.AsEnumerable();
        rows = _deviceFilter switch
        {
            ScannerDeviceFilter.Online => rows.Where(row => row.StatusText == "Online"),
            ScannerDeviceFilter.Offline => rows.Where(row => row.StatusText == "Offline"),
            _ => rows
        };

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            rows = rows.Where(row =>
                row.IpAddress.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || row.Hostname.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || row.MacAddress.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || row.Vendor.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        var suffix = _deviceFilter switch
        {
            ScannerDeviceFilter.Online => "online",
            ScannerDeviceFilter.Offline => "offline \u0111\u00e3 t\u1eebng k\u1ebft n\u1ed1i",
            _ => "thi\u1ebft b\u1ecb"
        };
        var filteredRows = rows.ToList();
        return new ScannerFilterResult(filteredRows,
            $"K\u1ebft qu\u1ea3 qu\u00e9t ({filteredRows.Count} {suffix}, {OnlineCount} online)");
    }

    private static bool TryGetMaskFromCidr(string cidr, out string subnetMask)
    {
        subnetMask = string.Empty;
        var parts = cidr.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var prefix) || prefix < 0 || prefix > 32)
            return false;

        var mask = prefix == 0 ? 0U : uint.MaxValue << (32 - prefix);
        var bytes = BitConverter.GetBytes(mask);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);

        subnetMask = new IPAddress(bytes).ToString();
        return true;
    }

    private static string ToRangeText(string cidr)
    {
        if (!TryParseCidr(cidr, out var network, out var prefix) || prefix is < 1 or > 30)
            return cidr;

        var mask = uint.MaxValue << (32 - prefix);
        var first = (network & mask) + 1;
        var last = (network & mask | ~mask) - 1;
        return $"{ToIpString(first)}  -  {ToIpString(last)}";
    }

    private static bool TryParseCidr(string cidr, out uint network, out int prefix)
    {
        network = 0;
        prefix = 0;
        var parts = cidr.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var address) || !int.TryParse(parts[1], out prefix))
            return false;

        network = ToUInt32(address);
        return true;
    }

    private static int CountHosts(string subnet)
    {
        var parts = subnet.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && int.TryParse(parts[1], out var prefix) && prefix >= 1 && prefix <= 30)
            return (int)Math.Min(int.MaxValue, (1L << (32 - prefix)) - 2);

        return 254;
    }

    private static NetworkDeviceRow CreateDeviceRow(NetworkDevice device, DateTime scanTime) => new()
    {
        IpAddress = device.IpAddress,
        Hostname = "-",
        MacAddress = string.IsNullOrWhiteSpace(device.MacAddress) ? "-" : device.MacAddress,
        StatusText = device.IsOnline ? "Online" : "Offline",
        ResponseTimeText = device.ResponseTimeMs.HasValue ? device.ResponseTimeMs.Value.ToString() : "-",
        Vendor = ResolveVendorByMac(device.MacAddress),
        LastSeen = (device.LastSeen ?? scanTime).ToString("HH:mm:ss")
    };

    private static string ResolveVendorByMac(string macAddress)
    {
        if (string.IsNullOrWhiteSpace(macAddress))
            return "Unknown";

        var normalized = macAddress.Replace(":", "-").ToUpperInvariant();
        if (normalized.Length < 8)
            return "Unknown";

        var oui = normalized[..8];
        return oui switch
        {
            "54-04-63" => "MediaTek Inc.",
            "30-4A-26" => "Intel Corporation",
            "6C-22-1A" => "Dell Inc.",
            "E2-A7-C3" => "Apple Inc.",
            "AE-01-A9" => "HP Inc.",
            "5A-D2-5B" => "Samsung Elec.",
            _ => "Unknown"
        };
    }

    private static bool TryGetLocalMac(string interfaceName, out string macAddress)
    {
        macAddress = string.Empty;
        try
        {
            var networkInterface = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(item => item.Name.Equals(interfaceName, StringComparison.OrdinalIgnoreCase));
            var bytes = networkInterface?.GetPhysicalAddress().GetAddressBytes();
            if (bytes == null || bytes.Length == 0)
                return false;

            macAddress = string.Join("-", bytes.Select(byteValue => byteValue.ToString("X2")));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int GetIpValue(string ipAddress)
    {
        var parts = ipAddress.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 4)
            return int.MaxValue;

        var value = 0;
        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var octet) || octet < 0 || octet > 255)
                return int.MaxValue;

            value = (value << 8) + octet;
        }

        return value;
    }

    private static uint ToUInt32(IPAddress ipAddress)
    {
        var bytes = ipAddress.GetAddressBytes();
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);

        return BitConverter.ToUInt32(bytes, 0);
    }

    private static string ToIpString(uint ipValue)
    {
        var bytes = BitConverter.GetBytes(ipValue);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);

        return new IPAddress(bytes).ToString();
    }
}

internal enum ScannerDeviceFilter
{
    All,
    Online,
    Offline
}

internal sealed record ScannerInterfaceDetails(
    string SubnetRange,
    string AdapterDescription,
    string IpAddress,
    string Gateway,
    string SubnetMask,
    string MacAddress);

internal sealed record ScannerFilterResult(IReadOnlyList<NetworkDeviceRow> Rows, string Title);
