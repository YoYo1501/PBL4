using NetworkAdminTool.Models;

namespace NetworkAdminTool.Services;

public sealed class NetworkDashboardState
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, NetworkDevice> _knownDevices = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<NetworkAlert> _alerts = new();

    public string LastSubnet { get; private set; } = string.Empty;
    public DateTime? LastScanAt { get; private set; }

    public bool HasScan
    {
        get
        {
            lock (_syncRoot)
                return LastScanAt.HasValue;
        }
    }

    public void UpdateScanResult(string subnet, IEnumerable<NetworkDevice> devices, DateTime scanAt)
    {
        lock (_syncRoot)
        {
            LastSubnet = subnet;
            LastScanAt = scanAt;
            var onlineDevices = devices.ToDictionary(device => device.IpAddress, StringComparer.OrdinalIgnoreCase);
            var onlineCount = onlineDevices.Count;
            var offlineCount = 0;

            foreach (var known in _knownDevices.Values.Where(device => IsInSubnet(device.IpAddress, subnet)))
            {
                if (!onlineDevices.ContainsKey(known.IpAddress))
                {
                    if (known.IsOnline)
                    {
                        AddAlert("Warning", $"Thiet bi {known.IpAddress} mat ket noi", "Device offline", scanAt);
                    }

                    known.IsOnline = false;
                    known.ResponseTimeMs = null;
                    offlineCount++;
                }
            }

            foreach (var device in onlineDevices.Values)
            {
                if (_knownDevices.TryGetValue(device.IpAddress, out var known))
                {
                    if (!known.IsOnline)
                    {
                        AddAlert("Info", $"Thiet bi {device.IpAddress} vua online", "Device online", scanAt);
                    }

                    known.MacAddress = string.IsNullOrWhiteSpace(device.MacAddress) ? known.MacAddress : device.MacAddress;
                    known.IsOnline = true;
                    known.ResponseTimeMs = device.ResponseTimeMs;
                    known.LastSeen = scanAt;
                }
                else
                {
                    AddAlert("Info", $"Phat hien thiet bi {device.IpAddress}", "New device online", scanAt);
                    _knownDevices[device.IpAddress] = new NetworkDevice
                    {
                        IpAddress = device.IpAddress,
                        MacAddress = device.MacAddress,
                        IsOnline = true,
                        ResponseTimeMs = device.ResponseTimeMs,
                        LastSeen = scanAt
                    };
                }
            }

            AddAlert("Info", $"Hoan tat quet mang {subnet}", $"Online: {onlineCount}, Offline da biet: {offlineCount}", scanAt);
        }
    }

    public IReadOnlyList<NetworkDevice> GetLatestDevices()
    {
        lock (_syncRoot)
            return _knownDevices.Values
                .Where(device => string.IsNullOrWhiteSpace(LastSubnet) || IsInSubnet(device.IpAddress, LastSubnet))
                .OrderBy(device => GetIpValue(device.IpAddress))
                .Select(Clone)
                .ToList();
    }

    public IReadOnlyList<NetworkAlert> GetRecentAlerts(int maxCount = 20)
    {
        lock (_syncRoot)
            return _alerts
                .OrderByDescending(alert => alert.CreatedAt)
                .Take(Math.Max(1, maxCount))
                .Select(alert => new NetworkAlert
                {
                    Severity = alert.Severity,
                    Title = alert.Title,
                    Message = alert.Message,
                    CreatedAt = alert.CreatedAt
                })
                .ToList();
    }

    private void AddAlert(string severity, string title, string message, DateTime createdAt)
    {
        _alerts.Add(new NetworkAlert
        {
            Severity = severity,
            Title = title,
            Message = message,
            CreatedAt = createdAt
        });

        if (_alerts.Count > 50)
        {
            _alerts.RemoveRange(0, _alerts.Count - 50);
        }
    }

    private static NetworkDevice Clone(NetworkDevice device)
    {
        return new NetworkDevice
        {
            IpAddress = device.IpAddress,
            MacAddress = device.MacAddress,
            IsOnline = device.IsOnline,
            ResponseTimeMs = device.ResponseTimeMs,
            LastSeen = device.LastSeen
        };
    }

    private static bool IsInSubnet(string ipAddress, string subnet)
    {
        if (string.IsNullOrWhiteSpace(subnet))
            return true;

        var parts = subnet.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !uint.TryParse(parts[1], out var prefix) || prefix is < 1 or > 30)
            return true;

        var ipValue = GetIpValue(ipAddress);
        var networkValue = GetIpValue(parts[0]);
        if (ipValue == uint.MaxValue || networkValue == uint.MaxValue)
            return true;

        var mask = uint.MaxValue << (int)(32 - prefix);
        return (ipValue & mask) == (networkValue & mask);
    }

    private static uint GetIpValue(string ipAddress)
    {
        var parts = ipAddress.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 4)
            return uint.MaxValue;

        uint value = 0;
        foreach (var part in parts)
        {
            if (!byte.TryParse(part, out var octet))
                return uint.MaxValue;

            value = (value << 8) + octet;
        }

        return value;
    }
}
