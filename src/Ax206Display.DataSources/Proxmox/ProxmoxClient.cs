using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Ax206Display.DataSources.Proxmox;

/// <summary>
/// Talks to the Proxmox VE API (https://pve.proxmox.com/pve-docs/api-viewer/):
/// ticket-based login at /api2/json/access/ticket, then the resulting ticket is
/// sent as the PVEAuthCookie cookie on subsequent requests.
/// </summary>
/// <remarks>
/// The supplied <see cref="HttpClient"/> must have <see cref="HttpClient.BaseAddress"/>
/// set to the node's API root (e.g. https://pve.example.com:8006). Proxmox
/// commonly serves a self-signed certificate - build the client via
/// <see cref="Ax206Display.DataSources.Http.IntegrationHttpClientFactory"/>
/// with <c>IntegrationConfig.PinnedCertificateSha256Thumbprint</c> set rather
/// than disabling certificate validation outright. Pass
/// <c>enableCookies: false</c> since this class manages the PVEAuthCookie
/// header itself.
/// </remarks>
public sealed class ProxmoxClient : IProxmoxClient
{
    private readonly HttpClient _httpClient;
    private string? _ticket;
    private string? _csrfPreventionToken;

    public ProxmoxClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task LoginAsync(string username, string password, string realm = "pam", CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["username"] = $"{username}@{realm}",
            ["password"] = password,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api2/json/access/ticket")
        {
            Content = new FormUrlEncodedContent(form),
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<TicketResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Proxmox ticket endpoint returned an empty response body.");

        _ticket = body.Data.Ticket;
        _csrfPreventionToken = body.Data.CsrfPreventionToken;
    }

    public async Task<IReadOnlyList<ProxmoxNodeStatus>> GetNodeStatusesAsync(CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, "/api2/json/nodes");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<NodesResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Proxmox nodes endpoint returned an empty response body.");

        return body.Data
            .Select(n => new ProxmoxNodeStatus
            {
                Node = n.Node,
                Status = n.Status,
                CpuUsageFraction = n.Cpu,
                MemoryUsedBytes = n.Mem,
                MemoryTotalBytes = n.MaxMem,
                UptimeSeconds = n.Uptime,
            })
            .ToList();
    }

    private HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string path)
    {
        if (_ticket is null)
        {
            throw new InvalidOperationException($"{nameof(ProxmoxClient)} must be logged in before calling authenticated endpoints.");
        }

        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", $"PVEAuthCookie={_ticket}");
        if (_csrfPreventionToken is not null && method != HttpMethod.Get)
        {
            request.Headers.Add("CSRFPreventionToken", _csrfPreventionToken);
        }

        return request;
    }

    private sealed class TicketResponse
    {
        [JsonPropertyName("data")]
        public TicketData Data { get; set; } = new();
    }

    private sealed class TicketData
    {
        [JsonPropertyName("ticket")]
        public string Ticket { get; set; } = string.Empty;

        [JsonPropertyName("CSRFPreventionToken")]
        public string CsrfPreventionToken { get; set; } = string.Empty;
    }

    private sealed class NodesResponse
    {
        [JsonPropertyName("data")]
        public List<NodeEntry> Data { get; set; } = [];
    }

    private sealed class NodeEntry
    {
        [JsonPropertyName("node")]
        public string Node { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("cpu")]
        public double Cpu { get; set; }

        [JsonPropertyName("mem")]
        public long Mem { get; set; }

        [JsonPropertyName("maxmem")]
        public long MaxMem { get; set; }

        [JsonPropertyName("uptime")]
        public long Uptime { get; set; }
    }
}
