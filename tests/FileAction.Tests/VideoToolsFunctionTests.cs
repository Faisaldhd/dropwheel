using System.Globalization;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileAction.App.UI.Tools.Video;
using FileAction.Core;
using Xunit.Abstractions;

namespace FileAction.Tests;

/// <summary>
/// Function tests for the two video tools: a real 3 s clip is generated with FFmpeg and the production
/// runners behind the Apply buttons are driven end to end. They assert the files on disk (full-resolution
/// PNGs, audio kept, pixels hidden only inside their time ranges) and the failure/cancel paths (clear
/// error, no partial output). FFmpeg is required in CI; a local run without it skips these tests.
/// </summary>
[Trait("Category", "Integration")]
public class VideoToolsFunctionTests : IClassFixture<VideoToolsFunctionTests.Samples>
{
    public sealed class Samples : IDisposable
    {
        public string Directory { get; } = OutputPaths.TempDirectory();

        public string? Ffmpeg { get; }

        public string Clip { get; }

        public Samples()
        {
            Ffmpeg = VideoToolTestTools.FindFfmpeg();
            Clip = Path.Combine(Directory, "clip.mp4");
            if (Ffmpeg is null)
                return;

            // Static gray background with a sharp white 4 px grid: the grid makes a blur measurable,
            // and the four colour bars are not needed. A 440 Hz tone proves audio survives redaction.
            ProcessRunner.RunAsync(Ffmpeg,
            [
                "-hide_banner", "-nostdin", "-y",
                "-f", "lavfi", "-i", "color=c=gray:s=320x180:d=3:r=30",
                "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=3",
                "-vf", "drawgrid=w=8:h=8:t=4:color=white",
                "-c:v", "libx264", "-preset", "veryfast", "-pix_fmt", "yuv420p",
                "-c:a", "aac", "-b:a", "96k",
                "-shortest",
                Clip,
            ], default).GetAwaiter().GetResult();
        }

        public void Dispose() => OutputPaths.TryDelete(Directory);
    }

    private readonly Samples _samples;
    private readonly ITestOutputHelper _output;

    public VideoToolsFunctionTests(Samples samples, ITestOutputHelper output)
    {
        _samples = samples;
        _output = output;
    }

    [Fact]
    public void Ffmpeg_is_available_for_video_function_tests()
    {
        // On CI the tool install step is best-effort; these function tests are required there. Locally
        // a machine without FFmpeg skips them instead of failing unrelated work.
        var ci = Environment.GetEnvironmentVariable("CI");
        if (string.Equals(ci, "true", StringComparison.OrdinalIgnoreCase) || ci == "1")
            Assert.NotNull(_samples.Ffmpeg);
        else if (_samples.Ffmpeg is null)
            _output.WriteLine("FFmpeg not found; video function tests are skipped locally.");
    }

    [Fact]
    public async Task Save_frames_exports_full_resolution_pngs_in_one_folder()
    {
        if (_samples.Ffmpeg is not { } ffmpeg)
            return;

        var output = OutputPaths.TempDirectory();
        try
        {
            var info = await VideoProbe.ReadAsync(ffmpeg, _samples.Clip, default);
            Assert.NotNull(info);
            Assert.Equal(320, info!.Width);
            Assert.Equal(180, info.Height);

            var frames = new[] { 0, 45, 89 };
            var folder = await VideoRunner.ExportFramesAsync(
                ffmpeg,
                new FileItem(_samples.Clip),
                frames,
                info.Fps,
                output,
                new Progress<(int Done, int Total)>(),
                default);

            Assert.Equal(Path.Combine(output, "clip frames"), folder);
            var files = Directory.GetFiles(folder, "*.png")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(3, files.Length);
            Assert.Equal(
                new[] { "clip frame 00000.png", "clip frame 00045.png", "clip frame 00089.png" },
                files.Select(Path.GetFileName).ToArray());
            Assert.Equal(3, files.Select(Path.GetFileName).Distinct(StringComparer.Ordinal).Count());

            foreach (var file in files)
            {
                var (width, height) = VideoToolTestTools.PngSize(file);
                Assert.Equal(320, width);
                Assert.Equal(180, height);
            }
        }
        finally
        {
            OutputPaths.TryDelete(output);
        }
    }

