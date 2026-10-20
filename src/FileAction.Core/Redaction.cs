namespace FileAction.Core;

/// <summary>How one redaction area hides what is under it.</summary>
public enum RedactionMode
{
    /// <summary>A solid colour (black unless the user picks another one).</summary>
    Solid,

    /// <summary>A Gaussian blur strong enough that the original pixels cannot be recovered.</summary>
    Blur,

    /// <summary>Pixelation: the area is replaced by large blocks of averaged colour.</summary>
    Pixelate,
}

/// <summary>A rectangle in image pixels with a top-left origin and a non-negative size.</summary>
public readonly record struct RedactionBox(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    /// <summary>True when the rectangle covers nothing (also for a negative or NaN size).</summary>
    public bool IsEmpty => !(Width > 0) || !(Height > 0);

    /// <summary>True when the point is inside or exactly on the border.</summary>
    public bool Contains(double x, double y) =>
        x >= X && x <= Right && y >= Y && y <= Bottom;

    /// <summary>True when the two rectangles overlap by a positive area (touching edges do not count).</summary>
    public bool Intersects(RedactionBox other) =>
        X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;

    /// <summary>Builds a rectangle from its four edges, never with a negative size.</summary>
    public static RedactionBox FromEdges(double left, double top, double right, double bottom) =>
        new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
}

/// <summary>
/// One redacted rectangle on the image. <see cref="Mode"/> and <see cref="Strength"/> are optional: null
/// means "use the global style", which is how one area can differ from the rest without duplicating the
/// defaults. Areas are kept in paint order, so the last one is on top.
/// </summary>
public sealed record RedactionArea(RedactionBox Box, RedactionMode? Mode = null, int? Strength = null);

/// <summary>The global defaults every area inherits unless it overrides them.</summary>
/// <param name="Mode">What a new area does.</param>
/// <param name="Strength">1–100, scaled per mode (blur radius, pixel block size).</param>
/// <param name="Color">Solid fill colour as 0xAARRGGBB.</param>
public readonly record struct RedactionStyle(RedactionMode Mode, int Strength, uint Color)
{
    /// <summary>Black, solid — the safe default for hiding something.</summary>
    public static readonly RedactionStyle Default = new(RedactionMode.Solid, 25, RedactionColors.Black);
}

/// <summary>RGBA helpers for the 0xAARRGGBB colour the plan carries.</summary>
public static class RedactionColors
{
    public const uint Black = 0xFF000000;

    /// <summary>Packs four channels into 0xAARRGGBB.</summary>
    public static uint FromArgb(byte a, byte r, byte g, byte b) =>
        ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;

    /// <summary>Unpacks 0xAARRGGBB into its four channels.</summary>
    public static (byte A, byte R, byte G, byte B) Split(uint color) =>
        ((byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color);
}

/// <summary>One flattened redaction in integer image pixels, in paint order.</summary>
/// <param name="BlurSigma">Gaussian sigma in pixels; 0 for the other modes.</param>
/// <param name="PixelBlock">Block edge in pixels; 1 for the other modes.</param>
public readonly record struct RedactionStep(
    int X,
    int Y,
    int Width,
    int Height,
    RedactionMode Mode,
    double BlurSigma,
    int PixelBlock,
    uint Color);

/// <summary>A snapped, non-empty rectangle in integer image pixels.</summary>
public readonly record struct RedactionRect(int X, int Y, int Width, int Height);

/// <summary>
/// Turns the interactive area list into the flat list of pixels operations the renderer applies, in paint
/// order. Everything here is pure: the tool window, the preview and the saved file all compute their plan
/// from the same rules, so what the user sees is what gets written.
/// </summary>
public static class RedactionPlan
{
    public const int MinStrength = 1;
    public const int MaxStrength = 100;

    // A strength of 1 already blurs/pixelates; 100 is deliberately destructive.
    private const double BlurSigmaAtZero = 1.0;
    private const double BlurSigmaPerPoint = 0.36;
    private const int PixelBlockAtZero = 2;
    private const double PixelBlockPerPoint = 0.30;

