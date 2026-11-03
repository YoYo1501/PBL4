namespace NetworkAdmin.Client.Application.Interfaces;

public interface IScreenShareConsent
{
    Task<bool> RequestConsentAsync(string serverName, CancellationToken cancellationToken);
    Task<bool> RequestControlConsentAsync(string serverName, CancellationToken cancellationToken);
    void SetSharing(bool active, Action stop);
    void SetControlState(bool active, Action stop);
}
