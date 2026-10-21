using FileAction.Core;
using ImageMagick;
using ImageMagick.Drawing;

namespace FileAction.Tests;

/// <summary>
/// The pure half of colour-key background removal: the sampled corner/edge key, the fuzz tolerance,
/// the soft alpha ramp and the output formats that can actually store transparency.
/// </summary>
public class RemoveBackgroundTests
{
    [Fact]
    public void Distance_is_normalized_so_black_against_white_is_255()
    {
        var black = new BackgroundColor(0, 0, 0);
        var white = new BackgroundColor(255, 255, 255);

        Assert.Equal(0, RemoveBackground.Distance(black, 0, 0, 0));
        Assert.Equal(255, RemoveBackground.Distance(black, 255, 255, 255), 6);
        Assert.Equal(255, RemoveBackground.Distance(white, 0, 0, 0), 6);
        Assert.Equal(0, RemoveBackground.Distance(white, 255, 255, 255));
    }

    [Fact]
    public void Fuzz_is_clamped_to_its_declared_range()
    {
        Assert.Equal(0, RemoveBackground.ClampFuzz(-100));
        Assert.Equal(0, RemoveBackground.ClampFuzz(0));
        Assert.Equal(50, RemoveBackground.ClampFuzz(50));
        Assert.Equal(100, RemoveBackground.ClampFuzz(100));
        Assert.Equal(100, RemoveBackground.ClampFuzz(500));

        Assert.Equal(0, RemoveBackground.Tolerance(0));
        Assert.Equal(255, RemoveBackground.Tolerance(100), 6);
    }

    [Fact]
    public void Fuzz_zero_removes_only_the_exact_key_colour()
    {
        var key = new BackgroundColor(20, 30, 40);

        Assert.Equal(0, RemoveBackground.AlphaFor(key, 20, 30, 40, 0));
        Assert.Equal(255, RemoveBackground.AlphaFor(key, 21, 30, 40, 0));
        Assert.Equal(255, RemoveBackground.AlphaFor(key, 0, 0, 0, 0));
        Assert.Equal(255, RemoveBackground.AlphaFor(key, 255, 255, 255, 0));
    }

    [Fact]
    public void The_feather_ramp_rises_smoothly_to_the_fuzz_tolerance()
    {
        const int fuzz = 40;
        var tolerance = RemoveBackground.Tolerance(fuzz);
        var inner = tolerance * (1 - RemoveBackground.FeatherFraction);

        Assert.Equal(0, RemoveBackground.AlphaFor(0, fuzz));
        Assert.Equal(0, RemoveBackground.AlphaFor(inner, fuzz));
        Assert.Equal(255, RemoveBackground.AlphaFor(tolerance, fuzz));
        Assert.Equal(255, RemoveBackground.AlphaFor(255, fuzz));

        // Somewhere in the band the result is partial rather than the hard 0/255 cut.
        Assert.InRange(RemoveBackground.AlphaFor((inner + tolerance) / 2, fuzz), 1, 254);

        var previous = -1;
        for (var step = 0; step <= 100; step++)
        {
            var alpha = RemoveBackground.AlphaFor(tolerance * step / 100.0, fuzz);
            Assert.True(alpha >= previous, $"alpha fell from {previous} to {alpha} at step {step}");
            previous = alpha;
        }
    }

    [Fact]
    public void Sample_reads_the_border_and_ignores_the_subject()
    {
        using var image = new MagickImage(new MagickColor(17, 34, 51), 120, 120);
        image.Draw(new Drawables().FillColor(MagickColors.Red).Rectangle(0, 0, 29, 29));
        image.Draw(new Drawables().FillColor(MagickColors.Blue).Rectangle(45, 45, 74, 74));

        Assert.Equal(new BackgroundColor(17, 34, 51), RemoveBackground.Sample(image));
    }

    [Fact]
    public void Only_alpha_capable_outputs_can_be_saved()
    {
        Assert.Equal(["png", "webp", "avif", "tiff", "ico"], Formats.AlphaImageOutputs);

        foreach (var format in Formats.AlphaImageOutputs)
        {
            Assert.True(Formats.SupportsAlpha(format));
            Assert.True(Formats.SupportsAlpha(format.ToUpperInvariant()));
            Assert.Equal(format, RemoveBackground.RequireAlphaOutput(format.ToUpperInvariant()));
        }

        foreach (var format in new[] { "jpg", "jpeg", "bmp", "pdf", "gif", "heic" })
        {
            Assert.False(Formats.SupportsAlpha(format));
            var error = Assert.Throws<ConversionException>(() => RemoveBackground.RequireAlphaOutput(format));
            Assert.Contains("alpha channel", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(Formats.Display(Formats.Normalize(format)), error.Message);
        }
    }
}

/// <summary>
/// End-to-end removal, using real files: a transparent corner, a feathered edge, the untouched source
/// and the five alpha-capable output formats.
/// </summary>
public sealed class RemoveBackgroundFileTests : IDisposable
{
    private static readonly MagickColor Background = new(255, 255, 255);
    private static readonly MagickColor Subject = new(220, 20, 30);

