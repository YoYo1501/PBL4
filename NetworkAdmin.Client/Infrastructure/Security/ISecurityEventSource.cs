namespace NetworkAdmin.Client.Infrastructure.Security;

internal interface ISecurityEventSource
{
    IAsyncEnumerable<SecurityEventRecord> ReadAsync(CancellationToken cancellationToken);
}
