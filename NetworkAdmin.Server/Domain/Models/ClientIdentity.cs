namespace NetworkAdmin.Server.Domain.Models;

/// <summary>Stable identity of a managed client, independent of its current network session.</summary>
public sealed record ClientIdentity(string ClientId, string DisplayName);
