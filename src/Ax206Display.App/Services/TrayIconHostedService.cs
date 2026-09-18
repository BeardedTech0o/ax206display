using System.IO;
using System.Windows;
using System.Windows.Controls;
using Ax206Display.App.Views;
using Ax206Display.Config.Services;
using H.NotifyIcon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;

namespace Ax206Display.App.Services;

/// <summary>Owns the tray icon and its context menu for the lifetime of the app.</summary>
public sealed class TrayIconHostedService : IHostedService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ConfigService _configService;
    private TaskbarIcon? _trayIcon;
    private MenuItem? _startWithWindowsMenuItem;

    public TrayIconHostedService(IServiceProvider serviceProvider, ConfigService configService)
    {
        _serviceProvider = serviceProvider;
        _configService = configService;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var designerMenuItem = new MenuItem { Header = "Widget Designer..." };
        designerMenuItem.Click += (_, _) => OpenWidgetDesigner();

        var integrationsMenuItem = new MenuItem { Header = "Integrations..." };
        integrationsMenuItem.Click += (_, _) => OpenIntegrations();

        _startWithWindowsMenuItem = new MenuItem { Header = "Start with Windows", IsCheckable = true, IsChecked = SafeIsRegistered() };
        _startWithWindowsMenuItem.Click += OnToggleStartWithWindows;

        var exportConfigMenuItem = new MenuItem { Header = "Export Config..." };
        exportConfigMenuItem.Click += async (_, _) => await OnExportConfigAsync();

        var importConfigMenuItem = new MenuItem { Header = "Import Config..." };
        importConfigMenuItem.Click += async (_, _) => await OnImportConfigAsync();

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
        contextMenu.Items.Add(integrationsMenuItem);
        contextMenu.Items.Add(_startWithWindowsMenuItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(exportConfigMenuItem);
        contextMenu.Items.Add(importConfigMenuItem);
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

    private void OpenIntegrations()
    {
        var window = _serviceProvider.GetRequiredService<IntegrationsWindow>();
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

    private async Task OnExportConfigAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export Config",
            Filter = "Ax206Display config package (*.zip)|*.zip",
            FileName = "ax206display-config.zip",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var config = await _configService.LoadAsync();
            await ConfigPackageService.ExportAsync(config, dialog.FileName);
            MessageBox.Show(
                "Config exported. Note that integration passwords/API tokens are not included - " +
                "they're encrypted with a key tied to this machine, so re-enter them after importing elsewhere.",
                "Ax206Display", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed: {ex.Message}", "Ax206Display", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task OnImportConfigAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Config",
            Filter = "Ax206Display config package (*.zip)|*.zip",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var backgroundsDirectory = Path.Combine(
                Path.GetDirectoryName(ConfigService.GetDefaultConfigPath()) ?? Path.GetTempPath(),
                "ImportedBackgrounds");

            var imported = await ConfigPackageService.ImportAsync(dialog.FileName, backgroundsDirectory);
            var existing = await _configService.LoadAsync();
            var merged = ConfigPackageService.MergeImported(existing, imported);
            await _configService.SaveAsync(merged);

            MessageBox.Show(
                "Config imported. Integration passwords/API tokens were not included in the package - " +
                "re-enter them in Integrations if you imported any integrations.",
                "Ax206Display", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Import failed: {ex.Message}", "Ax206Display", MessageBoxButton.OK, MessageBoxImage.Error);
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
