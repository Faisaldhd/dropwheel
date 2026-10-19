using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace FileAction.App.UI;

/// <summary>
/// Colours and fonts shared by the wheel, the job cards and the shelf. Follows the Windows
/// light/dark app setting; <see cref="Refresh"/> re-reads it before a window is built.
/// </summary>
internal static class Theme
{
    private static readonly Palette DarkPalette = new(
        Backdrop: Color.FromArgb(0xE6, 0x15, 0x11, 0x22),
        BackdropEdge: Color.FromArgb(0x33, 0xB7, 0x8A, 0xE7),
        Slice: Color.FromArgb(0xFF, 0x24, 0x1A, 0x35),
        SliceEdge: Color.FromArgb(0x26, 0xC0, 0x8C, 0xF0),
        Hub: Color.FromArgb(0xFF, 0x1A, 0x15, 0x28),
        Panel: Color.FromArgb(0xF7, 0x1D, 0x18, 0x30),
        PanelEdge: Color.FromArgb(0x2E, 0xB7, 0x8A, 0xE7),
        Text: Color.FromRgb(0xF5, 0xF2, 0xFA),
        Muted: Color.FromRgb(0xA8, 0x9F, 0xBF),
        Track: Color.FromArgb(0x33, 0xC0, 0x8C, 0xF0),
        Page: Color.FromRgb(0x18, 0x12, 0x20),
        Card: Color.FromRgb(0x23, 0x21, 0x31),
        Hover: Color.FromArgb(0x1F, 0xC0, 0x8C, 0xF0),
        Shadow: 0.6);

    private static readonly Palette LightPalette = new(
        Backdrop: Color.FromArgb(0xF0, 0xF6, 0xF3, 0xFB),
        BackdropEdge: Color.FromArgb(0x1F, 0x3D, 0x35, 0x5E),
        Slice: Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
        SliceEdge: Color.FromArgb(0x1F, 0x6A, 0x2B, 0xB8),
        Hub: Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
        Panel: Color.FromArgb(0xFA, 0xFB, 0xF9, 0xFE),
        PanelEdge: Color.FromArgb(0x1F, 0x3D, 0x35, 0x5E),
        Text: Color.FromRgb(0x1E, 0x15, 0x30),
        Muted: Color.FromRgb(0x6E, 0x66, 0x80),
        Track: Color.FromArgb(0x1F, 0x6A, 0x2B, 0xB8),
        Page: Color.FromRgb(0xF4, 0xF1, 0xF9),
        Card: Color.FromRgb(0xFF, 0xFF, 0xFF),
        Hover: Color.FromArgb(0x14, 0x6A, 0x2B, 0xB8),
        Shadow: 0.22);

    private sealed record Palette(
        Color Backdrop,
        Color BackdropEdge,
        Color Slice,
        Color SliceEdge,
        Color Hub,
        Color Panel,
        Color PanelEdge,
        Color Text,
        Color Muted,
        Color Track,
        Color Page,
        Color Card,
        Color Hover,
        double Shadow);

    private static Palette _palette = DarkPalette;

    public static readonly Color AccentColor = Color.FromRgb(0x82, 0x24, 0xD4);
    public static readonly Color AccentColor2 = Color.FromRgb(0xB4, 0x8A, 0xE0);

    /// <summary>Violet → lavender, used for the selected slice, active tab and progress bars.</summary>
    public static readonly LinearGradientBrush AccentGradient = Freeze(new LinearGradientBrush(AccentColor, AccentColor, new Point(0, 0), new Point(1, 1)));

    public static readonly SolidColorBrush Accent = Freeze(new SolidColorBrush(AccentColor));
    public static readonly SolidColorBrush TextOnAccent = Freeze(new SolidColorBrush(Colors.White));
    public static readonly SolidColorBrush Error = Freeze(new SolidColorBrush(Color.FromRgb(0xF0, 0x5A, 0x5A)));
    public static readonly SolidColorBrush Success = Freeze(new SolidColorBrush(Color.FromRgb(0x3C, 0xC3, 0x6E)));

    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    public static readonly FontFamily UiFont = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily DisplayFont = new("Segoe UI Variable Display, Segoe UI");

    public static bool IsDark { get; private set; } = true;

    public static Brush Backdrop => Solid(_palette.Backdrop);
    public static Brush BackdropEdge => Solid(_palette.BackdropEdge);
    public static Brush Slice => Solid(_palette.Slice);
    public static Brush SliceEdge => Solid(_palette.SliceEdge);
    public static Brush Hub => Solid(_palette.Hub);
    public static Brush Panel => Solid(_palette.Panel);
    public static Brush PanelBorder => Solid(_palette.PanelEdge);
    public static Brush Text => Solid(_palette.Text);
    public static Brush Muted => Solid(_palette.Muted);
    public static Brush Track => Solid(_palette.Track);
    public static Brush Page => Solid(_palette.Page);
    public static Brush Card => Solid(_palette.Card);
    public static Brush Hover => Solid(_palette.Hover);
    public static Brush AccentSoft => Solid(Color.FromArgb(IsDark ? (byte)0x38 : (byte)0x26, AccentColor.R, AccentColor.G, AccentColor.B));

    /// <summary>Soft drop shadow under floating surfaces.</summary>
    public static System.Windows.Media.Effects.DropShadowEffect Shadow(double blur = 24) =>
        new() { BlurRadius = blur, ShadowDepth = 3, Direction = 270, Opacity = _palette.Shadow, Color = Colors.Black };

    /// <summary>Coloured glow for the highlighted slice.</summary>
    public static System.Windows.Media.Effects.DropShadowEffect Glow() =>
        new() { BlurRadius = 14, ShadowDepth = 0, Opacity = IsDark ? 0.45 : 0.3, Color = AccentColor };

    /// <summary>Forces dark (true) or light (false) instead of following Windows; used by the self-test.</summary>
    public static bool? Override { get; set; }

    /// <summary>Applies the user's System/Dark/Light choice from settings.</summary>
    public static void Apply(ThemeMode mode)
    {
        Override = mode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => null,
        };
        Refresh();
    }

    /// <summary>Re-reads the Windows "app mode" (light/dark).</summary>
    public static void Refresh() => Use(Override ?? ReadSystemDark());

    private static void Use(bool dark)
    {
        IsDark = dark;
        DarkTitleBar.ApplyToOpenWindows();
        _palette = dark ? DarkPalette : LightPalette;
    }

    private static bool ReadSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int light || light == 0;
        }
        catch (System.Security.SecurityException)
        {
            return true;
        }
    }

    private static SolidColorBrush Solid(Color color) => Freeze(new SolidColorBrush(color));

    private static T Freeze<T>(T brush)
        where T : Freezable
    {
        brush.Freeze();
        return brush;
    }
}
