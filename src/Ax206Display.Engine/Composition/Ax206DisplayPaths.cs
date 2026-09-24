using Ax206Display.Config.Services;

namespace Ax206Display.Engine.Composition;

/// <summary>
/// Where one running instance keeps its state. Windows keeps the historical
/// %ProgramData%\Ax206Display layout; Linux gets a single data directory
/// (systemd's StateDirectory in the packaged service) so the whole install is
/// one folder to back up or wipe.
/// </summary>
public sealed record Ax206DisplayPaths(string DataDirectory, string ConfigPath, string SecretStorePath, string SecretKeyPath)
{
    public string BackgroundImagesDirectory => Path.Combine(DataDirectory, "backgrounds");

    public static Ax206DisplayPaths ForWindows() =>
        new(
            Path.GetDirectoryName(ConfigService.GetDefaultConfigPath())!,
            ConfigService.GetDefaultConfigPath(),
            ConfigService.GetDefaultSecretStorePath(),
            // Unused on Windows (DPAPI needs no key file), kept for symmetry.
            Path.Combine(Path.GetDirectoryName(ConfigService.GetDefaultConfigPath())!, "secret.key"));

    public static Ax206DisplayPaths ForDataDirectory(string dataDirectory) =>
        new(
            dataDirectory,
            Path.Combine(dataDirectory, "config.json"),
            Path.Combine(dataDirectory, "secrets.dat"),
            Path.Combine(dataDirectory, "secret.key"));

    /// <summary>
    /// Resolution order on Linux: an explicit AX206_DATA_DIR, then systemd's
    /// STATE_DIRECTORY (set by StateDirectory= in the unit file), then the XDG
    /// data home for someone running the binary by hand as their own user.
    /// </summary>
    public static Ax206DisplayPaths ResolveDefault()
    {
        if (Environment.GetEnvironmentVariable("AX206_DATA_DIR") is { Length: > 0 } explicitDirectory)
        {
            return ForDataDirectory(explicitDirectory);
        }

        if (OperatingSystem.IsWindows())
        {
            return ForWindows();
        }

        // StateDirectory= can list several colon-separated paths; ours sets one.
        if (Environment.GetEnvironmentVariable("STATE_DIRECTORY") is { Length: > 0 } stateDirectory)
        {
            return ForDataDirectory(stateDirectory.Split(':')[0]);
        }

        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdg
            ? xdg
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return ForDataDirectory(Path.Combine(dataHome, "ax206display"));
    }
}
