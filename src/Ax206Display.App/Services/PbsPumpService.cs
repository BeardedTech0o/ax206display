using Ax206Display.Config.Models;
using Ax206Display.Config.Secrets;
using Ax206Display.Config.Services;
using Ax206Display.DataSources.Http;
using Ax206Display.DataSources.Pbs;
using Ax206Display.Rendering.Playback;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ax206Display.App.Services;

/// <summary>
/// Polls every datastore's capacity from every configured PBS integration
/// (Kind == "pbs" in AppConfig.Integrations - like Proxmox, more than one
/// host is allowed) and publishes it into the RenderDataHub under keys
/// namespaced by that host's IntegrationConfig.Id (see PbsStatKeys - a store
/// name is only unique within one PBS host), plus keeps
/// PbsDatastoreDirectory current so the Widget Designer can list them
/// without touching the network itself. Same per-host independence as
/// ProxmoxPumpService (which this mirrors closely): one host being
/// unreachable or having an expired session never affects any other
/// configured host's polling. Basic support only - datastore capacity, not
/// backup job/snapshot status.
/// </summary>
public sealed partial class PbsPumpService : BackgroundService
{
    private const string IntegrationKind = "pbs";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    private readonly ConfigService _configService;
    private readonly SecretStore _secretStore;
    private readonly RenderDataHub _hub;
    private readonly PbsDatastoreDirectory _datastoreDirectory;
    private readonly ILogger<PbsPumpService> _logger;

    private readonly Dictionary<string, IPbsClient> _clientsByHostId = [];

    public PbsPumpService(
        ConfigService configService,
        SecretStore secretStore,
        RenderDataHub hub,
        PbsDatastoreDirectory datastoreDirectory,
        ILogger<PbsPumpService> logger)
    {
        _configService = configService;
        _secretStore = secretStore;
        _hub = hub;
        _datastoreDirectory = datastoreDirectory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAllHostsAsync(stoppingToken);
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

    private async Task PollAllHostsAsync(CancellationToken cancellationToken)
    {
        var config = await _configService.LoadAsync(cancellationToken);
        var hosts = config.Integrations.Where(i => i.Kind == IntegrationKind).ToList();
        var configuredHostIds = hosts.Select(h => h.Id).ToHashSet();

        foreach (var staleHostId in _clientsByHostId.Keys.Where(id => !configuredHostIds.Contains(id)).ToList())
        {
            _clientsByHostId.Remove(staleHostId);
        }

        foreach (var hostId in _datastoreDirectory.GetSnapshot().Keys.Where(id => !configuredHostIds.Contains(id)).ToList())
        {
            _datastoreDirectory.RemoveHost(hostId);
        }

        foreach (var host in hosts)
        {
            try
            {
                await PollHostAsync(host, cancellationToken);
            }
            catch (Exception ex)
            {
                LogHostPollFailed(ex, host.Id);
                _clientsByHostId.Remove(host.Id);
            }
        }
    }

    private async Task PollHostAsync(IntegrationConfig host, CancellationToken cancellationToken)
    {
        if (!_clientsByHostId.TryGetValue(host.Id, out var client))
        {
            var loggedIn = await LogInAsync(host, cancellationToken);
            if (loggedIn is null)
            {
                return;
            }

            client = loggedIn;
            _clientsByHostId[host.Id] = client;
        }

        var displayName = host.DisplayName ?? host.BaseUrl;

        var datastores = await client.GetDatastoreUsageAsync(cancellationToken);
        PbsStatsPublisher.Publish(host.Id, datastores, _hub.Publish);
        _datastoreDirectory.Update(host.Id, displayName, datastores);
    }

    private async Task<IPbsClient?> LogInAsync(IntegrationConfig integration, CancellationToken cancellationToken)
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
        var client = new PbsClient(httpClient);
        await client.LoginAsync(integration.Username, password, integration.Realm ?? "pam", cancellationToken);
        return client;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "PBS poll failed; will retry.")]
    private partial void LogPollFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "PBS host {HostId} poll failed; will retry that host.")]
    private partial void LogHostPollFailed(Exception exception, string hostId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "PBS integration {IntegrationId} is missing a username or password; skipping.")]
    private partial void LogMissingCredentials(string integrationId);
}
