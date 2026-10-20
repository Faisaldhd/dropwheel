using System.IO;
using System.Windows.Media.Imaging;
using FileAction.Core;
using ImageMagick;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Image;

/// <summary>
/// Applies a redaction plan with ImageMagick, both for the downsampled preview and for the saved file.
/// The saved file is flattened at the source image's own resolution, metadata (EXIF, GPS, profiles and the
/// embedded thumbnail) is stripped, and the result is written to a unique name next to the source.
/// </summary>
internal static class RedactRunner
{
    /// <summary>JPEG/WebP quality for the copy; PNG ignores it.</summary>
    private const uint OutputQuality = 92;

    /// <summary>Longest preview side, so a 100 MP photo does not hold a full-size bitmap in the window.</summary>
    public const int PreviewMaxSide = 2048;

    /// <summary>
    /// Reads the image with EXIF orientation applied and returns the original pixel size plus a downsampled
    /// PNG preview. The PNG bytes are reused for OCR and for re-rendering the effect preview.
    /// </summary>
    public static (BitmapSource Bitmap, byte[] Png, int Width, int Height) LoadPreview(string path)
    {
        using var image = new MagickImage(path);
        image.AutoOrient();
        var width = (int)image.Width;
        var height = (int)image.Height;

        var longSide = Math.Max(width, height);
        if (longSide > PreviewMaxSide)
        {
            var ratio = (double)PreviewMaxSide / longSide;
            image.Resize(new MagickGeometry(
                (uint)Math.Max(1, width * ratio),
                (uint)Math.Max(1, height * ratio)));
        }

        var png = image.ToByteArray(MagickFormat.Png);
        using var stream = new MemoryStream(png);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return (bitmap, png, width, height);
    }

    /// <summary>Flattens <paramref name="plan"/> into a new file and returns its path.</summary>
    public static string Apply(FileItem item, IReadOnlyList<RedactionStep> plan, string outputDirectory)
    {
        using var image = new MagickImage(item.Path);
        image.AutoOrient();
        Paint(image, plan);

        // No EXIF, GPS, comments, colour profiles or embedded thumbnail survive a redaction.
        image.Strip();
        image.Quality = OutputQuality;

        var extension = Path.GetExtension(item.Path).TrimStart('.');
        var target = OutputPaths.Unique(outputDirectory, $"{item.BaseName} {T("tool.redact.suffix")}", extension);
        image.Write(target);
        return target;
    }

    /// <summary>Renders the same plan over the downsampled preview and returns a frozen bitmap.</summary>
    public static BitmapSource RenderPreview(byte[] png, IReadOnlyList<RedactionStep> plan)
    {
        using var image = new MagickImage(png);
        Paint(image, plan);

        using var stream = new MemoryStream(image.ToByteArray(MagickFormat.Png));
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Paints every step in paint order, so a later area covers an earlier one.</summary>
    private static void Paint(MagickImage image, IReadOnlyList<RedactionStep> plan)
    {
        foreach (var step in plan)
        {
            if (step.X < 0 || step.Y < 0 || step.Width < 1 || step.Height < 1)
                continue;
            if (step.X >= image.Width || step.Y >= image.Height)
                continue;

            var width = Math.Min((uint)step.Width, image.Width - (uint)step.X);
            var height = Math.Min((uint)step.Height, image.Height - (uint)step.Y);
            if (width == 0 || height == 0)
                continue;

            switch (step.Mode)
            {
                case RedactionMode.Solid:
                    Solid(image, step, width, height);
                    break;
                case RedactionMode.Blur:
                    Blur(image, step, width, height);
                    break;
                case RedactionMode.Pixelate:
                    Pixelate(image, step, width, height);
                    break;
            }
        }
    }

    private static void Solid(MagickImage image, RedactionStep step, uint width, uint height)
    {
        var (a, r, g, b) = RedactionColors.Split(step.Color);
        using var patch = new MagickImage(new MagickColor(r, g, b, a), width, height);
        image.Composite(patch, step.X, step.Y, CompositeOperator.Copy);
    }

    private static void Blur(MagickImage image, RedactionStep step, uint width, uint height)
    {
        var sigma = Math.Max(0.1, step.BlurSigma);

        // Blur a padded copy so the edge of the area does not pull in transparent pixels, then keep only
        // the area itself: the pixels outside the box must not change.
        var padding = (int)Math.Ceiling(sigma * 3);
        var x = Math.Max(0, step.X - padding);
        var y = Math.Max(0, step.Y - padding);
        var right = (int)Math.Min(image.Width, (uint)Math.Min(int.MaxValue, step.X + step.Width + padding));
        var bottom = (int)Math.Min(image.Height, (uint)Math.Min(int.MaxValue, step.Y + step.Height + padding));
        var outerWidth = (uint)Math.Max(1, right - x);
        var outerHeight = (uint)Math.Max(1, bottom - y);

        using var region = new MagickImage(image.CloneArea(x, y, outerWidth, outerHeight));
        region.Blur(0, sigma);
        region.Crop(new MagickGeometry(step.X - x, step.Y - y, width, height));
        region.ResetPage();
        image.Composite(region, step.X, step.Y, CompositeOperator.Copy);
    }

    private static void Pixelate(MagickImage image, RedactionStep step, uint width, uint height)
    {
        var block = (uint)Math.Max(1, step.PixelBlock);
        using var region = new MagickImage(image.CloneArea(step.X, step.Y, width, height));

        // Downscale to one sample per block, then blow it back up with nearest-neighbour sampling.
        region.Scale(Math.Max(1u, width / block), Math.Max(1u, height / block));
        region.Resize(width, height, FilterType.Point);
        region.ResetPage();
        image.Composite(region, step.X, step.Y, CompositeOperator.Copy);
    }
}
