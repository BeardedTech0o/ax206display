using Ax206Display.Config.Models;
using Ax206Display.Config.Secrets;
using Ax206Display.Config.Services;
using Ax206Display.DataSources.Http;
using Ax206Display.DataSources.Proxmox;
using Ax206Display.Rendering.Playback;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ax206Display.App.Services;

/// <summary>
/// Polls every node and VM/container from a configured Proxmox integration
/// (Kind == "proxmox" in AppConfig.Integrations) and publishes their
/// CPU/memory (and each node's uptime) into the RenderDataHub, plus keeps
/// ProxmoxGuestDirectory/ProxmoxNodeDirectory current so the Widget Designer
/// can list them without touching the network itself. Idles quietly
/// (checking again next poll) if no Proxmox integration is configured yet,
/// and re-authenticates automatically if the session ticket is rejected
/// (e.g. after it expires).
/// </summary>
public sealed partial class ProxmoxPumpService : BackgroundService
{
    private const string IntegrationKind = "proxmox";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly ConfigService _configService;
    private readonly SecretStore _secretStore;
    private readonly RenderDataHub _hub;
    private readonly ProxmoxGuestDirectory _guestDirectory;
    private readonly ProxmoxNodeDirectory _nodeDirectory;
    private readonly ILogger<ProxmoxPumpService> _logger;

    private IProxmoxClient? _client;
    private string? _loggedInIntegrationId;

    public ProxmoxPumpService(
        ConfigService configService,
        SecretStore secretStore,
        RenderDataHub hub,
        ProxmoxGuestDirectory guestDirectory,
        ProxmoxNodeDirectory nodeDirectory,
        ILogger<ProxmoxPumpService> logger)
    {
        _configService = configService;
        _secretStore = secretStore;
        _hub = hub;
        _guestDirectory = guestDirectory;
        _nodeDirectory = nodeDirectory;
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
                // have been an expired/rejected session ticket.
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

        var nodes = await _client.GetNodeStatusesAsync(cancellationToken);
        ProxmoxStatsPublisher.PublishNodes(nodes, _hub.Publish);
        _nodeDirectory.Update(nodes);

        var guests = await _client.GetGuestStatusesAsync(cancellationToken);
        ProxmoxStatsPublisher.Publish(guests, _hub.Publish);
        _guestDirectory.Update(guests);
    }

    private async Task<IProxmoxClient?> LogInAsync(IntegrationConfig integration, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(integration.Username) || string.IsNullOrEmpty(integration.SecretKey))
        {
            LogMissingCredentials(integration.Id);
            return null;
        }

        await _secretStore.LoadAsync(cancellationToken);
        var password = _secretStore.GetSecret(integration.SecretKey);
        if (string.IsNullOrEmpty(password))
        {
            LogMissingCredentials(integration.Id);
            return null;
        }

        var httpClient = IntegrationHttpClientFactory.Create(integration, enableCookies: false);
        var client = new ProxmoxClient(httpClient);
        await client.LoginAsync(integration.Username, password, integration.Realm ?? "pam", cancellationToken);
        return client;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Proxmox poll failed; will retry.")]
    private partial void LogPollFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Proxmox integration {IntegrationId} is missing a username or password; skipping.")]
    private partial void LogMissingCredentials(string integrationId);
}