    private readonly string _directory = OutputPaths.TempDirectory();

    public void Dispose() => OutputPaths.TryDelete(_directory);

    [Fact]
    public void An_opaque_image_is_unchanged_at_fuzz_zero_when_no_pixel_matches_the_key()
    {
        // The eight border patches are red, green and blue; the per-channel medians make a key of
        // black, a colour that no pixel has. Fuzz 0 must therefore leave every pixel alone.
        using var image = new MagickImage();
        var pixels = new byte[100 * 100 * 4];
        for (var i = 0; i < 100 * 100; i++)
        {
            var offset = i * 4;
            pixels[offset] = 200;
            pixels[offset + 1] = 200;
            pixels[offset + 2] = 200;
            pixels[offset + 3] = 255;
        }

        void Paint(int x, int y, byte red, byte green, byte blue)
        {
            for (var py = y; py < y + 10; py++)
            {
                for (var px = x; px < x + 10; px++)
                {
                    var offset = (py * 100 + px) * 4;
                    pixels[offset] = red;
                    pixels[offset + 1] = green;
                    pixels[offset + 2] = blue;
                }
            }
        }

        Paint(0, 0, 255, 0, 0);
        Paint(90, 0, 0, 255, 0);
        Paint(0, 90, 0, 0, 255);
        Paint(90, 90, 255, 0, 0);
        Paint(45, 0, 0, 255, 0);
        Paint(45, 90, 255, 0, 0);
        Paint(0, 45, 0, 255, 0);
        Paint(90, 45, 0, 0, 255);
        image.ReadPixels(pixels, new PixelReadSettings(100u, 100u, StorageType.Char, PixelMapping.RGBA));

        Assert.Equal(new BackgroundColor(0, 0, 0), RemoveBackground.Sample(image));

        using var beforePixels = image.GetPixels();
        var before = beforePixels.ToByteArray(PixelMapping.RGBA)!;

        RemoveBackground.Apply(image, 0);

        using var afterPixels = image.GetPixels();
        Assert.Equal(before, afterPixels.ToByteArray(PixelMapping.RGBA)!);
    }

    [Fact]
    public async Task Saving_a_cutout_makes_the_corner_transparent_and_keeps_the_rgb()
    {
        var source = WritePhoto();

        var output = await RemoveBackground.SaveAsync(source, _directory, "photo cutout", "png", fuzz: 10);

        Assert.True(File.Exists(output));
        Assert.Equal("png", Formats.KeyOf(output));

        var corner = PixelAt(output, 0, 0);
        Assert.Equal(0, corner.Alpha);
        Assert.Equal((255, 255, 255), (corner.Red, corner.Green, corner.Blue));

        var inside = PixelAt(output, 40, 40);
        Assert.Equal(255, inside.Alpha);
        Assert.Equal((220, 20, 30), (inside.Red, inside.Green, inside.Blue));
    }

