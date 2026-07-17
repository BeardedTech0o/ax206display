using Ax206Display.Config.Models;
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
/// RenderDataHub. Pi-hole v6 replaced v5's stateless per-request API token
/// with a session login (an "app password" traded for a session id), so
/// this logs in once and reuses that session, re-authenticating
/// automatically if a poll fails (e.g. the session expired) - same shape as
/// ProxmoxPumpService/UniFiPumpService. Idles quietly if not configured yet.
/// </summary>
public sealed partial class PiHolePumpService : BackgroundService
{
    private const string IntegrationKind = "pihole";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    private readonly ConfigService _configService;
    private readonly SecretStore _secretStore;
    private readonly RenderDataHub _hub;
    private readonly ILogger<PiHolePumpService> _logger;

    private IPiHoleClient? _client;
    private string? _loggedInIntegrationId;

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
                // Force a fresh login attempt next time - the failure may
                // have been an expired/rejected session id.
                _client = null;
                _loggedInIntegrationId = null;
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
        if (integration is null)
        {
            _client = null;
            _loggedInIntegrationId = null;
            return;
        }

        if (_client is null || _loggedInIntegrationId != integration.Id)
        {
            _client = await LogInAsync(integration, cancellationToken);
            _loggedInIntegrationId = _client is null ? null : integration.Id;

            if (_client is null)
            {
                return;
            }
        }

        var summary = await _client.GetSummaryAsync(cancellationToken);
        PiHoleStatsPublisher.Publish(summary, _hub.Publish);
    }

    private async Task<IPiHoleClient?> LogInAsync(IntegrationConfig integration, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(integration.SecretKey))
        {
            LogMissingCredentials(integration.Id);
            return null;
        }

        await _secretStore.LoadAsync(cancellationToken);
        var appPassword = _secretStore.GetSecret(integration.SecretKey);
        if (string.IsNullOrEmpty(appPassword))
        {
            LogMissingCredentials(integration.Id);
            return null;
        }

        var httpClient = IntegrationHttpClientFactory.Create(integration, enableCookies: false);
        var client = new PiHoleClient(httpClient);
        await client.LoginAsync(appPassword, cancellationToken);
        return client;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pi-hole poll failed; will retry.")]
    private partial void LogPollFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pi-hole integration {IntegrationId} is missing its app password; skipping.")]
    private partial void LogMissingCredentials(string integrationId);
}
