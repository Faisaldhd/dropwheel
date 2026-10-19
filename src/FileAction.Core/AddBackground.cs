namespace FileAction.Core;

/// <summary>One 8-bit RGB colour, kept free of any UI framework so the geometry stays testable.</summary>
public readonly record struct BackgroundColor(byte R, byte G, byte B);

/// <summary>The nine built-in gradient backgrounds.</summary>
public enum GradientPreset
{
    Violet,
    Sunset,
    Ocean,
    Mint,
    Peach,
    Midnight,
    Lavender,
    Citrus,
    Rose,
}

/// <summary>Start and end colour of a preset gradient; both are drawn top to bottom.</summary>
public readonly record struct GradientStops(BackgroundColor Start, BackgroundColor End);

/// <summary>How the padded composition is cropped once the photo and its frame are placed.</summary>
public enum BackgroundRatio
{
    /// <summary>Keep the padded size; the photo decides the aspect.</summary>
    Auto,

    /// <summary>1:1.</summary>
    Square,

    /// <summary>16:9.</summary>
    Wide,

    /// <summary>4:5.</summary>
    Portrait,
}

/// <summary>A rectangle in composition pixels with a top-left origin; it may reach outside the canvas.</summary>
public readonly record struct BackgroundRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;
}

/// <summary>
/// Where the photo and its frame sit on the finished canvas: the canvas is the padded composition
/// cropped to the chosen ratio, the background covers the whole padded area, and the photo is placed
/// over it with rounded corners and a shadow. Every value is in the same unit as the inputs (preview
/// pixels for the live preview, original pixels for the saved file), so one composition renders at any
/// resolution.
/// </summary>
public readonly record struct BackgroundComposition(
    int CanvasWidth,
    int CanvasHeight,
    int BackgroundX,
    int BackgroundY,
    int BackgroundWidth,
    int BackgroundHeight,
    int ImageX,
    int ImageY,
    int ImageWidth,
    int ImageHeight,
    int CornerRadius,
    int ShadowBlur,
    int ShadowOffsetY,
    double ShadowOpacity);

/// <summary>
/// The Add Background tool without a window: the pad/corner/shadow geometry, the ratio crop, the
/// cover-fit rectangle for a background image and the built-in gradient/solid palettes. The sliders are
/// pixels of the preview decode (long side at most <see cref="PreviewMaxSide"/>); the save path multiplies
/// them by <see cref="ScaleFor"/> so the exported frame has the same proportions as the preview.
/// </summary>
public static class AddBackground
{
    public const int MaxPadding = 400;
    public const int MaxCorners = 400;
    public const int MaxShadow = 100;

    public const int DefaultPadding = 160;
    public const int DefaultCorners = 64;
    public const int DefaultShadow = 40;

    /// <summary>Longest side of the decoded preview; the slider values are pixels at this scale.</summary>
    public const int PreviewMaxSide = 2048;

    // A safety net for the export only: the photo is never resampled, so a huge photo plus a
    // proportionally huge frame would otherwise need gigabytes. Past these limits the frame (pad,
    // corners, shadow) shrinks together; on the preview the limits are never reached.
    public const int MaxCanvasSide = 16000;
    public const long MaxCanvasPixels = 64_000_000;

    private static readonly BackgroundColor[] Palette =
    [
        new(0xFF, 0xFF, 0xFF), // white
        new(0x11, 0x11, 0x11), // ink
        new(0x37, 0x41, 0x51), // slate
        new(0x6B, 0x72, 0x80), // stone
        new(0xE7, 0xD8, 0xC9), // sand
        new(0xF3, 0xC5, 0xC8), // blush
        new(0xA9, 0xC3, 0xA0), // sage
        new(0xA7, 0xC7, 0xE7), // sky
        new(0x6B, 0x4C, 0x7A), // plum
    ];

