using System.Globalization;
using System.Security.Cryptography;
using Ax206Display.Config.Models;
using Ax206Display.Config.Secrets;
using Ax206Display.Config.Services;
using Ax206Display.DataSources.Auth;
using Ax206Display.DataSources.Http;
using Ax206Display.DataSources.PiHole;
using Ax206Display.DataSources.Proxmox;
using Ax206Display.DataSources.UniFi;

namespace Ax206Display.Engine.Integrations;

/// <summary>
/// What a settings form submits for one integration. Blank secrets mean
/// "keep the saved one", so a form never has to echo a password back.
/// Pi-hole takes Host/Port/UseHttps instead of a free-text BaseUrl (see
/// <see cref="HostNormalizer"/> for why); the others take BaseUrl.
/// </summary>
public sealed record IntegrationSetupRequest
{
    public string? BaseUrl { get; init; }

    public string? Host { get; init; }

    public int? Port { get; init; }

    public bool UseHttps { get; init; }

    public string? Username { get; init; }

    public string? Realm { get; init; }

    public string? Site { get; init; }

    public string? PinnedCertificateSha256Thumbprint { get; init; }

    public string? Secret { get; init; }

    public string? TotpSecret { get; init; }
}

public sealed record IntegrationSetupResult(bool Success, string Message);

/// <summary>
/// Tests an integration's settings against the live service and, only if the
/// test passes, saves them (credentials into the <see cref="SecretStore"/>,
/// everything else into config). The same test-before-save flow the Windows
/// Integrations window uses, exposed for front ends that aren't WPF.
/// </summary>
public sealed class IntegrationSetupService : IDisposable
{
    public const string ProxmoxKind = "proxmox";
    public const string PiHoleKind = "pihole";
    public const string UniFiKind = "unifi";

    public static readonly IReadOnlyList<string> SupportedKinds = [ProxmoxKind, PiHoleKind, UniFiKind];

    private readonly ConfigService _configService;
    private readonly SecretStore _secretStore;
    private readonly SemaphoreSlim _secretLock = new(1, 1);

    public IntegrationSetupService(ConfigService configService, SecretStore secretStore)
    {
        _configService = configService;
        _secretStore = secretStore;
    }

    public async Task<IntegrationSetupResult> TestAndSaveAsync(string kind, IntegrationSetupRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return kind switch
        {
            ProxmoxKind => await TestAndSaveProxmoxAsync(request, cancellationToken),
            PiHoleKind => await TestAndSavePiHoleAsync(request, cancellationToken),
            UniFiKind => await TestAndSaveUniFiAsync(request, cancellationToken),
            _ => new IntegrationSetupResult(false, $"Unknown integration '{kind}'."),
        };
    }

    public async Task<bool> RemoveAsync(string kind, CancellationToken cancellationToken = default)
    {
        IntegrationConfig? removed = null;
        await _configService.UpdateAsync(
            config =>
            {
                removed = config.Integrations.FirstOrDefault(i => i.Kind == kind);
                return removed is null
                    ? config
                    : config with { Integrations = config.Integrations.Where(i => i.Kind != kind).ToList() };
            },
            cancellationToken);

        if (removed is null)
        {
            return false;
        }

        await _secretLock.WaitAsync(cancellationToken);
        try
        {
            await _secretStore.LoadAsync(cancellationToken);
            foreach (var key in new[] { removed.SecretKey, removed.TotpSecretKey })
            {
                if (key is not null)
                {
                    _secretStore.RemoveSecret(key);
                }
            }

            await _secretStore.SaveAsync(cancellationToken);
        }
        finally
        {
            _secretLock.Release();
        }

        return true;
    }

    /// <summary>
    /// Fetches the leaf certificate a host presents, for pinning a self-signed
    /// controller. Returns null when the host doesn't speak TLS. The caller
    /// must show the thumbprint to the user and let them decide to trust it.
    /// </summary>
    public static async Task<string?> DetectCertificateThumbprintAsync(string baseUrl, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(baseUrl?.Trim(), UriKind.Absolute, out var uri))
        {
            throw new FormatException("Enter a valid host URL first.");
        }

