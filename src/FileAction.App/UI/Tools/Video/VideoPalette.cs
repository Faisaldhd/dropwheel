using System.Windows;
using System.Windows.Media;

namespace FileAction.App.UI.Tools.Video;

/// <summary>
/// The single place in the video tools that names a literal colour: the four redaction content colours
/// (what the user paints with) plus the transparent and alpha-tint helpers. Every other surface uses
/// <see cref="Theme"/> brushes, so switching the app theme is a repaint, not a rebuild.
/// </summary>
internal static class VideoPalette
{
    public sealed record Option(string Token, Color Fill);

    /// <summary>Content colours offered for a solid redaction box.</summary>
    public static readonly IReadOnlyList<Option> Options =
    [
        new("black", Color.FromRgb(0x18, 0x15, 0x22)),
        new("0xFFFFFF", Colors.White),
        new("0xC62828", Color.FromRgb(0xC6, 0x28, 0x28)),
        new("0x8224D4", Color.FromRgb(0x82, 0x24, 0xD4)),
    ];

    /// <summary>Opaque black and white for dim overlays and the playhead; frozen once and shared.</summary>
    public static readonly Brush Black = Freeze(new SolidColorBrush(Colors.Black));
    public static readonly Brush White = Freeze(new SolidColorBrush(Colors.White));

    /// <summary>The same colour at a lower alpha (colour form of <see cref="Tint"/>).</summary>
    public static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);
    /// <summary>A brush that is hit-testable but paints nothing (panels need this for mouse input).</summary>
    public static readonly Brush Transparent = Freeze(new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)));

    /// <summary>The preview tint of a solid area; transparent surfaces fall back to the default swatch.</summary>
    public static Color PreviewFill(string token) =>
        Options.FirstOrDefault(option => string.Equals(option.Token, token, StringComparison.OrdinalIgnoreCase))?.Fill
        ?? Options[0].Fill;

    /// <summary>The same colour at a lower alpha, used for preview rectangles and drag feedback.</summary>
    public static Brush Tint(Color color, byte alpha) =>
        Freeze(new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B)));

    private static T Freeze<T>(T brush)
        where T : Freezable
    {
        brush.Freeze();
        return brush;
    }
}
