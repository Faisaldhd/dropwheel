using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FileAction.App.UI.Tools.Compress;
using FileAction.App.UI.Tools.Meta;
using FileAction.Core;
using FileAction.Core.Backends;
using ImageMagick;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit.Abstractions;

namespace FileAction.Tests;

/// <summary>
/// Drives the real compress and metadata runners against generated media and asserts the files they
/// write: the output is smaller, the resize and the target size are honoured, the duration survives,
/// the source stays byte-identical, and a failure leaves no partial file behind. Tests that need FFmpeg
/// report a skip (and pass) on machines without it; CI installs it before the test run.
/// </summary>
[Trait("Category", "Integration")]
public class CompressMetadataFunctionTests
{
    private readonly ITestOutputHelper _output;
    private readonly ToolLocator _tools = new();

    public CompressMetadataFunctionTests(ITestOutputHelper output) => _output = output;

    // ------------------------------------------------------------------ images

    [Fact]
    public async Task Compress_image_writes_a_smaller_resized_copy_and_never_touches_the_source()
    {
        var dir = OutputPaths.TempDirectory();
        try
        {
            var source = NoisyJpeg(dir, "photo.jpg", 1200, 900);
            var sourceBytes = new FileInfo(source).Length;
            var before = Hash(source);
            var plan = Compression.Plan(
                new CompressionRequest(MediaKind.Image, "jpg", CompressionMode.Preset, 1.0, 50, CompressionPreset.Low, 600),
                sourceBytes, 1200, 900, 0);

            var output = await CompressRunner.ImageAsync(new FileItem(source), plan, dir, CancellationToken.None, new Progress<double>());

            Assert.True(File.Exists(output), $"missing {output}");
            Assert.Equal("photo Compressed.jpg", Path.GetFileName(output));
            var outputBytes = new FileInfo(output).Length;
            _output.WriteLine($"source {sourceBytes} bytes, output {outputBytes} bytes");
            Assert.True(outputBytes < sourceBytes * 0.8,
                $"expected a much smaller copy, {outputBytes} vs source {sourceBytes}");

            using var result = new MagickImage(output);
            Assert.Equal(600, (int)result.Width);
            Assert.Equal(450, (int)result.Height);

            Assert.Equal(before, Hash(source));
            Assert.Empty(Directory.GetFiles(dir, "*.part.*"));
        }
        finally
        {
            OutputPaths.TryDelete(dir);
        }
    }

    [Fact]
    public async Task Compress_image_honours_the_target_size_in_the_written_file()
    {
        var dir = OutputPaths.TempDirectory();
        try
        {
            var source = NoisyJpeg(dir, "noise.jpg", 1600, 1200);
            var sourceBytes = new FileInfo(source).Length;
            var before = Hash(source);
            const double targetMegabytes = 0.2;
            var plan = Compression.Plan(
                new CompressionRequest(MediaKind.Image, "jpg", CompressionMode.TargetSize, targetMegabytes, 50,
                    CompressionPreset.Medium, 0),
                sourceBytes, 1600, 1200, 0);

            var output = await CompressRunner.ImageAsync(new FileItem(source), plan, dir, CancellationToken.None, new Progress<double>());

            var target = Compression.BytesFromMegabytes(targetMegabytes);
            var outputBytes = new FileInfo(output).Length;
            _output.WriteLine($"source {sourceBytes} bytes, target {target} bytes, output {outputBytes} bytes, " +
                              $"{plan.Width}x{plan.Height} q{plan.Quality}");
            Assert.True(outputBytes <= target,
                $"expected at most {target} bytes, got {outputBytes} in {Path.GetFileName(output)}");
            // The estimate under-shot this high-quality noisy source to 18 % of the target; the measured
            // retry must spend most of the allowed size instead of throwing image quality away.
            Assert.True(outputBytes >= target * Compression.AcceptableImageShare,
                $"expected at least {target * Compression.AcceptableImageShare:0} bytes, got {outputBytes}");

            Assert.Equal(before, Hash(source));
            Assert.Empty(Directory.GetFiles(dir, "*.part.*"));
        }
        finally
        {
            OutputPaths.TryDelete(dir);
        }
    }

    // ------------------------------------------------------------------ video

