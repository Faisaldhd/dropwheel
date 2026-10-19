namespace FileAction.Core;

/// <summary>What a tool declares about its window: fixed chrome around a flexible preview area.</summary>
/// <param name="ChromeWidth">Width the window needs on top of the preview (margins).</param>
/// <param name="ChromeHeight">Title bar + controls + bottom bar, together.</param>
/// <param name="PreviewMinWidth">Smallest preview the controls stay usable with.</param>
public readonly record struct ToolSpecLayout(
    double ChromeWidth,
    double ChromeHeight,
    double PreviewMinWidth,
    double PreviewMinHeight,
    double PreviewMaxWidth,
    double PreviewMaxHeight,
    double DefaultPreviewWidth,
    double DefaultPreviewHeight);

/// <summary>The window size and the preview box it was derived from, in DIPs.</summary>
public readonly record struct ToolWindowSize(double Width, double Height, double PreviewWidth, double PreviewHeight);

/// <summary>
/// Makes a tool window adapt to its media: the preview box follows the media aspect ratio inside the
/// tool's min/max bounds, the window adds the fixed chrome, everything is clamped to the work area of the
/// monitor (95% wide, 90% high, DPI aware), and small media is never blown up beyond 200%.
/// Pure functions, so every case below is unit tested without a window.
/// </summary>
public static class ToolWindowSizer
{
    /// <summary>Tiny media is shown at most twice its pixel size.</summary>
    public const double MaxUpscale = 2.0;

    public const double MaxWidthRatio = 0.95;
    public const double MaxHeightRatio = 0.90;

    /// <param name="layout">Chrome and preview bounds declared by the tool.</param>
    /// <param name="mediaWidth">Media width in pixels, or null while it is still being probed.</param>
    /// <param name="workAreaWidth">Work area in physical pixels (as Windows reports it).</param>
    /// <param name="dpiScale">Monitor scale, e.g. 1.25 for 125%.</param>
    public static ToolWindowSize Compute(
        ToolSpecLayout layout,
        double? mediaWidth,
        double? mediaHeight,
        double workAreaWidth,
        double workAreaHeight,
        double dpiScale)
    {
        var scale = dpiScale > 0 ? dpiScale : 1.0;
        var workWidth = workAreaWidth / scale;
        var workHeight = workAreaHeight / scale;
        var maxWindowWidth = workWidth * MaxWidthRatio;
        var maxWindowHeight = workHeight * MaxHeightRatio;

        // Room left for the preview once the chrome is paid for; never negative.
        var roomWidth = Math.Max(1, maxWindowWidth - layout.ChromeWidth);
        var roomHeight = Math.Max(1, maxWindowHeight - layout.ChromeHeight);

        double previewWidth, previewHeight;
        var hasMedia = mediaWidth is > 0 && mediaHeight is > 0;
        if (!hasMedia)
        {
            previewWidth = Math.Min(layout.DefaultPreviewWidth, roomWidth);
            previewHeight = Math.Min(layout.DefaultPreviewHeight, roomHeight);
        }
        else
        {
            var mediaW = mediaWidth!.Value;
            var mediaH = mediaHeight!.Value;
            var maxWidth = Math.Min(layout.PreviewMaxWidth, roomWidth);
            var maxHeight = Math.Min(layout.PreviewMaxHeight, roomHeight);
            var ratio = mediaW / Math.Max(1, mediaH);

            // Fit the media's aspect into the largest allowed box.
            previewWidth = maxWidth;
            previewHeight = maxWidth / ratio;
            if (previewHeight > maxHeight)
            {
                previewHeight = maxHeight;
                previewWidth = maxHeight * ratio;
            }

            // Never above 200% of the media's own pixels (tiny icons stay small), and never below the
            // tool's minimum once that minimum itself is inside the 200% cap.
            previewWidth = Math.Min(previewWidth, mediaW * MaxUpscale);
            previewHeight = Math.Min(previewHeight, mediaH * MaxUpscale);
            previewWidth = Math.Max(previewWidth, Math.Min(layout.PreviewMinWidth, mediaW * MaxUpscale));
            previewHeight = Math.Max(previewHeight, Math.Min(layout.PreviewMinHeight, mediaH * MaxUpscale));
        }

        // A small screen scales the preview down; the chrome is never reduced.
        previewWidth = Math.Max(1, Math.Min(previewWidth, roomWidth));
        previewHeight = Math.Max(1, Math.Min(previewHeight, roomHeight));

        var width = Math.Min(previewWidth + layout.ChromeWidth, maxWindowWidth);
        var height = Math.Min(previewHeight + layout.ChromeHeight, maxWindowHeight);
        return new ToolWindowSize(width, height, previewWidth, previewHeight);
    }
}
