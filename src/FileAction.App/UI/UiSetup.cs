using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FileAction.App.UI;

/// <summary>Small cross-window setup shared by every window the app creates.</summary>
internal static class UiSetup
{
    /// <summary>
    /// Reading direction plus pixel snapping, applied when a window is built. With <paramref name="animate"/>
    /// (the default) the shared open/close motion from <see cref="Motion"/> is attached here too, so every
    /// settings window, dialog and floating panel gets it without repeating the animation code.
    /// </summary>
    public static void Configure(Window window, bool animate = true)
    {
        window.FlowDirection = LanguageManager.FlowDirection;
        window.UseLayoutRounding = true;
        window.SnapsToDevicePixels = true;
        DarkTitleBar.Apply(window);
        if (animate)
            AttachWindowMotion(window);
    }

    private static readonly DependencyProperty WindowMotionProperty = DependencyProperty.RegisterAttached(
        "WindowMotion", typeof(bool), typeof(UiSetup), new PropertyMetadata(false));

    /// <summary>
    /// One fade/rise/settle in, one 120 ms fade out. Reduced motion is checked at event time, so turning
    /// the Windows setting or the app toggle off takes effect on the next window without a restart.
    /// </summary>
    private static void AttachWindowMotion(Window window)
    {
        if ((bool)window.GetValue(WindowMotionProperty))
            return;
        window.SetValue(WindowMotionProperty, true);

        Motion.Handle? handle = null;
        FrameworkElement? content = null;
        window.Loaded += (_, _) =>
        {
            if (!Motion.Enabled)
                return;
            content = window.Content as FrameworkElement;
            if (content is null)
                return;
            handle = Motion.Prepare(content);
            Motion.Play(Motion.WindowIn(window, content, handle), window);
        };
        window.Closing += (_, e) =>
        {
            if (!Motion.Enabled || !window.IsVisible || e.Cancel || window.Opacity <= 0.01 ||
                window.Dispatcher.HasShutdownStarted || DialogResultAlreadySet(window))
                return;
            e.Cancel = true;
            var surface = content;
            var motion = handle;
            Motion.Play(Motion.WindowOut(window, surface, motion, () =>
            {
                if (surface is not null && motion is not null)
                    Motion.Reset(motion);
                window.Close();
            }), window);
        };
    }

    /// <summary>
    /// A modal dialog closes itself when its DialogResult is assigned. Cancelling that close to play a
    /// fade would reset the result, so a dialog that already decided is never held back by motion.
    /// </summary>
    private static bool DialogResultAlreadySet(Window window)
    {
        try
        {
            return window.DialogResult is not null;
        }
        catch (InvalidOperationException)
        {
            // Not shown as a dialog.
            return false;
        }
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
