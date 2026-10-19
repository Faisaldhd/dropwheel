using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FileAction.App.UI;

/// <summary>
/// R-30: Windows draws the title bar light even for a dark WPF window. DWMWA_USE_IMMERSIVE_DARK_MODE tells
/// DWM to draw it dark; the attribute id is 20 on current Windows and 19 on older 1809-era builds.
/// </summary>
internal static class DarkTitleBar
{
    private const int UseImmersiveDarkMode = 20;
    private const int UseImmersiveDarkModeLegacy = 19;

    /// <summary>Applies the current theme to one window; safe to call before the handle exists.</summary>
    public static void Apply(Window window)
    {
        if (window is null)
            return;
        if (new WindowInteropHelper(window).Handle == IntPtr.Zero)
        {
            // Configure() runs before Show(), so retry once the handle is created.
            window.SourceInitialized -= OnSourceInitialized;
            window.SourceInitialized += OnSourceInitialized;
            return;
        }
        Set(window);
    }

    /// <summary>Re-applies the theme to every open window (used when the theme switches live).</summary>
    public static void ApplyToOpenWindows()
    {
        if (Application.Current is null)
            return;
        foreach (Window window in Application.Current.Windows)
        {
            if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
                Set(window);
        }
    }

    private static void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.SourceInitialized -= OnSourceInitialized;
            Set(window);
        }
    }

    private static void Set(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
                return;
            var enabled = Theme.IsDark ? 1 : 0;
            if (DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, UseImmersiveDarkModeLegacy, ref enabled, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            // DWM is unavailable (very old Windows or a remote session): the light title bar is cosmetic only.
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
