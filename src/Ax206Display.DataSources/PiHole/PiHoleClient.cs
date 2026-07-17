using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Ax206Display.DataSources.PiHole;

/// <summary>
/// Talks to the Pi-hole v6 API (https://ftl.pi-hole.net/development/): a
/// session login at POST /api/auth using an "app password" (a scoped
/// credential generated under Settings -&gt; API -&gt; App Passwords, separate
/// from the admin login), returning a session id sent as the X-FTL-SID
/// header on later requests. Unlike v5's static-token GET-only API, a
/// session eventually expires, so callers need to re-login and retry on
/// failure - see ProxmoxClient for the same shape.
/// </summary>
/// <remarks>
/// The supplied <see cref="HttpClient"/> must have <see cref="HttpClient.BaseAddress"/>
/// set to the Pi-hole host root (e.g. http://pi.hole or https://pi.example.com) -
/// build it via <see cref="Ax206Display.DataSources.Http.IntegrationHttpClientFactory"/>
/// with <c>enableCookies: false</c>, since the session id is carried explicitly
/// via header rather than a cookie.
/// </remarks>
public sealed class PiHoleClient : IPiHoleClient
{
    private readonly HttpClient _httpClient;
    private string? _sessionId;

    public PiHoleClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task LoginAsync(string appPassword, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth")
        {
            Content = JsonContent.Create(new LoginRequest(appPassword)),
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Pi-hole auth endpoint returned an empty response body.");

        if (body.Session.Sid is not { Length: > 0 } sid)
        {
            throw new InvalidOperationException($"Pi-hole login failed: {body.Session.Message}");
        }

        _sessionId = sid;
    }

    public async Task<PiHoleSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        if (_sessionId is null)
        {
            throw new InvalidOperationException($"{nameof(PiHoleClient)} must be logged in before calling authenticated endpoints.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/stats/summary");
        request.Headers.Add("X-FTL-SID", _sessionId);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<SummaryResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Pi-hole summary endpoint returned an empty response body.");

        return new PiHoleSummary
        {
            AdsBlockedToday = body.Queries.Blocked,
            AdsPercentageToday = body.Queries.PercentBlocked,
            DnsQueriesToday = body.Queries.Total,
        };
    }

    private sealed record LoginRequest([property: JsonPropertyName("password")] string Password);

    private sealed class AuthResponse
    {
        [JsonPropertyName("session")]
        public SessionInfo Session { get; set; } = new();
    }

    private sealed class SessionInfo
    {
        [JsonPropertyName("sid")]
        public string? Sid { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }

    private sealed class SummaryResponse
    {
        [JsonPropertyName("queries")]
        public QueriesInfo Queries { get; set; } = new();
    }

    private sealed class QueriesInfo
    {
        [JsonPropertyName("total")]
        public long Total { get; set; }

        [JsonPropertyName("blocked")]
        public long Blocked { get; set; }

        [JsonPropertyName("percent_blocked")]
        public double PercentBlocked { get; set; }
    }
}
