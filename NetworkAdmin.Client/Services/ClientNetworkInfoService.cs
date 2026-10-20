using NetworkAdmin.Shared.Messages;
using NetworkAdminTool.Interfaces;
using NetworkAdmin.Shared.Models;

namespace NetworkAdmin.Client.Services;

internal sealed class ClientNetworkInfoService
{
    private readonly INetworkInfoService _networkInfoService;
    private readonly ServerConnection _connection;
    private ClientNetworkInfo? _lastSnapshot;

    public ClientNetworkInfoService(INetworkInfoService networkInfoService, ServerConnection connection)
    {
        _networkInfoService = networkInfoService;
        _connection = connection;
    }

    public async Task SendSnapshotAsync(CancellationToken cancellationToken)
    {
        var info = CreateSnapshot();

        await _connection.SendAsync(MessageType.NetworkInfo, info, cancellationToken).ConfigureAwait(false);
        _lastSnapshot = info;
        Console.WriteLine($"NetworkInfo sent: {info.Hostname}, interfaces: {info.Interfaces.Count}");
    }

    public async Task RunAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var current = CreateSnapshot();
                if (_lastSnapshot is not null && SnapshotsEqual(_lastSnapshot, current))
                    continue;

                await _connection.SendAsync(MessageType.NetworkInfo, current, cancellationToken).ConfigureAwait(false);
                _lastSnapshot = current;
                Console.WriteLine($"NetworkInfo changed: {current.Hostname}, interfaces: {current.Interfaces.Count}");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private ClientNetworkInfo CreateSnapshot() => new()
    {
        Hostname = Environment.MachineName,
        Interfaces = _networkInfoService.GetAvailableNetworkInterfaces()
    };

    private static bool SnapshotsEqual(ClientNetworkInfo left, ClientNetworkInfo right)
    {
        var leftInterfaces = left.Interfaces
            .Select(ToComparable)
            .OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.IpAddress, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.MacAddress, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.Gateway, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.Subnet, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var rightInterfaces = right.Interfaces
            .Select(ToComparable)
            .OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.IpAddress, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.MacAddress, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.Gateway, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.Subnet, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return leftInterfaces.SequenceEqual(rightInterfaces);
    }

    private static ComparableInterface ToComparable(NetworkInterfaceInfo networkInterface) =>
        new(networkInterface.Name, networkInterface.IpAddress, networkInterface.MacAddress,
            networkInterface.Gateway, networkInterface.Subnet);

    private readonly record struct ComparableInterface(
        string Name,
        string IpAddress,
        string MacAddress,
        string Gateway,
        string Subnet);
}
