using System.Diagnostics;
using Microsoft.VisualBasic.Devices;
using NetworkAdminTool.Interfaces;
using NetworkAdminTool.Models;

namespace NetworkAdminTool.Services
{
    /// <summary>
    /// Theo dõi mức sử dụng CPU + RAM theo thời gian thực.
    /// Gộp chung vì MonitorForm luôn cần cả 2 giá trị mỗi lần Timer tick.
    /// CHỈ CHẠY TRÊN WINDOWS (PerformanceCounter là API của Windows).
    /// </summary>
    public class SystemMonitorService : ISystemMonitorService
    {
        private static readonly TimeSpan CpuSampleInterval = TimeSpan.FromMilliseconds(900);

        private readonly PerformanceCounter _cpuCounter =
            new("Processor", "% Processor Time", "_Total");

        private readonly ComputerInfo _computerInfo = new();
        private readonly object _cpuSyncRoot = new();
        private float _lastCpuUsagePercent;
        private DateTime _lastCpuSampleUtc = DateTime.MinValue;

        public SystemMonitorService()
        {
            try
            {
                _cpuCounter.NextValue();
            }
            catch
            {
                _lastCpuUsagePercent = 0f;
            }
        }

        public double GetCpuUsage()
        {
            return GetCpuUsagePercent();
        }

        public double GetMemoryUsage()
        {
            return GetRamUsagePercent();
        }

        public double GetDiskUsage()
        {
            var systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
            var systemDrive = DriveInfo.GetDrives()
                .FirstOrDefault(d => d.IsReady && string.Equals(d.Name, systemRoot, StringComparison.OrdinalIgnoreCase));
            if (systemDrive != null)
            {
                return CalculateDiskUsage(systemDrive);
            }

            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(CalculateDiskUsage)
                .ToList();

            return drives.Count == 0 ? 0 : drives.Average();
        }

        private static double CalculateDiskUsage(DriveInfo drive)
        {
            if (drive.TotalSize <= 0)
            {
                return 0;
            }

            return (drive.TotalSize - drive.AvailableFreeSpace) / (double)drive.TotalSize * 100;
        }

        public List<SystemStats> GetHistory(int sampleCount = 10)
        {
            var history = new List<SystemStats>();
            for (var i = 0; i < sampleCount; i++)
            {
                history.Add(GetCurrentStats());
            }

            return history;
        }

        public SystemStats GetSystemStats()
        {
            return GetCurrentStats();
        }

        /// <summary>
        /// Lấy % CPU đang dùng tại thời điểm gọi.
        /// </summary>
        public float GetCpuUsagePercent()
        {
            lock (_cpuSyncRoot)
            {
                var now = DateTime.UtcNow;
                if (now - _lastCpuSampleUtc < CpuSampleInterval)
                    return _lastCpuUsagePercent;

                try
                {
                    var value = _cpuCounter.NextValue();
                    if (!float.IsNaN(value) && !float.IsInfinity(value))
                    {
                        _lastCpuUsagePercent = Math.Max(0f, Math.Min(100f, value));
                    }

                    _lastCpuSampleUtc = now;
                }
                catch
                {
                    _lastCpuSampleUtc = now;
                }

                return _lastCpuUsagePercent;
            }
        }

        /// <summary>
        /// Lấy % RAM đang dùng tại thời điểm gọi.
        /// Dùng Microsoft.VisualBasic.Devices.ComputerInfo để lấy tổng RAM
        /// và RAM khả dụng, từ đó tính ra % đang sử dụng.
        /// </summary>
        public float GetRamUsagePercent()
        {
            ulong total = _computerInfo.TotalPhysicalMemory;
            ulong available = _computerInfo.AvailablePhysicalMemory;

            if (total == 0) return 0f;

            ulong used = total - available;
            return (float)used / total * 100f;
        }

        /// <summary>
        /// Lấy đồng thời cả CPU và RAM — dùng cho mỗi lần Timer tick.
        /// </summary>
        public SystemStats GetCurrentStats()
        {
            return new SystemStats
            {
                CpuUsagePercent = GetCpuUsagePercent(),
                RamUsagePercent = GetRamUsagePercent()
            };
        }
    }
}
