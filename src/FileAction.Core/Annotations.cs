namespace FileAction.Core;

/// <summary>What one annotation object draws on the image.</summary>
public enum AnnotationKind
{
    Arrow,
    Line,
    Rectangle,
    Ellipse,
    Text,
    Callout,
    Magnifier,
    Pen,
    Highlighter,
    Stamp,
}

/// <summary>
/// The toolbar tool the user picked. <see cref="Select"/> edits the objects that already exist; every
/// other value creates a new <see cref="AnnotationKind"/> with <see cref="AnnotationMath.KindFor"/>.
/// </summary>
public enum AnnotationTool
{
    Select,
    Arrow,
    Line,
    Rectangle,
    Ellipse,
    Text,
    Callout,
    Magnifier,
    Pen,
    Highlighter,
    Stamp,
}

/// <summary>Which part of an object a drag grabbed.</summary>
public enum AnnotationHandle
{
    None,
    Move,

    /// <summary>First point of a line or arrow.</summary>
    Start,

    /// <summary>Last point of a line or arrow.</summary>
    End,

    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
}

/// <summary>A point in the original image's pixel space (top-left origin).</summary>
public readonly record struct AnnotationPoint(double X, double Y)
{
    public AnnotationPoint Offset(double dx, double dy) => new(X + dx, Y + dy);

    public double DistanceTo(AnnotationPoint other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

/// <summary>
/// An axis-aligned rectangle in the original image's pixel space. Width and height are never negative;
/// use <see cref="FromPoints"/> to normalize a drag in any direction.
/// </summary>
public readonly record struct AnnotationRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double CenterX => X + Width / 2;

    public double CenterY => Y + Height / 2;

    public AnnotationPoint Center => new(CenterX, CenterY);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(AnnotationPoint point) =>
        point.X >= X && point.X <= Right && point.Y >= Y && point.Y <= Bottom;

    public AnnotationRect Inflate(double amount) =>
        new(X - amount, Y - amount, Width + 2 * amount, Height + 2 * amount);

    public AnnotationRect Union(AnnotationRect other)
    {
        var left = Math.Min(X, other.X);
        var top = Math.Min(Y, other.Y);
        var right = Math.Max(Right, other.Right);
        var bottom = Math.Max(Bottom, other.Bottom);
        return new AnnotationRect(left, top, right - left, bottom - top);
    }

    /// <summary>The rectangle of a drag between two points, in any direction.</summary>
    public static AnnotationRect FromPoints(double x1, double y1, double x2, double y2)
    {
        var left = Math.Min(x1, x2);
        var top = Math.Min(y1, y2);
        return new AnnotationRect(left, top, Math.Abs(x2 - x1), Math.Abs(y2 - y1));
    }

    /// <summary>
    /// Shrinks the rectangle to the image when needed and keeps it fully inside it. A rectangle larger
    /// than the image is reduced to the image itself.
    /// </summary>
    public AnnotationRect ClampInside(int imageWidth, int imageHeight)
    {
        var maxWidth = Math.Max(0, imageWidth);
        var maxHeight = Math.Max(0, imageHeight);
        var width = Math.Min(Math.Max(0, Width), maxWidth);
        var height = Math.Min(Math.Max(0, Height), maxHeight);
        var x = Math.Clamp(X, 0, Math.Max(0, maxWidth - width));
        var y = Math.Clamp(Y, 0, Math.Max(0, maxHeight - height));
        return new AnnotationRect(x, y, width, height);
    }
}

/// <summary>One ink colour, in the same ARGB byte order as a Windows colour.</summary>
public readonly record struct AnnotationColor(byte A, byte R, byte G, byte B)
{
    public static readonly AnnotationColor White = new(0xFF, 0xFF, 0xFF, 0xFF);
    public static readonly AnnotationColor Black = new(0xFF, 0x00, 0x00, 0x00);
    public static readonly AnnotationColor Red = new(0xFF, 0xE5, 0x39, 0x35);
    public static readonly AnnotationColor Orange = new(0xFF, 0xFB, 0x8C, 0x00);
    public static readonly AnnotationColor Yellow = new(0xFF, 0xFF, 0xD6, 0x0A);
    public static readonly AnnotationColor Green = new(0xFF, 0x43, 0xA0, 0x47);
    public static readonly AnnotationColor Blue = new(0xFF, 0x1E, 0x88, 0xE5);
    public static readonly AnnotationColor Purple = new(0xFF, 0x8E, 0x24, 0xAA);

    /// <summary>The ink colours the properties row offers, red first.</summary>
    public static readonly IReadOnlyList<AnnotationColor> Palette =
    [
        Red, Orange, Yellow, Green, Blue, Purple, White, Black,
    ];

    public static AnnotationColor FromRgb(byte r, byte g, byte b) => new(0xFF, r, g, b);

    public AnnotationColor WithAlpha(byte alpha) => this with { A = alpha };

    /// <summary>Relative luminance (0 = black, 1 = white) used to pick readable text on a filled shape.</summary>
    public double Luminance => (0.299 * R + 0.587 * G + 0.114 * B) / 255.0;

    /// <summary>Black text on light ink, white text on dark ink (used for callout numbers).</summary>
    public AnnotationColor ContrastText => Luminance > 0.6 ? Black : White;

    /// <summary>Parses "#RRGGBB" or "#AARRGGBB"; returns <see cref="Red"/> for anything else.</summary>
    public static AnnotationColor Parse(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return Red;
        var text = hex.Trim().TrimStart('#');
        if (text.Length == 6 && uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            return FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        if (text.Length == 8 && uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var argb))
            return new AnnotationColor((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
        return Red;
    }

    public string ToHex() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";
}

/// <summary>
/// One editable annotation on the image, in original image pixels. The record is immutable: moving,
/// resizing or restyling produces a copy, so undo/redo only ever keeps object lists.
/// </summary>
/// <param name="Bounds">The box for every kind except line/arrow/pen, which derive it from <paramref name="Points"/>.</param>
/// <param name="Points">Arrow and line: exactly two points. Pen and highlighter: the traced path.</param>
/// <param name="Text">The text of a <see cref="AnnotationKind.Text"/> object.</param>
/// <param name="Rtl">True when the text is right-to-left (Arabic), so it is shaped and aligned correctly.</param>
/// <param name="Number">The number of a <see cref="AnnotationKind.Callout"/> object.</param>
/// <param name="StampIndex">Index into the images the user added, for a <see cref="AnnotationKind.Stamp"/>.</param>
public sealed record AnnotationObject(
    AnnotationKind Kind,
    AnnotationRect Bounds,
    AnnotationColor Color,
    double StrokeWidth = 4,
    double FontSize = 20,
    bool Filled = false,
    string Text = "",
    bool Rtl = false,
    int Number = 0,
    int StampIndex = -1,
    IReadOnlyList<AnnotationPoint>? Points = null)
{
    /// <summary>The traced path, or an empty list for objects that only have a box.</summary>
    public IReadOnlyList<AnnotationPoint> Path => Points ?? [];

    /// <summary>True for the kinds whose geometry is a point list instead of the box.</summary>
    public bool IsPath => Kind is AnnotationKind.Arrow or AnnotationKind.Line or AnnotationKind.Pen or AnnotationKind.Highlighter;

    /// <summary>A line or arrow between two points; the bounds follow the points.</summary>
    public static AnnotationObject Segment(
        AnnotationKind kind, AnnotationPoint from, AnnotationPoint to, AnnotationColor color,
        double strokeWidth = 4, double fontSize = 20) =>
        new(kind, AnnotationMath.BoundsOf([from, to]), color, strokeWidth, fontSize, Points: [from, to]);

    /// <summary>A pen or highlighter path; the bounds follow the points.</summary>
    public static AnnotationObject Polyline(
        AnnotationKind kind, IReadOnlyList<AnnotationPoint> points, AnnotationColor color,
        double strokeWidth = 4, double fontSize = 20) =>
        new(kind, AnnotationMath.BoundsOf(points), color, strokeWidth, fontSize, Points: [.. points]);
}

/// <summary>
/// One drawable shape in original image pixels: the flattened form of an <see cref="AnnotationObject"/>.
/// The preview scales these primitives and the exporter draws them at scale 1, so both show the same
/// geometry; nothing about a primitive depends on the window size.
/// </summary>
public enum AnnotationPrimitiveKind
{
    /// <summary>One stroke between <see cref="AnnotationPrimitive.Points"/>[0] and [1].</summary>
    Segment,

    /// <summary>An open stroke through <see cref="AnnotationPrimitive.Points"/>.</summary>
    Polyline,

    /// <summary>A filled shape through <see cref="AnnotationPrimitive.Points"/> (an arrow head).</summary>
    Polygon,

    Rectangle,
    Ellipse,

    /// <summary>Text laid out inside <see cref="AnnotationPrimitive.Rect"/>.</summary>
    Text,

    /// <summary>A filled circle with a number in the middle (numbered callout).</summary>
    Badge,

    /// <summary>A circular magnifier: the image region <see cref="AnnotationPrimitive.SourceRect"/> inside the lens.</summary>
    Lens,

    /// <summary>The image at <see cref="AnnotationPrimitive.StampIndex"/> drawn into <see cref="AnnotationPrimitive.Rect"/>.</summary>
    Stamp,
}

/// <summary>One drawable shape, in original image pixels (see <see cref="AnnotationMath.Flatten"/>).</summary>
public sealed record AnnotationPrimitive(
    AnnotationPrimitiveKind Kind,
    AnnotationColor Color,
    IReadOnlyList<AnnotationPoint> Points,
    AnnotationRect Rect,
    double StrokeWidth = 0,
    double FontSize = 0,
    bool Filled = false,
    string Text = "",
    bool Rtl = false,
    int Number = 0,
    int StampIndex = -1,
    AnnotationRect? SourceRect = null,
    double Opacity = 1)
{
    public static AnnotationPrimitive Segment(AnnotationPoint from, AnnotationPoint to, AnnotationColor color, double width) =>
        new(AnnotationPrimitiveKind.Segment, color, [from, to], default, StrokeWidth: width);

    public static AnnotationPrimitive Polyline(IReadOnlyList<AnnotationPoint> points, AnnotationColor color, double width, double opacity = 1) =>
        new(AnnotationPrimitiveKind.Polyline, color, [.. points], default, StrokeWidth: width, Opacity: opacity);

    public static AnnotationPrimitive Polygon(IReadOnlyList<AnnotationPoint> points, AnnotationColor color) =>
        new(AnnotationPrimitiveKind.Polygon, color, [.. points], default, Filled: true);

    public static AnnotationPrimitive Rectangle(AnnotationRect rect, AnnotationColor color, double width, bool filled) =>
        new(AnnotationPrimitiveKind.Rectangle, color, [], rect, StrokeWidth: width, Filled: filled);

    public static AnnotationPrimitive Ellipse(AnnotationRect rect, AnnotationColor color, double width, bool filled) =>
        new(AnnotationPrimitiveKind.Ellipse, color, [], rect, StrokeWidth: width, Filled: filled);

    public static AnnotationPrimitive TextShape(AnnotationRect rect, string text, AnnotationColor color, double size, bool rtl, bool filled) =>
        new(AnnotationPrimitiveKind.Text, color, [], rect, FontSize: size, Filled: filled, Text: text, Rtl: rtl);

    public static AnnotationPrimitive Badge(AnnotationRect rect, int number, AnnotationColor color, double fontSize) =>
        new(AnnotationPrimitiveKind.Badge, color, [], rect, FontSize: fontSize, Filled: true, Number: number);

    public static AnnotationPrimitive Lens(AnnotationRect rect, AnnotationRect source, AnnotationColor color, double borderWidth) =>
        new(AnnotationPrimitiveKind.Lens, color, [], rect, StrokeWidth: borderWidth, SourceRect: source);

    public static AnnotationPrimitive Stamp(AnnotationRect rect, int stampIndex) =>
        new(AnnotationPrimitiveKind.Stamp, AnnotationColor.White, [], rect, StampIndex: stampIndex);
}

/// <summary>
/// The pure geometry of the Annotate tool: bounds and hit testing, moving and resizing, the arrow head,
/// the magnifier's source region and the flattened primitives. Nothing here knows about a window, a
/// bitmap or a mouse, so every rule is unit tested directly.
/// </summary>
public static class AnnotationMath
{
    /// <summary>Smallest editable object edge, in image pixels.</summary>
    public const double MinSize = 8;

    /// <summary>A click shorter than this creates the object at its default size instead.</summary>
    public const double MinDragSize = 4;

    /// <summary>How close a click must be to a line or path to hit it, in image pixels.</summary>
    public const double HitTolerance = 6;

    /// <summary>The magnifier shows the image at this scale.</summary>
    public const double MagnifierZoom = 2.0;

    /// <summary>The highlighter is this many times wider than a pen stroke, and translucent.</summary>
    public const double HighlighterWidthFactor = 4.0;

    public const double HighlighterOpacity = 0.35;

    /// <summary>The tool that creates this kind, or null for <see cref="AnnotationTool.Select"/>.</summary>
    public static AnnotationKind? KindFor(AnnotationTool tool) => tool switch
    {
        AnnotationTool.Arrow => AnnotationKind.Arrow,
        AnnotationTool.Line => AnnotationKind.Line,
        AnnotationTool.Rectangle => AnnotationKind.Rectangle,
        AnnotationTool.Ellipse => AnnotationKind.Ellipse,
        AnnotationTool.Text => AnnotationKind.Text,
        AnnotationTool.Callout => AnnotationKind.Callout,
        AnnotationTool.Magnifier => AnnotationKind.Magnifier,
        AnnotationTool.Pen => AnnotationKind.Pen,
        AnnotationTool.Highlighter => AnnotationKind.Highlighter,
        AnnotationTool.Stamp => AnnotationKind.Stamp,
        _ => null,
    };

    /// <summary>The smallest box that contains every point.</summary>
    public static AnnotationRect BoundsOf(IReadOnlyList<AnnotationPoint> points)
    {
        if (points.Count == 0)
            return default;
        double left = points[0].X, top = points[0].Y, right = left, bottom = top;
        for (var i = 1; i < points.Count; i++)
        {
            var point = points[i];
            left = Math.Min(left, point.X);
            top = Math.Min(top, point.Y);
            right = Math.Max(right, point.X);
            bottom = Math.Max(bottom, point.Y);
        }
        return new AnnotationRect(left, top, right - left, bottom - top);
    }

    /// <summary>The two ends of a line or arrow, falling back to the bounds' diagonal.</summary>
    public static (AnnotationPoint From, AnnotationPoint To) Segment(AnnotationObject obj) =>
        obj.Path.Count >= 2
            ? (obj.Path[0], obj.Path[^1])
            : (new AnnotationPoint(obj.Bounds.X, obj.Bounds.Y), new AnnotationPoint(obj.Bounds.Right, obj.Bounds.Bottom));

    /// <summary>The visible shaft of an arrow: it stops at the base of the head so the tip stays sharp.</summary>
    public static (AnnotationPoint From, AnnotationPoint To) ArrowShaft(AnnotationPoint from, AnnotationPoint to, double strokeWidth)
    {
        var head = ArrowHeadLength(strokeWidth);
        var length = from.DistanceTo(to);
        if (length <= head * 1.2)
            return (from, to);
        var ratio = (length - head) / length;
        return (from, new AnnotationPoint(from.X + (to.X - from.X) * ratio, from.Y + (to.Y - from.Y) * ratio));
    }

    /// <summary>The triangular arrow head, tip first, then the two back corners.</summary>
    public static IReadOnlyList<AnnotationPoint> ArrowHead(AnnotationPoint from, AnnotationPoint to, double strokeWidth)
    {
        var head = ArrowHeadLength(strokeWidth);
        var length = from.DistanceTo(to);
        if (length < 0.001)
            return [to];
        var ux = (to.X - from.X) / length;
        var uy = (to.Y - from.Y) / length;
        var back = new AnnotationPoint(to.X - ux * head, to.Y - uy * head);
        var half = head * 0.5;
        // Perpendicular to the arrow direction.
        var left = new AnnotationPoint(back.X - uy * half, back.Y + ux * half);
        var right = new AnnotationPoint(back.X + uy * half, back.Y - ux * half);
        return [to, left, right];
    }

    /// <summary>The arrow head is proportional to the stroke, with a floor so a thin arrow still reads.</summary>
    public static double ArrowHeadLength(double strokeWidth) => Math.Max(10, strokeWidth * 4);

    /// <summary>Which handle of <paramref name="obj"/> is within <paramref name="tolerance"/> of the point.</summary>
    public static AnnotationHandle HandleAt(AnnotationObject obj, AnnotationPoint point, double tolerance)
    {
        if (obj.Kind is AnnotationKind.Arrow or AnnotationKind.Line && obj.Path.Count >= 2)
        {
            var (from, to) = Segment(obj);
            var toStart = point.DistanceTo(from);
            var toEnd = point.DistanceTo(to);
            if (toStart <= tolerance && toStart <= toEnd)
                return AnnotationHandle.Start;
            if (toEnd <= tolerance)
                return AnnotationHandle.End;
            return AnnotationHandle.None;
        }

        var best = AnnotationHandle.None;
        var bestDistance = double.MaxValue;
        foreach (var handle in BoxHandles)
        {
            var distance = point.DistanceTo(HandlePoint(obj, handle));
            if (distance <= tolerance && distance < bestDistance)
            {
                best = handle;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>The eight box handles, clockwise from the top-left corner.</summary>
    public static readonly IReadOnlyList<AnnotationHandle> BoxHandles =
    [
        AnnotationHandle.TopLeft,
        AnnotationHandle.Top,
        AnnotationHandle.TopRight,
        AnnotationHandle.Right,
        AnnotationHandle.BottomRight,
        AnnotationHandle.Bottom,
        AnnotationHandle.BottomLeft,
        AnnotationHandle.Left,
    ];

    /// <summary>Where one handle sits, in image pixels.</summary>
    public static AnnotationPoint HandlePoint(AnnotationObject obj, AnnotationHandle handle)
    {
        if (handle is AnnotationHandle.Start or AnnotationHandle.End)
        {
            var (from, to) = Segment(obj);
            return handle == AnnotationHandle.Start ? from : to;
        }

        var rect = obj.Bounds;
        return handle switch
        {
            AnnotationHandle.TopLeft => new AnnotationPoint(rect.X, rect.Y),
            AnnotationHandle.Top => new AnnotationPoint(rect.CenterX, rect.Y),
            AnnotationHandle.TopRight => new AnnotationPoint(rect.Right, rect.Y),
            AnnotationHandle.Right => new AnnotationPoint(rect.Right, rect.CenterY),
            AnnotationHandle.BottomRight => new AnnotationPoint(rect.Right, rect.Bottom),
            AnnotationHandle.Bottom => new AnnotationPoint(rect.CenterX, rect.Bottom),
            AnnotationHandle.BottomLeft => new AnnotationPoint(rect.X, rect.Bottom),
            AnnotationHandle.Left => new AnnotationPoint(rect.X, rect.CenterY),
            _ => new AnnotationPoint(rect.CenterX, rect.CenterY),
        };
    }

    /// <summary>True when the point is on the object (its box, its stroke or its path).</summary>
    public static bool HitTest(AnnotationObject obj, AnnotationPoint point, double tolerance = HitTolerance)
    {
        switch (obj.Kind)
        {
            case AnnotationKind.Arrow:
            case AnnotationKind.Line:
            {
                var (from, to) = Segment(obj);
                return DistanceToSegment(point, from, to) <= tolerance + obj.StrokeWidth / 2;
            }
            case AnnotationKind.Pen:
            case AnnotationKind.Highlighter:
            {
                var width = obj.Kind == AnnotationKind.Highlighter ? obj.StrokeWidth * HighlighterWidthFactor : obj.StrokeWidth;
                var limit = tolerance + width / 2;
                for (var i = 1; i < obj.Path.Count; i++)
                    if (DistanceToSegment(point, obj.Path[i - 1], obj.Path[i]) <= limit)
                        return true;
                return obj.Path.Count == 1 && point.DistanceTo(obj.Path[0]) <= limit;
            }
            case AnnotationKind.Callout:
            case AnnotationKind.Magnifier:
            {
                var radius = Math.Max(obj.Bounds.Width, obj.Bounds.Height) / 2;
                return point.DistanceTo(obj.Bounds.Center) <= radius + tolerance;
            }
            default:
                return obj.Bounds.Inflate(tolerance).Contains(point);
        }
    }

    /// <summary>The object under the point, from the front of the image backwards; -1 when none.</summary>
    public static int HitTest(IReadOnlyList<AnnotationObject> objects, AnnotationPoint point, double tolerance = HitTolerance)
    {
        for (var i = objects.Count - 1; i >= 0; i--)
            if (HitTest(objects[i], point, tolerance))
                return i;
        return -1;
    }

    /// <summary>Shortest distance from a point to a line segment.</summary>
    public static double DistanceToSegment(AnnotationPoint point, AnnotationPoint from, AnnotationPoint to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared < 0.000001)
            return point.DistanceTo(from);

        var t = ((point.X - from.X) * dx + (point.Y - from.Y) * dy) / lengthSquared;
        t = Math.Clamp(t, 0, 1);
        return point.DistanceTo(new AnnotationPoint(from.X + t * dx, from.Y + t * dy));
    }

    /// <summary>
    /// Moves the object by (dx, dy), clamped so it never leaves the image. Path objects translate all
    /// their points, so their shape never deforms at an edge.
    /// </summary>
    public static AnnotationObject Move(AnnotationObject obj, double dx, double dy, int imageWidth, int imageHeight)
    {
        var bounds = obj.Bounds;
        var x = Math.Clamp(bounds.X + dx, 0, Math.Max(0, imageWidth - bounds.Width));
        var y = Math.Clamp(bounds.Y + dy, 0, Math.Max(0, imageHeight - bounds.Height));
        var actualX = x - bounds.X;
        var actualY = y - bounds.Y;

        if (obj.Path.Count > 0)
        {
            var points = obj.Path.Select(p => p.Offset(actualX, actualY)).ToArray();
            return obj with { Points = points, Bounds = BoundsOf(points) };
        }

        return obj with { Bounds = new AnnotationRect(x, y, bounds.Width, bounds.Height) };
    }

    /// <summary>
    /// Drags one handle by (dx, dy) pixels. Box objects keep a minimum size and stay inside the image;
    /// a line or arrow moves the grabbed endpoint; a pen or highlighter path is scaled with its box;
    /// callouts and magnifiers stay circular.
    /// </summary>
    public static AnnotationObject Resize(
        AnnotationObject obj, AnnotationHandle handle, double dx, double dy, int imageWidth, int imageHeight)
    {
        if (handle is AnnotationHandle.None or AnnotationHandle.Move)
            return obj;

        if (obj.Kind is AnnotationKind.Arrow or AnnotationKind.Line && handle is AnnotationHandle.Start or AnnotationHandle.End)
        {
            var points = obj.Path.ToArray();
            if (points.Length < 2)
                return obj;
            var index = handle == AnnotationHandle.Start ? 0 : points.Length - 1;
            var moved = new AnnotationPoint(
                Math.Clamp(points[index].X + dx, 0, Math.Max(0, imageWidth)),
                Math.Clamp(points[index].Y + dy, 0, Math.Max(0, imageHeight)));
            points[index] = moved;
            return obj with { Points = points, Bounds = BoundsOf(points) };
        }

        var start = obj.Bounds;
        var box = ClampInside(ResizeBox(start, handle, dx, dy), imageWidth, imageHeight);

        if (obj.Kind is AnnotationKind.Callout or AnnotationKind.Magnifier)
            box = ClampInside(Square(box, handle, imageWidth, imageHeight), imageWidth, imageHeight);

        if (obj.Kind is AnnotationKind.Pen or AnnotationKind.Highlighter && obj.Path.Count > 0)
        {
            var scaleX = start.Width > 0.0001 ? box.Width / start.Width : 1;
            var scaleY = start.Height > 0.0001 ? box.Height / start.Height : 1;
            var points = obj.Path
                .Select(p => new AnnotationPoint(
                    box.X + (p.X - start.X) * scaleX,
                    box.Y + (p.Y - start.Y) * scaleY))
                .ToArray();
            return obj with { Points = points, Bounds = box };
        }

        return obj with { Bounds = box };
    }

    /// <summary>Keeps a box inside the image and at least <see cref="MinSize"/> wide and high.</summary>
    public static AnnotationRect ClampInside(AnnotationRect rect, int imageWidth, int imageHeight)
    {
        var maxWidth = Math.Max(MinSize, imageWidth);
        var maxHeight = Math.Max(MinSize, imageHeight);
        var width = Math.Clamp(rect.Width, MinSize, maxWidth);
        var height = Math.Clamp(rect.Height, MinSize, maxHeight);
        var x = Math.Clamp(rect.X, 0, Math.Max(0, imageWidth - width));
        var y = Math.Clamp(rect.Y, 0, Math.Max(0, imageHeight - height));
        return new AnnotationRect(x, y, width, height);
    }

    /// <summary>The region of the image the lens shows: the lens box reduced by the zoom, inside the image.</summary>
    public static AnnotationRect MagnifierSource(AnnotationRect lens, int imageWidth, int imageHeight, double zoom = MagnifierZoom)
    {
        var factor = zoom > 0 ? zoom : MagnifierZoom;
        var width = Math.Max(1, lens.Width / factor);
        var height = Math.Max(1, lens.Height / factor);
        var x = Math.Clamp(lens.CenterX - width / 2, 0, Math.Max(0, imageWidth - width));
        var y = Math.Clamp(lens.CenterY - height / 2, 0, Math.Max(0, imageHeight - height));
        return new AnnotationRect(x, y, width, height);
    }

    /// <summary>
    /// The drawable primitives of one object, at original resolution. One object can flatten to several
    /// primitives (an arrow is the shaft plus the head triangle).
    /// </summary>
    public static IReadOnlyList<AnnotationPrimitive> Flatten(AnnotationObject obj, int imageWidth, int imageHeight)
    {
        switch (obj.Kind)
        {
            case AnnotationKind.Arrow:
            {
                var (from, to) = Segment(obj);
                var shaft = ArrowShaft(from, to, obj.StrokeWidth);
                return
                [
                    AnnotationPrimitive.Segment(shaft.From, shaft.To, obj.Color, obj.StrokeWidth),
                    AnnotationPrimitive.Polygon(ArrowHead(from, to, obj.StrokeWidth), obj.Color),
                ];
            }
            case AnnotationKind.Line:
            {
                var (from, to) = Segment(obj);
                return [AnnotationPrimitive.Segment(from, to, obj.Color, obj.StrokeWidth)];
            }
            case AnnotationKind.Pen:
                return [AnnotationPrimitive.Polyline(obj.Path, obj.Color, obj.StrokeWidth)];
            case AnnotationKind.Highlighter:
                return
                [
                    AnnotationPrimitive.Polyline(
                        obj.Path, obj.Color, obj.StrokeWidth * HighlighterWidthFactor, HighlighterOpacity),
                ];
            case AnnotationKind.Rectangle:
                return [AnnotationPrimitive.Rectangle(obj.Bounds, obj.Color, obj.StrokeWidth, obj.Filled)];
            case AnnotationKind.Ellipse:
                return [AnnotationPrimitive.Ellipse(obj.Bounds, obj.Color, obj.StrokeWidth, obj.Filled)];
            case AnnotationKind.Text:
                return
                [
                    AnnotationPrimitive.TextShape(obj.Bounds, obj.Text, obj.Color, obj.FontSize, obj.Rtl, obj.Filled),
                ];
            case AnnotationKind.Callout:
            {
                var side = Math.Min(obj.Bounds.Width, obj.Bounds.Height);
                var fontSize = Math.Clamp(obj.FontSize, 8, Math.Max(8, side * 0.8));
                return [AnnotationPrimitive.Badge(obj.Bounds, obj.Number, obj.Color, fontSize)];
            }
            case AnnotationKind.Magnifier:
                return
                [
                    AnnotationPrimitive.Lens(
                        obj.Bounds,
                        MagnifierSource(obj.Bounds, imageWidth, imageHeight),
                        obj.Color,
                        Math.Max(2, obj.StrokeWidth)),
                ];
            case AnnotationKind.Stamp:
                return [AnnotationPrimitive.Stamp(obj.Bounds, obj.StampIndex)];
            default:
                return [];
        }
    }

    /// <summary>Every primitive of every object, in z-order (the last object is drawn on top).</summary>
    public static IReadOnlyList<AnnotationPrimitive> Flatten(IEnumerable<AnnotationObject> objects, int imageWidth, int imageHeight) =>
        objects.SelectMany(o => Flatten(o, imageWidth, imageHeight)).ToArray();

    private static AnnotationRect ResizeBox(AnnotationRect start, AnnotationHandle handle, double dx, double dy)
    {
        double left = start.X, top = start.Y, right = start.Right, bottom = start.Bottom;
        switch (handle)
        {
            case AnnotationHandle.TopLeft: left += dx; top += dy; break;
            case AnnotationHandle.Top: top += dy; break;
            case AnnotationHandle.TopRight: right += dx; top += dy; break;
            case AnnotationHandle.Right: right += dx; break;
            case AnnotationHandle.BottomRight: right += dx; bottom += dy; break;
            case AnnotationHandle.Bottom: bottom += dy; break;
            case AnnotationHandle.BottomLeft: left += dx; bottom += dy; break;
            case AnnotationHandle.Left: left += dx; break;
            default: return start;
        }

        if (right - left < MinSize)
        {
            if (handle is AnnotationHandle.TopLeft or AnnotationHandle.BottomLeft or AnnotationHandle.Left)
                left = right - MinSize;
            else
                right = left + MinSize;
        }
        if (bottom - top < MinSize)
        {
            if (handle is AnnotationHandle.TopLeft or AnnotationHandle.TopRight or AnnotationHandle.Top)
                top = bottom - MinSize;
            else
                bottom = top + MinSize;
        }

        return AnnotationRect.FromPoints(left, top, right, bottom);
    }

    /// <summary>Forces a square (callout/magnifier), anchored on the side opposite the dragged handle.</summary>
    private static AnnotationRect Square(AnnotationRect rect, AnnotationHandle handle, int imageWidth, int imageHeight)
    {
        var limit = Math.Max(MinSize, Math.Min(imageWidth, imageHeight));
        var side = Math.Min(Math.Max(Math.Max(rect.Width, rect.Height), MinSize), limit);
        var x = rect.X;
        var y = rect.Y;
        if (handle is AnnotationHandle.TopLeft or AnnotationHandle.Left or AnnotationHandle.BottomLeft)
            x = rect.Right - side;
        if (handle is AnnotationHandle.TopLeft or AnnotationHandle.Top or AnnotationHandle.TopRight)
            y = rect.Bottom - side;
        return new AnnotationRect(x, y, side, side);
    }
}

/// <summary>
/// The annotation objects on one image, back to front. The document is pure data: the window writes
/// through it, <see cref="Flatten"/> reads it for drawing and exporting, and undo/redo keeps
/// <see cref="Snapshot"/> lists. Every coordinate is in the original image's pixels.
/// </summary>
public sealed class AnnotationDocument
{
    private readonly List<AnnotationObject> _objects = [];

    public AnnotationDocument(int imageWidth, int imageHeight)
    {
        ImageWidth = Math.Max(1, imageWidth);
        ImageHeight = Math.Max(1, imageHeight);
    }

    public int ImageWidth { get; }

    public int ImageHeight { get; }

    /// <summary>The objects, back to front.</summary>
    public IReadOnlyList<AnnotationObject> Objects => _objects;

    public int Count => _objects.Count;

    /// <summary>Adds an object on top and returns its index.</summary>
    public int Add(AnnotationObject obj)
    {
        _objects.Add(obj);
        return _objects.Count - 1;
    }

    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= _objects.Count)
            return false;
        _objects.RemoveAt(index);
        return true;
    }

    public bool Replace(int index, AnnotationObject obj)
    {
        if (index < 0 || index >= _objects.Count)
            return false;
        _objects[index] = obj;
        return true;
    }

    /// <summary>Moves an object to the top of the z-order. False when it is already there.</summary>
    public bool BringToFront(int index)
    {
        if (index < 0 || index >= _objects.Count || index == _objects.Count - 1)
            return false;
        var obj = _objects[index];
        _objects.RemoveAt(index);
        _objects.Add(obj);
        return true;
    }

    /// <summary>Moves an object behind every other. False when it is already at the back.</summary>
    public bool SendToBack(int index)
    {
        if (index <= 0 || index >= _objects.Count)
            return false;
        var obj = _objects[index];
        _objects.RemoveAt(index);
        _objects.Insert(0, obj);
        return true;
    }

    /// <summary>
    /// The number the next callout gets: one above every callout already on the image, so numbering is
    /// stable when an earlier callout is deleted.
    /// </summary>
    public int NextCalloutNumber()
    {
        var highest = 0;
        foreach (var obj in _objects)
            if (obj.Kind == AnnotationKind.Callout && obj.Number > highest)
                highest = obj.Number;
        return highest + 1;
    }

    /// <summary>The drawable primitives of the whole document, at original resolution, in z-order.</summary>
    public IReadOnlyList<AnnotationPrimitive> Flatten() => AnnotationMath.Flatten(_objects, ImageWidth, ImageHeight);

    /// <summary>An immutable copy of the object list for undo/redo.</summary>
    public IReadOnlyList<AnnotationObject> Snapshot() => _objects.ToArray();

    /// <summary>Replaces the whole object list (undo/redo).</summary>
    public void Restore(IReadOnlyList<AnnotationObject> objects)
    {
        _objects.Clear();
        _objects.AddRange(objects);
    }
}

/// <summary>
/// Undo and redo for the annotation document. A snapshot is taken before every change, so undo always
/// restores the state the user saw before the gesture they are undoing.
/// </summary>
public sealed class AnnotationHistory
{
    private readonly List<IReadOnlyList<AnnotationObject>> _undo = [];
    private readonly List<IReadOnlyList<AnnotationObject>> _redo = [];
    private readonly int _capacity;

    public AnnotationHistory(int capacity = 100) => _capacity = Math.Max(1, capacity);

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>Records the state before a change and drops the redo branch.</summary>
    public void Push(AnnotationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _undo.Add(document.Snapshot());
        if (_undo.Count > _capacity)
            _undo.RemoveAt(0);
        _redo.Clear();
    }

    /// <summary>Restores the previous snapshot; false when there is nothing to undo.</summary>
    public bool Undo(AnnotationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_undo.Count == 0)
            return false;
        var previous = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(document.Snapshot());
        document.Restore(previous);
        return true;
    }

    /// <summary>Reapplies the snapshot the last undo replaced; false when there is nothing to redo.</summary>
    public bool Redo(AnnotationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_redo.Count == 0)
            return false;
        var next = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(document.Snapshot());
        document.Restore(next);
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
