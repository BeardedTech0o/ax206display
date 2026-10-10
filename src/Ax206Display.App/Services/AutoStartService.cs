using System.Diagnostics;
using Ax206Display.Config.Services;

namespace Ax206Display.App.Services;

/// <summary>
/// Registers/unregisters the app to start at logon via Windows Task Scheduler,
/// using the built-in schtasks.exe rather than a Task Scheduler client library
/// so no extra native COM interop surface is needed. The task runs with the
/// highest available privileges, matching this app's own elevated manifest.
/// </summary>
public static class AutoStartService
{
    private const string TaskName = "Ax206Display";

    public static bool IsRegistered()
    {
        var result = RunSchtasks("/Query", "/TN", TaskName);
        return result.ExitCode == 0;
    }

    public static void Register()
    {
        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine the running executable's path.");

        // The task below runs this exe with the highest privileges at every
        // logon. If the exe sits somewhere a normal user can write (Downloads,
        // the Desktop - where the portable zip tends to end up), any program
        // running as that user could swap it, or its libusb-1.0.dll, and get
        // administrator rights at the next logon.
        if (!ProtectedPathCheck.IsUnderProtectedRoot(exePath, ProtectedRoots()))
        {
            throw new InvalidOperationException(
                "Starting with Windows runs this app with administrator rights, so it has to be installed somewhere only administrators can change, such as Program Files. " +
                "Install it with the MSI installer (or move the folder there) and try again.");
        }

        // schtasks.exe re-parses /TR's value as its own mini command line (so
        // it can support "/TR \"app.exe\" -arg"), so the path needs an
        // embedded literal quote pair here even though ArgumentList below
        // already quotes each argument correctly at the process level - this
        // keeps the quoting complexity scoped to one documented value instead
        // of hand-escaping the whole command line.
        var quotedExePath = $"\"{exePath}\"";

        var result = RunSchtasks("/Create", "/F", "/SC", "ONLOGON", "/RL", "HIGHEST", "/TN", TaskName, "/TR", quotedExePath);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to register the auto-start task: {result.StandardError}");
        }
    }

    // Program Files only. The Windows folder is not on this list on purpose:
    // it has user-writable subfolders (Temp, Tasks).
    private static string[] ProtectedRoots() =>
    [
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
    ];

    public static void Unregister()
    {
        var result = RunSchtasks("/Delete", "/F", "/TN", TaskName);
        if (result.ExitCode != 0 && !result.StandardError.Contains("cannot find", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Failed to remove the auto-start task: {result.StandardError}");
        }
    }

    private static (int ExitCode, string StandardError) RunSchtasks(params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stderr);
    }
}
