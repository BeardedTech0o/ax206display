using System.Net;
using Ax206Display.DataSources.Http;

namespace Ax206Display.Tests.DataSources;

public class IntegrationTargetPolicyTests
{
    [Theory]
    [InlineData("169.254.169.254")]
    [InlineData("169.254.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.255")]
    [InlineData("224.0.0.251")]
    [InlineData("fe80::1")]
    [InlineData("::")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("0.0.0.1")]
    [InlineData("0.255.255.255")]
    [InlineData("168.63.129.16")]        // Azure
    [InlineData("100.100.100.200")]      // Alibaba
    [InlineData("fd00:ec2::254")]        // AWS IPv6 metadata
    public void IsBlockedAddress_BlocksLinkLocalUnspecifiedAndMulticast(string address)
    {
        Assert.True(IntegrationTargetPolicy.IsBlockedAddress(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.5")]
    [InlineData("192.168.1.1")]
    [InlineData("172.16.0.9")]
    [InlineData("8.8.8.8")]
    [InlineData("::1")]
    [InlineData("fd00::1")]
    [InlineData("100.64.0.1")]           // carrier-grade NAT range, e.g. Tailscale
    [InlineData("168.63.129.17")]
    public void IsBlockedAddress_AllowsLoopbackAndPrivateLanAddresses(string address)
    {
        Assert.False(IntegrationTargetPolicy.IsBlockedAddress(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("https://[fe80::1]:8443")]
    [InlineData("http://2852039166/")]              // 169.254.169.254 written as one decimal number
    [InlineData("http://0xA9FEA9FE/")]              // ...as hex
    [InlineData("http://0251.0376.0251.0376/")]     // ...as octal
    [InlineData("http://169.254.169.254.:80/")]     // trailing-dot form
    [InlineData("http://[::ffff:169.254.169.254]/")] // IPv4-mapped IPv6
    [InlineData("ftp://192.168.1.1")]
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://192.168.1.1")]
    [InlineData("https://admin:hunter2@192.168.1.1")]
    [InlineData("not a url")]
    [InlineData("")]
    [InlineData(null)]
    public async Task CheckAsync_RejectsUnsafeAddresses(string? url)
    {
        Assert.NotNull(await IntegrationTargetPolicy.CheckAsync(url));
    }

    [Theory]
    [InlineData("https://192.168.1.1")]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("https://10.0.0.2:8006/")]
    [InlineData("  https://192.168.1.1  ")]
    public async Task CheckAsync_AcceptsLanAndLoopbackAddresses(string url)
    {
        Assert.Null(await IntegrationTargetPolicy.CheckAsync(url));
    }

    [Fact]
    public async Task CheckAsync_RejectsAHostNameTheResolverCannotAccept()
    {
        // Longer than the 255-character DNS limit: the resolver throws
        // ArgumentException for this, which must become a clean rejection.
        var url = "https://" + new string('a', 300) + ".example";

        var error = await IntegrationTargetPolicy.CheckAsync(url);

        Assert.NotNull(error);
    }

    [Fact]
    public async Task CheckAsync_LetsAnUnresolvableHostThroughSoTheRealConnectionReportsIt()
    {
        // RFC 6761: ".invalid" never resolves.
        Assert.Null(await IntegrationTargetPolicy.CheckAsync("https://no-such-host.invalid"));
    }
}
