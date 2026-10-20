using System.IO;
using FileAction.Core;
using ImageMagick;
using ImageMagick.Drawing;

namespace FileAction.App.UI.Tools.Collage;

/// <summary>
/// Draws the collage at the output resolution from the original files (never from the preview), then
/// writes it into the resolved output folder as <c>Collage.png</c> or <c>Collage.jpg</c>. The file name is
/// unique via <see cref="OutputPaths.ProduceAsync"/>, so an existing collage is never overwritten.
/// </summary>
internal static class CollageRenderer
{
    /// <summary>Suffix of the temporary file while the image is being written.</summary>
    private const string BaseName = "Collage";

    /// <summary>JPEG quality when every input is a JPEG; PNG inputs stay lossless.</summary>
    private const int JpegQuality = 92;

    public static Task<string> RenderAsync(IReadOnlyList<string> paths, CollageOptions options, string directory)
    {
        if (paths.Count == 0)
            throw new ArgumentException("A collage needs at least one image.", nameof(paths));
        var extension = ExtensionFor(paths);
        return OutputPaths.ProduceAsync(directory, BaseName, extension, temp => Task.Run(() => Write(paths, options, temp)));
    }

    /// <summary>The output is JPEG only when every source is a JPEG; otherwise PNG keeps the source quality.</summary>
    public static string ExtensionFor(IReadOnlyList<string> paths) =>
        paths.All(path => string.Equals(Formats.KeyOf(path), "jpg", StringComparison.OrdinalIgnoreCase)) ? "jpg" : "png";

    private static void Write(IReadOnlyList<string> paths, CollageOptions options, string target)
    {
        var size = CollagePresets.Clamp(options.Size.Width, options.Size.Height);
        var cells = CollageMath.Cells(paths.Count, options.Layout, size.Width, size.Height, options.Spacing);

        using var canvas = new MagickImage(ToMagick(options.Background.Start), (uint)size.Width, (uint)size.Height);
        PaintBackground(canvas, options.Background, size);

        for (var i = 0; i < paths.Count; i++)
        {
            var cell = cells[i];
            using var source = new MagickImage(paths[i]);
            source.AutoOrient();
            var placement = CollageMath.Place(cell, (int)source.Width, (int)source.Height, options.Fit);

            var sourceX = Clamp((int)Math.Round(placement.Source.X), 0, Math.Max(0, (int)source.Width - 1));
            var sourceY = Clamp((int)Math.Round(placement.Source.Y), 0, Math.Max(0, (int)source.Height - 1));
            var sourceWidth = Clamp((int)Math.Round(placement.Source.Width), 1, Math.Max(1, (int)source.Width - sourceX));
            var sourceHeight = Clamp((int)Math.Round(placement.Source.Height), 1, Math.Max(1, (int)source.Height - sourceY));
            source.Crop(new MagickGeometry(sourceX, sourceY, (uint)sourceWidth, (uint)sourceHeight));

            var targetWidth = Math.Max(1, (int)Math.Round(placement.Image.Width));
            var targetHeight = Math.Max(1, (int)Math.Round(placement.Image.Height));
            source.Resize(new MagickGeometry((uint)targetWidth, (uint)targetHeight));

            var radius = CollageMath.CornerRadius(cell.Bounds, options.Radius);
            if (radius >= 0.5)
                RoundCorners(source, radius);

            var left = Clamp((int)Math.Round(placement.Image.X), 0, Math.Max(0, size.Width - 1));
            var top = Clamp((int)Math.Round(placement.Image.Y), 0, Math.Max(0, size.Height - 1));
            canvas.Composite(source, left, top, CompositeOperator.Over);
        }

        if (string.Equals(Path.GetExtension(target).TrimStart('.'), "jpg", StringComparison.OrdinalIgnoreCase))
            canvas.Quality = JpegQuality;
        canvas.Write(target);
    }

    /// <summary>A vertical gradient is drawn as thin bands, so no gradient-specific Magick API is needed.</summary>
    private static void PaintBackground(MagickImage canvas, CollageBackground background, CollageSize size)
    {
        if (background.Kind == CollageBackgroundKind.Solid || background.Start == background.End)
            return;

        var bands = Math.Clamp(size.Height / 2, 2, 512);
        var draw = new Drawables();
        for (var band = 0; band < bands; band++)
        {
            var t = band / (double)(bands - 1);
            var colour = background.Start.Mix(background.End, t);
            var top = (int)((long)band * size.Height / bands);
            var bottom = (int)((long)(band + 1) * size.Height / bands) - 1;
            draw.FillColor(ToMagick(colour));
            draw.Rectangle(0, top, size.Width - 1, Math.Max(top, bottom));
        }
        draw.Draw(canvas);
    }

    /// <summary>
    /// Rounds the corners with a transparency mask: an opaque white rounded rectangle on a transparent
    /// canvas, copied into the image's alpha channel, so the collage background shows through the corners.
    /// </summary>
    private static void RoundCorners(MagickImage image, double radius)
    {
        var width = (uint)Math.Max(1, (int)image.Width);
        var height = (uint)Math.Max(1, (int)image.Height);
        using var mask = new MagickImage(MagickColors.Transparent, width, height);
        new Drawables()
            .FillColor(MagickColors.White)
            .RoundRectangle(0, 0, width - 1, height - 1, radius, radius)
            .Draw(mask);
        image.Alpha(AlphaAction.Set);
        image.Composite(mask, CompositeOperator.CopyAlpha);
    }

    private static MagickColor ToMagick(CollageColor colour) => new(colour.R, colour.G, colour.B);

    private static int Clamp(int value, int min, int max) => Math.Clamp(value, min, Math.Max(min, max));
}
