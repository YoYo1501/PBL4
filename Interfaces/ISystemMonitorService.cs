using NetworkAdminTool.Models;

namespace NetworkAdminTool.Interfaces;

public interface ISystemMonitorService
{
    SystemStats GetSystemStats();
    double GetCpuUsage();
    double GetMemoryUsage();
    double GetDiskUsage();
    List<SystemStats> GetHistory(int sampleCount = 10);
}