    /// <summary>The nine solid colours offered next to the gradients.</summary>
    public static IReadOnlyList<BackgroundColor> SolidColors => Palette;

    /// <summary>The two stops of one gradient preset.</summary>
    public static GradientStops Gradient(GradientPreset preset) => preset switch
    {
        GradientPreset.Sunset => new(new(0xFF, 0x7E, 0x5F), new(0xFE, 0xB4, 0x7B)),
        GradientPreset.Ocean => new(new(0x2E, 0x31, 0x92), new(0x1B, 0xFF, 0xFF)),
        GradientPreset.Mint => new(new(0x43, 0xC6, 0xAC), new(0xF8, 0xFF, 0xAE)),
        GradientPreset.Peach => new(new(0xFF, 0xD3, 0xA5), new(0xFD, 0x65, 0x85)),
        GradientPreset.Midnight => new(new(0x23, 0x25, 0x26), new(0x41, 0x43, 0x45)),
        GradientPreset.Lavender => new(new(0x97, 0x96, 0xF0), new(0xFB, 0xC7, 0xD4)),
        GradientPreset.Citrus => new(new(0xF7, 0x97, 0x1E), new(0xFF, 0xD2, 0x00)),
        GradientPreset.Rose => new(new(0xFF, 0x9A, 0x9E), new(0xFA, 0xD0, 0xC4)),
        _ => new(new(0x82, 0x24, 0xD4), new(0xB4, 0x8A, 0xE0)),
    };

    /// <summary>Width / height of a ratio; 0 for <see cref="BackgroundRatio.Auto"/>.</summary>
    public static double RatioValue(BackgroundRatio ratio) => ratio switch
    {
        BackgroundRatio.Square => 1.0,
        BackgroundRatio.Wide => 16.0 / 9.0,
        BackgroundRatio.Portrait => 4.0 / 5.0,
        _ => 0,
    };

    /// <summary>The largest centred rectangle with the ratio's aspect that fits in the composition.</summary>
    public static (int Width, int Height) CropToRatio(int width, int height, BackgroundRatio ratio)
    {
        var w = Math.Max(1, width);
        var h = Math.Max(1, height);
        var ratioValue = RatioValue(ratio);
        if (ratioValue <= 0)
            return (w, h);
        if (w / (double)h > ratioValue)
            return (Math.Clamp((int)Math.Round(h * ratioValue), 1, w), h);
        return (w, Math.Clamp((int)Math.Round(w / ratioValue), 1, h));
    }

    /// <summary>
    /// Padding after the export safety cap: never negative, and the padded canvas always fits inside
    /// <see cref="MaxCanvasSide"/> and <see cref="MaxCanvasPixels"/>.
    /// </summary>
    public static int LimitPadding(int imageWidth, int imageHeight, int padding)
    {
        var w = Math.Max(1, imageWidth);
        var h = Math.Max(1, imageHeight);
        var value = Math.Max(0, padding);
        if (value == 0)
            return 0;

        // The photo alone may already be over the pixel limit; then there is no room for a frame.
        if ((long)w * h >= MaxCanvasPixels)
            return 0;

        value = Math.Min(value, Math.Max(0, MaxCanvasSide - Math.Max(w, h)) / 2);

        // (w + 2p)(h + 2p) <= MaxCanvasPixels  =>  4p² + 2(w + h)p + w·h - MaxCanvasPixels <= 0.
        var sum = (double)w + h;
        var inside = sum * sum - 4.0 * ((double)w * h - MaxCanvasPixels);
        if (inside <= 0)
            return 0;
        var pixelLimit = (-sum + Math.Sqrt(inside)) / 4.0;
        if (pixelLimit < value)
            value = Math.Max(0, (int)Math.Floor(pixelLimit));
        return value;
    }

