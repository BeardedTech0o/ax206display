using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Ax206Display.DataSources.PiHole;

/// <summary>
/// Talks to the Pi-hole v5 API (https://docs.pi-hole.net/api/): a single
/// token-authenticated GET, no login/session step - unlike Proxmox/UniFi,
/// there's nothing to log into. Uses `summaryRaw` deliberately (not
/// `summary`): the plain `summary` endpoint renders some fields as
/// comma-formatted display strings (e.g. "1,234") instead of numbers.
/// </summary>
/// <remarks>
/// The supplied <see cref="HttpClient"/> must have <see cref="HttpClient.BaseAddress"/>
/// set to the Pi-hole host root (e.g. http://pi.hole or https://pi.example.com) -
/// build it via <see cref="Ax206Display.DataSources.Http.IntegrationHttpClientFactory"/>
/// with <c>enableCookies: false</c>, same as Proxmox.
/// </remarks>
public sealed class PiHoleClient : IPiHoleClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiToken;

    public PiHoleClient(HttpClient httpClient, string apiToken)
    {
        _httpClient = httpClient;
        _apiToken = apiToken;
    }

    public async Task<PiHoleSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var requestUri = $"/admin/api.php?summaryRaw&auth={Uri.EscapeDataString(_apiToken)}";
        using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<SummaryResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Pi-hole summary endpoint returned an empty response body.");

        return new PiHoleSummary
        {
            Status = body.Status,
            AdsBlockedToday = body.AdsBlockedToday,
            AdsPercentageToday = body.AdsPercentageToday,
            DnsQueriesToday = body.DnsQueriesToday,
        };
    }

    private sealed class SummaryResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("ads_blocked_today")]
        public long AdsBlockedToday { get; set; }

        [JsonPropertyName("ads_percentage_today")]
        public double AdsPercentageToday { get; set; }

        [JsonPropertyName("dns_queries_today")]
        public long DnsQueriesToday { get; set; }
    }
}
