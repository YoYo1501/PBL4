using System.Net;
using System.Net.Sockets;
using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Protocol;
using Xunit;

namespace NetworkAdmin.Shared.Tests;

public sealed class RemoteAdministrationProtocolTests
{
    [Fact]
    public async Task Remote_command_and_result_round_trip_over_json_line_protocol()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        using var client = new TcpClient();
        var connectTask = client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using var server = await listener.AcceptTcpClientAsync();
        await connectTask;
        listener.Stop();

        await using var clientStream = client.GetStream();
        await using var serverStream = server.GetStream();
        const string requestId = "request-1";

        await JsonLineProtocol.SendAsync(
            clientStream,
            MessageType.RemoteAdministrationCommand,
            requestId,
            new RemoteAdministrationCommandMessage
            {
                Command = RemoteAdministrationCommandType.GetAgentInfo,
                AuthorizationToken = "test-token"
            });

        var request = await JsonLineProtocol.ReceiveAsync(serverStream);
        Assert.NotNull(request);
        Assert.Equal(MessageType.RemoteAdministrationCommand, request!.Type);
        Assert.Equal(requestId, request.RequestId);
        var command = JsonLineProtocol.GetPayload<RemoteAdministrationCommandMessage>(request);
        Assert.Equal(RemoteAdministrationCommandType.GetAgentInfo, command!.Command);

        await JsonLineProtocol.SendAsync(
            serverStream,
            MessageType.RemoteAdministrationResult,
            requestId,
            new RemoteAdministrationResultMessage
            {
                Command = command.Command,
                Status = RemoteAdministrationCommandStatus.Completed,
                ClientId = "client-1",
                Result = "read-only result",
                TimestampUtc = DateTime.UtcNow
            });

        var response = await JsonLineProtocol.ReceiveAsync(clientStream);
        Assert.NotNull(response);
        Assert.Equal(MessageType.RemoteAdministrationResult, response!.Type);
        Assert.Equal(requestId, response.RequestId);
        var result = JsonLineProtocol.GetPayload<RemoteAdministrationResultMessage>(response);
        Assert.Equal(RemoteAdministrationCommandStatus.Completed, result!.Status);
        Assert.Equal("read-only result", result.Result);
    }
}
