using FileAction.App.UI.Tools.Compress;
using FileAction.App.UI.Tools.Image;
using FileAction.Core;
using FileAction.Core.Operations;
using ImageMagick;
using Xunit;
using static FileAction.Core.Loc;

namespace FileAction.Tests;

/// <summary>
/// The 2.4.7 image fixes: HEIC is never offered as a same-format output, animated GIFs keep their frames
/// (or are explicitly turned into a still PNG), the compress probe uses the EXIF-oriented size, Redact
/// honours the drop folder, and ICO writes are clamped and atomic.
/// </summary>
public class ImageFixTests : IDisposable
{
    private readonly string _directory = OutputPaths.TempDirectory();

    public void Dispose() => OutputPaths.TryDelete(_directory);

    // ---------------------------------------------------------------- output containers

    [Fact]
    public void Heic_is_saved_as_a_real_raster_container()
    {
        Assert.Equal("jpg", ImageOutput.FormatFor("heic"));
        Assert.Equal("png", ImageOutput.FormatFor("svg"));
        Assert.Equal("gif", ImageOutput.FormatFor("gif"));
        Assert.Equal("png", ImageOutput.StillFormatFor("gif"));
        Assert.Equal("jpg", ImageOutput.StillFormatFor("heic"));
        Assert.True(ImageOutput.IsHeic("heic"));
        Assert.True(ImageOutput.IsGif("gif"));
    }

    // ---------------------------------------------------------------- animation

    [Fact]
    public async Task Animated_gif_keeps_every_frame_through_crop_compress_and_redact()
    {
        var source = AnimatedGif("anim.gif", 80, 60, frames: 4);

        var cropDirectory = Path.Combine(_directory, "crop");
        var cropped = CropRunner.Apply(new FileItem(source), CropMath.Full(80, 60), cropDirectory);
        Assert.Equal("anim Cropped.gif", Path.GetFileName(cropped));
        AssertFrameCount(cropped, 4);

        var compressDirectory = Path.Combine(_directory, "compress");
        var plan = Compression.Plan(
            new CompressionRequest(MediaKind.Image, "gif", CompressionMode.Preset, 1.0, 50, CompressionPreset.Medium, 0),
            new FileInfo(source).Length, 80, 60, 0);
        var compressed = await CompressRunner.ImageAsync(new FileItem(source), plan, compressDirectory,
            CancellationToken.None, new Progress<double>());
        Assert.Equal("anim Compressed.gif", Path.GetFileName(compressed));
        AssertFrameCount(compressed, 4);

        var redactDirectory = Path.Combine(_directory, "redact");
        var redaction = RedactionPlan.Build(
            80, 60, [new RedactionArea(new RedactionBox(10, 10, 20, 20))], RedactionStyle.Default);
        var redacted = RedactRunner.Apply(new FileItem(source), redaction, redactDirectory);
        Assert.Equal("anim Redacted.gif", Path.GetFileName(redacted));
        AssertFrameCount(redacted, 4);
    }

    [Fact]
    public async Task Catalog_image_edit_keeps_every_gif_frame()
    {
        var source = AnimatedGif("rotate.gif", 40, 30, frames: 4);
        var operation = Catalog.Build().Single(o => o.Id == "image.rotate.right");
        var context = new OperationContext
        {
            Tools = new ToolLocator(),
            OutputDirectory = Path.Combine(_directory, "catalog"),
        };

        var output = Assert.Single((await operation.ExecuteAsync([new FileItem(source)], context)).Outputs);

        Assert.Equal($"rotate ({T("catalog.image.rotate.suffix")}).gif", Path.GetFileName(output));
        AssertFrameCount(output, 4);
        using var first = new MagickImage(output);
        Assert.Equal(30u, first.Width);
        Assert.Equal(40u, first.Height);
    }

