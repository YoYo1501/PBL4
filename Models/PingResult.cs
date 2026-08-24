namespace NetworkAdminTool.Models
{
    /// <summary>
    /// Dữ liệu thuần mô tả kết quả sau khi ping một host.
    /// </summary>
    public class PingResult
    {
        public string Host { get; set; } = string.Empty;
        public bool Success { get; set; }
        public long RoundtripTimeMs { get; set; }
        public string StatusMessage { get; set; } = string.Empty;
    }
}
