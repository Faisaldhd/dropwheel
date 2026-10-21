using FileAction.Core;

namespace FileAction.App.UI.Tools;

/// <summary>
/// Every tool's chrome and preview bounds in one place, so the defaults are easy to tune. The window
/// size itself is computed per media by <see cref="ToolWindowSizer"/>; nothing here is a fixed size.
/// </summary>
internal static class ToolSizes
{
    /// <summary>Fallback for a tool that does not declare its own layout.</summary>
    public static readonly ToolSpecLayout Default = new(
        ChromeWidth: 36,
        ChromeHeight: 270,
        PreviewMinWidth: 320,
        PreviewMinHeight: 320,
        PreviewMaxWidth: 640,
        PreviewMaxHeight: 640,
        DefaultPreviewWidth: 350,
        DefaultPreviewHeight: 450);

    /// <summary>
    /// Crop opens on a comfortable 4:3 canvas instead of a tall portrait placeholder, so the first frame
    /// does not show large empty bands above and below a landscape photo.
    /// </summary>
    public static readonly ToolSpecLayout Crop = new(
        ChromeWidth: 36,
        ChromeHeight: 270,
        // The ratio chips and W/H row need ~380 DIP; below that the window clipped its own Apply button.
        PreviewMinWidth: 380,
        PreviewMinHeight: 240,
        PreviewMaxWidth: 640,
        PreviewMaxHeight: 640,
        DefaultPreviewWidth: 560,
        DefaultPreviewHeight: 420);

    public static ToolSpecLayout For(string toolId) => toolId switch
    {
        "tools.crop" => Crop,
        _ => Default,
    };
}
