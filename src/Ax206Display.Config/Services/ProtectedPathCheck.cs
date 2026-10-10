namespace Ax206Display.Config.Services;

/// <summary>
/// Whether a file lives under one of a set of roots that only administrators
/// can write to (Program Files and the like). Used before registering an
/// elevated logon task: a task that runs <c>Ax206Display.exe</c> with the
/// highest privileges is a free admin shell for anyone who can overwrite that
/// exe, so it must not point at a folder like Downloads or the Desktop.
/// </summary>
public static class ProtectedPathCheck
{
    public static bool IsUnderProtectedRoot(string path, IEnumerable<string> protectedRoots)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(protectedRoots);

        // GetFullPath collapses "..\" segments, so a path can't dodge the
        // prefix test by climbing out of the protected folder.
        var fullPath = Path.GetFullPath(path);

        foreach (var root in protectedRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var fullRoot = Path.GetFullPath(root);
            if (!fullRoot.EndsWith(Path.DirectorySeparatorChar))
            {
                fullRoot += Path.DirectorySeparatorChar;
            }

            if (fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
