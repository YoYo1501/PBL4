using NetworkAdmin.Shared.Models;

namespace NetworkAdmin.Client.Application.Interfaces;

public interface ISystemMonitorService
{
    SystemStats GetSystemStats();
    double GetCpuUsage();
    double GetMemoryUsage();
    double GetDiskUsage();
    List<SystemStats> GetHistory(int sampleCount = 10);
}