    [Fact]
    public async Task Redaction_hides_only_inside_the_time_ranges_and_keeps_audio()
    {
        if (_samples.Ffmpeg is not { } ffmpeg)
            return;

        var output = OutputPaths.TempDirectory();
        try
        {
            var graph = RedactionGraph.Build(
            [
                new RedactionArea(10, 10, 60, 40, 0.5, 1.5, RedactionStyle.Solid, "black"),
                new RedactionArea(100, 60, 120, 60, 1.8, 2.6, RedactionStyle.Blur),
            ],
            320,
            180);
            Assert.NotEmpty(graph);

            var redacted = await VideoRunner.RedactAsync(
                ffmpeg,
                new FileItem(_samples.Clip),
                graph,
                3.0,
                new Settings().VideoCrf,
                output,
                new Progress<double>(),
                default);

            Assert.Equal(Path.Combine(output, "clip Redacted.mp4"), redacted);
            Assert.True(new FileInfo(redacted).Length > 0);
            Assert.True(await VideoToolTestTools.HasAudioAsync(ffmpeg, redacted), "the audio stream was dropped");

            var before = await VideoToolTestTools.ExtractFrameAsync(ffmpeg, redacted, 0.2, output, "before.png");
            var solid = await VideoToolTestTools.ExtractFrameAsync(ffmpeg, redacted, 1.0, output, "solid.png");
            var blurred = await VideoToolTestTools.ExtractFrameAsync(ffmpeg, redacted, 2.2, output, "blurred.png");

            var (width, height, beforePixels) = VideoToolTestTools.Pixels(before);
            var (_, _, solidPixels) = VideoToolTestTools.Pixels(solid);
            var (_, _, blurredPixels) = VideoToolTestTools.Pixels(blurred);

            // The solid box exists at 1.0 s and is gone at 0.2 s and 2.2 s.
            Assert.True(VideoToolTestTools.Luma(solidPixels, width, 40, 30) < 45, "the solid box is not black inside 0.5–1.5 s");
            Assert.True(VideoToolTestTools.Luma(beforePixels, width, 40, 30) > 70, "the solid box leaked before its range");
            Assert.True(VideoToolTestTools.Luma(blurredPixels, width, 40, 30) > 70, "the solid box leaked after its range");

            // The sharp grid is smoothed inside the blur range and untouched before it.
            var sharp = VideoToolTestTools.RegionStdDev(beforePixels, width, height, 100, 60, 120, 60);
            var soft = VideoToolTestTools.RegionStdDev(blurredPixels, width, height, 100, 60, 120, 60);
            Assert.True(sharp > 25, $"the generated clip has no detail to blur (stddev {sharp:0.0})");
            Assert.True(soft < sharp * 0.75, $"the blur is not visible inside 1.8–2.6 s (sharp {sharp:0.0}, blurred {soft:0.0})");
            var untouched = VideoToolTestTools.RegionStdDev(solidPixels, width, height, 100, 60, 120, 60);
            Assert.True(untouched > sharp * 0.75, $"the blur leaked before its range (sharp {sharp:0.0}, before {untouched:0.0})");
        }
        finally
        {
            OutputPaths.TryDelete(output);
        }
    }

