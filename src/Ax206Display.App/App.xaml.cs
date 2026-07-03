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

        if (!VerifyBinaryIntegrity())
        {
            Shutdown(1);
            return;
        }

        _host = HostFactory.Create();
        await _host.StartAsync();
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
