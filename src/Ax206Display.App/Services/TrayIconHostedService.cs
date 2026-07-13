using System.Windows;
using System.Windows.Controls;
using Ax206Display.App.Views;
using H.NotifyIcon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ax206Display.App.Services;

/// <summary>Owns the tray icon and its context menu for the lifetime of the app.</summary>
public sealed class TrayIconHostedService : IHostedService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private TaskbarIcon? _trayIcon;
    private MenuItem? _startWithWindowsMenuItem;

    public TrayIconHostedService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var designerMenuItem = new MenuItem { Header = "Widget Designer..." };
        designerMenuItem.Click += (_, _) => OpenWidgetDesigner();

        _startWithWindowsMenuItem = new MenuItem { Header = "Start with Windows", IsCheckable = true, IsChecked = SafeIsRegistered() };
        _startWithWindowsMenuItem.Click += OnToggleStartWithWindows;

        var exitMenuItem = new MenuItem { Header = "Exit" };
        // Not _lifetime.StopApplication(): that only stops the generic
        // host's hosted services, it has no connection to the WPF
        // Application's own lifetime. With ShutdownMode=OnExplicitShutdown
        // (see App.xaml) nothing else ever calls Shutdown(), so the process
        // lingers with no window and no tray icon until killed manually.
        // Shutdown() closes any open windows and raises Exit, which drives
        // App.OnExit's existing host.StopAsync()/Dispose().
        exitMenuItem.Click += (_, _) => Application.Current.Shutdown();

        var contextMenu = new ContextMenu();
        contextMenu.Items.Add(designerMenuItem);
        contextMenu.Items.Add(_startWithWindowsMenuItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(exitMenuItem);

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Ax206Display",
            ContextMenu = contextMenu,
        };

        if (Environment.ProcessPath is { } exePath)
        {
            _trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
        }

        _trayIcon.ForceCreate();

        // A tray-only app with nothing visible on launch is easy to miss -
        // open the designer immediately so there's always something on
        // screen right after starting, instead of just a tray icon.
        OpenWidgetDesigner();

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _trayIcon?.Dispose();
    }

    private void OpenWidgetDesigner()
    {
        var window = _serviceProvider.GetRequiredService<WidgetDesignerWindow>();
        window.Show();
        window.Activate();
    }

    private void OnToggleStartWithWindows(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_startWithWindowsMenuItem!.IsChecked)
            {
                AutoStartService.Register();
            }
            else
            {
                AutoStartService.Unregister();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not update the auto-start setting: {ex.Message}", "Ax206Display", MessageBoxButton.OK, MessageBoxImage.Warning);
            _startWithWindowsMenuItem!.IsChecked = !_startWithWindowsMenuItem.IsChecked;
        }
    }

    private static bool SafeIsRegistered()
    {
        try
        {
            return AutoStartService.IsRegistered();
        }
        catch (Exception)
        {
            return false;
        }
    }
}
