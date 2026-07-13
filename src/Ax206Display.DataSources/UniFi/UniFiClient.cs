using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Ax206Display.DataSources.UniFi;

/// <summary>
/// Talks to a UniFi OS console (UDM/UDM-Pro/Cloud Key Gen2+) using its local
/// API: a session login at /api/auth/login followed by CSRF-token-guarded
/// calls to the Network application under /proxy/network/api/...
/// </summary>
/// <remarks>
/// Build the supplied <see cref="HttpClient"/> via
/// <see cref="Ax206Display.DataSources.Http.IntegrationHttpClientFactory"/>
/// with <c>enableCookies: true</c>, since the session cookie set by login must
/// flow automatically to later requests. UniFi OS consoles commonly serve a
/// self-signed certificate - set <c>IntegrationConfig.PinnedCertificateSha256Thumbprint</c>
/// rather than disabling certificate validation outright.
/// </remarks>
public sealed class UniFiClient : IUniFiClient
{
    private readonly HttpClient _httpClient;
    private string? _csrfToken;

    public UniFiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(username, password, false)),
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Headers.TryGetValues("X-CSRF-Token", out var values))
        {
            _csrfToken = values.FirstOrDefault();
        }
    }

    public async Task<UniFiSiteStatus> GetSiteHealthAsync(string site = "default", CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/proxy/network/api/s/{site}/stat/health");
        if (_csrfToken is not null)
        {
            request.Headers.Add("X-CSRF-Token", _csrfToken);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<HealthResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("UniFi health endpoint returned an empty response body.");

        var subsystems = body.Data
            .Select(d => new UniFiSubsystemHealth(d.Subsystem, d.Status))
            .ToList();

        return new UniFiSiteStatus(subsystems);
    }

    private sealed record LoginRequest(string Username, string Password, bool RememberMe);

    private sealed class HealthResponse
    {
        [JsonPropertyName("data")]
        public List<HealthEntry> Data { get; set; } = [];
    }

    private sealed class HealthEntry
    {
        [JsonPropertyName("subsystem")]
        public string Subsystem { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;
    }
}
