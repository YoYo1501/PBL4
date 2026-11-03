using NetworkAdmin.Shared.Policies;
using NetworkAdmin.Server.Domain.Models;

namespace NetworkAdmin.Server.Application.Models;

public sealed record InterfaceSnapshot(string Name, string IpAddress, string MacAddress, string Gateway,
    string Subnet, string Description = "")
{
    public bool IsVirtual => NetworkInterfacePolicy.IsVirtual(Name, Description);
}

public sealed record ScanHostSnapshot(string IpAddress, string MacAddress, string Status,
    string ManagedClient, string Hostname);

public sealed record ClientSnapshot(string SessionId, string ClientId, string Hostname, string RemoteAddress,
    bool Connected, DateTime ConnectedAt, DateTime LastSeen, DateTime? LastTelemetryAt,
    float? Cpu, float? Ram, IReadOnlyList<InterfaceSnapshot> Interfaces)
{
    public ClientIdentity Identity => new(ClientId, Hostname);
    public InterfaceSnapshot? PrimaryInterface => Interfaces
        .Where(i => !i.IsVirtual && NetworkInterfacePolicy.HasUsableIpv4(i.IpAddress))
        .OrderByDescending(i => NetworkInterfacePolicy.HasUsableIpv4(i.Gateway)).FirstOrDefault();
    public bool HasFreshTelemetry(DateTime utcNow) => Connected && LastTelemetryAt is DateTime at
        && utcNow - at <= TimeSpan.FromSeconds(10);
    public string IpAddress => PrimaryInterface?.IpAddress ?? RemoteAddress;
    public string MacAddress => PrimaryInterface?.MacAddress ?? "";
    public override string ToString() => $"{Hostname} | {IpAddress} | {(Connected ? "Connected" : "Disconnected")} | SessionId: {SessionId}";
}
