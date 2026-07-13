using System.Diagnostics;

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
