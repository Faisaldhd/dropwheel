namespace FileAction.Core;

/// <summary>A crop rectangle in image pixels (top-left origin).</summary>
public readonly record struct CropRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

/// <summary>The eight drag handles around a crop rectangle.</summary>
public enum CropHandle
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
/// The crop rectangle rules: keep it inside the image, keep a minimum size, snap it to an aspect ratio
/// and drag it by one of its handles (ratio-locked when a ratio is chosen). Pure math so the interactive
/// tool has no window or bitmap dependency in its tests.
/// </summary>
public static class CropMath
{
    /// <summary>Smallest crop edge, in pixels.</summary>
    public const double MinSize = 16;

    public static CropRect Full(int imageWidth, int imageHeight) =>
        new(0, 0, Math.Max(MinSize, imageWidth), Math.Max(MinSize, imageHeight));

    /// <summary>Keeps the rectangle inside the image and at least <see cref="MinSize"/> wide and high.</summary>
    public static CropRect Clamp(CropRect rect, int imageWidth, int imageHeight)
    {
        var maxWidth = Math.Max(MinSize, imageWidth);
        var maxHeight = Math.Max(MinSize, imageHeight);
        var width = Math.Clamp(rect.Width, MinSize, maxWidth);
        var height = Math.Clamp(rect.Height, MinSize, maxHeight);
        var x = Math.Clamp(rect.X, 0, maxWidth - width);
        var y = Math.Clamp(rect.Y, 0, maxHeight - height);
        return new CropRect(x, y, width, height);
    }

    /// <summary>
    /// Largest rectangle with <paramref name="ratio"/> (width / height) that fits in the image, centred on
    /// the current rectangle's centre. This is what choosing an aspect-ratio chip does.
    /// </summary>
    public static CropRect SnapToRatio(CropRect current, int imageWidth, int imageHeight, double ratio)
    {
        if (ratio <= 0)
            return Clamp(current, imageWidth, imageHeight);

        var width = Math.Min(imageWidth, imageHeight * ratio);
        var height = width / ratio;
        var centreX = current.X + current.Width / 2;
        var centreY = current.Y + current.Height / 2;
        var x = Math.Clamp(centreX - width / 2, 0, Math.Max(0, imageWidth - width));
        var y = Math.Clamp(centreY - height / 2, 0, Math.Max(0, imageHeight - height));
        return new CropRect(x, y, width, height);
    }

    /// <summary>
    /// Drags one handle by (dx, dy) pixels. With <paramref name="ratio"/> set, the opposite corner or edge
    /// stays anchored and the other dimension follows the ratio.
    /// </summary>
    public static CropRect Drag(
        CropHandle handle,
        CropRect start,
        double dx,
        double dy,
        int imageWidth,
        int imageHeight,
        double? ratio)
    {
        if (handle == CropHandle.None)
            return Clamp(start, imageWidth, imageHeight);

        double left = start.X, top = start.Y, right = start.Right, bottom = start.Bottom;
        switch (handle)
        {
            case CropHandle.TopLeft: left += dx; top += dy; break;
            case CropHandle.Top: top += dy; break;
            case CropHandle.TopRight: right += dx; top += dy; break;
            case CropHandle.Right: right += dx; break;
            case CropHandle.BottomRight: right += dx; bottom += dy; break;
            case CropHandle.Bottom: bottom += dy; break;
            case CropHandle.BottomLeft: left += dx; bottom += dy; break;
            case CropHandle.Left: left += dx; break;
        }

        if (right - left < MinSize)
        {
            if (handle is CropHandle.TopLeft or CropHandle.BottomLeft or CropHandle.Left)
                left = right - MinSize;
            else
                right = left + MinSize;
        }
        if (bottom - top < MinSize)
        {
            if (handle is CropHandle.TopLeft or CropHandle.TopRight or CropHandle.Top)
                top = bottom - MinSize;
            else
                bottom = top + MinSize;
        }

        if (ratio is double locked && locked > 0)
            (left, top, right, bottom) = LockRatio(handle, left, top, right, bottom, locked);

        return Clamp(new CropRect(left, top, right - left, bottom - top), imageWidth, imageHeight);
    }

    private static (double Left, double Top, double Right, double Bottom) LockRatio(
        CropHandle handle, double left, double top, double right, double bottom, double ratio)
    {
        var width = right - left;
        var height = bottom - top;

        if (handle is CropHandle.Left or CropHandle.Right)
            height = width / ratio;
        else if (handle is CropHandle.Top or CropHandle.Bottom)
            width = height * ratio;
        else if (width / ratio >= height)
            height = width / ratio;
        else
            width = height * ratio;

        switch (handle)
        {
            case CropHandle.TopLeft:
                left = right - width;
                top = bottom - height;
                break;
            case CropHandle.TopRight:
                right = left + width;
                top = bottom - height;
                break;
            case CropHandle.BottomRight:
                right = left + width;
                bottom = top + height;
                break;
            case CropHandle.BottomLeft:
                left = right - width;
                bottom = top + height;
                break;
            case CropHandle.Top:
            {
                var centre = (left + right) / 2;
                left = centre - width / 2;
                right = centre + width / 2;
                bottom = top + height;
                break;
            }
            case CropHandle.Bottom:
            {
                var centre = (left + right) / 2;
                left = centre - width / 2;
                right = centre + width / 2;
                top = bottom - height;
                break;
            }
            case CropHandle.Left:
            {
                var centre = (top + bottom) / 2;
                top = centre - height / 2;
                bottom = centre + height / 2;
                right = left + width;
                break;
            }
            case CropHandle.Right:
            {
                var centre = (top + bottom) / 2;
                top = centre - height / 2;
                bottom = centre + height / 2;
                left = right - width;
                break;
            }
        }

        return (left, top, right, bottom);
    }

    /// <summary>The pixel size a crop rectangle writes: rounded, at least one pixel.</summary>
    public static (int Width, int Height) Pixels(CropRect rect) =>
        (Math.Max(1, (int)Math.Round(rect.Width)), Math.Max(1, (int)Math.Round(rect.Height)));

    /// <summary>Builds a rectangle from the width/height boxes, clamped to the image.</summary>
    public static CropRect FromPixels(double x, double y, double width, double height, int imageWidth, int imageHeight) =>
        Clamp(new CropRect(x, y, width, height), imageWidth, imageHeight);
}

/// <summary>Clamps a tool window's declared size to the monitor work area.</summary>
public static class ToolLayout
{
    /// <summary>At most 95% of the work-area width and 90% of its height.</summary>
    public static (double Width, double Height) Clamp(double width, double height, double workWidth, double workHeight)
    {
        var maxWidth = Math.Max(200, workWidth * 0.95);
        var maxHeight = Math.Max(200, workHeight * 0.90);
        return (Math.Min(width, maxWidth), Math.Min(height, maxHeight));
    }
}
