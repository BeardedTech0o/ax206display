using System.Net;
using System.Text;
using Ax206Display.DataSources.PiHole;
using Ax206Display.Tests.TestSupport;

namespace Ax206Display.Tests.DataSources;

public class PiHoleClientTests
{
    [Fact]
    public async Task LoginAsync_PostsToAuthEndpoint()
    {
        string? capturedPath = null;
        var handler = new FakeHttpMessageHandler(request =>
        {
            capturedPath = request.RequestUri!.AbsolutePath;
            const string json = """{ "session": { "valid": true, "sid": "session-abc", "message": "password correct" } }""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        });
        var client = CreateClient(handler);

        await client.LoginAsync("app-password");

        Assert.Equal("/api/auth", capturedPath);
    }

    [Fact]
    public async Task LoginAsync_NoSidInResponse_ThrowsWithTheServerMessage()
    {
        var handler = new FakeHttpMessageHandler(_ =>
        {
            const string json = """{ "session": { "valid": false, "sid": null, "message": "incorrect password" } }""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        });
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.LoginAsync("wrong-password"));
        Assert.Contains("incorrect password", ex.Message);
    }

    [Fact]
    public async Task GetSummaryAsync_WithoutLoggingIn_Throws()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetSummaryAsync());
    }

    [Fact]
    public async Task GetSummaryAsync_ForwardsSessionIdAndParsesQueryCounts()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/auth")
            {
                const string loginJson = """{ "session": { "valid": true, "sid": "session-xyz", "message": "password correct" } }""";
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(loginJson, Encoding.UTF8, "application/json") };
            }

            Assert.Equal("/api/stats/summary", request.RequestUri!.AbsolutePath);
            Assert.True(request.Headers.TryGetValues("X-FTL-SID", out var values));
            Assert.Equal("session-xyz", values!.Single());

            const string summaryJson = """
                {
                  "queries": { "total": 9876, "blocked": 1234, "percent_blocked": 12.5, "unique_domains": 543, "forwarded": 4000, "cached": 5000 },
                  "clients": { "active": 8, "total": 12 },
                  "gravity": { "domains_being_blocked": 150000 }
                }
                """;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(summaryJson, Encoding.UTF8, "application/json") };
        });
        var client = CreateClient(handler);
        await client.LoginAsync("app-password");

        var summary = await client.GetSummaryAsync();

        Assert.Equal(1234, summary.AdsBlockedToday);
        Assert.Equal(12.5, summary.AdsPercentageToday);
        Assert.Equal(9876, summary.DnsQueriesToday);
        Assert.Equal(150000, summary.DomainsOnBlocklist);
        Assert.Equal(5000, summary.QueriesCached);
        Assert.Equal(4000, summary.QueriesForwarded);
        Assert.Equal(543, summary.UniqueDomains);
        Assert.Equal(8, summary.ActiveClients);
        Assert.Equal(12, summary.TotalClients);
    }

    private static PiHoleClient CreateClient(FakeHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://pi.hole") };
        return new PiHoleClient(httpClient);
    }
}
