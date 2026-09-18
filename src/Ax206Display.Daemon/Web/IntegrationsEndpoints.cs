using Ax206Display.Config.Models;
using Ax206Display.Config.Secrets;
using Ax206Display.Config.Services;
using Ax206Display.DataSources.Auth;
using Ax206Display.DataSources.Http;
using Ax206Display.DataSources.PiHole;
using Ax206Display.DataSources.Proxmox;
using Ax206Display.DataSources.UniFi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ax206Display.Daemon.Web;

/// <summary>
/// Web counterpart to Ax206Display.App's IntegrationsWindow: one "Test &amp;
/// Save" endpoint per integration kind that logs in for real with the
/// entered credentials before ever saving them (same DataSources clients
/// the Windows app and the live pump services use), a saved
/// password/API token/TOTP secret is never sent back to the browser (the
/// *Request DTOs' secret fields are nullable - null/omitted means "keep
/// whatever's already saved", mirroring IntegrationsWindow's "leave blank to
/// keep the saved one"), and a certificate-detection endpoint for the same
/// trust-on-first-use pinning flow. There is deliberately no generic
/// "PUT /api/integrations/{kind}" - saving without a successful login test
/// first would let a typo silently break a pump service's next poll instead
/// of failing immediately where the user can see it.
/// </summary>
public static class IntegrationsEndpoints
{
    private const string ProxmoxKind = "proxmox";
    private const string PiHoleKind = "pihole";
    private const string UniFiKind = "unifi";

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/integrations", GetIntegrationsAsync);
        app.MapPost("/api/integrations/proxmox/test-and-save", TestAndSaveProxmoxAsync);
        app.MapPost("/api/integrations/pihole/test-and-save", TestAndSavePiHoleAsync);
        app.MapPost("/api/integrations/unifi/test-and-save", TestAndSaveUniFiAsync);
        app.MapDelete("/api/integrations/{kind}", RemoveIntegrationAsync);
        app.MapPost("/api/integrations/detect-certificate", DetectCertificateAsync);
    }

    private static async Task<IResult> GetIntegrationsAsync(ConfigService configService, CancellationToken cancellationToken)
    {
        var config = await configService.LoadAsync(cancellationToken);

        object? Describe(string kind)
        {
            var integration = config.Integrations.FirstOrDefault(i => i.Kind == kind);
            if (integration is null)
            {
                return new { configured = false };
            }

            return kind switch
            {
                ProxmoxKind => new
                {
                    configured = true,
                    baseUrl = integration.BaseUrl,
                    username = integration.Username,
                    realm = integration.Realm ?? "pam",
                    pinnedCertificateSha256Thumbprint = integration.PinnedCertificateSha256Thumbprint,
                },
                PiHoleKind => DescribePiHole(integration),
                UniFiKind => new
                {
                    configured = true,
                    baseUrl = integration.BaseUrl,
                    username = integration.Username,
                    site = integration.Site ?? "default",
                    hasTotpSecret = integration.TotpSecretKey is not null,
                    pinnedCertificateSha256Thumbprint = integration.PinnedCertificateSha256Thumbprint,
                },
                _ => new { configured = true },
            };
        }

        return Results.Ok(new
        {
            proxmox = Describe(ProxmoxKind),
            pihole = Describe(PiHoleKind),
            unifi = Describe(UniFiKind),
        });
    }

    private static object DescribePiHole(IntegrationConfig integration)
    {
        if (!Uri.TryCreate(integration.BaseUrl, UriKind.Absolute, out var uri))
        {
            return new { configured = true, host = integration.BaseUrl, port = 80, useHttps = false, pinnedCertificateSha256Thumbprint = integration.PinnedCertificateSha256Thumbprint };
        }

        return new
        {
            configured = true,
            host = uri.Host,
            port = uri.Port,
            useHttps = uri.Scheme == Uri.UriSchemeHttps,
            pinnedCertificateSha256Thumbprint = integration.PinnedCertificateSha256Thumbprint,
        };
    }

    private static async Task<IResult> TestAndSaveProxmoxAsync(ProxmoxTestRequest request, ConfigService configService, SecretStore secretStore, CancellationToken cancellationToken)
    {
        var baseUrl = request.BaseUrl?.Trim() ?? string.Empty;
        var username = request.Username?.Trim() ?? string.Empty;
        var realm = string.IsNullOrWhiteSpace(request.Realm) ? "pam" : request.Realm.Trim();
        var thumbprint = string.IsNullOrWhiteSpace(request.PinnedCertificateSha256Thumbprint) ? null : request.PinnedCertificateSha256Thumbprint.Trim();

        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(username))
        {
            return Results.BadRequest(new { detail = "Host URL and username are required." });
        }

        var config = await configService.LoadAsync(cancellationToken);
        var existing = config.Integrations.FirstOrDefault(i => i.Kind == ProxmoxKind);

        var password = await ResolveSecretAsync(secretStore, request.Password, existing?.SecretKey, cancellationToken);
        if (string.IsNullOrEmpty(password))
        {
            return Results.BadRequest(new { detail = "Password is required." });
        }

        var integrationId = existing?.Id ?? Guid.NewGuid().ToString("N");
        var secretKey = existing?.SecretKey ?? $"integration.{integrationId}";

        var testConfig = new IntegrationConfig
        {
            Id = integrationId,
            Kind = ProxmoxKind,
            BaseUrl = baseUrl,
            Username = username,
            Realm = realm,
            SecretKey = secretKey,
            PinnedCertificateSha256Thumbprint = thumbprint,
        };

        try
        {
            using var httpClient = IntegrationHttpClientFactory.Create(testConfig, enableCookies: false);
            var client = new ProxmoxClient(httpClient);
            await client.LoginAsync(username, password, realm, cancellationToken);
            var guests = await client.GetGuestStatusesAsync(cancellationToken);

            await SaveIntegrationAsync(configService, secretStore, config, testConfig, secretKey, password, cancellationToken: cancellationToken);
            return Results.Ok(new { message = $"Connected - found {guests.Count} guest(s). Saved." });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { detail = $"Failed: {ex.Message}" });
        }
    }

    private static async Task<IResult> TestAndSavePiHoleAsync(PiHoleTestRequest request, ConfigService configService, SecretStore secretStore, CancellationToken cancellationToken)
    {
        var host = HostNormalizer.Normalize(request.Host ?? string.Empty);
        var thumbprint = string.IsNullOrWhiteSpace(request.PinnedCertificateSha256Thumbprint) ? null : request.PinnedCertificateSha256Thumbprint.Trim();

        if (string.IsNullOrEmpty(host))
        {
            return Results.BadRequest(new { detail = "Host is required." });
        }

        if (request.Port is < 1 or > 65535)
        {
            return Results.BadRequest(new { detail = "Port must be a number between 1 and 65535." });
        }

        var scheme = request.UseHttps ? "https" : "http";
        var baseUrl = $"{scheme}://{host}:{request.Port}";

        var config = await configService.LoadAsync(cancellationToken);
        var existing = config.Integrations.FirstOrDefault(i => i.Kind == PiHoleKind);

        var appPassword = await ResolveSecretAsync(secretStore, request.AppPassword, existing?.SecretKey, cancellationToken);
        if (string.IsNullOrEmpty(appPassword))
        {
            return Results.BadRequest(new { detail = "App password is required." });
        }

        var integrationId = existing?.Id ?? Guid.NewGuid().ToString("N");
        var secretKey = existing?.SecretKey ?? $"integration.{integrationId}";

        var testConfig = new IntegrationConfig
        {
            Id = integrationId,
            Kind = PiHoleKind,
            BaseUrl = baseUrl,
            SecretKey = secretKey,
            PinnedCertificateSha256Thumbprint = thumbprint,
        };

        try
        {
            using var httpClient = IntegrationHttpClientFactory.Create(testConfig, enableCookies: false);
            var client = new PiHoleClient(httpClient);
            await client.LoginAsync(appPassword, cancellationToken);
            var summary = await client.GetSummaryAsync(cancellationToken);

            await SaveIntegrationAsync(configService, secretStore, config, testConfig, secretKey, appPassword, cancellationToken: cancellationToken);
            return Results.Ok(new { message = $"Connected - {summary.DnsQueriesToday} DNS queries today, {summary.AdsBlockedToday} blocked. Saved." });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { detail = $"Failed: {ex.Message}" });
        }
    }

    private static async Task<IResult> TestAndSaveUniFiAsync(UniFiTestRequest request, ConfigService configService, SecretStore secretStore, CancellationToken cancellationToken)
    {
        var baseUrl = request.BaseUrl?.Trim() ?? string.Empty;
        var username = request.Username?.Trim() ?? string.Empty;
        var site = string.IsNullOrWhiteSpace(request.Site) ? "default" : request.Site.Trim();
        var thumbprint = string.IsNullOrWhiteSpace(request.PinnedCertificateSha256Thumbprint) ? null : request.PinnedCertificateSha256Thumbprint.Trim();

        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(username))
        {
            return Results.BadRequest(new { detail = "Host URL and username are required." });
        }

        var config = await configService.LoadAsync(cancellationToken);
        var existing = config.Integrations.FirstOrDefault(i => i.Kind == UniFiKind);

        var password = await ResolveSecretAsync(secretStore, request.Password, existing?.SecretKey, cancellationToken);
        if (string.IsNullOrEmpty(password))
        {
            return Results.BadRequest(new { detail = "Password is required." });
        }

        // Null when the account has no 2FA - the common case. See
        // IntegrationConfig.TotpSecretKey and UniFiClient.LoginAsync.
        var totpSecret = await ResolveSecretAsync(secretStore, request.TotpSecret, existing?.TotpSecretKey, cancellationToken);

        var integrationId = existing?.Id ?? Guid.NewGuid().ToString("N");
        var secretKey = existing?.SecretKey ?? $"integration.{integrationId}";
        var totpSecretKey = totpSecret is null ? null : existing?.TotpSecretKey ?? $"integration.{integrationId}.totp";

        var testConfig = new IntegrationConfig
        {
            Id = integrationId,
            Kind = UniFiKind,
            BaseUrl = baseUrl,
            Username = username,
            Site = site,
            SecretKey = secretKey,
            TotpSecretKey = totpSecretKey,
            PinnedCertificateSha256Thumbprint = thumbprint,
        };

        try
        {
            using var httpClient = IntegrationHttpClientFactory.Create(testConfig, enableCookies: true);
            var client = new UniFiClient(httpClient);
            await client.LoginAsync(username, password, totpSecret, cancellationToken);
            var status = await client.GetSiteHealthAsync(site, cancellationToken);

            var extraSecret = totpSecretKey is not null && totpSecret is not null ? (totpSecretKey, totpSecret) : ((string, string)?)null;
            await SaveIntegrationAsync(configService, secretStore, config, testConfig, secretKey, password, extraSecret, cancellationToken);
            return Results.Ok(new { message = $"Connected - {status.Subsystems.Count} subsystem(s) reporting. Saved." });
        }
        catch (Exception ex)
        {
            // See IntegrationsWindow.OnUniFiTestAndSaveClick: UniFi OS
            // reports a wrong TOTP code with the exact same error as a wrong
            // password, so surface what the entered secret computes right
            // now - a mismatch against the user's authenticator app means
            // the secret itself is wrong, not the password.
            var totpHint = string.IsNullOrEmpty(totpSecret) ? string.Empty : TryComputeTotpHint(totpSecret);
            return Results.BadRequest(new { detail = $"Failed: {ex.Message}{totpHint}" });
        }
    }

    private static string TryComputeTotpHint(string totpSecret)
    {
        try
        {
            var code = TotpGenerator.GenerateCode(totpSecret);
            return $" (The TOTP secret currently entered computes {code} right now - compare it against your authenticator app at this same moment. If they don't match, the saved secret itself is wrong.)";
        }
        catch (FormatException)
        {
            return " (The TOTP secret currently entered isn't valid base32, so no code could even be computed from it - re-check what was pasted into that field.)";
        }
    }

    private static async Task<IResult> RemoveIntegrationAsync(string kind, ConfigService configService, SecretStore secretStore, CancellationToken cancellationToken)
    {
        var config = await configService.LoadAsync(cancellationToken);
        var existing = config.Integrations.FirstOrDefault(i => i.Kind == kind);
        if (existing is null)
        {
            return Results.NotFound();
        }

        if (existing.SecretKey is { } secretKey)
        {
            await secretStore.LoadAsync(cancellationToken);
            secretStore.RemoveSecret(secretKey);
            if (existing.TotpSecretKey is { } totpSecretKey)
            {
                secretStore.RemoveSecret(totpSecretKey);
            }

            await secretStore.SaveAsync(cancellationToken);
        }

        var updated = config.Integrations.Where(i => i.Kind != kind).ToList();
        await configService.SaveAsync(config with { Integrations = updated }, cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> DetectCertificateAsync(DetectCertificateRequest request, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(request.BaseUrl?.Trim(), UriKind.Absolute, out var uri))
        {
            return Results.BadRequest(new { detail = "Enter a valid host URL first." });
        }

        try
        {
            var certificate = await TlsCertificateProbe.FetchCertificateAsync(uri.Host, uri.Port, cancellationToken);
            if (certificate is null)
            {
                return Results.BadRequest(new { detail = "Could not retrieve a certificate from that host - is it using HTTPS?" });
            }

            return Results.Ok(new { thumbprint = certificate.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256) });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { detail = $"Could not detect a certificate: {ex.Message}" });
        }
    }

    /// <summary>Returns the freshly entered secret, or - if left blank - the previously saved one under existingKey. Null existingKey plus a blank entry resolves to null (never configured), not an error - matches IntegrationsWindow.ResolveSecretByKeyAsync.</summary>
    private static async Task<string?> ResolveSecretAsync(SecretStore secretStore, string? enteredValue, string? existingKey, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(enteredValue))
        {
            return enteredValue;
        }

        if (existingKey is null)
        {
            return null;
        }

        await secretStore.LoadAsync(cancellationToken);
        return secretStore.GetSecret(existingKey);
    }

    private static async Task SaveIntegrationAsync(
        ConfigService configService,
        SecretStore secretStore,
        AppConfig config,
        IntegrationConfig testConfig,
        string secretKey,
        string secretValue,
        (string Key, string Value)? extraSecret = null,
        CancellationToken cancellationToken = default)
    {
        await secretStore.LoadAsync(cancellationToken);
        secretStore.SetSecret(secretKey, secretValue);
        if (extraSecret is { } extra)
        {
            secretStore.SetSecret(extra.Key, extra.Value);
        }

        await secretStore.SaveAsync(cancellationToken);

        var updatedIntegrations = config.Integrations.Where(i => i.Kind != testConfig.Kind).ToList();
        updatedIntegrations.Add(testConfig);
        await configService.SaveAsync(config with { Integrations = updatedIntegrations }, cancellationToken);
    }

    private sealed record ProxmoxTestRequest(string BaseUrl, string Username, string? Realm, string? Password, string? PinnedCertificateSha256Thumbprint);

    private sealed record PiHoleTestRequest(string Host, int Port, bool UseHttps, string? AppPassword, string? PinnedCertificateSha256Thumbprint);

    private sealed record UniFiTestRequest(string BaseUrl, string Username, string? Site, string? Password, string? TotpSecret, string? PinnedCertificateSha256Thumbprint);

    private sealed record DetectCertificateRequest(string BaseUrl);
}
