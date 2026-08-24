using NetworkAdminTool.Interfaces;

namespace NetworkAdminTool.Services
{
    /// <summary>
    /// Ghi log hoạt động (scan, ping, lỗi...) ra thư mục /Logs.
    /// </summary>
    public class LoggerService : ILoggerService
    {
        private readonly string _logFolder;

        public LoggerService(string logFolder = "Logs")
        {
            _logFolder = logFolder;
            Directory.CreateDirectory(_logFolder);
        }

        public void Log(string message)
        {
            Write(message);
        }

        public void LogError(string message)
        {
            Write($"ERROR: {message}");
        }

        public void LogWarning(string message)
        {
            Write($"WARNING: {message}");
        }

        public void Write(string message)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            var filePath = Path.Combine(_logFolder, $"{DateTime.Now:yyyy-MM-dd}.log");
            File.AppendAllText(filePath, line + Environment.NewLine);
        }
    }
}