    [Fact]
    public async Task Compress_video_reaches_the_target_size_and_keeps_duration_and_audio()
    {
        if (!_tools.Has(ExternalTool.Ffmpeg))
        {
            _output.WriteLine("skipped: FFmpeg is not installed");
            return;
        }

        var dir = OutputPaths.TempDirectory();
        try
        {
            var ffmpeg = _tools.Require(ExternalTool.Ffmpeg);
            var source = Path.Combine(dir, "clip.mp4");
            await ProcessRunner.RunAsync(ffmpeg,
            [
                "-hide_banner", "-nostdin", "-y",
                "-f", "lavfi", "-i", "testsrc=size=320x240:rate=15",
                "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100",
                "-t", "8",
                // Realistic footage is harder to encode than a flat test pattern; noise makes the two-pass
                // bitrate target meaningful instead of letting x264 undershoot on trivial content.
                "-vf", "noise=alls=35:allf=t+u",
                "-c:v", "libx264", "-preset", "veryfast", "-pix_fmt", "yuv420p",
                "-c:a", "aac", "-b:a", "96k",
                source,
            ], CancellationToken.None);

            var sourceBytes = new FileInfo(source).Length;
            var before = Hash(source);
            var duration = await FfmpegBackend.ProbeDurationAsync(ffmpeg, source, CancellationToken.None);
            Assert.NotNull(duration);

            const double targetMegabytes = 0.4;
            var plan = Compression.Plan(
                new CompressionRequest(MediaKind.Video, "mp4", CompressionMode.TargetSize, targetMegabytes, 50, CompressionPreset.Medium, 0),
                sourceBytes, 320, 240, duration!.Value);

            var output = await CompressRunner.MediaAsync(new FileItem(source), plan, duration.Value, dir, _tools,
                CancellationToken.None, new Progress<double>());

            Assert.True(File.Exists(output), $"missing {output}");
            Assert.Equal("clip Compressed.mp4", Path.GetFileName(output));

            var target = Compression.BytesFromMegabytes(targetMegabytes);
            var outputBytes = new FileInfo(output).Length;
            _output.WriteLine($"source {sourceBytes} bytes, target {target} bytes, output {outputBytes} bytes");
            // The rate control never exceeds the target by more than the container/audio overhead; easy
            // content can undershoot because the encoder will not waste bits, which is the desired outcome.
            Assert.True(outputBytes <= target * 1.10,
                $"expected at most {target * 1.10:0} bytes for a {target}-byte target, got {outputBytes}");
            Assert.True(outputBytes > target * 0.6,
                $"output {outputBytes} is far below the {target}-byte target");
            Assert.True(outputBytes < sourceBytes * 0.5,
                $"expected a much smaller copy, {outputBytes} vs source {sourceBytes}");

            var outputDuration = await FfmpegBackend.ProbeDurationAsync(ffmpeg, output, CancellationToken.None);
            Assert.NotNull(outputDuration);
            Assert.InRange(outputDuration!.Value, duration.Value - 0.25, duration.Value + 0.25);

            var banner = await BannerAsync(ffmpeg, output);
            var probe = MetadataFfmpeg.ParseProbe(banner);
            Assert.Contains(probe.Document.Fields, field => field.GroupKey == MetadataGroups.AudioKey);

            Assert.Equal(before, Hash(source));
            Assert.Empty(Directory.GetFiles(dir, "*.part.*"));
        }
        finally
        {
            OutputPaths.TryDelete(dir);
        }
    }

    // ------------------------------------------------------------------ audio

