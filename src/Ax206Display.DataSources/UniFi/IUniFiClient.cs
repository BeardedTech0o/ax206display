namespace Ax206Display.DataSources.UniFi;

public interface IUniFiClient
{
    Task LoginAsync(string username, string password, string? totpSecret = null, CancellationToken cancellationToken = default);

    Task<UniFiSiteStatus> GetSiteHealthAsync(string site = "default", CancellationToken cancellationToken = default);
}
