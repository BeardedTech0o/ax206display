using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Ax206Display.Config.Services;

/// <summary>
/// Creates a directory if missing and, on Windows, locks its ACL down to
/// Administrators and SYSTEM only. Ax206Display's config/secrets live under
/// %ProgramData%, which by default inherits looser ACLs than credential-
/// holding, always-elevated app data warrants.
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