    [Fact]
    public async Task Compress_audio_gets_smaller_and_keeps_its_duration()
    {
        if (!_tools.Has(ExternalTool.Ffmpeg))
        {
            _output.WriteLine("skipped: FFmpeg is not installed");
            return;
        }

        var dir = OutputPaths.TempDirectory();
        try
        {
            var ffmpeg = _tools.Require(ExternalTool.Ffmpeg);
            var source = Path.Combine(dir, "tone.mp3");
            await ProcessRunner.RunAsync(ffmpeg,
            [
                "-hide_banner", "-nostdin", "-y",
                "-f", "lavfi", "-i", "sine=frequency=330:sample_rate=44100",
                "-t", "6", "-c:a", "libmp3lame", "-b:a", "192k",
                source,
            ], CancellationToken.None);

            var sourceBytes = new FileInfo(source).Length;
            var before = Hash(source);
            var duration = await FfmpegBackend.ProbeDurationAsync(ffmpeg, source, CancellationToken.None);
            Assert.NotNull(duration);

            var plan = Compression.Plan(
                new CompressionRequest(MediaKind.Audio, "mp3", CompressionMode.TargetSize, 0.08, 50, CompressionPreset.Medium, 0),
                sourceBytes, 0, 0, duration!.Value);

            var output = await CompressRunner.MediaAsync(new FileItem(source), plan, duration.Value, dir, _tools,
                CancellationToken.None, new Progress<double>());

            Assert.True(File.Exists(output), $"missing {output}");
            Assert.Equal("tone Compressed.mp3", Path.GetFileName(output));
            _output.WriteLine($"source {sourceBytes} bytes, output {new FileInfo(output).Length} bytes");
            Assert.True(new FileInfo(output).Length < sourceBytes * 0.8,
                $"expected a smaller audio file, {new FileInfo(output).Length} vs {sourceBytes}");

            var outputDuration = await FfmpegBackend.ProbeDurationAsync(ffmpeg, output, CancellationToken.None);
            Assert.NotNull(outputDuration);
            Assert.InRange(outputDuration!.Value, duration.Value - 0.25, duration.Value + 0.25);

            Assert.Equal(before, Hash(source));
            Assert.Empty(Directory.GetFiles(dir, "*.part.*"));
        }
        finally
        {
            OutputPaths.TryDelete(dir);
        }
    }

    // ------------------------------------------------------------------ metadata

    [Fact]
    public async Task Metadata_edit_writes_camera_and_gps_and_remove_all_clears_them()
    {
        var dir = OutputPaths.TempDirectory();
        try
        {
            var source = Path.Combine(dir, "shot.jpg");
            using (var image = new MagickImage(MagickColors.SkyBlue, 640, 480))
            {
                var exif = new ExifProfile();
                exif.SetValue(ExifTag.Make, "BeforeCam");
                exif.SetValue(ExifTag.Model, "BeforeModel");
                image.SetProfile(exif);
                image.Write(source, MagickFormat.Jpeg);
            }

            var before = Hash(source);
            var item = new FileItem(source);
            var edit = new MetadataEdit
            {
                CameraMake = "TestCam",
                CameraModel = "TestModel",
                Latitude = "24.7136",
                Longitude = "46.6753",
            };

            var output = await MetadataRunner.WriteAsync(item, MetadataKind.Image, edit, Array.Empty<MetadataChapter>(),
                0, dir, _tools, CancellationToken.None, new Progress<double>());

            Assert.Equal("shot Edited metadata.jpg", Path.GetFileName(output));
            using (var read = new MagickImage(output))
            {
                var exif = read.GetExifProfile();
                Assert.NotNull(exif);
                Assert.Equal("TestCam", exif!.GetValue(ExifTag.Make)?.GetValue() as string);
                Assert.Equal("TestModel", exif.GetValue(ExifTag.Model)?.GetValue() as string);
                Assert.Equal("N", exif.GetValue(ExifTag.GPSLatitudeRef)?.GetValue() as string);
                Assert.Equal("E", exif.GetValue(ExifTag.GPSLongitudeRef)?.GetValue() as string);
                Assert.NotNull(exif.GetValue(ExifTag.GPSLatitude));
                Assert.NotNull(exif.GetValue(ExifTag.GPSLongitude));
            }

            var cleared = await MetadataRunner.WriteAsync(item, MetadataKind.Image, new MetadataEdit { RemoveAll = true },
                Array.Empty<MetadataChapter>(), 0, dir, _tools, CancellationToken.None, new Progress<double>());

            Assert.Equal("shot Edited metadata (2).jpg", Path.GetFileName(cleared));
            using (var read = new MagickImage(cleared))
            {
                var exif = read.GetExifProfile();
                Assert.True(exif is null || exif.GetValue(ExifTag.Make) is null, "Remove All kept the camera make");
                Assert.True(exif is null || exif.GetValue(ExifTag.GPSLatitude) is null, "Remove All kept the GPS tag");
            }

            Assert.Equal(before, Hash(source));
            Assert.Empty(Directory.GetFiles(dir, "*.part.*"));
        }
        finally
        {
            OutputPaths.TryDelete(dir);
        }
    }

