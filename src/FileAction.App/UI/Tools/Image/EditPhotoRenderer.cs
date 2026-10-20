using System.IO;
using System.Windows.Media.Imaging;
using FileAction.Core;
using FileAction.Core.Backends;
using ImageMagick;

namespace FileAction.App.UI.Tools.Image;

/// <summary>
/// The image work behind the editor: decode one downsampled copy for the live preview, run the same
/// <see cref="PhotoEdit.Apply"/> on it while a slider moves, and on the original when Apply is pressed.
/// Nothing here touches the UI thread's controls, so the callers run it inside <c>Task.Run</c>.
/// </summary>
internal static class EditPhotoRenderer
{
    /// <summary>The preview bitmap, the PNG it was decoded from, and the original pixel size.</summary>
    /// <param name="Original">The untouched downsampled photo, used for before/after comparison.</param>
    /// <param name="PreviewBytes">A PNG of the same downsampled photo, re-read for every live render.</param>
    /// <param name="Width">The original width in pixels (the window is sized from this, not the preview).</param>
    /// <param name="Height">The original height in pixels.</param>
    internal sealed record LoadedPhoto(BitmapSource Original, byte[] PreviewBytes, int Width, int Height);

    /// <summary>Reads the photo, applies EXIF orientation and keeps a preview of at most 1200 px.</summary>
    public static LoadedPhoto Load(string path)
    {
        using var image = new MagickImage(path);
        Prepare(image);
        var width = (int)image.Width;
        var height = (int)image.Height;
        var longSide = Math.Max(width, height);
        if (longSide > PhotoEdit.PreviewMaxSide)
        {
            var scale = (double)PhotoEdit.PreviewMaxSide / longSide;
            image.Resize(new MagickGeometry((uint)Math.Max(1, width * scale), (uint)Math.Max(1, height * scale)));
        }

        var bytes = image.ToByteArray(MagickFormat.Png);
        return new LoadedPhoto(ToBitmap(bytes), bytes, width, height);
    }

    /// <summary>Edits the downsampled copy with exactly the parameters the final render will use.</summary>
    public static BitmapSource RenderPreview(byte[] previewBytes, PhotoParameters parameters)
    {
        using var image = new MagickImage(previewBytes);
        PhotoEdit.Apply(image, parameters);
        // BMP is lossless and uncompressed: a noisy (grain) preview would cost hundreds of milliseconds
        // to PNG-encode on every slider move, and WPF decodes BMP instantly.
        return ToBitmap(image.ToByteArray(MagickFormat.Bmp));
    }

    /// <summary>
    /// Edits the original at full resolution and writes a new file next to the source (or into the chosen
    /// drop folder). The source file is only read; an existing file is never overwritten.
    ///
    /// With every slider at its default there is nothing to render, so the source bytes are copied
    /// verbatim: the result is byte-identical (no recompression, no orientation rewrite) and instant.
    /// </summary>
    public static string RenderFinal(FileItem item, PhotoParameters parameters, string suffix, string outputDirectory)
    {
        var extension = Path.GetExtension(item.Path).TrimStart('.');
        var target = OutputPaths.Unique(outputDirectory, $"{item.BaseName} {suffix}", extension);
        if (PhotoEdit.IsNeutral(parameters))
        {
            File.Copy(item.Path, target);
            return target;
        }

        using var image = new MagickImage(item.Path);
        Prepare(image);
        PhotoEdit.Apply(image, parameters);
        // Write by the normalized format, not by the file name: a ".jfif" source is still a JPEG.
        image.Write(target, ImageBackend.ToMagickFormat(item.Format));
        return target;
    }

    /// <summary>
    /// Orients the pixels and moves the image to 8-bit direct colour. Palette PNGs and grey images
    /// otherwise reach the channel operations as one indexed channel, which a tone curve cannot address.
    /// </summary>
    private static void Prepare(MagickImage image)
    {
        image.AutoOrient();
        image.ColorType = image.HasAlpha ? ColorType.TrueColorAlpha : ColorType.TrueColor;
    }

    /// <summary>Decodes bytes to a frozen bitmap, so it can be handed to the UI thread from a worker.</summary>
    private static BitmapSource ToBitmap(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
