using Ax206Display.DataSources.Auth;

namespace Ax206Display.Tests.DataSources;

public class TotpGeneratorTests
{
    // RFC 6238 Appendix B's SHA1 test secret is the ASCII string
    // "12345678901234567890", i.e. this base32 encoding of it - confirmed
    // against Otp.NET's test suite (a widely-used, independently maintained
    // .NET TOTP implementation) rather than reproduced from memory. RFC 6238
    // publishes its vectors as 8-digit codes; GenerateCode always returns 6,
    // which is just the last 6 digits of the same underlying dynamic
    // truncation - so each expected value below is the RFC's published
    // 8-digit code with its leading 2 digits dropped.
    private const string Rfc6238SecretSha1Base32 = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    [Theory]
    [InlineData(59, "287082")]
    [InlineData(1111111109, "081804")]
    [InlineData(1111111111, "050471")]
    [InlineData(1234567890, "005924")]
    [InlineData(2000000000, "279037")]
    public void GenerateCode_MatchesRfc6238TestVectors(long unixSeconds, string expectedCode)
    {
        var at = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

        var code = TotpGenerator.GenerateCode(Rfc6238SecretSha1Base32, at);

        Assert.Equal(expectedCode, code);
    }

    [Theory]
    [InlineData(59)]
    [InlineData(1111111109)]
    [InlineData(1111111111)]
    [InlineData(1234567890)]
    [InlineData(2000000000)]
    public void GenerateCode_IsAlwaysSixDigitsZeroPadded(long unixSeconds)
    {
        var at = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

        var code = TotpGenerator.GenerateCode(Rfc6238SecretSha1Base32, at);

        Assert.Equal(6, code.Length);
    }

    [Fact]
    public void GenerateCode_AcceptsLowercaseAndPaddedSecret()
    {
        var at = DateTimeOffset.FromUnixTimeSeconds(59);

        var code = TotpGenerator.GenerateCode(Rfc6238SecretSha1Base32.ToLowerInvariant() + "====", at);

        Assert.Equal("287082", code);
    }

    [Fact]
    public void GenerateCode_ThrowsForInvalidBase32Character()
    {
        Assert.Throws<FormatException>(() => TotpGenerator.GenerateCode("not-valid-base32-1"));
    }
}