    [Fact]
    public async Task Corrupt_input_reports_an_error_and_writes_nothing()
    {
        if (_samples.Ffmpeg is not { } ffmpeg)
            return;

        var root = OutputPaths.TempDirectory();
        var output = Path.Combine(root, "out");
        Directory.CreateDirectory(output);
        var corrupt = Path.Combine(root, "broken.mp4");
        File.WriteAllText(corrupt, "this is not a video");
        try
        {
            Assert.Null(await VideoProbe.ReadAsync(ffmpeg, corrupt, default));

            var error = await Assert.ThrowsAsync<ConversionException>(() => VideoRunner.ExportFramesAsync(
                ffmpeg,
                new FileItem(corrupt),
                [0],
                30,
                output,
                new Progress<(int Done, int Total)>(),
                default));

            Assert.False(string.IsNullOrWhiteSpace(error.Message));
            Assert.Empty(Directory.GetFileSystemEntries(output));
        }
        finally
        {
            OutputPaths.TryDelete(root);
        }
    }

    [Fact]
    public async Task Missing_ffmpeg_reports_an_error_and_writes_nothing()
    {
        var root = OutputPaths.TempDirectory();
        var output = Path.Combine(root, "out");
        Directory.CreateDirectory(output);
        var missing = Path.Combine(root, "missing-ffmpeg.exe");
        try
        {
            var error = await Assert.ThrowsAsync<ConversionException>(() => VideoRunner.ExportFramesAsync(
                missing,
                new FileItem(_samples.Clip),
                [0],
                30,
                output,
                new Progress<(int Done, int Total)>(),
                default));

            Assert.Contains("missing-ffmpeg.exe", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Directory.GetFileSystemEntries(output));
        }
        finally
        {
            OutputPaths.TryDelete(root);
        }
    }

    [Fact]
    public async Task Read_only_output_folder_reports_an_error_and_writes_nothing()
    {
        if (_samples.Ffmpeg is not { } ffmpeg)
            return;

        var root = OutputPaths.TempDirectory();
        var output = Path.Combine(root, "readonly");
        Directory.CreateDirectory(output);
        var info = new DirectoryInfo(output);
        var security = info.GetAccessControl();
        var user = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("no Windows identity");
        var deny = new FileSystemAccessRule(
            user,
            FileSystemRights.CreateFiles | FileSystemRights.WriteData | FileSystemRights.AppendData,
            AccessControlType.Deny);
        security.AddAccessRule(deny);
        info.SetAccessControl(security);
        try
        {
            var error = await Assert.ThrowsAnyAsync<Exception>(() => VideoRunner.ExportFramesAsync(
                ffmpeg,
                new FileItem(_samples.Clip),
                [0],
                30,
                output,
                new Progress<(int Done, int Total)>(),
                default));

            Assert.True(error is UnauthorizedAccessException or IOException, $"unexpected error type {error.GetType().Name}");
            Assert.False(string.IsNullOrWhiteSpace(error.Message));
            Assert.Empty(Directory.GetFileSystemEntries(output));
        }
        finally
        {
            security.RemoveAccessRule(deny);
            info.SetAccessControl(security);
            OutputPaths.TryDelete(root);
        }
    }

    [Fact]
    public async Task Cancelling_export_mid_run_leaves_no_partial_folder()
    {
        if (_samples.Ffmpeg is not { } ffmpeg)
            return;

        var root = OutputPaths.TempDirectory();
        var output = Path.Combine(root, "out");
        Directory.CreateDirectory(output);
        try
        {
            using var cancel = new CancellationTokenSource();
            var frames = Enumerable.Range(0, 20).ToArray();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => VideoRunner.ExportFramesAsync(
                ffmpeg,
                new FileItem(_samples.Clip),
                frames,
                30,
                output,
                new CancelOnFirstReport(cancel),
                cancel.Token));

            Assert.Empty(Directory.GetFileSystemEntries(output));
        }
        finally
        {
            OutputPaths.TryDelete(root);
        }
    }

    [Fact]
    public async Task Cancelling_redaction_mid_run_leaves_no_partial_file()
    {
        if (_samples.Ffmpeg is not { } ffmpeg)
            return;

        var root = OutputPaths.TempDirectory();
        var output = Path.Combine(root, "out");
        Directory.CreateDirectory(output);
        try
        {
            var graph = RedactionGraph.Build(
                [new RedactionArea(10, 10, 60, 40, 0.5, 1.5, RedactionStyle.Solid, "black")],
                320,
                180);

            using var cancel = new CancellationTokenSource();
            var task = VideoRunner.RedactAsync(
                ffmpeg,
                new FileItem(_samples.Clip),
                graph,
                3.0,
                new Settings().VideoCrf,
                output,
                new Progress<double>(),
                cancel.Token);
            cancel.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            Assert.Empty(Directory.GetFileSystemEntries(output));
        }
        finally
        {
            OutputPaths.TryDelete(root);
        }
    }

    /// <summary>Cancels the run from the first real progress report, which is raised mid-loop.</summary>
    private sealed class CancelOnFirstReport(CancellationTokenSource cancel) : IProgress<(int Done, int Total)>
    {
        public void Report((int Done, int Total) value) => cancel.Cancel();
    }
}

