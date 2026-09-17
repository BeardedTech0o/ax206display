namespace Ax206Display.Daemon;

/// <summary>
/// Default filesystem locations for the Linux daemon, following FHS: editable
/// config under /etc, generated/runtime state (secrets, the key that
/// protects them) under /var/lib. Overridable via environment variables so
/// the daemon can also run rootless/unprivileged (e.g. in a container) with
/// paths under the invoking user's own directories.
/// </summary>
public static class LinuxPaths
{
    private const string AppDirectoryName = "ax206display";

    public static string GetConfigPath() =>
        Environment.GetEnvironmentVariable("AX206DISPLAY_CONFIG_PATH")
            ?? Path.Combine("/etc", AppDirectoryName, "config.json");

    public static string GetSecretStorePath() =>
        Environment.GetEnvironmentVariable("AX206DISPLAY_SECRETS_PATH")
            ?? Path.Combine("/var/lib", AppDirectoryName, "secrets.dat");

    public static string GetSecretKeyPath() =>
        Environment.GetEnvironmentVariable("AX206DISPLAY_SECRET_KEY_PATH")
            ?? Path.Combine("/var/lib", AppDirectoryName, "protector.key");
}
