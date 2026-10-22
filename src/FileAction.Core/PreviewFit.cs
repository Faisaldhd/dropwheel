namespace FileAction.Core;

/// <summary>
/// Fits a bitmap into a preview box without ever enlarging it past 200% of its own pixels. The bitmap is
/// centred; the space left over is the letterbox. A bitmap larger than the preview is scaled down until
/// the whole frame fits, so a preview that has room for the native size never shows it below 100%.
/// Pure geometry: the same rules apply to the WPF previews and to their tests without a window.
/// </summary>
public readonly record struct PreviewFit(
    double Scale,
    double Width,
    double Height,
    double OriginX,
    double OriginY)
{
    /// <summary>Tiny icons are shown at most twice their pixel size.</summary>
    public const double MaxUpscale = 2.0;

    /// <summary>Fits a <paramref name="pixelWidth"/> × <paramref name="pixelHeight"/> bitmap into the view.</summary>
    public static PreviewFit Fit(double viewWidth, double viewHeight, int pixelWidth, int pixelHeight)
    {
        if (!(viewWidth > 0) || !(viewHeight > 0) || pixelWidth <= 0 || pixelHeight <= 0)
            return new PreviewFit(1, 0, 0, 0, 0);

        var fit = Math.Min(viewWidth / pixelWidth, viewHeight / pixelHeight);
        if (!(fit > 0) || double.IsInfinity(fit))
            fit = 1;

        // fit is already at least 1 whenever the preview can hold the bitmap at native size, so the
        // upper cap alone gives the 1x..2x range for small media and the natural fit for large media.
        var scale = Math.Min(fit, MaxUpscale);
        var width = pixelWidth * scale;
        var height = pixelHeight * scale;
        return new PreviewFit(
            scale,
            width,
            height,
            (viewWidth - width) / 2,
            (viewHeight - height) / 2);
    }

    /// <summary>The scale alone, for callers that place their own composition from it.</summary>
    public static double ScaleFor(double viewWidth, double viewHeight, int pixelWidth, int pixelHeight) =>
        Fit(viewWidth, viewHeight, pixelWidth, pixelHeight).Scale;
}
