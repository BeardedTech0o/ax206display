using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Ax206Display.Config.Services;

/// <summary>
/// Creates a directory if missing and locks it down: on Windows, an ACL of
/// Administrators and SYSTEM only (config/secrets live under %ProgramData%,
/// which by default inherits looser ACLs than credential-holding,
/// always-elevated app data warrants); elsewhere, mode 0700 so only the
/// account the service runs as can list or read it.
/// </summary>
public static class SecureDirectory
{
    public static void EnsureExists(string path)
    {
        Directory.CreateDirectory(path);

        if (OperatingSystem.IsWindows())
        {
            HardenAcl(path);
        }
        else
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void HardenAcl(string path)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        foreach (var sid in new[]
        {
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
        })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                sid,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        new DirectoryInfo(path).SetAccessControl(security);
    }
}
