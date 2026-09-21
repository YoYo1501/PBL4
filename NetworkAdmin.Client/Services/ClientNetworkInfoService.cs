using NetworkAdmin.Shared.Messages;
using NetworkAdminTool.Interfaces;
using NetworkAdminTool.Models;

namespace NetworkAdmin.Client.Services;

internal sealed class ClientNetworkInfoService
{
    private readonly INetworkInfoService _networkInfoService;
    private readonly ServerConnection _connection;

    public ClientNetworkInfoService(INetworkInfoService networkInfoService, ServerConnection connection)
    {
        _networkInfoService = networkInfoService;
        _connection = connection;
    }

    public async Task SendSnapshotAsync(CancellationToken cancellationToken)
    {
        var info = new ClientNetworkInfo
        {
            Hostname = Environment.MachineName,
            Interfaces = _networkInfoService.GetAvailableNetworkInterfaces()
        };

        await _connection.SendAsync(MessageType.NetworkInfo, info, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"NetworkInfo sent: {info.Hostname}, interfaces: {info.Interfaces.Count}");
    }
}
