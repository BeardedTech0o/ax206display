using Ax206Display.Config.Secrets;
using Ax206Display.Config.Services;
using Ax206Display.Daemon.Secrets;
using Ax206Display.Daemon.Services;
using Ax206Display.Daemon.SystemMonitor;
using Ax206Display.Daemon.Web;
using Ax206Display.DataSources.Network;
using Ax206Display.DataSources.Proxmox;
using Ax206Display.DataSources.SystemMonitor;
using Ax206Display.DataSources.Weather;
using Ax206Display.Rendering.Playback;
using Ax206Display.Transport.Discovery;
using Ax206Display.Transport.LibUsb;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Ax206Display.Daemon.Composition;

/// <summary>
/// Builds the daemon's host - the Linux/headless counterpart to
/// Ax206Display.App's HostFactory, plus the web designer UI that replaces
/// the WPF app's Widget Designer/Integrations windows (see
/// <see cref="WebEndpoints"/>), all in one process/one systemd unit rather
/// than a second service. Wires the same interfaces to the same
/// cross-platform implementations (Protocol/Transport/Rendering/DataSources
/// are untouched), swapping only the two Windows-only pieces: DPAPI secret
/// protection becomes a local key-file-backed one
/// (<see cref="LinuxFileSecretProtector"/>), and the tray icon/Task Scheduler
/// auto-start hosted services are dropped entirely (systemd owns both
/// concerns on Linux).
/// </summary>
public static class HostFactory
{
    public static WebApplication Create(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // A no-op when not launched by systemd (e.g. `dotnet run` during
        // development) - only takes effect when the NOTIFY_SOCKET
        // environment variable is present, which systemd sets itself for a
        // Type=notify unit. Lets the unit use Type=notify/WatchdogSec instead
        // of the fixed guess-a-startup-time Type=simple.
        builder.Host.UseSystemd();

        // A LAN-reachable service, not a localhost-only dev server -
        // ASPNETCORE_URLS (e.g. set by a systemd Environment= override)
        // still wins if someone sets it explicitly.
        if (Environment.GetEnvironmentVariable("ASPNETCORE_URLS") is null)
        {
            builder.WebHost.UseUrls("http://0.0.0.0:8080");
        }

        ConfigureServices(builder.Services);

        var app = builder.Build();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        WebEndpoints.Map(app);
        IntegrationsEndpoints.Map(app);
        return app;
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(_ => new ConfigService(LinuxPaths.GetConfigPath()));
        services.AddSingleton<ISecretProtector>(_ => new LinuxFileSecretProtector(LinuxPaths.GetSecretKeyPath()));
        services.AddSingleton(sp => new SecretStore(sp.GetRequiredService<ISecretProtector>(), LinuxPaths.GetSecretStorePath()));

        services.AddSingleton<IAx206DeviceDiscovery, LibUsbAx206DeviceDiscovery>();
        services.AddSingleton<ISystemMonitorSource, LinuxSystemMonitorSource>();
        services.AddSingleton<INetworkSpeedSource, NetworkInterfaceSpeedSource>();
        services.AddHttpClient<IWeatherSource, OpenMeteoWeatherSource>();

        services.AddSingleton<RenderDataHub>();
        services.AddSingleton<IRenderDataProvider>(sp => sp.GetRequiredService<RenderDataHub>());
        services.AddSingleton<ProxmoxGuestDirectory>();
        services.AddSingleton<ProxmoxNodeDirectory>();

        services.AddHostedService<SystemMonitorPumpService>();
        services.AddHostedService<NetworkSpeedPumpService>();
        services.AddHostedService<ProxmoxPumpService>();
        services.AddHostedService<PiHolePumpService>();
        services.AddHostedService<UniFiPumpService>();
        services.AddSingleton<DisplayManagerHostedService>();
        services.AddHostedService(sp => sp.GetRequiredService<DisplayManagerHostedService>());
    }
}
