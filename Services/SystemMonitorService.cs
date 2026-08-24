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
        private readonly PerformanceCounter _cpuCounter =
            new("Processor", "% Processor Time", "_Total");

        private readonly ComputerInfo _computerInfo = new();

        public SystemMonitorService()
        {
            _cpuCounter.NextValue();
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
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => (double)(d.TotalSize == 0 ? 0 : (d.TotalSize - d.AvailableFreeSpace) / (double)d.TotalSize * 100))
                .ToList();

            return drives.Count == 0 ? 0 : drives.Average();
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
            return _cpuCounter.NextValue();
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
