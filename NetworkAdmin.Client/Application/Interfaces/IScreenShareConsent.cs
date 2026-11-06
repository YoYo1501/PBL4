namespace NetworkAdmin.Client.Application.Interfaces;

/// <summary>Client-side consent and local status UI for screen viewing requests.</summary>
internal interface IScreenShareConsent
{
    Task<bool> RequestConsentAsync(string serverName, CancellationToken cancellationToken);
    void SetSharing(bool active, Action stop);
}
