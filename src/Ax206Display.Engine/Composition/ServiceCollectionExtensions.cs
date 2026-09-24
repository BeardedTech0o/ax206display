using Ax206Display.Config.Secrets;
using Ax206Display.Config.Services;
using Ax206Display.DataSources.Network;
using Ax206Display.DataSources.Proxmox;
using Ax206Display.DataSources.SystemMonitor;
using Ax206Display.DataSources.Weather;
using Ax206Display.Engine.Services;
using Ax206Display.Rendering.Playback;
using Ax206Display.Transport.Discovery;
using Ax206Display.Transport.LibUsb;
using Microsoft.Extensions.DependencyInjection;

namespace Ax206Display.Engine.Composition;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything that drives the physical displays: config and
    /// secrets, USB discovery, the data sources and their pump services, and
    /// the device supervisor. Front ends (the WPF tray app, the web server)
    /// add their own UI on top. Picks the platform's own sensor and secret
    /// implementations, so callers never branch on the OS themselves.
    /// </summary>
    public static IServiceCollection AddAx206DisplayCore(this IServiceCollection services, Ax206DisplayPaths paths)
    {
        services.AddSingleton(paths);
        services.AddSingleton(_ => new ConfigService(paths.ConfigPath));
        services.AddSingleton(sp => new SecretStore(sp.GetRequiredService<ISecretProtector>(), paths.SecretStorePath));

        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
            services.AddSingleton<ISystemMonitorSource, LibreHardwareMonitorSystemSource>();
        }
        else
        {
            services.AddSingleton<ISecretProtector>(_ => new KeyFileSecretProtector(paths.SecretKeyPath));
            services.AddSingleton<ISystemMonitorSource>(_ => new LinuxProcSystemSource());
        }

        services.AddSingleton<IAx206DeviceDiscovery, LibUsbAx206DeviceDiscovery>();
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

        return services;
    }
}
