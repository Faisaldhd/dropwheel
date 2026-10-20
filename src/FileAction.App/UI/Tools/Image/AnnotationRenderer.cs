using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileAction.Core;

namespace FileAction.App.UI.Tools.Image;

/// <summary>
/// Draws the flattened <see cref="AnnotationPrimitive"/> list. The same code paints the downsampled
/// preview (with a scale and an origin) and the export (scale 1, origin 0, original pixel size), so what
/// the user sees is exactly what is written. Primitive coordinates are always original image pixels.
/// </summary>
internal static class AnnotationRenderer
{
    /// <summary>How much of its own colour a filled shape shows behind its stroke.</summary>
    private const double FillOpacity = 0.22;

    /// <summary>How much of its own colour a text object shows as a background (fill on).</summary>
    private const double TextFillOpacity = 0.18;

    public static void Draw(
        DrawingContext dc,
        IReadOnlyList<AnnotationPrimitive> primitives,
        BitmapSource? source,
        double imageWidth,
        double imageHeight,
        double originX,
        double originY,
        double scale,
        IReadOnlyList<BitmapSource> stamps,
        double pixelsPerDip = 1.0)
    {
        ArgumentNullException.ThrowIfNull(dc);
        foreach (var primitive in primitives)
            DrawPrimitive(dc, primitive, source, imageWidth, imageHeight, originX, originY, scale, stamps, pixelsPerDip);
    }

