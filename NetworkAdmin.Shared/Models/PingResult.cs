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
        public int Sent { get; set; }
        public int Received { get; set; }
        public int Lost => Sent - Received;
        public double PacketLossPercent => Sent == 0 ? 0 : Lost * 100.0 / Sent;
        public double? AverageResponseTimeMs { get; set; }
        public List<PingAttempt> Attempts { get; set; } = new();
    }

    public sealed class PingAttempt
    {
        public int Number { get; set; }
        public bool Success { get; set; }
        public long? ResponseTimeMs { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
