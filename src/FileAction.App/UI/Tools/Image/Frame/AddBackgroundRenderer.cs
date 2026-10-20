using System.IO;
using FileAction.Core;
using ImageMagick;
using ImageMagick.Drawing;

namespace FileAction.App.UI.Tools.Image.Frame;

/// <summary>What the backdrop is made of: a preset gradient, one flat colour, or a chosen image.</summary>
internal enum BackgroundKind
{
    Gradient,
    Solid,
    Image,
}

/// <summary>The background the user picked, in a form the renderer can repeat at any resolution.</summary>
internal readonly record struct BackgroundFill(
    BackgroundKind Kind,
    GradientPreset Gradient,
    BackgroundColor Color,
    string? ImagePath,
    bool Blur)
{
    public static BackgroundFill OfGradient(GradientPreset preset) =>
        new(BackgroundKind.Gradient, preset, default, null, false);

    public static BackgroundFill OfColor(BackgroundColor color) =>
        new(BackgroundKind.Solid, GradientPreset.Violet, color, null, false);

    public static BackgroundFill OfImage(string path, bool blur) =>
        new(BackgroundKind.Image, GradientPreset.Violet, default, path, blur);
}

/// <summary>
/// Renders one "photo with a background" at the photo's own resolution with Magick.NET: the backdrop
/// (gradient, colour or cover-fitted image) fills the padded canvas, the photo is rounded, its shadow is
/// a blurred copy of the same rounded rectangle, and the finished canvas keeps the source's alpha. The
/// source file is only read; the caller writes to a temporary path and moves it into place.
/// </summary>
internal static class AddBackgroundRenderer
{
    public static void Render(
        string sourcePath,
        BackgroundFill fill,
        int padding,
        int corners,
        int shadow,
        BackgroundRatio ratio,
        string target)
    {
        using var photo = Load(sourcePath);
        var composition = AddBackground.Compose(
            (int)photo.Width, (int)photo.Height, padding, corners, shadow, ratio);

        using var canvas = CreateBackground(fill, composition);
        RoundCorners(photo, composition.CornerRadius);
        DrawShadow(canvas, composition);
        CompositeClipped(canvas, photo, composition.ImageX, composition.ImageY);

        canvas.ResetPage();
        canvas.Write(target, MagickFormat.Png);
    }

    /// <summary>
    /// Composites a layer at a possibly negative position by cropping the invisible part first. ImageMagick
    /// does clip negative offsets too, but cropping keeps the work (and the memory) proportional to what is
    /// actually visible.
    /// </summary>
    private static void CompositeClipped(MagickImage canvas, MagickImage layer, int x, int y)
    {
        var left = Math.Max(0, -x);
        var top = Math.Max(0, -y);
        var right = Math.Max(0, x + (int)layer.Width - (int)canvas.Width);
        var bottom = Math.Max(0, y + (int)layer.Height - (int)canvas.Height);
        if (left == 0 && top == 0 && right == 0 && bottom == 0)
        {
            canvas.Composite(layer, x, y, CompositeOperator.SrcOver);
            return;
        }

        var width = (int)layer.Width - left - right;
        var height = (int)layer.Height - top - bottom;
        if (width <= 0 || height <= 0)
            return;

        layer.Crop(new MagickGeometry(left, top, (uint)width, (uint)height));
        layer.ResetPage();
        canvas.Composite(layer, x + left, y + top, CompositeOperator.SrcOver);
    }

    // ------------------------------------------------------------------ background

    private static MagickImage CreateBackground(BackgroundFill fill, BackgroundComposition composition) =>
        fill.Kind switch
        {
            BackgroundKind.Image when !string.IsNullOrWhiteSpace(fill.ImagePath) && File.Exists(fill.ImagePath)
                => ImageBackground(fill.ImagePath!, fill.Blur, composition),
            BackgroundKind.Solid
                => SolidBackground(fill.Color, composition),
            _ => GradientBackground(fill.Gradient, composition),
        };

    /// <summary>Solid colour: the same everywhere, so only the final canvas size matters.</summary>
    private static MagickImage SolidBackground(BackgroundColor color, BackgroundComposition composition) =>
        new(new MagickColor(color.R, color.G, color.B),
            (uint)Math.Max(1, composition.CanvasWidth),
            (uint)Math.Max(1, composition.CanvasHeight));