    [Fact]
    public async Task The_source_is_never_modified_and_the_save_is_atomic()
    {
        var source = WritePhoto();
        var before = File.ReadAllBytes(source);
        var stamp = File.GetLastWriteTimeUtc(source);

        var output = await RemoveBackground.SaveAsync(source, _directory, "photo cutout", "png", fuzz: 10);

        Assert.True(before.SequenceEqual(File.ReadAllBytes(source)), "the source bytes changed");
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(source));
        Assert.NotEqual(Path.GetFullPath(source), Path.GetFullPath(output));
        Assert.True(File.Exists(output));
        Assert.DoesNotContain(
            Directory.GetFiles(_directory),
            path => Path.GetFileName(path).Contains(".part", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_second_save_gets_a_unique_name_and_never_overwrites()
    {
        var source = WritePhoto();

        var first = await RemoveBackground.SaveAsync(source, _directory, "photo cutout", "png", fuzz: 10);
        var second = await RemoveBackground.SaveAsync(source, _directory, "photo cutout", "png", fuzz: 10);

        Assert.NotEqual(first, second);
        Assert.EndsWith(" (2).png", second, StringComparison.Ordinal);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
    }

    [Fact]
    public async Task The_saved_edge_has_a_soft_alpha_ramp()
    {
        var source = WriteGradientPhoto();

        var output = await RemoveBackground.SaveAsync(source, _directory, "gradient cutout", "png", fuzz: 20);

        using var image = new MagickImage(output);
        using var pixels = image.GetPixels();
        var data = pixels.ToByteArray(PixelMapping.RGBA)!;
        var alphas = new List<byte>();
        for (var x = 20; x <= 79; x++)
            alphas.Add(data[(50 * (int)image.Width + x) * 4 + 3]);

        Assert.Equal(0, alphas[0]);
        Assert.Equal(255, alphas[^1]);
        Assert.Contains(alphas, alpha => alpha is > 0 and < 255);

        var previous = -1;
        foreach (var alpha in alphas)
        {
            Assert.True(alpha >= previous, $"alpha fell from {previous} to {alpha}");
            previous = alpha;
        }
    }

    [Theory]
    [InlineData("png")]
    [InlineData("webp")]
    [InlineData("avif")]
    [InlineData("tiff")]
    [InlineData("ico")]
    public async Task Every_alpha_output_keeps_the_transparent_corner(string format)
    {
        var source = WritePhoto();

        var output = await RemoveBackground.SaveAsync(source, _directory, "cutout " + format, format, fuzz: 10);

        Assert.Equal(format, Formats.KeyOf(output));
        using var image = new MagickImage(output);
        Assert.True(image.HasAlpha, $"{format} should keep an alpha channel");

        var corner = PixelAt(output, 0, 0);
        Assert.True(corner.Alpha < 8, $"{format} corner alpha is {corner.Alpha}");

        // ICO's writer expands an 80 px image into its standard icon sizes, so the subject is no
        // longer at the source coordinates in the first frame; the alpha channel is still there.
        if (format != "ico")
        {
            var subject = PixelAt(output, 40, 40);
            Assert.True(subject.Alpha > 247, $"{format} subject alpha is {subject.Alpha}");
        }
    }

    [Fact]
    public async Task An_alpha_less_output_is_refused_before_anything_is_written()
    {
        var source = WritePhoto();

        await Assert.ThrowsAsync<ConversionException>(() =>
            RemoveBackground.SaveAsync(source, _directory, "photo cutout", "jpg", fuzz: 10));

        Assert.False(File.Exists(Path.Combine(_directory, "photo cutout.jpg")));
        Assert.DoesNotContain(
            Directory.GetFiles(_directory),
            path => Path.GetFileName(path).Contains(".part", StringComparison.Ordinal));
    }

    private string WritePhoto()
    {
        var path = Path.Combine(_directory, "photo.png");
        using var image = new MagickImage(Background, 80, 80);
        image.Draw(new Drawables().FillColor(Subject).Rectangle(20, 20, 59, 59));
        image.Write(path, MagickFormat.Png);
        return path;
    }

    /// <summary>A white border with a white-to-red gradient inside, so the alpha ramp is visible in pixels.</summary>
    private string WriteGradientPhoto()
    {
        var path = Path.Combine(_directory, "gradient.png");
        using var image = new MagickImage(Background, 100, 100);
        var pixels = new byte[100 * 100 * 4];
        for (var y = 0; y < 100; y++)
        {
            for (var x = 0; x < 100; x++)
            {
                var offset = (y * 100 + x) * 4;
                pixels[offset] = 255;
                byte green = x < 20 || x >= 80 ? (byte)255 : (byte)Math.Round(255 - (x - 20) * 255 / 59.0);
                pixels[offset + 1] = green;
                pixels[offset + 2] = green;
                pixels[offset + 3] = 255;
            }
        }
        image.ReadPixels(pixels, new PixelReadSettings(100u, 100u, StorageType.Char, PixelMapping.RGBA));
        image.Write(path, MagickFormat.Png);
        return path;
    }

    private static (byte Red, byte Green, byte Blue, byte Alpha) PixelAt(string path, int x, int y)
    {
        using var image = new MagickImage(path);
        using var pixels = image.GetPixels();
        var data = pixels.ToByteArray(PixelMapping.RGBA)!;
        var offset = (y * (int)image.Width + x) * 4;
        return (data[offset], data[offset + 1], data[offset + 2], data[offset + 3]);
    }
}
