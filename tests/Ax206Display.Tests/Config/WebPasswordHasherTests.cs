using Ax206Display.Config.Secrets;

namespace Ax206Display.Tests.Config;

public class WebPasswordHasherTests
{
    [Fact]
    public void Verify_WithCorrectPassword_ReturnsTrue()
    {
        var hash = WebPasswordHasher.Hash("correct-horse-battery");

        Assert.True(WebPasswordHasher.Verify("correct-horse-battery", hash));
    }

    [Fact]
    public void Verify_WithWrongPassword_ReturnsFalse()
    {
        var hash = WebPasswordHasher.Hash("correct-horse-battery");

        Assert.False(WebPasswordHasher.Verify("wrong-password", hash));
    }

    [Fact]
    public void Hash_ProducesDifferentOutputForSamePassword()
    {
        // Random per-hash salt - two hashes of the same password should never collide.
        var first = WebPasswordHasher.Hash("same-password");
        var second = WebPasswordHasher.Hash("same-password");

        Assert.NotEqual(first, second);
        Assert.True(WebPasswordHasher.Verify("same-password", first));
        Assert.True(WebPasswordHasher.Verify("same-password", second));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-valid-hash")]
    [InlineData("abc.def")]
    [InlineData("notanumber.c2FsdA==.aGFzaA==")]
    [InlineData("100.not-base64!.aGFzaA==")]
    public void Verify_WithMalformedHash_ReturnsFalseInsteadOfThrowing(string malformedHash)
    {
        Assert.False(WebPasswordHasher.Verify("anything", malformedHash));
    }
}
