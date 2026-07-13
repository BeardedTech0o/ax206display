using Ax206Display.Config.Secrets;

namespace Ax206Display.Tests.Config;

public class DpapiSecretProtectorTests
{
    [Fact]
    public void Protect_OnNonWindows_ThrowsPlatformNotSupported()
    {
        // DPAPI itself only runs on Windows and is exercised manually there;
        // this test just pins the cross-platform guard that makes the rest of
        // the config/secrets pipeline safely testable on Linux CI.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // Deliberately calling a [SupportedOSPlatform("windows")] API off Windows
        // to assert its own runtime guard - the CA1416 warning here is expected.
#pragma warning disable CA1416
        var protector = new DpapiSecretProtector();

        Assert.Throws<PlatformNotSupportedException>(() => protector.Protect([1, 2, 3]));
#pragma warning restore CA1416
    }
}
