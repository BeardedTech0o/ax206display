using Ax206Display.DataSources.Http;

namespace Ax206Display.Tests.DataSources;

public class HostNormalizerTests
{
    [Theory]
    [InlineData("pi.hole", "pi.hole")]
    [InlineData("192.168.1.42", "192.168.1.42")]
    [InlineData("  pi.hole  ", "pi.hole")]
    // The exact bug this class was extracted to fix: pasting a full URL
    // (with a scheme) into what's meant to be a bare hostname field used
    // to produce a malformed "http://http://host:port" string once the
    // caller concatenated its own scheme back on.
    [InlineData("http://192.168.1.42", "192.168.1.42")]
    [InlineData("https://pi.hole", "pi.hole")]
    [InlineData("HTTP://pi.hole", "pi.hole")]
    // A port or path pasted alongside the host is also stripped - the
    // caller has its own dedicated Port field for that.
    [InlineData("192.168.1.42:8080", "192.168.1.42")]
    [InlineData("http://192.168.1.42:8080", "192.168.1.42")]
    [InlineData("pi.hole/admin", "pi.hole")]
    [InlineData("http://pi.hole:80/admin", "pi.hole")]
    public void Normalize_StripsSchemePortAndPath(string rawHost, string expected)
    {
        Assert.Equal(expected, HostNormalizer.Normalize(rawHost));
    }
}
