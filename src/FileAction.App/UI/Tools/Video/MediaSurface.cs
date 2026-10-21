using System.Windows;
using System.Windows.Media;

namespace FileAction.App.UI.Tools.Video;

/// <summary>
/// Clips a media well or preview host to the shared card curve. WPF's Border draws its own background
/// rounded but never clips its children, so a photo or video frame would otherwise paint square over
/// the rounded card inside the tool window.
/// </summary>
internal static class MediaSurface
{
    public static void Round(FrameworkElement surface, double radius = Design.Radius.Card)
    {
        void Apply()
        {
            if (surface.ActualWidth <= 0 || surface.ActualHeight <= 0)
                return;
            surface.Clip = new RectangleGeometry(
                new Rect(0, 0, surface.ActualWidth, surface.ActualHeight), radius, radius);
        }

        surface.SizeChanged += (_, _) => Apply();
        Apply();
    }
}