    [Fact]
    public async Task Metadata_edit_updates_the_pdf_info_dictionary()
    {
        var dir = OutputPaths.TempDirectory();
        try
        {
            var source = Path.Combine(dir, "doc.pdf");
            using (var pdf = new PdfDocument())
            {
                pdf.AddPage();
                pdf.Info.Title = "Old title";
                pdf.Save(source);
            }

            var before = Hash(source);
            var edit = new MetadataEdit
            {
                Title = "New title",
                Author = "Dana",
                Subject = "Quarterly report",
                Keywords = "k1, k2",
            };

            var output = await MetadataRunner.WriteAsync(new FileItem(source), MetadataKind.Pdf, edit,
                Array.Empty<MetadataChapter>(), 0, dir, _tools, CancellationToken.None, new Progress<double>());

            Assert.Equal("doc Edited metadata.pdf", Path.GetFileName(output));
            using var reread = PdfReader.Open(output, PdfDocumentOpenMode.Import);
            Assert.Equal("New title", reread.Info.Title);
            Assert.Equal("Dana", reread.Info.Author);
            Assert.Equal("Quarterly report", reread.Info.Subject);
            Assert.Equal("k1, k2", reread.Info.Keywords);
            Assert.Equal(1, reread.PageCount);
            Assert.Equal(before, Hash(source));
        }
        finally
        {
            OutputPaths.TryDelete(dir);
        }
    }

    // ------------------------------------------------------------------ failure paths

    [Fact]
    public async Task A_corrupt_image_fails_without_leaving_any_file()
    {
        var dir = OutputPaths.TempDirectory();
        try
        {
            var source = Path.Combine(dir, "broken.jpg");
            // A JPEG start marker followed by garbage: Magick cannot decode it.
            File.WriteAllBytes(source, [0xFF, 0xD8, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05]);

            var plan = Compression.Plan(
                new CompressionRequest(MediaKind.Image, "jpg", CompressionMode.Preset, 1.0, 50, CompressionPreset.Medium, 0),
                8, 0, 0, 0);

            var error = await Assert.ThrowsAnyAsync<Exception>(() =>
                CompressRunner.ImageAsync(new FileItem(source), plan, dir, CancellationToken.None, new Progress<double>()));
            Assert.False(string.IsNullOrWhiteSpace(error.Message));

            Assert.Empty(Directory.GetFiles(dir, "*.part.*"));
            Assert.Single(Directory.GetFiles(dir));
            Assert.Equal(8, new FileInfo(source).Length);
        }
        finally
        {
            OutputPaths.TryDelete(dir);
        }
    }

    [Fact]
    public async Task An_unwritable_output_folder_fails_without_partial_files()
    {
        var dir = OutputPaths.TempDirectory();
        try
        {
            var source = NoisyJpeg(dir, "photo.jpg", 320, 240);
            var before = Hash(source);

            // The output directory's parent is a file, so CreateDirectory cannot succeed (a stand-in for a
            // read-only or protected folder that works without touching ACLs).
            var blocker = Path.Combine(dir, "blocked");
            File.WriteAllText(blocker, "not a folder");
            var blockedDirectory = Path.Combine(blocker, "out");

            var plan = Compression.Plan(
                new CompressionRequest(MediaKind.Image, "jpg", CompressionMode.Preset, 1.0, 50, CompressionPreset.Medium, 0),
                new FileInfo(source).Length, 320, 240, 0);

            await Assert.ThrowsAnyAsync<Exception>(() =>
                CompressRunner.ImageAsync(new FileItem(source), plan, blockedDirectory, CancellationToken.None, new Progress<double>()));

            Assert.False(Directory.Exists(blockedDirectory));
            Assert.Empty(Directory.GetFiles(dir, "*.part.*"));
            Assert.Equal(before, Hash(source));
        }
        finally
        {
            OutputPaths.TryDelete(dir);
        }
    }

