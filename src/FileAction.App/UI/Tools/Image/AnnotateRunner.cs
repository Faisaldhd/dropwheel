using System.IO;
using System.Windows.Media.Imaging;
using FileAction.Core;
using ImageMagick;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Image;

/// <summary>
/// Reads the photo for the Annotate tool and writes the flattened result. The preview is downsampled;
/// the export is rendered again at the original pixel size, so annotation coordinates never depend on
/// the window (they are always original image pixels).
/// </summary>
internal static class AnnotateRunner
{
    /// <summary>Longest side of the preview bitmap; the document keeps the original pixel size.</summary>
    public const int PreviewMaxSide = 1600;

    /// <summary>
    /// Decodes the image with EXIF orientation applied and returns the oriented pixel size. A positive
    /// <paramref name="maxSide"/> downsamples the bitmap, while the returned size stays the original one.
    /// </summary>
    public static (BitmapSource Bitmap, int Width, int Height) Load(string path, int maxSide)
    {
        using var image = new MagickImage(path);
        image.AutoOrient();
        var width = (int)image.Width;
        var height = (int)image.Height;
        if (maxSide > 0)
        {
            var longSide = Math.Max(width, height);
            if (longSide > maxSide)
            {
                var scale = (double)maxSide / longSide;
                image.Resize(new MagickGeometry(
                    (uint)Math.Max(1, (int)Math.Round(width * scale)),
                    (uint)Math.Max(1, (int)Math.Round(height * scale))));
            }
        }

        using var stream = new MemoryStream(image.ToByteArray(MagickFormat.Png));
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return (bitmap, width, height);
    }

    /// <summary>
    /// Renders the annotated image at its original size and writes it next to the source (or into the
    /// drop folder). Must be started on the UI thread: the WPF render needs the dispatcher, while the
    /// decode and the encode run on the thread pool.
    /// </summary>
    public static async Task<string?> ApplyAsync(
        FileItem item, AnnotationDocument document, IReadOnlyList<BitmapSource> stamps, string outputDirectory)
    {
        if (document.Count == 0)
            return null;

        var (full, _, _) = await Task.Run(() => Load(item.Path, 0));

        RenderTargetBitmap rendered;
        try
        {
            rendered = AnnotationRenderer.RenderOriginalResolution(document, full, stamps);
        }
        catch (Exception ex) when (ex is OutOfMemoryException or OverflowException or ArgumentException or InvalidOperationException)
        {
            // A pathological image size is reported like any other image-processing failure.
            throw new ConversionException(T("error.image.process", ex.Message));
        }

        // Annotate renders one still bitmap, so an animated GIF becomes an explicit PNG still instead of
        // a GIF with only frame 1 left. The view shows that conversion as a note before Apply.
        var format = ImageOutput.StillFormatFor(Formats.Normalize(item.Format));
        var baseName = $"{item.BaseName} {T("tool.annotate.suffix")}";
        return await ImageOutput.WriteAtomicAsync(outputDirectory, baseName, format, target => Task.Run(() =>
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rendered));
            using var png = new MemoryStream();
            encoder.Save(png);
            png.Position = 0;

            // The PNG render is written back as JPG/WebP/… by explicit format, never by the temp name.
            using var image = new MagickImage(png);
            ImageOutput.Write(image, target, format);
        }));
    }
}
