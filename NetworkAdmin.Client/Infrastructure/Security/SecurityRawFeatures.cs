namespace NetworkAdmin.Client.Infrastructure.Security;

internal sealed record SecurityRawFeatures(
    int EventCode,
    string Timestamp,
    string User,
    string ImagePath,
    string ProcessName,
    string ParentImage,
    string ParentName,
    string CommandLine,
    int DestinationPort);
