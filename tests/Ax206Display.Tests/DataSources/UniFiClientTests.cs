using System.Net;
using System.Text;
using Ax206Display.DataSources.Auth;
using Ax206Display.DataSources.UniFi;
using Ax206Display.Tests.TestSupport;

namespace Ax206Display.Tests.DataSources;

public class UniFiClientTests
{
    [Fact]
    public async Task LoginAsync_RetriesWithComputedTotpCodeWhenServerRequiresTwoFactor()
    {
        const string totpSecret = "JBSWY3DPEHPK3PXP";
        var requestBodies = new List<string>();
        var callCount = 0;
        var handler = new FakeHttpMessageHandler(request =>
        {
            callCount++;
            // Captured here, not from handler.Requests afterwards - the
            // request (and its Content) is disposed by UniFiClient's own
            // `using` as soon as SendAsync returns, before this test method
            // gets to make any assertions.
            requestBodies.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());

            return callCount == 1
                ? new HttpResponseMessage((HttpStatusCode)499)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        });
        var client = CreateClient(handler);

        await client.LoginAsync("admin", "password", totpSecret);

        Assert.Equal(2, requestBodies.Count);
        // UniFi OS's schema validation rejects "token":null outright (400
        // VALIDATION_ERROR) - the field must be omitted entirely, not
        // present-but-null, on this first no-token attempt.
        Assert.DoesNotContain("token", requestBodies[0]);
        var expectedCode = TotpGenerator.GenerateCode(totpSecret);
        Assert.Contains(expectedCode, requestBodies[1]);
    }

    [Fact]
    public async Task LoginAsync_DoesNotRetryWhenFirstAttemptSucceedsEvenWithTotpSecretConfigured()
    {
        var handler = new FakeHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
            response.Headers.Add("X-CSRF-Token", "csrf-abc");
            return response;
        });
        var client = CreateClient(handler);

        await client.LoginAsync("admin", "password", "JBSWY3DPEHPK3PXP");

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task LoginAsync_IncludesResponseBodyInExceptionMessageWhenLoginFails()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"errorCode":400,"message":"api.err.Invalid"}""", Encoding.UTF8, "application/json"),
            });
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.LoginAsync("admin", "password"));

        Assert.Contains("api.err.Invalid", exception.Message);
    }

    [Fact]
    public async Task LoginAsync_ThrowsWithoutRetryingWhenServerRequiresTwoFactorButNoSecretConfigured()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage((HttpStatusCode)499));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.LoginAsync("admin", "password"));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task LoginAsync_CapturesCsrfTokenFromResponseHeader()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            Assert.Equal("/api/auth/login", request.RequestUri!.AbsolutePath);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
            response.Headers.Add("X-CSRF-Token", "csrf-abc");
            return response;
        });
        var client = CreateClient(handler);

        await client.LoginAsync("admin", "password");

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetSiteHealthAsync_ForwardsCsrfTokenAndParsesSubsystems()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/auth/login", StringComparison.Ordinal))
            {
                var loginResponse = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
                loginResponse.Headers.Add("X-CSRF-Token", "csrf-xyz");
                return loginResponse;
            }

            Assert.True(request.Headers.TryGetValues("X-CSRF-Token", out var values));
            Assert.Equal("csrf-xyz", values!.Single());

            const string json = """{ "data": [ { "subsystem": "wan", "status": "ok" }, { "subsystem": "www", "status": "warning" } ] }""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        });
        var client = CreateClient(handler);
        await client.LoginAsync("admin", "password");

        var status = await client.GetSiteHealthAsync();

        Assert.Equal(2, status.Subsystems.Count);
        Assert.Contains(status.Subsystems, s => s.Subsystem == "wan" && s.Status == "ok");
    }

    [Fact]
    public async Task GetSiteHealthAsync_ParsesClientCountAndWanThroughputFields()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/auth/login", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
            }

            const string json = """
                {
                  "data": [
                    { "subsystem": "wan", "status": "ok", "rx_bytes-r": 1250000, "tx_bytes-r": 375000 },
                    { "subsystem": "lan", "status": "ok", "num_user": 5 },
                    { "subsystem": "wlan", "status": "ok", "num_user": 12 }
                  ]
                }
                """;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        });
        var client = CreateClient(handler);
        await client.LoginAsync("admin", "password");

        var status = await client.GetSiteHealthAsync();

        var wan = status.Subsystems.Single(s => s.Subsystem == "wan");
        Assert.Equal(1250000, wan.RxBytesPerSecond);
        Assert.Equal(375000, wan.TxBytesPerSecond);
        Assert.Equal(5, status.Subsystems.Single(s => s.Subsystem == "lan").NumUser);
        Assert.Equal(12, status.Subsystems.Single(s => s.Subsystem == "wlan").NumUser);
    }

    private static UniFiClient CreateClient(FakeHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://udm.local") };
        return new UniFiClient(httpClient);
    }
}
