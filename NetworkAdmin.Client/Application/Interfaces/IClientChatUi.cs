namespace NetworkAdmin.Client.Application.Interfaces;

/// <summary>Attaches a chat session to the Client's presentation layer.</summary>
internal interface IClientChatUi
{
    void Attach(IClientChatSession session);
    void Detach(IClientChatSession session);
}
