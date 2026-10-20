using FileAction.Core;

namespace FileAction.App.UI.Tools.Audio;

/// <summary>
/// Audio tools have no picture to follow, so they open at one comfortable size (about 560 × 520 DIP) and
/// keep it. The declared preview box plus the chrome adds up to exactly that; the sizer still clamps it to
/// a small work area.
/// </summary>
internal static class AudioToolLayout
{
    public static readonly ToolSpecLayout Window = new(
        ChromeWidth: 36,
        ChromeHeight: 230,
        PreviewMinWidth: 420,
        PreviewMinHeight: 250,
        PreviewMaxWidth: 900,
        PreviewMaxHeight: 420,
        DefaultPreviewWidth: 524,
        DefaultPreviewHeight: 290);
}
