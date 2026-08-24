namespace NetworkAdminTool.Models
{
    /// <summary>
    /// Snapshot số liệu hệ thống tại 1 thời điểm — dùng cho MonitorForm.
    /// </summary>
    public class SystemStats
    {
        public float CpuUsagePercent { get; set; }
        public float RamUsagePercent { get; set; }
    }
}
