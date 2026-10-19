using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace FileAction.App.UI.Tools.Audio;

/// <summary>
/// The two content colours the audio tools add to the shared palette (the orange trim handles asked for by
/// the board, and the waveform dimming) plus live repainting when Windows switches light/dark while a tool
/// is open. Everything else uses <see cref="Theme"/> brushes; these two live here so no drawing code
/// contains a colour literal and so both can move into <see cref="Theme"/> with one edit at merge.
/// </summary>
internal static class AudioTheme
{
    /// <summary>Trim end handles and the selection outline.</summary>
    public static readonly Brush EndHandle = Freeze(new SolidColorBrush(Color.FromRgb(0xF2, 0x8B, 0x2E)));

    /// <summary>Shading over the parts of the waveform the trim cuts away.</summary>
    public static readonly Brush Dim = Freeze(new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)));

    private static readonly List<WeakReference<FrameworkElement>> Watched = [];
    private static bool _hooked;

    /// <summary>Starts live-repainting a tool root after the Windows app theme changes.</summary>
    public static void Watch(FrameworkElement root)
    {
        lock (Watched)
        {
            Watched.RemoveAll(reference => !reference.TryGetTarget(out var alive) || ReferenceEquals(alive, root));
            Watched.Add(new WeakReference<FrameworkElement>(root));
        }

        if (_hooked)
            return;
        _hooked = true;
        try
        {
            SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
        }
        catch (InvalidOperationException)
        {
            // No interactive window station (headless test host): there is nothing to repaint.
        }
    }

    public static void Unwatch(FrameworkElement root)
    {
        lock (Watched)
            Watched.RemoveAll(reference => !reference.TryGetTarget(out var alive) || ReferenceEquals(alive, root));
    }

    private static void OnPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        try
        {
            var application = Application.Current;
            application?.Dispatcher.BeginInvoke(new Action(Repaint));
        }
        catch (InvalidOperationException)
        {
            // The dispatcher is shutting down; the tool windows are closing anyway.
        }
    }

    private static void Repaint()
    {
        Theme.Refresh();

        List<FrameworkElement> roots = [];
        lock (Watched)
        {
            foreach (var reference in Watched)
            {
                if (reference.TryGetTarget(out var root))
                    roots.Add(root);
            }
            Watched.RemoveAll(reference => !reference.TryGetTarget(out _));
        }

        foreach (var root in roots)
            RepaintTree(root);
    }

    private static void RepaintTree(DependencyObject node)
    {
        switch (node)
        {
            case AudioSlider slider:
                slider.RefreshTheme();
                break;
            case AudioCheckBox checkBox:
                checkBox.RefreshTheme();
                break;
            case UIElement element:
                element.InvalidateVisual();
                break;
        }

        var children = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < children; i++)
            RepaintTree(VisualTreeHelper.GetChild(node, i));
    }

    private static T Freeze<T>(T brush)
        where T : Freezable
    {
        brush.Freeze();
        return brush;
    }
}