    [Fact]
    public async Task A_missing_ffmpeg_reports_a_clear_error_and_writes_nothing()
    {
        if (_tools.Has(ExternalTool.Ffmpeg))
        {
            _output.WriteLine("skipped: FFmpeg is installed, a missing tool cannot be simulated here");
            return;
        }

        var dir = OutputPaths.TempDirectory();
        try
        {
            var source = Path.Combine(dir, "clip.mp4");
            File.WriteAllBytes(source, new byte[64]);
            var plan = Compression.Plan(
                new CompressionRequest(MediaKind.Video, "mp4", CompressionMode.Preset, 1.0, 50, CompressionPreset.Low, 0),
                64, 0, 0, 0);

            var error = await Assert.ThrowsAsync<ConversionException>(() =>
                CompressRunner.MediaAsync(new FileItem(source), plan, 0, dir, _tools, CancellationToken.None, new Progress<double>()));

            Assert.False(string.IsNullOrWhiteSpace(error.Message));
            Assert.Empty(Directory.GetFiles(dir, "*.part.*"));
            Assert.Single(Directory.GetFiles(dir));
        }
        finally
        {
            OutputPaths.TryDelete(dir);
        }
    }

    [Fact]
    public async Task A_corrupt_metadata_input_fails_without_writing_anything()
    {
        var dir = OutputPaths.TempDirectory();
        try
        {
            var source = Path.Combine(dir, "broken.jpg");
            File.WriteAllBytes(source, [0xFF, 0xD8, 0x10, 0x11, 0x12]);

            await Assert.ThrowsAnyAsync<Exception>(() =>
                MetadataRunner.WriteAsync(new FileItem(source), MetadataKind.Image, new MetadataEdit { CameraMake = "X" },
                    Array.Empty<MetadataChapter>(), 0, dir, _tools, CancellationToken.None, new Progress<double>()));

            Assert.Empty(Directory.GetFiles(dir, "*.part.*"));
            Assert.Single(Directory.GetFiles(dir));
        }
        finally
        {
            OutputPaths.TryDelete(dir);
        }
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>A high-entropy JPEG: a lower quality has a measurably smaller output.</summary>
    private static string NoisyJpeg(string directory, string name, int width, int height)
    {
        var path = Path.Combine(directory, name);
        using var image = new MagickImage(MagickColors.Gray, (uint)width, (uint)height);
        image.AddNoise(NoiseType.Random);
        image.Quality = 96;
        image.Write(path, MagickFormat.Jpeg);
        return path;
    }

    private static async Task<string> BannerAsync(string ffmpeg, string path)
    {
        var banner = new StringBuilder();
        await ProcessRunner.RunAsync(ffmpeg, ["-hide_banner", "-nostdin", "-i", path], CancellationToken.None,
            line =>
            {
                lock (banner)
                    banner.AppendLine(line);
            },
            throwOnError: false);
        return banner.ToString();
    }

    private static string Hash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}

/// <summary>
/// The tool windows must take every colour from the shared theme, so a light/dark switch is a rebuild
/// away and nothing keeps a stale brush. These checks scan the tool sources; the shared ToolWindow calls
/// Theme.Refresh() before it builds, so each opening already paints with the current palette.
/// </summary>
public class ToolThemeTests
{
    private static readonly string[] ThemeCheckedFiles =
    [
        Path.Combine("src", "FileAction.App", "UI", "Tools", "ToolControls.cs"),
        Path.Combine("src", "FileAction.App", "UI", "Tools", "Compress", "CompressView.cs"),
        Path.Combine("src", "FileAction.App", "UI", "Tools", "Meta", "MetadataView.cs"),
    ];

    [Fact]
    public void Tool_views_use_theme_brushes_only()
    {
        foreach (var relative in ThemeCheckedFiles)
        {
            var text = File.ReadAllText(Path.Combine(RepoRoot(), relative));

            Assert.DoesNotMatch("#[0-9A-Fa-f]{6}", text);
            Assert.DoesNotMatch(@"Color\.From", text);
            Assert.DoesNotMatch(@"\bColors\.", text);
            Assert.DoesNotMatch(@"static\s+readonly\s+(SolidColorBrush|Brush|LinearGradientBrush)", text);

            foreach (Match brush in Regex.Matches(text, @"\bBrushes\.(\w+)"))
                Assert.Equal("Transparent", brush.Groups[1].Value);
        }
    }

    [Fact]
    public void The_shared_window_refreshes_the_theme_before_it_builds()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", "FileAction.App", "UI", "Tools", "ToolWindow.cs"));
        var constructor = text.IndexOf("public ToolWindow(", StringComparison.Ordinal);
        Assert.True(constructor >= 0, "ToolWindow constructor not found");

        var refresh = text.IndexOf("Theme.Refresh();", constructor, StringComparison.Ordinal);
        Assert.True(refresh > constructor, "ToolWindow must call Theme.Refresh() before building the view");
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
