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
/// Polls every node and VM/container from every configured Proxmox
/// integration (Kind == "proxmox" in AppConfig.Integrations - there can be
/// more than one host, unlike Pi-hole/UniFi) and publishes their CPU/memory
/// (and each node's uptime) into the RenderDataHub under keys namespaced by
/// that host's IntegrationConfig.Id (see ProxmoxGuestKeys/ProxmoxNodeKeys -
/// a VMID or node name is only unique within one host, not across several),
/// plus keeps ProxmoxGuestDirectory/ProxmoxNodeDirectory current so the
/// Widget Designer can list them without touching the network itself. Each
/// host is logged in/polled/re-authenticated independently - one host being
/// unreachable or having an expired session ticket never affects any other
/// configured host's polling.
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

    private readonly Dictionary<string, IProxmoxClient> _clientsByHostId = [];

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
                await PollAllHostsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // Only reachable for a failure outside PollHostAsync's own
                // per-host try/catch (e.g. ConfigService.LoadAsync itself
                // failing) - an individual host's failure never bubbles up
                // this far, see PollAllHostsAsync.
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

        // Drop state for any host removed from config since the last poll -
        // otherwise its directory entry (and a stale logged-in client) would
        // linger forever.
        foreach (var staleHostId in _clientsByHostId.Keys.Where(id => !configuredHostIds.Contains(id)).ToList())
        {
            _clientsByHostId.Remove(staleHostId);
        }

        foreach (var hostId in _guestDirectory.GetSnapshot().Keys.Where(id => !configuredHostIds.Contains(id)).ToList())
        {
            _guestDirectory.RemoveHost(hostId);
            _nodeDirectory.RemoveHost(hostId);
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
                // Force a fresh login attempt for just this host next time -
                // the failure may have been an expired/rejected session
                // ticket. Other hosts' clients are untouched.
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

        var nodes = await client.GetNodeStatusesAsync(cancellationToken);
        ProxmoxStatsPublisher.PublishNodes(host.Id, nodes, _hub.Publish);
        _nodeDirectory.Update(host.Id, displayName, nodes);

        var guests = await client.GetGuestStatusesAsync(cancellationToken);
        ProxmoxStatsPublisher.Publish(host.Id, guests, _hub.Publish);
        _guestDirectory.Update(host.Id, displayName, guests);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Proxmox host {HostId} poll failed; will retry that host.")]
    private partial void LogHostPollFailed(Exception exception, string hostId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Proxmox integration {IntegrationId} is missing a username or password; skipping.")]
    private partial void LogMissingCredentials(string integrationId);
}
