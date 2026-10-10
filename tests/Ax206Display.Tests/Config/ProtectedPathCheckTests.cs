using Ax206Display.Config.Services;

namespace Ax206Display.Tests.Config;

public class ProtectedPathCheckTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "protected root");

    [Fact]
    public void FileInsideTheRoot_IsProtected()
    {
        Assert.True(ProtectedPathCheck.IsUnderProtectedRoot(Path.Combine(Root, "Ax206Display", "Ax206Display.exe"), [Root]));
    }

    [Fact]
    public void ComparisonIgnoresCase()
    {
        Assert.True(ProtectedPathCheck.IsUnderProtectedRoot(Path.Combine(Root.ToUpperInvariant(), "app.exe"), [Root]));
    }

    [Fact]
    public void FileOutsideTheRoot_IsNotProtected()
    {
        var downloads = Path.Combine(Path.GetTempPath(), "Users", "someone", "Downloads", "Ax206Display.exe");

        Assert.False(ProtectedPathCheck.IsUnderProtectedRoot(downloads, [Root]));
    }

    [Fact]
    public void SiblingFolderSharingTheRootsPrefix_IsNotProtected()
    {
        // "protected root" vs "protected root (x86)" is a different folder.
        Assert.False(ProtectedPathCheck.IsUnderProtectedRoot(Path.Combine(Root + " (x86)", "app.exe"), [Root]));
    }

    [Fact]
    public void DotDotSegmentsCannotClimbOutOfTheRootUndetected()
    {
        var sneaky = Path.Combine(Root, "..", "elsewhere", "app.exe");

        Assert.False(ProtectedPathCheck.IsUnderProtectedRoot(sneaky, [Root]));
    }

    [Fact]
    public void BlankRootsAreIgnoredAndNoRootsMeansNotProtected()
    {
        var file = Path.Combine(Root, "app.exe");

        Assert.False(ProtectedPathCheck.IsUnderProtectedRoot(file, ["", "  "]));
        Assert.False(ProtectedPathCheck.IsUnderProtectedRoot(file, []));
    }

    [Fact]
    public void MultipleRoots_AnyMatchWins()
    {
        var other = Path.Combine(Path.GetTempPath(), "other root");

        Assert.True(ProtectedPathCheck.IsUnderProtectedRoot(Path.Combine(other, "app.exe"), [Root, other]));
    }
}
