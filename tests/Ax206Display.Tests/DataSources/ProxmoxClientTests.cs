using System.Net;
using System.Text;
using Ax206Display.DataSources.Proxmox;
using Ax206Display.Tests.TestSupport;

namespace Ax206Display.Tests.DataSources;

public class ProxmoxClientTests
{
    [Fact]
    public async Task LoginAsync_SendsRealmQualifiedUsername()
    {
        string? capturedBody = null;
        var handler = new FakeHttpMessageHandler(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            const string json = """{ "data": { "ticket": "TICKET123", "CSRFPreventionToken": "CSRF456" } }""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        });
        var client = CreateClient(handler);

        await client.LoginAsync("root", "hunter2", "pam");

        Assert.Contains("username=root%40pam", capturedBody);
    }

    [Fact]
    public async Task GetNodeStatusesAsync_BeforeLogin_Throws()
    {
        var client = CreateClient(new FakeHttpMessageHandler(_ => throw new InvalidOperationException("should not be called")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetNodeStatusesAsync());
    }

    [Fact]
    public async Task GetNodeStatusesAsync_SendsAuthCookieAndParsesNodes()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/access/ticket", StringComparison.Ordinal))
            {
                const string ticketJson = """{ "data": { "ticket": "TICKET123", "CSRFPreventionToken": "CSRF456" } }""";
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ticketJson, Encoding.UTF8, "application/json") };
            }

            Assert.True(request.Headers.TryGetValues("Cookie", out var cookies));
            Assert.Contains("PVEAuthCookie=TICKET123", cookies!.Single());

            const string nodesJson = """{ "data": [ { "node": "pve1", "status": "online", "cpu": 0.12, "mem": 1000, "maxmem": 4000, "uptime": 3600 } ] }""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(nodesJson, Encoding.UTF8, "application/json") };
        });
        var client = CreateClient(handler);
        await client.LoginAsync("root", "hunter2");

        var nodes = await client.GetNodeStatusesAsync();

        var node = Assert.Single(nodes);
        Assert.Equal("pve1", node.Node);
        Assert.Equal("online", node.Status);
        Assert.Equal(0.12, node.CpuUsageFraction);
        Assert.Equal(4000, node.MemoryTotalBytes);
    }

    private static ProxmoxClient CreateClient(FakeHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://pve.local:8006") };
        return new ProxmoxClient(httpClient);
    }
}
