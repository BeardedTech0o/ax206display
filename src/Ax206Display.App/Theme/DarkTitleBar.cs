using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Ax206Display.App.Theme;

/// <summary>
/// Switches a window's native title bar to dark mode via DWM. The title bar
/// is drawn by the OS, not WPF - ForgeTheme.xaml's resources have no way to
/// reach it, this is the only supported route. Requires Windows 10 20H1
/// (build 19041) or later; this app's TargetFramework is already pinned to
/// net8.0-windows10.0.19041 (SkiaSharp.Views.WPF's constraint - see
/// Ax206Display.App.csproj), so every machine this runs on supports it.
/// </summary>
internal static class DarkTitleBar
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Call before the window is shown - it hooks SourceInitialized, since the native handle doesn't exist yet in a constructor.</summary>
    internal static void Apply(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            var useDarkMode = 1;
            // Best-effort: an older Windows build just ignores the call
            // (returns a failure HRESULT) rather than throwing, so there's
            // nothing worth handling here.
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref useDarkMode, sizeof(int));
        };
    }
}
