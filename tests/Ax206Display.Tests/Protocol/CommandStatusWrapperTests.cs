using Ax206Display.Protocol.Transport;

namespace Ax206Display.Tests.Protocol;

public class CommandStatusWrapperTests
{
    [Fact]
    public void Parse_ValidSuccessResponse_Succeeds()
    {
        var bytes = new byte[] { 0x55, 0x53, 0x42, 0x53, 0xEF, 0xBE, 0xAD, 0xDE, 0, 0, 0, 0, 0x00 };

        var csw = CommandStatusWrapper.Parse(bytes);

        Assert.True(csw.SignatureValid);
        Assert.Equal(0, csw.Status);
        Assert.True(csw.Succeeded);
    }

    [Fact]
    public void Parse_NonZeroStatus_DoesNotSucceed()
    {
        var bytes = new byte[] { 0x55, 0x53, 0x42, 0x53, 0xEF, 0xBE, 0xAD, 0xDE, 0, 0, 0, 0, 0x01 };

        var csw = CommandStatusWrapper.Parse(bytes);

        Assert.True(csw.SignatureValid);
        Assert.False(csw.Succeeded);
    }

    [Fact]
    public void Parse_BadSignature_IsNotValid()
    {
        var bytes = new byte[] { 0, 0, 0, 0, 0xEF, 0xBE, 0xAD, 0xDE, 0, 0, 0, 0, 0x00 };

        var csw = CommandStatusWrapper.Parse(bytes);

        Assert.False(csw.SignatureValid);
        Assert.False(csw.Succeeded);
    }

    [Fact]
    public void Parse_TooShort_Throws()
    {
        Assert.Throws<ArgumentException>(() => CommandStatusWrapper.Parse(new byte[5]));
    }
}