    /// <summary>
    /// The operations for the whole image, in paint order. Boxes are clamped to the image and snapped to
    /// whole pixels; boxes that collapse to nothing are dropped.
    /// </summary>
    public static IReadOnlyList<RedactionStep> Build(
        int imageWidth,
        int imageHeight,
        IReadOnlyList<RedactionArea> areas,
        RedactionStyle style)
    {
        ArgumentNullException.ThrowIfNull(areas);

        var steps = new List<RedactionStep>(areas.Count);
        foreach (var area in areas)
        {
            if (area is null || Snap(area.Box, imageWidth, imageHeight) is not { } rect)
                continue;

            var mode = ResolveMode(area, style);
            var strength = ResolveStrength(area, style);
            steps.Add(new RedactionStep(
                rect.X,
                rect.Y,
                rect.Width,
                rect.Height,
                mode,
                mode == RedactionMode.Blur ? BlurSigma(strength) : 0,
                mode == RedactionMode.Pixelate ? PixelBlock(strength) : 1,
                style.Color));
        }
        return steps;
    }

    /// <summary>
    /// The same plan at preview resolution: the boxes, the blur radius and the pixel block are scaled by
    /// <paramref name="scale"/> so the downsampled preview shows the effect the saved file will get.
    /// </summary>
    public static IReadOnlyList<RedactionStep> ToPreview(
        IReadOnlyList<RedactionStep> steps,
        int imageWidth,
        int imageHeight,
        double scale)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (!double.IsFinite(scale) || scale <= 0)
            return [];

        var result = new List<RedactionStep>(steps.Count);
        foreach (var step in steps)
        {
            var box = new RedactionBox(step.X * scale, step.Y * scale, step.Width * scale, step.Height * scale);
            if (Snap(box, imageWidth, imageHeight) is not { } rect)
                continue;

            result.Add(step with
            {
                X = rect.X,
                Y = rect.Y,
                Width = rect.Width,
                Height = rect.Height,
                BlurSigma = step.Mode == RedactionMode.Blur ? Math.Max(0.5, step.BlurSigma * scale) : 0,
                PixelBlock = step.Mode == RedactionMode.Pixelate
                    ? Math.Max(1, (int)Math.Round(step.PixelBlock * scale, MidpointRounding.AwayFromZero))
                    : 1,
            });
        }
        return result;
    }

    /// <summary>The mode an area uses: its own override, or the global one.</summary>
    public static RedactionMode ResolveMode(RedactionArea area, RedactionStyle style) =>
        area.Mode ?? style.Mode;

    /// <summary>The strength an area uses, clamped to <see cref="MinStrength"/>–<see cref="MaxStrength"/>.</summary>
    public static int ResolveStrength(RedactionArea area, RedactionStyle style) =>
        Math.Clamp(area.Strength ?? style.Strength, MinStrength, MaxStrength);

    /// <summary>Gaussian sigma in pixels for a 1–100 strength; always at least one pixel.</summary>
    public static double BlurSigma(int strength) =>
        BlurSigmaAtZero + Math.Clamp(strength, MinStrength, MaxStrength) * BlurSigmaPerPoint;

    /// <summary>Pixel block edge for a 1–100 strength; always at least two pixels.</summary>
    public static int PixelBlock(int strength) =>
        PixelBlockAtZero + (int)Math.Round(
            Math.Clamp(strength, MinStrength, MaxStrength) * PixelBlockPerPoint,
            MidpointRounding.AwayFromZero);

    /// <summary>
    /// Rounds a box to whole pixels, clamps it into the image and returns null when it would cover no
    /// pixel. Rounding is away-from-zero so a half-pixel boundary consistently grows the area.
    /// </summary>
    public static RedactionRect? Snap(RedactionBox box, int imageWidth, int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || box.IsEmpty)
            return null;
        if (!double.IsFinite(box.X) || !double.IsFinite(box.Y) ||
            !double.IsFinite(box.Width) || !double.IsFinite(box.Height))
            return null;

        var left = Math.Clamp((int)Math.Round(box.X, MidpointRounding.AwayFromZero), 0, imageWidth);
        var top = Math.Clamp((int)Math.Round(box.Y, MidpointRounding.AwayFromZero), 0, imageHeight);
        var right = Math.Clamp((int)Math.Round(box.Right, MidpointRounding.AwayFromZero), 0, imageWidth);
        var bottom = Math.Clamp((int)Math.Round(box.Bottom, MidpointRounding.AwayFromZero), 0, imageHeight);

        var width = right - left;
        var height = bottom - top;
        return width < 1 || height < 1 ? null : new RedactionRect(left, top, width, height);
    }
}

