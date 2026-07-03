namespace Ax206Display.DataSources.UniFi;

public interface IUniFiClient
{
    Task LoginAsync(string username, string password, CancellationToken cancellationToken = default);

    Task<UniFiSiteStatus> GetSiteHealthAsync(string site = "default", CancellationToken cancellationToken = default);
}
