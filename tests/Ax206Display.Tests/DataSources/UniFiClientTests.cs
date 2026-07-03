using System.Net;
using System.Text;
using Ax206Display.DataSources.UniFi;
using Ax206Display.Tests.TestSupport;

namespace Ax206Display.Tests.DataSources;

public class UniFiClientTests
{
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
            if (request.RequestUri!.AbsolutePath.EndsWith("/auth/login"))
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

    private static UniFiClient CreateClient(FakeHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://udm.local") };
        return new UniFiClient(httpClient);
    }
}
