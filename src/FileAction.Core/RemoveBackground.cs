using FileAction.Core.Backends;
using ImageMagick;

namespace FileAction.Core;

/// <summary>
/// Colour-key background removal without a window: the background colour is sampled from a ring of
/// corner and edge patches, pixels within the fuzz tolerance become transparent, and the band just
/// inside the tolerance is softened so the cut-out does not get a hard, jagged rim.
///
/// Only the alpha channel is written. The red, green and blue values of every pixel — including the
/// fully transparent ones — stay exactly as they were, which is what lets a later matte or the
/// Add Background tool blend the cut-out without a coloured fringe.
/// </summary>
public static class RemoveBackground
{
    /// <summary>Lowest accepted fuzz; only the exact sampled colour becomes transparent.</summary>
    public const int MinFuzz = 0;

    /// <summary>Highest accepted fuzz; the tolerance covers the whole RGB cube.</summary>
    public const int MaxFuzz = 100;

    /// <summary>A background that differs slightly from the sampled colour is still removed.</summary>
    public const int DefaultFuzz = 20;

    /// <summary>No soft ramp: the tolerance edge is a hard alpha cut.</summary>
    public const int MinFeather = 0;

    /// <summary>The whole tolerance band is a smooth alpha ramp.</summary>
    public const int MaxFeather = 100;

    /// <summary>The shipped edge softness, a little over a third of the tolerance band.</summary>
    public const int DefaultFeather = 35;

    /// <summary>The default result: PNG is lossless and always keeps the alpha channel.</summary>
    public const string DefaultFormat = "png";

    /// <summary>
    /// Share of the fuzz tolerance used for the soft alpha ramp at <see cref="DefaultFeather"/>. At fuzz 0
    /// this is zero, so an exact key match alone is removed and every other pixel is left untouched.
    /// </summary>
    public const double FeatherFraction = DefaultFeather / 100.0;

    /// <summary>Largest possible channel difference, i.e. the tolerance at <see cref="MaxFuzz"/>.</summary>
    public const double MaxDistance = 255.0;

    /// <summary>Output formats that can carry the alpha channel this tool writes.</summary>
    public static IReadOnlyList<string> AlphaOutputs => Formats.AlphaImageOutputs;

    /// <summary>The fuzz value clamped into its declared 0–100 range.</summary>
    public static int ClampFuzz(int fuzz) => Math.Clamp(fuzz, MinFuzz, MaxFuzz);

    /// <summary>The feather value clamped into its declared 0–100 range.</summary>
    public static int ClampFeather(int feather) => Math.Clamp(feather, MinFeather, MaxFeather);

    /// <summary>Colour distance accepted at <paramref name="fuzz"/>, in the same 0–255 scale as <see cref="Distance"/>.</summary>
    public static double Tolerance(int fuzz) => ClampFuzz(fuzz) / 100.0 * MaxDistance;

    /// <summary>
    /// Distance between a pixel and the sampled background, normalised so black against white is
    /// <see cref="MaxDistance"/>. The root-mean-square keeps all three channels equally important.
    /// </summary>
    public static double Distance(BackgroundColor key, byte r, byte g, byte b)
    {
        var dr = r - key.R;
        var dg = g - key.G;
        var db = b - key.B;
        return Math.Sqrt((dr * dr + dg * dg + db * db) / 3.0);
    }

    /// <summary>
    /// Alpha for a colour at <paramref name="distance"/> from the key. The value is 0 up to the
    /// feathered inner edge, rises smoothly to 1 at the tolerance and stays 1 beyond it. A fuzz of 0
    /// deliberately has no feather: only <paramref name="distance"/> 0 is removed.
    /// </summary>
    public static byte AlphaFor(double distance, int fuzz) =>
        AlphaFor(distance, fuzz, DefaultFeather);

    /// <summary>
    /// Alpha for one pixel at an explicit feather percentage. Feather 0 is a hard cut at the tolerance;
    /// higher values start the soft ramp earlier inside the band, up to the whole band at 100.
    /// </summary>
    public static byte AlphaFor(double distance, int fuzz, int feather)
    {
        var tolerance = Tolerance(fuzz);
        if (tolerance <= 0)
            return distance <= 0 ? (byte)0 : (byte)255;
        if (distance >= tolerance)
            return 255;
        return AlphaForDistance(distance, tolerance, feather);
    }

