using NetworkAdmin.Shared.Messages;

namespace NetworkAdmin.Client.Application.Interfaces;

internal interface IClientChatUi
{
    void BindTransport(Func<string, CancellationToken, Task> sendAsync);
    void ShowMessage(RemoteChatMessage message);
    void SetConnectionState(bool connected, string? detail = null);
}
