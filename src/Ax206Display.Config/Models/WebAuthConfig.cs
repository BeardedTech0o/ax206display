namespace Ax206Display.Config.Models;

/// <summary>
/// Credentials gating the web UI (Ax206Display.Daemon's dashboard/editor/
/// integrations pages and their APIs). Null <see cref="AppConfig.WebAuth"/>
/// means no password has been set yet - the web UI stays open, same as
/// before this existed, so upgrading doesn't lock anyone out. Only ever
/// created/read by the daemon; the Windows app has no web server and never
/// touches this. See <see cref="Ax206Display.Config.Secrets.WebPasswordHasher"/> for how
/// <see cref="PasswordHash"/> is produced/verified - it is never the
/// plaintext password.
/// </summary>
public sealed record WebAuthConfig
{
    public required string Username { get; init; }

    public required string PasswordHash { get; init; }
}
