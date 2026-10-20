using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FileAction.App.UI.Tools.Video;

/// <summary>
/// The <see cref="Theme"/> colours a video window was built with. Captured before a theme change so the
/// window can be retinted in place: the old colour identifies the role, the new palette supplies the brush.
/// </summary>
internal readonly record struct VideoThemePalette(
    Color Text,
    Color Muted,
    Color Card,
    Color PanelBorder,
    Color Track,
    Color AccentSoft,
    Color Page,
    Color Panel,
    Color PanelGlass)
{
    public static VideoThemePalette Capture()
    {
        var panel = ColorOf(Theme.Panel);
        return new VideoThemePalette(
            ColorOf(Theme.Text),
            ColorOf(Theme.Muted),
            ColorOf(Theme.Card),
            ColorOf(Theme.PanelBorder),
            ColorOf(Theme.Track),
            ColorOf(Theme.AccentSoft),
            ColorOf(Theme.Page),
            panel,
            // The shared ToolWindow paints its glass as Panel with a fixed alpha; derive the same colour.
            Color.FromArgb(0xE6, panel.R, panel.G, panel.B));
    }

    private static Color ColorOf(Brush brush) => (brush as SolidColorBrush)?.Color ?? default;
}

/// <summary>
/// Repaints a built video tool (and its shared window chrome) when the theme changes, without rebuilding
/// the view and losing the user's trim range, crop rectangle or speed. Technical content colours (black
/// dimming, white playhead/thumb) are intentionally not in the palette map and stay unchanged.
/// </summary>
internal static class VideoThemeTint
{
    /// <summary>Maps one palette colour; anything else (content colour) is returned unchanged.</summary>
    public static Color Map(Color color, VideoThemePalette before, VideoThemePalette after)
    {
        if (color == before.Text) return after.Text;
        if (color == before.Muted) return after.Muted;
        if (color == before.Card) return after.Card;
        if (color == before.PanelBorder) return after.PanelBorder;
        if (color == before.Track) return after.Track;
        if (color == before.AccentSoft) return after.AccentSoft;
        if (color == before.Page) return after.Page;
        if (color == before.Panel) return after.Panel;
        if (color == before.PanelGlass) return after.PanelGlass;
        return color;
    }

    /// <summary>Replaces a solid brush that belongs to the old palette; gradients and content colours stay.</summary>
    public static Brush? MapBrush(Brush? brush, VideoThemePalette before, VideoThemePalette after)
    {
        if (brush is not SolidColorBrush solid)
            return brush;
        var mapped = Map(solid.Color, before, after);
        if (mapped == solid.Color)
            return brush;
        var replacement = new SolidColorBrush(mapped);
        replacement.Freeze();
        return replacement;
    }

    /// <summary>Walks the visual tree and retints every themed brush in place.</summary>
    public static void Apply(DependencyObject root, VideoThemePalette before, VideoThemePalette after)
    {
        ArgumentNullException.ThrowIfNull(root);
        Retint(root, before, after);

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
            Apply(VisualTreeHelper.GetChild(root, index), before, after);
    }

    private static void Retint(DependencyObject item, VideoThemePalette before, VideoThemePalette after)
    {
        switch (item)
        {
            case Border border:
                border.Background = MapBrush(border.Background, before, after);
                border.BorderBrush = MapBrush(border.BorderBrush, before, after);
                break;
            case Panel panel:
                panel.Background = MapBrush(panel.Background, before, after);
                break;
            case Control control:
                control.Background = MapBrush(control.Background, before, after);
                control.Foreground = MapBrush(control.Foreground, before, after);
                control.BorderBrush = MapBrush(control.BorderBrush, before, after);
                if (control is TextBox box)
                    box.CaretBrush = MapBrush(box.CaretBrush, before, after);
                break;
            case TextBlock text:
                text.Foreground = MapBrush(text.Foreground, before, after);
                text.Background = MapBrush(text.Background, before, after);
                break;
            case Shape shape:
                shape.Fill = MapBrush(shape.Fill, before, after);
                shape.Stroke = MapBrush(shape.Stroke, before, after);
                break;
        }
    }
}
