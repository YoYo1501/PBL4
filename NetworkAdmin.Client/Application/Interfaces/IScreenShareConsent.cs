namespace NetworkAdmin.Client.Application.Interfaces;

public interface IScreenShareConsent
{
    Task<bool> RequestConsentAsync(string serverName, CancellationToken cancellationToken);
    void SetSharing(bool active, Action stop);
}
