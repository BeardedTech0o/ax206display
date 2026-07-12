using System.Globalization;
// Needed explicitly: the WindowsDesktop SDK removes System.IO from the
// implicit-usings set that plain net8.0 projects get.
using System.IO;
using System.Windows;
using Ax206Display.App.Composition;
using Ax206Display.App.Security;
using Microsoft.Extensions.Hosting;

namespace Ax206Display.App;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (_, args) => TryWriteCrashLog(args.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, args) => TryWriteCrashLog(args.Exception);

        if (!VerifyBinaryIntegrity())
        {
            Shutdown(1);
            return;
        }

        try
        {
            _host = HostFactory.Create();
            await _host.StartAsync();
        }
        catch (Exception ex)
        {
            ReportFatalStartupError(ex);
            Shutdown(1);
        }
    }

    private static bool VerifyBinaryIntegrity()
    {
        if (AuthenticodeVerifier.IsCurrentAssemblyTrusted())
        {
            return true;
        }

        if (!AuthenticodeVerifier.EnforcementEnabled)
        {
            // No release pipeline signs the binary yet - see
            // AuthenticodeVerifier's doc comment. Once it does, flip
            // EnforcementEnabled and this becomes a hard stop below instead
            // of a silent pass-through.
            return true;
        }

        MessageBox.Show(
            "Ax206Display's executable is unsigned or its signature could not be verified. Refusing to start.",
            "Ax206Display - Security",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        return false;
    }

    /// <summary>
    /// A tray app has no console, so an unreported startup failure looks like
    /// the app simply not launching. Always surface fatal startup errors in a
    /// message box and persist the full exception to a crash log.
    /// </summary>
    private static void ReportFatalStartupError(Exception exception)
    {
        var crashLogPath = TryWriteCrashLog(exception);

        var hint = exception.ToString().Contains("libusb", StringComparison.OrdinalIgnoreCase)
            ? "\n\nThis usually means libusb-1.0.dll is missing. Download the official libusb Windows release from https://github.com/libusb/libusb/releases and copy VS2022\\MS64\\dll\\libusb-1.0.dll next to Ax206Display.exe."
            : string.Empty;

        var logNote = crashLogPath is null
            ? string.Empty
            : $"\n\nFull details were written to:\n{crashLogPath}";

        MessageBox.Show(
            $"Ax206Display failed to start:\n\n{exception.Message}{hint}{logNote}",
            "Ax206Display - Startup Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private static string? TryWriteCrashLog(Exception? exception)
    {
        if (exception is null)
        {
            return null;
        }

        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ax206Display");
            Directory.CreateDirectory(directory);

            var crashLogPath = Path.Combine(directory, "crash.log");
            var entry = string.Create(
                CultureInfo.InvariantCulture,
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}]{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
            File.AppendAllText(crashLogPath, entry);
            return crashLogPath;
        }
        catch (Exception)
        {
            // Never let crash reporting itself take the app down (or hide
            // the original failure) - the message box still shows the error.
            return null;
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