/// <summary>Small FFmpeg/image helpers for the video function tests.</summary>
internal static class VideoToolTestTools
{
    /// <summary>The app's FFmpeg, plus the Chocolatey shim path the CI install step uses.</summary>
    public static string? FindFfmpeg()
    {
        var located = new ToolLocator().Ffmpeg;
        if (!string.IsNullOrEmpty(located) && File.Exists(located))
            return located;

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "chocolatey", "bin", "ffmpeg.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links", "ffmpeg.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin", "ffmpeg.exe"),
        };
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    public static async Task<bool> HasAudioAsync(string ffmpeg, string path)
    {
        var lines = new List<string>();
        await ProcessRunner.RunAsync(ffmpeg, ["-hide_banner", "-nostdin", "-i", path], default,
            line =>
            {
                lock (lines)
                    lines.Add(line);
            },
            throwOnError: false).ConfigureAwait(false);
        lock (lines)
            return lines.Any(line => line.Contains("Audio:", StringComparison.Ordinal));
    }

    public static async Task<string> ExtractFrameAsync(string ffmpeg, string video, double seconds, string directory, string name)
    {
        var path = Path.Combine(directory, name);
        await ProcessRunner.RunAsync(ffmpeg,
        [
            "-hide_banner", "-nostdin", "-y",
            "-ss", seconds.ToString("0.000", CultureInfo.InvariantCulture),
            "-i", video,
            "-frames:v", "1",
            "-update", "1",
            path,
        ], default).ConfigureAwait(false);
        return path;
    }

    public static (int Width, int Height) PngSize(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        return (decoder.Frames[0].PixelWidth, decoder.Frames[0].PixelHeight);
    }

    public static (int Width, int Height, byte[] Bgra) Pixels(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var stride = width * 4;
        var data = new byte[stride * height];
        converted.CopyPixels(data, stride, 0);
        return (width, height, data);
    }

    /// <summary>Rec. 601 luma of one BGRA pixel.</summary>
    public static double Luma(byte[] bgra, int width, int x, int y)
    {
        var offset = (y * width + x) * 4;
        return 0.114 * bgra[offset] + 0.587 * bgra[offset + 1] + 0.299 * bgra[offset + 2];
    }

    /// <summary>Standard deviation of luma in a rectangle: high for the sharp grid, low after a blur.</summary>
    public static double RegionStdDev(byte[] bgra, int width, int height, int x, int y, int regionWidth, int regionHeight)
    {
        var values = new List<double>(regionWidth * regionHeight);
        for (var row = y; row < Math.Min(height, y + regionHeight); row++)
        {
            for (var column = x; column < Math.Min(width, x + regionWidth); column++)
                values.Add(Luma(bgra, width, column, row));
        }
        if (values.Count == 0)
            return 0;
        var mean = values.Average();
        var variance = values.Select(value => (value - mean) * (value - mean)).Average();
        return Math.Sqrt(variance);
    }
}