/// <summary>The eight drag handles around a redaction area.</summary>
public enum RedactionHandle
{
    None,
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
}

/// <summary>
/// The area rules the tool window drives with the mouse: which area is under a point (topmost first),
/// which handle is near a point, drawing a new area by dragging, moving one inside the image and resizing
/// it from any of the eight handles. Pure geometry, so the interaction is unit tested without a window.
/// </summary>
public static class RedactionEditor
{
    /// <summary>Smallest area edge, in image pixels.</summary>
    public const double MinSize = 8;

    private static readonly RedactionHandle[] Handles =
    [
        RedactionHandle.TopLeft,
        RedactionHandle.Top,
        RedactionHandle.TopRight,
        RedactionHandle.Right,
        RedactionHandle.BottomRight,
        RedactionHandle.Bottom,
        RedactionHandle.BottomLeft,
        RedactionHandle.Left,
    ];

    /// <summary>The index of the topmost area containing the point, or -1.</summary>
    public static int HitTest(IReadOnlyList<RedactionArea> areas, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(areas);
        for (var i = areas.Count - 1; i >= 0; i--)
            if (areas[i] is { } area && area.Box.Contains(x, y))
                return i;
        return -1;
    }

    /// <summary>
    /// The handle within <paramref name="tolerance"/> of the point (nearest wins), or
    /// <see cref="RedactionHandle.None"/>. Tolerance is in image pixels, so the caller divides its DIP
    /// hit area by the preview scale.
    /// </summary>
    public static RedactionHandle HandleAt(RedactionBox box, double x, double y, double tolerance)
    {
        if (box.IsEmpty || !(tolerance >= 0))
            return RedactionHandle.None;

        var best = RedactionHandle.None;
        var bestDistance = double.MaxValue;
        foreach (var handle in Handles)
        {
            var (hx, hy) = HandlePoint(box, handle);
            if (Math.Abs(x - hx) > tolerance || Math.Abs(y - hy) > tolerance)
                continue;
            var distance = (x - hx) * (x - hx) + (y - hy) * (y - hy);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = handle;
            }
        }
        return best;
    }

    /// <summary>The point a handle sits on, in image pixels.</summary>
    public static (double X, double Y) HandlePoint(RedactionBox box, RedactionHandle handle) => handle switch
    {
        RedactionHandle.TopLeft => (box.X, box.Y),
        RedactionHandle.Top => (box.X + box.Width / 2, box.Y),
        RedactionHandle.TopRight => (box.Right, box.Y),
        RedactionHandle.Right => (box.Right, box.Y + box.Height / 2),
        RedactionHandle.BottomRight => (box.Right, box.Bottom),
        RedactionHandle.Bottom => (box.X + box.Width / 2, box.Bottom),
        RedactionHandle.BottomLeft => (box.X, box.Bottom),
        RedactionHandle.Left => (box.X, box.Y + box.Height / 2),
        _ => (box.X, box.Y),
    };

    /// <summary>
    /// The area a drag from (x0, y0) to (x1, y1) describes, in any direction and clamped to the image. The
    /// tool window discards it when it is smaller than a few pixels on screen.
    /// </summary>
    public static RedactionBox Create(double x0, double y0, double x1, double y1, int imageWidth, int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
            return default;
        if (!double.IsFinite(x0) || !double.IsFinite(y0) || !double.IsFinite(x1) || !double.IsFinite(y1))
            return default;

        var left = Math.Clamp(Math.Min(x0, x1), 0, imageWidth);
        var top = Math.Clamp(Math.Min(y0, y1), 0, imageHeight);
        var right = Math.Clamp(Math.Max(x0, x1), 0, imageWidth);
        var bottom = Math.Clamp(Math.Max(y0, y1), 0, imageHeight);
        return new RedactionBox(left, top, right - left, bottom - top);
    }

    /// <summary>Moves an area by a pixel delta, keeping all of it inside the image.</summary>
    public static RedactionBox Move(RedactionBox box, double dx, double dy, int imageWidth, int imageHeight)
    {
        if (box.IsEmpty || imageWidth <= 0 || imageHeight <= 0)
            return box;

        var width = Math.Min(box.Width, imageWidth);
        var height = Math.Min(box.Height, imageHeight);
        var x = Math.Clamp(box.X + dx, 0, Math.Max(0, imageWidth - width));
        var y = Math.Clamp(box.Y + dy, 0, Math.Max(0, imageHeight - height));
        return new RedactionBox(x, y, width, height);
    }

    /// <summary>
    /// Resizes an area by dragging one handle: the opposite edge or corner stays anchored, the dragged
    /// edges are clamped to the image and the result never drops below <see cref="MinSize"/> (or the image
    /// size, whichever is smaller).
    /// </summary>
    public static RedactionBox Resize(
        RedactionHandle handle,
        RedactionBox start,
        double dx,
        double dy,
        int imageWidth,
        int imageHeight)
    {
        if (handle == RedactionHandle.None || imageWidth <= 0 || imageHeight <= 0)
            return start;

        double left = start.X, top = start.Y, right = start.Right, bottom = start.Bottom;
        switch (handle)
        {
            case RedactionHandle.TopLeft:
                left += dx;
                top += dy;
                break;
            case RedactionHandle.Top:
                top += dy;
                break;
            case RedactionHandle.TopRight:
                right += dx;
                top += dy;
                break;
            case RedactionHandle.Right:
                right += dx;
                break;
            case RedactionHandle.BottomRight:
                right += dx;
                bottom += dy;
                break;
            case RedactionHandle.Bottom:
                bottom += dy;
                break;
            case RedactionHandle.BottomLeft:
                left += dx;
                bottom += dy;
                break;
            case RedactionHandle.Left:
                left += dx;
                break;
        }

        left = Math.Clamp(left, 0, imageWidth);
        right = Math.Clamp(right, 0, imageWidth);
        top = Math.Clamp(top, 0, imageHeight);
        bottom = Math.Clamp(bottom, 0, imageHeight);

        var minWidth = Math.Min(MinSize, imageWidth);
        var minHeight = Math.Min(MinSize, imageHeight);
        if (right - left < minWidth)
        {
            if (handle is RedactionHandle.TopLeft or RedactionHandle.BottomLeft or RedactionHandle.Left)
                left = Math.Max(0, right - minWidth);
            else
                right = Math.Min(imageWidth, left + minWidth);
        }
        if (bottom - top < minHeight)
        {
            if (handle is RedactionHandle.TopLeft or RedactionHandle.TopRight or RedactionHandle.Top)
                top = Math.Max(0, bottom - minHeight);
            else
                bottom = Math.Min(imageHeight, top + minHeight);
        }

        return new RedactionBox(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Moves an area to the top of the paint order (which is what selecting an area does) and returns its
    /// new index. The relative order of the other areas does not change.
    /// </summary>
    public static int BringToFront(IList<RedactionArea> areas, int index)
    {
        ArgumentNullException.ThrowIfNull(areas);
        if (index < 0 || index >= areas.Count - 1)
            return index;

        var area = areas[index];
        areas.RemoveAt(index);
        areas.Add(area);
        return areas.Count - 1;
    }
}

/// <summary>Geometry for turning the word boxes Windows OCR reports into one area per text line.</summary>
public static class RedactionText
{
    /// <summary>The bounding box of every non-empty box, or an empty box when there is none.</summary>
    public static RedactionBox Union(IEnumerable<RedactionBox> boxes)
    {
        ArgumentNullException.ThrowIfNull(boxes);

        double left = 0, top = 0, right = 0, bottom = 0;
        var any = false;
        foreach (var box in boxes)
        {
            if (box.IsEmpty)
                continue;
            if (!any)
            {
                left = box.X;
                top = box.Y;
                right = box.Right;
                bottom = box.Bottom;
                any = true;
            }
            else
            {
                left = Math.Min(left, box.X);
                top = Math.Min(top, box.Y);
                right = Math.Max(right, box.Right);
                bottom = Math.Max(bottom, box.Bottom);
            }
        }
        return any ? RedactionBox.FromEdges(left, top, right, bottom) : default;
    }

    /// <summary>Grows a box by <paramref name="amount"/> on every side; a negative amount shrinks it.</summary>
    public static RedactionBox Pad(RedactionBox box, double amount)
    {
        if (box.IsEmpty || !double.IsFinite(amount))
            return box;
        var left = box.X - amount;
        var top = box.Y - amount;
        var right = box.Right + amount;
        var bottom = box.Bottom + amount;
        if (right <= left || bottom <= top)
            return default;
        return RedactionBox.FromEdges(left, top, right, bottom);
    }
}
