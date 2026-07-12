using Ax206Display.App.Logging;
using Ax206Display.App.Services;
using Ax206Display.App.Views;
using Ax206Display.Config.Secrets;
using Ax206Display.Config.Services;
using Ax206Display.DataSources.SystemMonitor;
using Ax206Display.DataSources.Weather;
using Ax206Display.Transport.Discovery;
using Ax206Display.Transport.LibUsb;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ax206Display.App.Composition;

/// <summary>Builds the app's Generic Host - the single place that wires interfaces to their real, hardware-touching implementations.</summary>
public static class HostFactory
{
    public static IHost Create()
    {
        return Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.AddProvider(new FileLoggerProvider(FileLoggerProvider.GetDefaultLogFilePath())))
            .ConfigureServices(ConfigureServices)
            .Build();
    }

    private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        services.AddSingleton(_ => new ConfigService(ConfigService.GetDefaultConfigPath()));
        services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
        services.AddSingleton(sp => new SecretStore(sp.GetRequiredService<ISecretProtector>(), ConfigService.GetDefaultSecretStorePath()));

        services.AddSingleton<IAx206DeviceDiscovery, LibUsbAx206DeviceDiscovery>();
        services.AddSingleton<ISystemMonitorSource, LibreHardwareMonitorSystemSource>();
        services.AddHttpClient<IWeatherSource, OpenMeteoWeatherSource>();

        services.AddSingleton<TrayIconHostedService>();
        services.AddHostedService(sp => sp.GetRequiredService<TrayIconHostedService>());
        services.AddHostedService<DisplayManagerHostedService>();
        services.AddTransient<WidgetDesignerWindow>();
    }
}
