using Ax206Display.App.Services;
using Ax206Display.App.Views;
using Ax206Display.Engine.Composition;
using Ax206Display.Engine.Logging;
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
        // Registered ahead of the core services: hosted services start in
        // registration order, and the tray icon should appear straight away
        // rather than after the display manager's initial USB scan.
        services.AddSingleton<TrayIconHostedService>();
        services.AddHostedService(sp => sp.GetRequiredService<TrayIconHostedService>());

        services.AddAx206DisplayCore(Ax206DisplayPaths.ForWindows());

        services.AddTransient<WidgetDesignerWindow>();
        services.AddTransient<IntegrationsWindow>();
    }
}
