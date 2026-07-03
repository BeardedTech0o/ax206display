using System.Diagnostics;

namespace Ax206Display.App.Services;

/// <summary>
/// Registers/unregisters the app to start at logon via Windows Task Scheduler,
/// using the built-in schtasks.exe rather than a Task Scheduler client library
/// so no extra native COM interop surface is needed. The task runs with the
/// highest available privileges, matching this app's own elevated manifest.
/// </summary>
public sealed class AutoStartService
{
    private const string TaskName = "Ax206Display";

    public bool IsRegistered()
    {
        var result = RunSchtasks($"/Query /TN \"{TaskName}\"");
        return result.ExitCode == 0;
    }

    public void Register()
    {
        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine the running executable's path.");

        var result = RunSchtasks(
            $"/Create /F /SC ONLOGON /RL HIGHEST /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\"\"");

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to register the auto-start task: {result.StandardError}");
        }
    }

    public void Unregister()
    {
        var result = RunSchtasks($"/Delete /F /TN \"{TaskName}\"");
        if (result.ExitCode != 0 && !result.StandardError.Contains("cannot find", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Failed to remove the auto-start task: {result.StandardError}");
        }
    }

    private static (int ExitCode, string StandardError) RunSchtasks(string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        process.Start();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stderr);
    }
}