    [Fact]
    public void Annotate_declares_the_gif_to_png_conversion()
    {
        Assert.Equal("png", ImageOutput.StillFormatFor("gif"));

        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "FileAction.App", "UI", "Tools", "Image", "AnnotateView.cs"));
        Assert.Contains("tool.annotate.gifNote", source, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- EXIF orientation

    [Fact]
    public void Compress_probe_reports_the_exif_oriented_size_and_plans_the_long_side_correctly()
    {
        var source = ExifOrientedJpeg("portrait.jpg", 400, 300, 6);

        using (var fixture = new MagickImage(source))
            Assert.Equal(6, Convert.ToInt32(fixture.Orientation));

        // The stored pixels are 400×300; the delivered photo is 300×400.
        var (width, height) = CompressView.ProbeImageSize(source);
        Assert.Equal(300, width);
        Assert.Equal(400, height);

        // Capping the long side at 200 must produce 150×200, not 200×150.
        var plan = Compression.PlanImage(
            width, height, new FileInfo(source).Length, "jpg", CompressionMode.Preset,
            targetMegabytes: 1.0, percentage: 50, preset: CompressionPreset.Medium, maxLongSide: 200);
        Assert.True(plan.Resized);
        Assert.Equal(150, plan.Width);
        Assert.Equal(200, plan.Height);

        // The reported 4000×3000 / orientation 6 case: the "1920" option must land on 1440×1920.
        var phone = Compression.PlanImage(
            3000, 4000, 4_000_000, "jpg", CompressionMode.Preset,
            targetMegabytes: 1.0, percentage: 50, preset: CompressionPreset.Medium, maxLongSide: 1920);
        Assert.True(phone.Resized);
        Assert.Equal(1440, phone.Width);
        Assert.Equal(1920, phone.Height);
    }

    // ---------------------------------------------------------------- ICO and atomic writes

    [Fact]
    public void Ico_output_is_clamped_to_256_pixels()
    {
        var path = Path.Combine(_directory, "large.ico");
        using (var image = new MagickImage(MagickColors.Purple, 512, 512))
            ImageOutput.Write(image, path, "ico");

        using var written = new MagickImage(path);
        Assert.True(written.Width <= ImageOutput.IcoMaxSide, $"ICO width {written.Width}");
        Assert.True(written.Height <= ImageOutput.IcoMaxSide, $"ICO height {written.Height}");
    }

    [Fact]
    public void A_failed_image_write_leaves_the_source_and_no_partial_file()
    {
        var source = Path.Combine(_directory, "broken.jpg");
        File.WriteAllBytes(source, [0xFF, 0xD8, 0x00, 0x01, 0x02, 0x03]);

        Assert.ThrowsAny<Exception>(() =>
            CropRunner.Apply(new FileItem(source), CropMath.Full(10, 10), _directory));

        Assert.Equal(new[] { source }, Directory.GetFiles(_directory).Order(StringComparer.Ordinal).ToArray());
        Assert.Empty(Directory.GetFiles(_directory, "*.part.*"));
        Assert.Equal(6, new FileInfo(source).Length);
    }

    // ---------------------------------------------------------------- Redact drop folder

    [Fact]
    public void Redact_passes_the_drop_targets_to_the_shared_output_folder()
    {
        var tool = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "FileAction.App", "UI", "Tools", "Image", "RedactTool.cs"));
        var view = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "FileAction.App", "UI", "Tools", "Image", "RedactView.cs"));

        Assert.Contains("new RedactView(items[0], targets)", tool, StringComparison.Ordinal);
        Assert.Contains("ToolOutputFolder.For(_item, _targets)", view, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- helpers

    private string AnimatedGif(string name, int width, int height, int frames)
    {
        var path = Path.Combine(_directory, name);
        using var collection = new MagickImageCollection();
        for (var i = 0; i < frames; i++)
        {
            var frame = new MagickImage(
                new MagickColor((byte)(40 * i), (byte)(80 + 30 * i), 120), (uint)width, (uint)height);
            frame.AnimationDelay = 10;
            collection.Add(frame);
        }
        collection.Write(path, MagickFormat.Gif);
        return path;
    }

    private static void AssertFrameCount(string path, int expected)
    {
        using var frames = new MagickImageCollection(path);
        Assert.Equal(expected, frames.Count);
    }

    /// <summary>
    /// A JPEG whose pixels are <paramref name="width"/>×<paramref name="height"/> but whose EXIF
    /// orientation is <paramref name="orientation"/> (6 = rotate 90° clockwise when displayed). The
    /// APP1 segment is built by hand so the fixture needs no binary in the repository.
    /// </summary>
    private string ExifOrientedJpeg(string name, int width, int height, ushort orientation)
    {
        using var image = new MagickImage(MagickColors.Orange, (uint)width, (uint)height);
        using var encoded = new MemoryStream();
        image.Write(encoded, MagickFormat.Jpeg);
        var jpeg = encoded.ToArray();

        // Little-endian TIFF header, one IFD0 entry (0x0112 Orientation, SHORT, 1 value).
        byte[] tiff =
        [
            0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00,
            0x01, 0x00,
            0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00,
            (byte)orientation, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        ];

        var payload = new byte[6 + tiff.Length];
        "Exif\0\0"u8.CopyTo(payload);
        tiff.CopyTo(payload, 6);

        var segment = new byte[4 + payload.Length];
        segment[0] = 0xFF;
        segment[1] = 0xE1;
        var length = (ushort)(2 + payload.Length);
        segment[2] = (byte)(length >> 8);
        segment[3] = (byte)length;
        payload.CopyTo(segment, 4);

        var path = Path.Combine(_directory, name);
        var patched = new byte[jpeg.Length + segment.Length];
        jpeg.AsSpan(0, 2).CopyTo(patched); // SOI
        segment.CopyTo(patched, 2);
        jpeg.AsSpan(2).CopyTo(patched.AsSpan(2 + segment.Length));
        File.WriteAllBytes(path, patched);
        return path;
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FileAction.sln")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new DirectoryNotFoundException("FileAction.sln not found above the test output folder.");
    }
}
