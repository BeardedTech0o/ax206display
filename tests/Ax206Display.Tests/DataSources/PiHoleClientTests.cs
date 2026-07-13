using System.Net;
using System.Text;
using Ax206Display.DataSources.PiHole;
using Ax206Display.Tests.TestSupport;

namespace Ax206Display.Tests.DataSources;

public class PiHoleClientTests
{
    [Fact]
    public async Task GetSummaryAsync_SendsTokenAndParsesSummaryRaw()
    {
        string? capturedPath = null;
        var handler = new FakeHttpMessageHandler(request =>
        {
            capturedPath = request.RequestUri!.PathAndQuery;
            const string json = """
                {
                  "status": "enabled",
                  "ads_blocked_today": 1234,
                  "ads_percentage_today": 12.5,
                  "dns_queries_today": 9876
                }
                """;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://pi.hole") };
        var client = new PiHoleClient(httpClient, "secret-token");

        var summary = await client.GetSummaryAsync();

        Assert.Contains("summaryRaw", capturedPath);
        Assert.Contains("auth=secret-token", capturedPath);
        Assert.Equal("enabled", summary.Status);
        Assert.Equal(1234, summary.AdsBlockedToday);
        Assert.Equal(12.5, summary.AdsPercentageToday);
        Assert.Equal(9876, summary.DnsQueriesToday);
    }

    [Fact]
    public async Task GetSummaryAsync_UrlEncodesTheToken()
    {
        string? capturedPath = null;
        var handler = new FakeHttpMessageHandler(request =>
        {
            capturedPath = request.RequestUri!.PathAndQuery;
            const string json = """{ "status": "enabled", "ads_blocked_today": 0, "ads_percentage_today": 0, "dns_queries_today": 0 }""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://pi.hole") };
        var client = new PiHoleClient(httpClient, "a b&c");

        await client.GetSummaryAsync();

        Assert.DoesNotContain("a b&c", capturedPath);
        Assert.Contains(Uri.EscapeDataString("a b&c"), capturedPath);
    }
}
