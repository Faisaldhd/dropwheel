using FileAction.Core;
using FileAction.Core.Backends;
using ImageMagick;

namespace FileAction.App.UI.Tools.Image;

/// <summary>
/// One output-format policy for every image tool. Two input formats cannot be written back as
/// themselves by the bundled Magick.NET build: HEIC has an HEVC decoder but no encoder, and SVG is a
/// read-only vector input. Both are converted to a real raster container (JPEG for photos, PNG when
/// transparency is expected) instead of offering an edit that would always fail. Tools that keep an
/// animation call <see cref="FormatFor"/>; tools that flatten call <see cref="StillFormatFor"/>.
///
/// It also owns the two writer promises the tools share: a multi-frame GIF is written as a collection
/// (never silently reduced to frame 1) and an ICO output is clamped to 256 px.
/// </summary>
internal static class ImageOutput
{
    /// <summary>Windows ICO containers only accept frames up to 256 × 256.</summary>
    public const int IcoMaxSide = 256;

    /// <summary>JPEG/WebP quality for copies whose source did not choose one.</summary>
    public const int DefaultQuality = 92;

    /// <summary>The container an edit can write; HEIC always becomes a JPEG copy.</summary>
    public static string FormatFor(string inputFormat) => inputFormat switch
    {
        "heic" => "jpg",
        "svg" => "png",
        _ => inputFormat,
    };

    /// <summary>
    /// The container for a tool that flattens its input into one still image. GIF joins HEIC/SVG here
    /// because Annotate renders one WPF bitmap and cannot reproduce frame timing.
    /// </summary>
    public static string StillFormatFor(string inputFormat) => inputFormat switch
    {
        "heic" => "jpg",
        "gif" => "png",
        "svg" => "png",
        _ => inputFormat,
    };

    public static bool IsHeic(string format) => string.Equals(format, "heic", StringComparison.OrdinalIgnoreCase);

    public static bool IsGif(string format) => string.Equals(format, "gif", StringComparison.OrdinalIgnoreCase);

    /// <summary>Quality, alpha, JPEG and ICO rules shared by the single-image and animation writers.</summary>
    public static void PrepareForWrite(MagickImage image, string format, int quality)
    {
        image.Quality = (uint)Math.Clamp(quality, 1, 100);

        if (format is "jpg" or "bmp" && image.HasAlpha)
        {
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaAction.Remove);
        }

        if (format == "jpg")
        {
            image.Settings.SetDefine(MagickFormat.Jpeg, "sampling-factor", "4:2:0");
            image.Settings.Interlace = Interlace.Jpeg;
        }

        if (format == "ico")
        {
            if (image.Width > IcoMaxSide || image.Height > IcoMaxSide)
            {
                // Fit inside 256 × 256 without distorting the icon.
                image.Resize(new MagickGeometry((uint)IcoMaxSide, (uint)IcoMaxSide));
            }

            image.Settings.SetDefine(MagickFormat.Ico, "auto-resize", "256,128,64,48,32,16");
        }
    }

    /// <summary>Writes one image with an explicit container, never by the temporary file's extension.</summary>
    public static void Write(MagickImage image, string path, string format, int quality = DefaultQuality)
    {
        PrepareForWrite(image, format, quality);
        image.Write(path, ImageBackend.ToMagickFormat(format));
    }

    /// <summary>
    /// Writes every frame of an animation. A one-frame collection is saved like a still image; more
    /// than one frame is only valid for an animated container (GIF here).
    /// </summary>
    public static void WriteAnimation(MagickImageCollection frames, string path, string format, int quality = DefaultQuality)
    {
        foreach (var item in frames)
            PrepareForWrite((MagickImage)item, format, quality);

        if (frames.Count == 1)
            frames[0].Write(path, ImageBackend.ToMagickFormat(format));
        else
            frames.Write(path, ImageBackend.ToMagickFormat(format));
    }

    /// <summary>Atomic write through <see cref="OutputPaths"/>; the temporary file is removed on failure.</summary>
    public static Task<string> WriteAtomicAsync(string directory, string baseName, string extension, Action<string> write) =>
        OutputPaths.ProduceAsync(directory, baseName, extension, temporary => Task.Run(() => write(temporary)));

    /// <summary>Blocking form for runners that are already called from a worker thread.</summary>
    public static string WriteAtomic(string directory, string baseName, string extension, Action<string> write) =>
        WriteAtomicAsync(directory, baseName, extension, write).GetAwaiter().GetResult();
}
