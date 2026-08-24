namespace NetworkAdminTool.Models
{
    /// <summary>
    /// Dữ liệu thuần mô tả 1 thiết bị/host trong mạng LAN sau khi scan.
    /// </summary>
    public class NetworkDevice
    {
        public string IpAddress { get; set; } = string.Empty;
        public string MacAddress { get; set; } = string.Empty;
        public bool IsOnline { get; set; }
        public long? ResponseTimeMs { get; set; }
    }
}