    /// <summary>The composition for one photo and one set of framed values, in the inputs' own unit.</summary>
    public static BackgroundComposition Compose(
        int imageWidth,
        int imageHeight,
        int padding,
        int corners,
        int shadow,
        BackgroundRatio ratio)
    {
        var w = Math.Max(1, imageWidth);
        var h = Math.Max(1, imageHeight);

        // The requested padding may be capped; corners and shadow shrink with it so the look stays
        // proportional instead of turning into a thin frame with a huge blur.
        var asked = Math.Max(0, padding);
        var pad = LimitPadding(w, h, asked);
        var factor = asked > 0 ? pad / (double)asked : 1.0;

        var corner = (int)Math.Round(Math.Max(0, corners) * factor);
        corner = Math.Clamp(corner, 0, Math.Min(w, h) / 2);
        var blur = Math.Max(0, (int)Math.Round(Math.Max(0, shadow) * factor));
        var offset = ShadowOffsetFor(blur);
        var opacity = ShadowOpacityFor(blur, Math.Min(w, h));

        var fullWidth = w + 2 * pad;
        var fullHeight = h + 2 * pad;
        var (canvasWidth, canvasHeight) = CropToRatio(fullWidth, fullHeight, ratio);
        var cropX = (fullWidth - canvasWidth) / 2;
        var cropY = (fullHeight - canvasHeight) / 2;

        return new BackgroundComposition(
            CanvasWidth: canvasWidth,
            CanvasHeight: canvasHeight,
            BackgroundX: -cropX,
            BackgroundY: -cropY,
            BackgroundWidth: fullWidth,
            BackgroundHeight: fullHeight,
            ImageX: pad - cropX,
            ImageY: pad - cropY,
            ImageWidth: w,
            ImageHeight: h,
            CornerRadius: corner,
            ShadowBlur: blur,
            ShadowOffsetY: offset,
            ShadowOpacity: opacity);
    }

    /// <summary>
    /// The rectangle a background image has to fill so it covers the target without distortion; the
    /// overflow is split evenly on both sides (its X/Y are negative when the image is cropped).
    /// </summary>
    public static BackgroundRect CoverRect(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        var sourceW = Math.Max(1, sourceWidth);
        var sourceH = Math.Max(1, sourceHeight);
        var targetW = Math.Max(1, targetWidth);
        var targetH = Math.Max(1, targetHeight);

        var scale = Math.Max(targetW / (double)sourceW, targetH / (double)sourceH);
        var width = Math.Max(targetW, (int)Math.Ceiling(sourceW * scale));
        var height = Math.Max(targetH, (int)Math.Ceiling(sourceH * scale));
        return new BackgroundRect((targetW - width) / 2, (targetH - height) / 2, width, height);
    }

    /// <summary>Original pixels per preview pixel; 1 when the preview was not downsampled.</summary>
    public static double ScaleFor(int originalWidth, int previewWidth)
    {
        if (originalWidth <= 0 || previewWidth <= 0)
            return 1.0;
        return Math.Max(1.0, originalWidth / (double)previewWidth);
    }

    /// <summary>A slider value at the original resolution: rounded, never negative.</summary>
    public static int Scale(double value, double factor) =>
        (int)Math.Round(Math.Max(0, value) * Math.Max(0, factor), MidpointRounding.AwayFromZero);

    /// <summary>
    /// Shadow opacity from its blur and the photo's short side. The blur is relative to the photo, so a
    /// preview and a full-resolution export of the same slider draw the same shadow.
    /// </summary>
    public static double ShadowOpacityFor(int blur, int shortSide)
    {
        if (blur <= 0)
            return 0;
        var reference = Math.Max(1.0, Math.Max(1, shortSide) / 20.0);
        return 0.18 + 0.24 * Math.Clamp(blur / reference, 0, 1);
    }

    /// <summary>A small downward offset so the shadow reads as light from above.</summary>
    public static int ShadowOffsetFor(int blur) => blur <= 0 ? 0 : Math.Max(1, (int)Math.Round(blur * 0.18));
}
