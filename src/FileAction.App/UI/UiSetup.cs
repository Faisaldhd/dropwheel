using System.Windows;
using System.Windows.Controls;

namespace FileAction.App.UI;

/// <summary>Small cross-window setup shared by every window the app creates.</summary>
internal static class UiSetup
{
    /// <summary>Reading direction plus pixel snapping, applied when a window is built.</summary>
    public static void Configure(Window window)
    {
        window.FlowDirection = LanguageManager.FlowDirection;
        window.UseLayoutRounding = true;
        window.SnapsToDevicePixels = true;
        DarkTitleBar.Apply(window);
    }

    /// <summary>
    /// Keeps a window inside the work area at high display scaling; at 200 % a fixed 820-DIP
    /// window is taller than a 1080p desktop, so its content would otherwise start off-screen.
    /// </summary>
    public static void FitToWorkArea(Window window, double preferredWidth, double preferredHeight,
        double minWidth = 0, double minHeight = 0)
    {
        var work = SystemParameters.WorkArea;
        if (work.Width <= 0 || work.Height <= 0)
        {
            window.Width = preferredWidth;
            window.Height = preferredHeight;
            return;
        }
        window.Width = Math.Max(minWidth, Math.Min(preferredWidth, work.Width - 24));
        window.Height = Math.Max(minHeight, Math.Min(preferredHeight, work.Height - 24));
    }

    /// <summary>A tooltip that is not attached to the window tree yet, so it needs its own direction.</summary>
    public static ToolTip Tip(string text) => new()
    {
        Content = text,
        FlowDirection = LanguageManager.FlowDirection,
    };
}
