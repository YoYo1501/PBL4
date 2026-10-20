namespace NetworkAdminTool.Interfaces;

public interface ILoggerService
{
    void Log(string message);
    void LogError(string message);
    void LogWarning(string message);
}
