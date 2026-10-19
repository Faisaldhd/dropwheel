using System.Windows;
using System.Windows.Media;

namespace FileAction.App.UI.Tools.Audio;

/// <summary>
/// The one colour the audio tools add to the shared palette: the orange trim handles requested for this
/// tool. It lives here (not scattered through the drawing code) so it can move into <see cref="Theme"/>
/// without touching a tool. Everything else uses the shared theme brushes.
/// </summary>
internal static class AudioTheme
{
    /// <summary>Trim end handles and the selection outline.</summary>
    public static readonly Brush EndHandle = Freeze(new SolidColorBrush(Color.FromRgb(0xF2, 0x8B, 0x2E)));

    private static T Freeze<T>(T brush)
        where T : Freezable
    {
        brush.Freeze();
        return brush;
    }
}