    /// <summary>
    /// Renders the whole annotated document at the image's original pixel size. The caller must be on
    /// the UI thread; the returned bitmap is frozen and can be encoded on a background thread.
    /// </summary>
    public static RenderTargetBitmap RenderOriginalResolution(
        AnnotationDocument document, BitmapSource baseImage, IReadOnlyList<BitmapSource> stamps)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(baseImage);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(baseImage, new Rect(0, 0, document.ImageWidth, document.ImageHeight));
            Draw(dc, document.Flatten(), baseImage, document.ImageWidth, document.ImageHeight, 0, 0, 1, stamps);
        }

        var bitmap = new RenderTargetBitmap(
            document.ImageWidth, document.ImageHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static void DrawPrimitive(
        DrawingContext dc,
        AnnotationPrimitive primitive,
        BitmapSource? source,
        double imageWidth,
        double imageHeight,
        double originX,
        double originY,
        double scale,
        IReadOnlyList<BitmapSource> stamps,
        double pixelsPerDip)
    {
        var opacity = Math.Clamp(primitive.Opacity, 0, 1);
        if (opacity <= 0)
            return;

        switch (primitive.Kind)
        {
            case AnnotationPrimitiveKind.Segment:
            {
                if (primitive.Points.Count < 2)
                    return;
                var width = Math.Max(0.6, primitive.StrokeWidth * scale);
                dc.DrawLine(Stroke(primitive.Color, width, opacity),
                    Map(primitive.Points[0], originX, originY, scale),
                    Map(primitive.Points[1], originX, originY, scale));
                return;
            }
            case AnnotationPrimitiveKind.Polyline:
            {
                if (primitive.Points.Count == 0)
                    return;
                var width = Math.Max(0.6, primitive.StrokeWidth * scale);
                var pen = Stroke(primitive.Color, width, opacity);
                if (primitive.Points.Count == 1)
                {
                    var point = Map(primitive.Points[0], originX, originY, scale);
                    dc.DrawEllipse(Brush(primitive.Color, opacity), null, point, width / 2, width / 2);
                    return;
                }
                var geometry = BuildGeometry(primitive.Points, originX, originY, scale, closed: false);
                dc.DrawGeometry(null, pen, geometry);
                return;
            }
            case AnnotationPrimitiveKind.Polygon:
            {
                if (primitive.Points.Count < 3)
                    return;
                var geometry = BuildGeometry(primitive.Points, originX, originY, scale, closed: true);
                dc.DrawGeometry(Brush(primitive.Color, opacity), null, geometry);
                return;
            }
            case AnnotationPrimitiveKind.Rectangle:
            {
                var rect = Map(primitive.Rect, originX, originY, scale);
                var fill = primitive.Filled ? Brush(primitive.Color, FillOpacity * opacity) : null;
                dc.DrawRectangle(fill, Stroke(primitive.Color, primitive.StrokeWidth * scale, opacity), rect);
                return;
            }
            case AnnotationPrimitiveKind.Ellipse:
            {
                var rect = Map(primitive.Rect, originX, originY, scale);
                var center = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
                var fill = primitive.Filled ? Brush(primitive.Color, FillOpacity * opacity) : null;
                dc.DrawEllipse(fill, Stroke(primitive.Color, primitive.StrokeWidth * scale, opacity),
                    center, rect.Width / 2, rect.Height / 2);
                return;
            }
            case AnnotationPrimitiveKind.Text:
            {
                DrawText(dc, primitive, Map(primitive.Rect, originX, originY, scale), scale, opacity, pixelsPerDip);
                return;
            }
            case AnnotationPrimitiveKind.Badge:
            {
                DrawBadge(dc, primitive, Map(primitive.Rect, originX, originY, scale), scale, opacity, pixelsPerDip);
                return;
            }
            case AnnotationPrimitiveKind.Lens:
            {
                DrawLens(dc, primitive, source, imageWidth, imageHeight, originX, originY, scale, opacity);
                return;
            }
            case AnnotationPrimitiveKind.Stamp:
            {
                if (primitive.StampIndex < 0 || primitive.StampIndex >= stamps.Count)
                    return;
                dc.DrawImage(stamps[primitive.StampIndex], Map(primitive.Rect, originX, originY, scale));
                return;
            }
        }
    }

    private static void DrawText(
        DrawingContext dc, AnnotationPrimitive primitive, Rect rect, double scale, double opacity, double pixelsPerDip)
    {
        var text = primitive.Text ?? string.Empty;
        if (text.Length == 0 || rect.Width <= 0 || rect.Height <= 0)
            return;

        if (primitive.Filled)
            dc.DrawRoundedRectangle(
                Brush(primitive.Color, TextFillOpacity * opacity), null, rect, 4 * scale, 4 * scale);

        var formatted = Format(primitive, text, Math.Max(1, primitive.FontSize * scale), rect.Width, pixelsPerDip, opacity);
        // Right-to-left text is aligned to the right edge of its box; left-to-right to the left edge.
        dc.DrawText(formatted, new Point(rect.X, rect.Y));
    }

    private static void DrawBadge(
        DrawingContext dc, AnnotationPrimitive primitive, Rect rect, double scale, double opacity, double pixelsPerDip)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return;
        var center = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        dc.DrawEllipse(Brush(primitive.Color, opacity), null, center, rect.Width / 2, rect.Height / 2);

        // The number keeps Western digits in both languages, like every other technical value in the app.
        var size = Math.Max(1, Math.Min(primitive.FontSize * scale, Math.Min(rect.Width, rect.Height) * 0.9));
        var formatted = new FormattedText(
            primitive.Number.ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface,
            size,
            Brush(primitive.Color.ContrastText, opacity),
            pixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, rect.Width),
            TextAlignment = TextAlignment.Center,
        };
        var top = rect.Y + (rect.Height - formatted.Height) / 2;
        dc.DrawText(formatted, new Point(rect.X, top));
    }

    private static void DrawLens(
        DrawingContext dc,
        AnnotationPrimitive primitive,
        BitmapSource? source,
        double imageWidth,
        double imageHeight,
        double originX,
        double originY,
        double scale,
        double opacity)
    {
        var rect = Map(primitive.Rect, originX, originY, scale);
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        var center = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        var clip = new EllipseGeometry(center, rect.Width / 2, rect.Height / 2);
        dc.PushClip(clip);
        try
        {
            if (source is not null && primitive.SourceRect is { } region &&
                imageWidth > 0 && imageHeight > 0 && region.Width > 0 && region.Height > 0)
            {
                // The photo is normally painted into the display rectangle; the lens re-paints the same
                // bitmap with a transform that maps the source region (original image pixels) onto the
                // lens circle, which is exactly the zoom the user asked for.
                var display = new Rect(originX, originY, imageWidth * scale, imageHeight * scale);
                var sourceView = Map(region, originX, originY, scale);
                var zoomX = rect.Width / sourceView.Width;
                var zoomY = rect.Height / sourceView.Height;
                dc.PushTransform(new TranslateTransform(
                    rect.X - sourceView.X * zoomX, rect.Y - sourceView.Y * zoomY));
                dc.PushTransform(new ScaleTransform(zoomX, zoomY));
                dc.DrawImage(source, display);
                dc.Pop();
                dc.Pop();
            }
            else
            {
                dc.DrawRectangle(Brush(primitive.Color, 0.15 * opacity), null, rect);
            }
        }
        finally
        {
            dc.Pop();
        }

        var pen = Stroke(primitive.Color, Math.Max(2, primitive.StrokeWidth * scale), opacity);
        dc.DrawEllipse(null, pen, center, rect.Width / 2, rect.Height / 2);
    }

    private static FormattedText Format(
        AnnotationPrimitive primitive, string text, double size, double maxWidth, double pixelsPerDip, double opacity)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            primitive.Rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            Typeface,
            Math.Max(1, size),
            Brush(primitive.Color, opacity),
            pixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            TextAlignment = primitive.Rtl ? TextAlignment.Right : TextAlignment.Left,
            Trimming = TextTrimming.None,
        };
        return formatted;
    }

    private static readonly Typeface Typeface =
        new(Theme.UiFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private static Point Map(AnnotationPoint point, double originX, double originY, double scale) =>
        new(originX + point.X * scale, originY + point.Y * scale);

    private static Rect Map(AnnotationRect rect, double originX, double originY, double scale) =>
        new(
            originX + rect.X * scale,
            originY + rect.Y * scale,
            Math.Max(0.75, rect.Width * scale),
            Math.Max(0.75, rect.Height * scale));

    private static StreamGeometry BuildGeometry(
        IReadOnlyList<AnnotationPoint> points, double originX, double originY, double scale, bool closed)
    {
        var mapped = points.Select(p => Map(p, originX, originY, scale)).ToArray();
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(mapped[0], closed, closed);
            if (mapped.Length > 1)
                context.PolyLineTo(mapped[1..], true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static SolidColorBrush Brush(AnnotationColor color, double opacity = 1)
    {
        var alpha = (byte)Math.Clamp(color.A * Math.Clamp(opacity, 0, 1), 0, 255);
        var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private static Pen Stroke(AnnotationColor color, double width, double opacity = 1) =>
        new(Brush(color, opacity), Math.Max(0.6, width))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
}
