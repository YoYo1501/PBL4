using NetworkAdmin.Shared.Messages;

namespace NetworkAdmin.Client.Application.Interfaces;

internal interface IRemoteInputService
{
    bool Execute(RemoteInputEventMessage input);
    void ReleaseAll();
}