        using var certificate = await TlsCertificateProbe.FetchCertificateAsync(uri.Host, uri.Port, cancellationToken);
        return certificate?.GetCertHashString(HashAlgorithmName.SHA256);
    }

    /// <summary>Builds Pi-hole's base URL from its separate host/port/HTTPS fields.</summary>
    public static string BuildPiHoleBaseUrl(string host, int port, bool useHttps) =>
        string.Create(CultureInfo.InvariantCulture, $"{(useHttps ? "https" : "http")}://{HostNormalizer.Normalize(host)}:{port}");

    private async Task<IntegrationSetupResult> TestAndSaveProxmoxAsync(IntegrationSetupRequest request, CancellationToken cancellationToken)
    {
        var baseUrl = request.BaseUrl?.Trim();
        var username = request.Username?.Trim();
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(username))
        {
            return new IntegrationSetupResult(false, "Host URL and username are required.");
        }

        var realm = string.IsNullOrWhiteSpace(request.Realm) ? "pam" : request.Realm.Trim();
        var existing = await FindExistingAsync(ProxmoxKind, cancellationToken);

        var password = await ResolveSecretAsync(request.Secret, existing?.SecretKey, cancellationToken);
        if (string.IsNullOrEmpty(password))
        {
            return new IntegrationSetupResult(false, "Password is required.");
        }

        var integrationId = existing?.Id ?? Guid.NewGuid().ToString("N");
        var secretKey = existing?.SecretKey ?? $"integration.{integrationId}";
        var candidate = new IntegrationConfig
        {
            Id = integrationId,
            Kind = ProxmoxKind,
            BaseUrl = baseUrl,
            Username = username,
            Realm = realm,
            SecretKey = secretKey,
            PinnedCertificateSha256Thumbprint = NullIfBlank(request.PinnedCertificateSha256Thumbprint),
        };

        try
        {
            using var httpClient = IntegrationHttpClientFactory.Create(candidate, enableCookies: false);
            var client = new ProxmoxClient(httpClient);
            await client.LoginAsync(username, password, realm, cancellationToken);
            var guests = await client.GetGuestStatusesAsync(cancellationToken);

            await SaveAsync(candidate, [(secretKey, password)], cancellationToken);
            return new IntegrationSetupResult(true, $"Connected - found {guests.Count} guest(s). Saved.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new IntegrationSetupResult(false, "Failed: " + ex.Message);
        }
    }

    private async Task<IntegrationSetupResult> TestAndSavePiHoleAsync(IntegrationSetupRequest request, CancellationToken cancellationToken)
    {
        var host = HostNormalizer.Normalize(request.Host ?? string.Empty);
        if (string.IsNullOrEmpty(host))
        {
            return new IntegrationSetupResult(false, "Host is required.");
        }

        if (request.Port is not { } port || port is < 1 or > 65535)
        {
            return new IntegrationSetupResult(false, "Port must be a number between 1 and 65535.");
        }

        var existing = await FindExistingAsync(PiHoleKind, cancellationToken);
        var appPassword = await ResolveSecretAsync(request.Secret, existing?.SecretKey, cancellationToken);
        if (string.IsNullOrEmpty(appPassword))
        {
            return new IntegrationSetupResult(false, "App password is required.");
        }

        var integrationId = existing?.Id ?? Guid.NewGuid().ToString("N");
        var secretKey = existing?.SecretKey ?? $"integration.{integrationId}";
        var candidate = new IntegrationConfig
        {
            Id = integrationId,
            Kind = PiHoleKind,
            BaseUrl = BuildPiHoleBaseUrl(host, port, request.UseHttps),
            SecretKey = secretKey,
            PinnedCertificateSha256Thumbprint = NullIfBlank(request.PinnedCertificateSha256Thumbprint),
        };

        try
        {
            using var httpClient = IntegrationHttpClientFactory.Create(candidate, enableCookies: false);
            var client = new PiHoleClient(httpClient);
            await client.LoginAsync(appPassword, cancellationToken);
            var summary = await client.GetSummaryAsync(cancellationToken);

            await SaveAsync(candidate, [(secretKey, appPassword)], cancellationToken);
            return new IntegrationSetupResult(true, $"Connected - {summary.DnsQueriesToday} DNS queries today, {summary.AdsBlockedToday} blocked. Saved.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new IntegrationSetupResult(false, "Failed: " + ex.Message);
        }
    }

    private async Task<IntegrationSetupResult> TestAndSaveUniFiAsync(IntegrationSetupRequest request, CancellationToken cancellationToken)
    {
        var baseUrl = request.BaseUrl?.Trim();
        var username = request.Username?.Trim();
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(username))
        {
            return new IntegrationSetupResult(false, "Host URL and username are required.");
        }

        var site = string.IsNullOrWhiteSpace(request.Site) ? "default" : request.Site.Trim();
        var existing = await FindExistingAsync(UniFiKind, cancellationToken);

        var password = await ResolveSecretAsync(request.Secret, existing?.SecretKey, cancellationToken);
        if (string.IsNullOrEmpty(password))
        {
            return new IntegrationSetupResult(false, "Password is required.");
        }

        var totpSecret = await ResolveSecretAsync(request.TotpSecret, existing?.TotpSecretKey, cancellationToken);

        var integrationId = existing?.Id ?? Guid.NewGuid().ToString("N");
        var secretKey = existing?.SecretKey ?? $"integration.{integrationId}";
        var totpSecretKey = totpSecret is null ? null : existing?.TotpSecretKey ?? $"integration.{integrationId}.totp";
        var candidate = new IntegrationConfig
        {
            Id = integrationId,
            Kind = UniFiKind,
            BaseUrl = baseUrl,
            Username = username,
            Site = site,
            SecretKey = secretKey,
            TotpSecretKey = totpSecretKey,
            PinnedCertificateSha256Thumbprint = NullIfBlank(request.PinnedCertificateSha256Thumbprint),
        };

        try
        {
            using var httpClient = IntegrationHttpClientFactory.Create(candidate, enableCookies: true);
            var client = new UniFiClient(httpClient);
            await client.LoginAsync(username, password, totpSecret, cancellationToken);
            var status = await client.GetSiteHealthAsync(site, cancellationToken);

            var secrets = new List<(string Key, string Value)> { (secretKey, password) };
            if (totpSecretKey is not null && totpSecret is not null)
            {
                secrets.Add((totpSecretKey, totpSecret));
            }

            await SaveAsync(candidate, secrets, cancellationToken);
            return new IntegrationSetupResult(true, $"Connected - {status.Subsystems.Count} subsystem(s) reporting. Saved.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var hint = string.IsNullOrEmpty(totpSecret) ? string.Empty : ComputeTotpHint(totpSecret);
            return new IntegrationSetupResult(false, "Failed: " + ex.Message + hint);
        }
    }

    private static string ComputeTotpHint(string totpSecret)
    {
        try
        {
            var code = TotpGenerator.GenerateCode(totpSecret);
            return $" (The TOTP secret computes {code} right now - compare it against your authenticator app at this same moment. If they don't match, the secret itself is wrong.)";
        }
        catch (FormatException)
        {
            return " (The TOTP secret isn't valid base32, so no code could be computed from it - re-check what was pasted.)";
        }
    }

    private async Task<IntegrationConfig?> FindExistingAsync(string kind, CancellationToken cancellationToken)
    {
        var config = await _configService.LoadAsync(cancellationToken);
        return config.Integrations.FirstOrDefault(i => i.Kind == kind);
    }

    private async Task<string?> ResolveSecretAsync(string? enteredValue, string? existingKey, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(enteredValue))
        {
            return enteredValue;
        }

        if (existingKey is null)
        {
            return null;
        }

        await _secretStore.LoadAsync(cancellationToken);
        return _secretStore.GetSecret(existingKey);
    }

    private async Task SaveAsync(IntegrationConfig integration, IReadOnlyList<(string Key, string Value)> secrets, CancellationToken cancellationToken)
    {
        // Secrets first: the integration only becomes visible to the pump
        // services once config names it, and by then its secret must exist.
        await _secretLock.WaitAsync(cancellationToken);
        try
        {
            await _secretStore.LoadAsync(cancellationToken);
            foreach (var (key, value) in secrets)
            {
                _secretStore.SetSecret(key, value);
            }

            await _secretStore.SaveAsync(cancellationToken);
        }
        finally
        {
            _secretLock.Release();
        }

        await _configService.UpdateAsync(
            config => config with { Integrations = [.. config.Integrations.Where(i => i.Kind != integration.Kind), integration] },
            cancellationToken);
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public void Dispose() => _secretLock.Dispose();
}
