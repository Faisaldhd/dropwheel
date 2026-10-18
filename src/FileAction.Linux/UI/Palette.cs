using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Platform;

namespace FileAction.Linux.UI;

/// <summary>The same palette as the Windows app, for light and dark desktops.</summary>
public static class Palette
{
    public static bool IsDark { get; private set; } = true;

    /// <summary>Forces dark (true) or light (false); used by the self-test.</summary>
    public static bool? Override { get; set; }

    public static void Refresh()
    {
        var variant = Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant;
        IsDark = Override ?? variant != PlatformThemeVariant.Light;
    }

    private static IBrush Solid(byte a, byte r, byte g, byte b) => new ImmutableSolidColorBrush(Color.FromArgb(a, r, g, b));

    public static readonly Color AccentColor = Color.FromRgb(0xFF, 0x8A, 0x1F);
    public static readonly Color AccentColor2 = Color.FromRgb(0xFF, 0x4F, 0x6A);

    public static IBrush AccentGradient { get; } = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(AccentColor, 0), new GradientStop(AccentColor2, 1) },
    }.ToImmutable();

    public static IBrush Accent { get; } = new ImmutableSolidColorBrush(AccentColor);
    public static IBrush TextOnAccent { get; } = Brushes.White;
    public static IBrush Error { get; } = Solid(0xFF, 0xF0, 0x5A, 0x5A);
    public static IBrush Success { get; } = Solid(0xFF, 0x3C, 0xC3, 0x6E);

    public static IBrush Backdrop => IsDark ? Solid(0xE6, 0x16, 0x16, 0x1B) : Solid(0xEE, 0xF6, 0xF6, 0xF9);
    public static IBrush BackdropEdge => IsDark ? Solid(0x26, 0xFF, 0xFF, 0xFF) : Solid(0x1A, 0, 0, 0);
    public static IBrush Slice => IsDark ? Solid(0xFF, 0x26, 0x26, 0x2E) : Brushes.White;
    public static IBrush SliceEdge => IsDark ? Solid(0x14, 0xFF, 0xFF, 0xFF) : Solid(0x14, 0, 0, 0);
    public static IBrush Hub => IsDark ? Solid(0xFF, 0x1D, 0x1D, 0x24) : Brushes.White;
    public static IBrush Panel => IsDark ? Solid(0xF7, 0x1F, 0x1F, 0x26) : Solid(0xFA, 0xFB, 0xFB, 0xFD);
    public static IBrush PanelBorder => IsDark ? Solid(0x24, 0xFF, 0xFF, 0xFF) : Solid(0x1C, 0, 0, 0);
    public static IBrush Text => IsDark ? Solid(0xFF, 0xF5, 0xF5, 0xF7) : Solid(0xFF, 0x1B, 0x1B, 0x22);
    public static IBrush Muted => IsDark ? Solid(0xFF, 0x9C, 0x9C, 0xA8) : Solid(0xFF, 0x6E, 0x6E, 0x7A);
    public static IBrush Track => IsDark ? Solid(0x30, 0xFF, 0xFF, 0xFF) : Solid(0x1E, 0, 0, 0);
    public static IBrush Page => IsDark ? Solid(0xFF, 0x1C, 0x1C, 0x21) : Solid(0xFF, 0xF3, 0xF3, 0xF6);

    public static DropShadowEffect Shadow(double blur = 24) =>
        new() { BlurRadius = blur, OffsetX = 0, OffsetY = 3, Color = Colors.Black, Opacity = IsDark ? 0.55 : 0.22 };

    public static DropShadowEffect Glow() =>
        new() { BlurRadius = 26, OffsetX = 0, OffsetY = 0, Color = AccentColor, Opacity = IsDark ? 0.75 : 0.5 };
}
