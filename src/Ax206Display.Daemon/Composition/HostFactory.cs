using Ax206Display.Config.Secrets;
using Ax206Display.Config.Services;
using Ax206Display.Daemon.Secrets;
using Ax206Display.Daemon.Services;
using Ax206Display.Daemon.SystemMonitor;
using Ax206Display.DataSources.Network;
using Ax206Display.DataSources.Proxmox;
using Ax206Display.DataSources.SystemMonitor;
using Ax206Display.DataSources.Weather;
using Ax206Display.Rendering.Playback;
using Ax206Display.Transport.Discovery;
using Ax206Display.Transport.LibUsb;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ax206Display.Daemon.Composition;

/// <summary>
/// Builds the daemon's Generic Host - the Linux/headless counterpart to
/// Ax206Display.App's HostFactory. Wires the same interfaces to the same
/// cross-platform implementations (Protocol/Transport/Rendering/DataSources
/// are untouched), swapping only the two Windows-only pieces: DPAPI secret
/// protection becomes a local key-file-backed one
/// (<see cref="LinuxFileSecretProtector"/>), and the tray icon/Task Scheduler
/// auto-start hosted services are dropped entirely (systemd owns both
/// concerns on Linux).
/// </summary>
public static class HostFactory
{
    public static IHost Create(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureServices(ConfigureServices)
            .Build();
    }

    private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
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
