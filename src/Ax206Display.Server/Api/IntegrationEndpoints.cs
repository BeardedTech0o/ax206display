using Ax206Display.Config.Secrets;
using Ax206Display.Config.Services;
using Ax206Display.Engine.Integrations;

namespace Ax206Display.Server.Api;

/// <summary>
/// Pi-hole, Proxmox and UniFi settings. Secrets go in and never come back
/// out: a GET reports only whether one is stored.
/// </summary>
public static class IntegrationEndpoints
{
    public static void MapIntegrationEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/integrations", async (ConfigService configService, SecretStore secretStore, CancellationToken cancellationToken) =>
        {
            var config = await configService.LoadAsync(cancellationToken);
            await secretStore.LoadAsync(cancellationToken);

            // "Saved" means present in this machine's store, not merely
            // named in config: a config.json copied over from Windows names
            // secrets that were DPAPI-encrypted there and never came along.
            return Results.Ok(config.Integrations
                .Where(i => IntegrationSetupService.SupportedKinds.Contains(i.Kind))
                .Select(i => new
                {
                    kind = i.Kind,
                    baseUrl = i.BaseUrl,
                    username = i.Username,
                    realm = i.Realm,
                    site = i.Site,
                    pinnedCertificateSha256Thumbprint = i.PinnedCertificateSha256Thumbprint,
                    hasSecret = i.SecretKey is { } key && secretStore.HasSecret(key),
                    hasTotpSecret = i.TotpSecretKey is { } totpKey && secretStore.HasSecret(totpKey),
                }));
        });

        api.MapPut("/integrations/{kind}", async (string kind, IntegrationSetupRequest request, IntegrationSetupService setup, CancellationToken cancellationToken) =>
        {
            if (!IntegrationSetupService.SupportedKinds.Contains(kind))
            {
                return Results.NotFound();
            }

            var result = await setup.TestAndSaveAsync(kind, request, cancellationToken);
            return result.Success
                ? Results.Ok(new { message = result.Message })
                : Results.UnprocessableEntity(new { error = result.Message });
        });

        api.MapDelete("/integrations/{kind}", async (string kind, IntegrationSetupService setup, CancellationToken cancellationToken) =>
            await setup.RemoveAsync(kind, cancellationToken) ? Results.NoContent() : Results.NotFound());

        api.MapPost("/integrations/detect-certificate", async (DetectCertificateRequest request, CancellationToken cancellationToken) =>
        {
            try
            {
                var thumbprint = await IntegrationSetupService.DetectCertificateThumbprintAsync(request.BaseUrl ?? string.Empty, cancellationToken);
                return thumbprint is null
                    ? Results.UnprocessableEntity(new { error = "Could not retrieve a certificate from that host. Is it using HTTPS?" })
                    : Results.Ok(new { thumbprint });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Results.UnprocessableEntity(new { error = "Could not detect a certificate: " + ex.Message });
            }
        });
    }

    public sealed record DetectCertificateRequest(string? BaseUrl);
}
