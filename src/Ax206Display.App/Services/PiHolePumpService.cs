using Ax206Display.Config.Secrets;
using Ax206Display.Config.Services;
using Ax206Display.DataSources.Http;
using Ax206Display.DataSources.PiHole;
using Ax206Display.Rendering.Playback;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ax206Display.App.Services;

/// <summary>
/// Polls a configured Pi-hole integration (Kind == "pihole" in
/// AppConfig.Integrations) and publishes its summary stats into the
/// RenderDataHub. Unlike Proxmox, Pi-hole's v5 API has no login/session
/// step - every request just carries the API token - so there's no
/// reconnect-on-expiry logic needed here. Idles quietly if not configured.
/// </summary>
public sealed partial class PiHolePumpService : BackgroundService
{
    private const string IntegrationKind = "pihole";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    private readonly ConfigService _configService;
    private readonly SecretStore _secretStore;
    private readonly RenderDataHub _hub;
    private readonly ILogger<PiHolePumpService> _logger;

    public PiHolePumpService(ConfigService configService, SecretStore secretStore, RenderDataHub hub, ILogger<PiHolePumpService> logger)
    {
        _configService = configService;
        _secretStore = secretStore;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                LogPollFailed(ex);
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        var config = await _configService.LoadAsync(cancellationToken);
        var integration = config.Integrations.FirstOrDefault(i => i.Kind == IntegrationKind);
        if (integration is null || string.IsNullOrEmpty(integration.SecretKey))
        {
            return;
        }

        await _secretStore.LoadAsync(cancellationToken);
        var apiToken = _secretStore.GetSecret(integration.SecretKey);
        if (string.IsNullOrEmpty(apiToken))
        {
            LogMissingToken(integration.Id);
            return;
        }

        using var httpClient = IntegrationHttpClientFactory.Create(integration, enableCookies: false);
        var client = new PiHoleClient(httpClient, apiToken);

        var summary = await client.GetSummaryAsync(cancellationToken);
        PiHoleStatsPublisher.Publish(summary, _hub.Publish);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pi-hole poll failed; will retry.")]
    private partial void LogPollFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pi-hole integration {IntegrationId} is missing its API token; skipping.")]
    private partial void LogMissingToken(string integrationId);
}
