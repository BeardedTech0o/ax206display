using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Ax206Display.DataSources.Auth;

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
    // Not a registered/standard HttpStatusCode - System.Net.HttpStatusCode
    // has no member for it, which is also why an unhandled failure here
    // used to surface as the unhelpful "status code 499 (unknown)". UniFi OS
    // uses it specifically to mean api.err.Ubic2faTokenRequired: the
    // credentials were fine, but the account has 2FA enabled and the login
    // needs a token too.
    private const int TwoFactorRequiredStatusCode = 499;

    private readonly HttpClient _httpClient;
    private string? _csrfToken;

    public UniFiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <param name="totpSecret">
    /// Base32 TOTP shared secret for a 2FA-enabled account (see
    /// <see cref="Config.Models.IntegrationConfig.TotpSecretKey"/>). Login is
    /// tried without a token first; only if UniFi OS comes back asking for
    /// one (see <see cref="TwoFactorRequiredStatusCode"/>) is a code computed
    /// from this secret and the login retried with it - so this is a no-op
    /// extra check, not an extra round trip, for the common no-2FA account.
    /// Null/empty for an account with no 2FA.
    /// </param>
    public async Task LoginAsync(string username, string password, string? totpSecret = null, CancellationToken cancellationToken = default)
    {
        var response = await SendLoginRequestAsync(username, password, token: null, cancellationToken);

        if ((int)response.StatusCode == TwoFactorRequiredStatusCode && totpSecret is { Length: > 0 })
        {
            response.Dispose();
            var code = TotpGenerator.GenerateCode(totpSecret);
            response = await SendLoginRequestAsync(username, password, code, cancellationToken);
        }

        using (response)
        {
            await EnsureSuccessWithBodyAsync(response, cancellationToken);

            if (response.Headers.TryGetValues("X-CSRF-Token", out var values))
            {
                _csrfToken = values.FirstOrDefault();
            }
        }
    }

    /// <summary>
    /// Same job as <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/>,
    /// but the exception message includes the response body - UniFi OS
    /// returns a JSON error body (e.g. api.err.InvalidPayload,
    /// api.err.Invalid for a rejected 2FA token) that
    /// EnsureSuccessStatusCode's bare "status code 400 (Bad Request)"
    /// discards, which is exactly the detail needed to tell "wrong
    /// password" apart from "wrong/stale TOTP code" apart from "clock skew"
    /// apart from something else entirely.
    /// </summary>
    private static async Task EnsureSuccessWithBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? body = null;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception)
        {
            // Worst case we fall back to the bare status code below - still
            // strictly better than crashing the whole login attempt trying
            // to fetch extra diagnostic detail for the error we're already
            // in the middle of reporting.
        }

        var detail = string.IsNullOrWhiteSpace(body) ? string.Empty : $": {body.Trim()}";
        throw new HttpRequestException($"UniFi request failed with status {(int)response.StatusCode} ({response.StatusCode}){detail}");
    }

    private async Task<HttpResponseMessage> SendLoginRequestAsync(string username, string password, string? token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(username, password, false, token)),
        };

        return await _httpClient.SendAsync(request, cancellationToken);
    }

    public async Task<UniFiSiteStatus> GetSiteHealthAsync(string site = "default", CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/proxy/network/api/s/{site}/stat/health");
        if (_csrfToken is not null)
        {
            request.Headers.Add("X-CSRF-Token", _csrfToken);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessWithBodyAsync(response, cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<HealthResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("UniFi health endpoint returned an empty response body.");

        var subsystems = body.Data
            .Select(d => new UniFiSubsystemHealth(d.Subsystem, d.Status, d.NumUser, d.RxBytesPerSecond, d.TxBytesPerSecond))
            .ToList();

        return new UniFiSiteStatus(subsystems);
    }

    // UniFi OS's own request-schema validation rejects an explicit
    // "token":null with 400 VALIDATION_ERROR ("Expected string, received
    // null") - it needs the field omitted entirely on the no-token first
    // attempt, not present-but-null. Confirmed by hitting exactly that
    // error: System.Text.Json includes null properties by default, so
    // without this the very first login attempt (before UniFi OS has even
    // had a chance to say 2FA is required) always failed.
    private sealed record LoginRequest(string Username, string Password, bool RememberMe, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Token = null);

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

        [JsonPropertyName("num_user")]
        public int NumUser { get; set; }

        [JsonPropertyName("rx_bytes-r")]
        public double RxBytesPerSecond { get; set; }

        [JsonPropertyName("tx_bytes-r")]
        public double TxBytesPerSecond { get; set; }
    }
}