    /// <summary>Alpha for one pixel; see <see cref="AlphaFor(double, int)"/>.</summary>
    public static byte AlphaFor(BackgroundColor key, byte r, byte g, byte b, int fuzz) =>
        AlphaFor(key, r, g, b, fuzz, DefaultFeather);

    /// <summary>Alpha for one pixel at an explicit feather percentage.</summary>
    public static byte AlphaFor(BackgroundColor key, byte r, byte g, byte b, int fuzz, int feather) =>
        AlphaFor(Distance(key, r, g, b), fuzz, feather);

    /// <summary>
    /// Samples the background from four corner patches and four edge-midpoint patches. The median of
    /// the samples is used per channel, so one corner covered by the subject does not move the key.
    /// </summary>
    public static BackgroundColor Sample(MagickImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Width == 0 || image.Height == 0)
            return new BackgroundColor(0, 0, 0);

        var (reds, greens, blues) = BorderSamples(image, opaqueOnly: true);
        if (reds.Count == 0)
            (reds, greens, blues) = BorderSamples(image, opaqueOnly: false);
        if (reds.Count == 0)
            return new BackgroundColor(0, 0, 0);

        return new BackgroundColor(Median(reds), Median(greens), Median(blues));
    }

    /// <summary>Removes the sampled background from <paramref name="image"/> in place.</summary>
    public static void Apply(MagickImage image, int fuzz = DefaultFuzz)
    {
        ArgumentNullException.ThrowIfNull(image);
        Apply(image, fuzz, Sample(image), DefaultFeather);
    }

    /// <summary>Removes <paramref name="key"/> from <paramref name="image"/> in place.</summary>
    public static void Apply(MagickImage image, int fuzz, BackgroundColor key) =>
        Apply(image, fuzz, key, DefaultFeather);

    /// <summary>Removes <paramref name="key"/> in place with an explicit edge feather.</summary>
    public static void Apply(MagickImage image, int fuzz, BackgroundColor key, int feather)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Width == 0 || image.Height == 0)
            return;

        // Channel operations need a plain RGB(A) image. Grey and CMYK sources would otherwise expose
        // a single colour channel and receive four-byte RGBA rows in the wrong order.
        if (image.ColorSpace is ColorSpace.Gray or ColorSpace.CMYK)
            image.ColorSpace = ColorSpace.sRGB;
        image.ColorType = image.HasAlpha ? ColorType.TrueColorAlpha : ColorType.TrueColor;
        image.Alpha(AlphaAction.Set);

        using var pixels = image.GetPixels();
        var data = pixels.ToByteArray(PixelMapping.RGBA);
        if (data is null || data.Length < 4)
            return;
        var tolerance = Tolerance(fuzz);
        var toleranceSquared = tolerance * tolerance;
        var softness = ClampFeather(feather);
        for (var offset = 0; offset + 3 < data.Length; offset += 4)
        {
            var dr = data[offset] - key.R;
            var dg = data[offset + 1] - key.G;
            var db = data[offset + 2] - key.B;
            var squared = (dr * dr + dg * dg + db * db) / 3.0;

            // Every pixel outside the tolerance is left fully opaque, and the RGB bytes are never
            // touched. The square root is only needed for the narrow feathered band. Existing
            // transparency is kept: removal may add transparency, never make a clear pixel opaque.
            var keyed = tolerance <= 0
                ? squared == 0 ? (byte)0 : (byte)255
                : squared >= toleranceSquared ? (byte)255 : AlphaForDistance(Math.Sqrt(squared), tolerance, softness);
            data[offset + 3] = Math.Min(data[offset + 3], keyed);
        }
        pixels.SetBytePixels(data);
    }

    /// <summary>
    /// Removes the background and writes a new file through <see cref="OutputPaths.ProduceAsync"/>, so
    /// a failed run leaves no partial output and an existing file is never overwritten. The source is
    /// only read.
    /// </summary>
    /// <param name="input">Source image.</param>
    /// <param name="outputDirectory">Folder for the result; created when needed.</param>
    /// <param name="baseName">Result file name without the extension.</param>
    /// <param name="format">An alpha-capable output format; defaults to PNG.</param>
    /// <param name="fuzz">Tolerance, 0–100.</param>
    /// <param name="feather">Edge softness, 0–100.</param>
    /// <param name="ct">Cancellation for the decode, the removal and the encode.</param>
    public static Task<string> SaveAsync(
        string input,
        string outputDirectory,
        string baseName,
        string format = DefaultFormat,
        int fuzz = DefaultFuzz,
        int feather = DefaultFeather,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(input);
        ArgumentException.ThrowIfNullOrEmpty(outputDirectory);
        ArgumentException.ThrowIfNullOrEmpty(baseName);

        var outputFormat = RequireAlphaOutput(format);
        var value = ClampFuzz(fuzz);
        var softness = ClampFeather(feather);
        return OutputPaths.ProduceAsync(outputDirectory, baseName, outputFormat, temp => Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var image = ImageBackend.Load(input);
            Apply(image, value, Sample(image), softness);
            ct.ThrowIfCancellationRequested();
            ImageBackend.Save(image, temp, outputFormat);
        }, ct));
    }

    /// <summary>
    /// The normalised output format, or a <see cref="ConversionException"/> when the format cannot store
    /// transparency. JPEG and BMP are refused rather than silently flattened to white; PDF is not offered
    /// by this tool either.
    /// </summary>
    public static string RequireAlphaOutput(string format)
    {
        ArgumentException.ThrowIfNullOrEmpty(format);
        var normalized = Formats.Normalize(format);
        if (!Formats.SupportsAlpha(normalized))
        {
            var allowed = string.Join(", ", Formats.AlphaImageOutputs.Select(Formats.Display));
            throw new ConversionException(
                $"Background removal needs an output format with an alpha channel; {Formats.Display(normalized)} cannot store transparency. " +
                $"Save as {allowed} instead.");
        }
        return normalized;
    }

    /// <summary>Alpha from a distance already known to be inside <paramref name="tolerance"/>.</summary>
    private static byte AlphaForDistance(double distance, double tolerance, int feather)
    {
        var inner = tolerance * (1 - ClampFeather(feather) / 100.0);
        if (distance <= inner)
            return 0;

        var t = Math.Clamp((distance - inner) / (tolerance - inner), 0, 1);
        var smooth = t * t * (3 - 2 * t);
        return (byte)Math.Round(smooth * 255, MidpointRounding.AwayFromZero);
    }

    /// <summary>Reads the eight border patches and returns the per-channel samples.</summary>
    private static (List<byte> Reds, List<byte> Greens, List<byte> Blues) BorderSamples(MagickImage image, bool opaqueOnly)
    {
        var reds = new List<byte>();
        var greens = new List<byte>();
        var blues = new List<byte>();
        var width = (int)image.Width;
        var height = (int)image.Height;
        var side = Math.Clamp(Math.Min(width, height) / 10, 1, 32);
        var patchWidth = Math.Min(side, width);
        var patchHeight = Math.Min(side, height);
        var right = width - patchWidth;
        var bottom = height - patchHeight;
        var centreX = (width - patchWidth) / 2;
        var centreY = (height - patchHeight) / 2;

        (int X, int Y, int Width, int Height)[] patches =
        [
            (0, 0, patchWidth, patchHeight),
            (right, 0, patchWidth, patchHeight),
            (0, bottom, patchWidth, patchHeight),
            (right, bottom, patchWidth, patchHeight),
            (centreX, 0, patchWidth, patchHeight),
            (centreX, bottom, patchWidth, patchHeight),
            (0, centreY, patchWidth, patchHeight),
            (right, centreY, patchWidth, patchHeight),
        ];

        using var pixels = image.GetPixels();
        var red = pixels.GetChannelIndex(PixelChannel.Red) ?? 0;
        var green = pixels.GetChannelIndex(PixelChannel.Green) ?? 1;
        var blue = pixels.GetChannelIndex(PixelChannel.Blue) ?? 2;
        var alpha = image.HasAlpha ? pixels.GetChannelIndex(PixelChannel.Alpha) : null;

        foreach (var patch in patches)
        {
            for (var y = patch.Y; y < patch.Y + patch.Height; y++)
            {
                for (var x = patch.X; x < patch.X + patch.Width; x++)
                {
                    var pixel = pixels.GetPixel(x, y);
                    if (opaqueOnly && alpha is { } alphaIndex && pixel.GetChannel(alphaIndex) < 128)
                        continue;
                    reds.Add(pixel.GetChannel(red));
                    greens.Add(pixel.GetChannel(green));
                    blues.Add(pixel.GetChannel(blue));
                }
            }
        }
        return (reds, greens, blues);
    }

    /// <summary>Middle value; for an even count the two middle values are averaged.</summary>
    private static byte Median(List<byte> values)
    {
        values.Sort();
        var middle = values.Count / 2;
        if (values.Count % 2 == 1)
            return values[middle];
        return (byte)((values[middle - 1] + values[middle] + 1) / 2);
    }
}
