namespace NetworkAdmin.Shared.Messages;

public enum MessageType
{
    Hello = 1,
    HelloAck = 2,
    SystemStats = 3,
    NetworkInfo = 4,
    PingRequest = 5,
    PingResult = 6,
    ScanRequest = 7,
    ScanResult = 8
}
