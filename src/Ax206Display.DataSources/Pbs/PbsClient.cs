using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Ax206Display.DataSources.Pbs;

/// <summary>
/// Talks to the Proxmox Backup Server API
/// (https://pbs.proxmox.com/docs/api-viewer/): ticket-based login at
/// /api2/json/access/ticket - the same shape as Proxmox VE's own API (PBS is
/// built on the same framework) - then the resulting ticket is sent as the
/// PBSAuthCookie cookie on subsequent requests (PVEAuthCookie is PVE's own,
/// distinct cookie name).
/// </summary>
/// <remarks>
/// The supplied <see cref="HttpClient"/> must have <see cref="HttpClient.BaseAddress"/>
/// set to the PBS API root (e.g. https://pbs.example.com:8007). PBS commonly
/// serves a self-signed certificate - build the client via
/// <see cref="Ax206Display.DataSources.Http.IntegrationHttpClientFactory"/>
/// with <c>IntegrationConfig.PinnedCertificateSha256Thumbprint</c> set rather
/// than disabling certificate validation outright. Pass
/// <c>enableCookies: false</c> since this class manages the PBSAuthCookie
/// header itself.
/// </remarks>
public sealed class PbsClient : IPbsClient
{
    private readonly HttpClient _httpClient;
    private string? _ticket;
    private string? _csrfPreventionToken;

    public PbsClient(HttpClient httpClient)
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
            ?? throw new InvalidOperationException("PBS ticket endpoint returned an empty response body.");

        _ticket = body.Data.Ticket;
        _csrfPreventionToken = body.Data.CsrfPreventionToken;
    }

    public async Task<IReadOnlyList<PbsDatastoreStatus>> GetDatastoreUsageAsync(CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, "/api2/json/status/datastore-usage");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<DatastoreUsageResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("PBS datastore-usage endpoint returned an empty response body.");

        return body.Data
            .Select(d => new PbsDatastoreStatus
            {
                Store = d.Store,
                TotalBytes = d.Total,
                UsedBytes = d.Used,
                AvailableBytes = d.Avail,
            })
            .ToList();
    }

    private HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string path)
    {
        if (_ticket is null)
        {
            throw new InvalidOperationException($"{nameof(PbsClient)} must be logged in before calling authenticated endpoints.");
        }

        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", $"PBSAuthCookie={_ticket}");
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

    private sealed class DatastoreUsageResponse
    {
        [JsonPropertyName("data")]
        public List<DatastoreUsageEntry> Data { get; set; } = [];
    }

    private sealed class DatastoreUsageEntry
    {
        [JsonPropertyName("store")]
        public string Store { get; set; } = string.Empty;

        [JsonPropertyName("total")]
        public long Total { get; set; }

        [JsonPropertyName("used")]
        public long Used { get; set; }

        [JsonPropertyName("avail")]
        public long Avail { get; set; }
    }
}
