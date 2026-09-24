using Ax206Display.Engine.Composition;

namespace Ax206Display.Tests.Engine;

public class Ax206DisplayPathsTests
{
    [Fact]
    public void ForDataDirectory_KeepsEverythingInOneFolder()
    {
        var paths = Ax206DisplayPaths.ForDataDirectory("/var/lib/ax206display");

        Assert.Equal(Path.Combine("/var/lib/ax206display", "config.json"), paths.ConfigPath);
        Assert.Equal(Path.Combine("/var/lib/ax206display", "secrets.dat"), paths.SecretStorePath);
        Assert.Equal(Path.Combine("/var/lib/ax206display", "secret.key"), paths.SecretKeyPath);
        Assert.Equal(Path.Combine("/var/lib/ax206display", "backgrounds"), paths.BackgroundImagesDirectory);
    }
}