    /// <summary>
    /// A one-pixel-wide column is enough for a top-to-bottom gradient; it is stretched to the padded
    /// background width so the visible part of the gradient matches the preview exactly.
    /// </summary>
    private static MagickImage GradientBackground(GradientPreset preset, BackgroundComposition composition)
    {
        var stops = AddBackground.Gradient(preset);
        var width = Math.Max(1, composition.BackgroundWidth);
        var height = Math.Max(1, composition.BackgroundHeight);

        var column = new byte[height * 3];
        for (var y = 0; y < height; y++)
        {
            var t = height == 1 ? 0 : y / (double)(height - 1);
            column[y * 3] = Mix(stops.Start.R, stops.End.R, t);
            column[y * 3 + 1] = Mix(stops.Start.G, stops.End.G, t);
            column[y * 3 + 2] = Mix(stops.Start.B, stops.End.B, t);
        }

        using var strip = new MagickImage();
        strip.ReadPixels(column, new PixelReadSettings(1, (uint)height, StorageType.Char, PixelMapping.RGB));
        strip.Resize((uint)width, (uint)height);

        var canvas = new MagickImage(MagickColors.Transparent,
            (uint)Math.Max(1, composition.CanvasWidth), (uint)Math.Max(1, composition.CanvasHeight));
        CompositeClipped(canvas, strip, composition.BackgroundX, composition.BackgroundY);
        return canvas;
    }

    /// <summary>The chosen image, scaled to cover the padded background and centred (never letterboxed).</summary>
    private static MagickImage ImageBackground(string path, bool blur, BackgroundComposition composition)
    {
        using var image = Load(path);
        var rect = AddBackground.CoverRect(
            (int)image.Width, (int)image.Height, composition.BackgroundWidth, composition.BackgroundHeight);

        image.Resize(new MagickGeometry((uint)Math.Max(1, rect.Width), (uint)Math.Max(1, rect.Height)));

        // The blur is relative to the canvas, like the preview's BlurEffect, so both look alike.
        if (blur)
            image.Blur(0, Math.Max(1.0, composition.BackgroundWidth * 0.008));

        var canvas = new MagickImage(MagickColors.Transparent,
            (uint)Math.Max(1, composition.CanvasWidth), (uint)Math.Max(1, composition.CanvasHeight));
        CompositeClipped(canvas, image, composition.BackgroundX + rect.X, composition.BackgroundY + rect.Y);
        return canvas;
    }

    private static byte Mix(byte from, byte to, double t) =>
        (byte)Math.Clamp((int)Math.Round(from + (to - from) * t), 0, 255);

    // ------------------------------------------------------------------ photo and shadow

    /// <summary>Keeps the source alpha and multiplies it by a rounded-rectangle mask.</summary>
    private static void RoundCorners(MagickImage image, int radius)
    {
        if (!image.HasAlpha)
            image.Alpha(AlphaAction.Set);
        if (radius <= 0)
            return;

        var width = (int)image.Width;
        var height = (int)image.Height;
        using var mask = new MagickImage(MagickColors.Transparent, (uint)width, (uint)height);
        var corner = Math.Min(radius, Math.Min(width, height) / 2);
        new Drawables()
            .FillColor(MagickColors.White)
            .RoundRectangle(0, 0, width - 1, height - 1, corner, corner)
            .Draw(mask);
        image.Composite(mask, CompositeOperator.DstIn);
    }

    /// <summary>
    /// The shadow is the photo's rounded rectangle, drawn black on transparency, blurred and made partly
    /// transparent; its canvas is only as big as the photo plus the blur, which keeps the render cheap.
    /// </summary>
    private static void DrawShadow(MagickImage canvas, BackgroundComposition composition)
    {
        if (composition.ShadowBlur <= 0 || composition.ShadowOpacity <= 0)
            return;

        var blur = composition.ShadowBlur;
        var margin = (int)Math.Ceiling(blur * 2.0) + Math.Abs(composition.ShadowOffsetY) + 2;
        var width = composition.ImageWidth + 2 * margin;
        var height = composition.ImageHeight + 2 * margin;
        using var shadow = new MagickImage(MagickColors.Transparent, (uint)width, (uint)height);

        var corner = Math.Min(composition.CornerRadius,
            Math.Min(composition.ImageWidth, composition.ImageHeight) / 2);
        var drawables = new Drawables().FillColor(MagickColors.Black);
        if (corner > 0)
            drawables.RoundRectangle(margin, margin,
                margin + composition.ImageWidth - 1, margin + composition.ImageHeight - 1, corner, corner);
        else
            drawables.Rectangle(margin, margin,
                margin + composition.ImageWidth - 1, margin + composition.ImageHeight - 1);
        drawables.Draw(shadow);

        shadow.Blur(0, Math.Max(0.3, blur / 2.0));
        shadow.Evaluate(Channels.Alpha, EvaluateOperator.Multiply, composition.ShadowOpacity);

        CompositeClipped(canvas, shadow,
            composition.ImageX - margin,
            composition.ImageY - margin + composition.ShadowOffsetY);
    }

    /// <summary>First frame only, EXIF orientation applied, never resampled here.</summary>
    private static MagickImage Load(string path)
    {
        var settings = new MagickReadSettings { FrameIndex = 0, FrameCount = 1 };
        var image = new MagickImage(path, settings);
        image.AutoOrient();
        return image;
    }
}
