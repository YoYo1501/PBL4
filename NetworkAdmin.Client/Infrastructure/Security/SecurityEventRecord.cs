namespace NetworkAdmin.Client.Infrastructure.Security;

internal sealed record SecurityEventRecord(
    string EventId,
    DateTime DetectionTimestampUtc,
    string Source,
    int EventCode,
    string UserName,
    string ProcessName,
    string ParentProcessName,
    string ImagePath,
    string CommandLine,
    int DestinationPort);
