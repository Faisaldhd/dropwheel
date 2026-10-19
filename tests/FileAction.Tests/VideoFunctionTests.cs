using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using FileAction.Core;
using Xunit;

namespace FileAction.Tests;

/// <summary>
/// Real-FFmpeg function tests for the three video tools. They generate a short clip with testsrc + a
/// 440 Hz sine, run the shipped <see cref="VideoToolRunner"/> and assert the produced file with ffprobe.
/// The whole class is skipped (with the reason in the test report) only when FFmpeg is not installed.
/// </summary>
[Trait("Category", "Integration")]
public sealed class VideoFunctionTests : IClassFixture<VideoFunctionTests.Samples>
{
    private const int SampleFps = 30;
    private static readonly double OneFrame = 1.0 / SampleFps;
    private const double ContainerSlack = 0.005;

    private readonly Samples _samples;

    public VideoFunctionTests(Samples samples) => _samples = samples;

    // ---------------------------------------------------------------- trim

    [FfmpegFact]
    public async Task Trim_outputs_the_requested_range_keeps_audio_and_leaves_the_source_untouched()
    {
        var source = _samples.Clip;
        var before = await FileSnapshot.TakeAsync(source);
        var outputDirectory = _samples.NewOutputDirectory("trim");

        var request = new VideoTrimRequest(
            source,
            string.Empty,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(3),
            "mp4",
            VideoTrimMode.ReEncode,
            HasAudio: true);

        var output = await VideoToolRunner.TrimAsync(
            _samples.Ffmpeg, request, outputDirectory, "sample Trimmed", default);

        Assert.Equal("sample Trimmed.mp4", Path.GetFileName(output));
        Assert.True(File.Exists(output), output);

        var probe = await ProbeAsync(output);
        Assert.True(probe.HasVideo);
        Assert.True(probe.HasAudio);
        Assert.InRange(Math.Abs(probe.Duration - 2.0), 0.0, OneFrame + ContainerSlack);

        Assert.Single(Directory.GetFiles(outputDirectory, "*", SearchOption.AllDirectories));
        await before.AssertUnchangedAsync(source);
    }

    [FfmpegFact]
    public async Task Trim_stream_copy_still_keeps_the_audio_stream()
    {
        var outputDirectory = _samples.NewOutputDirectory("trim-copy");
        var request = new VideoTrimRequest(
            _samples.Clip, string.Empty, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3),
            "mp4", VideoTrimMode.StreamCopy, HasAudio: true);

        var output = await VideoToolRunner.TrimAsync(
            _samples.Ffmpeg, request, outputDirectory, "sample Copy", default);

        var probe = await ProbeAsync(output);
        Assert.True(probe.HasAudio);
        Assert.True(probe.HasVideo);
        Assert.Single(Directory.GetFiles(outputDirectory, "*", SearchOption.AllDirectories));
    }

    // ---------------------------------------------------------------- crop

    [FfmpegFact]
    public async Task Crop_writes_the_exact_even_size_and_keeps_audio()
    {
        var source = _samples.Clip;
        var before = await FileSnapshot.TakeAsync(source);
        var outputDirectory = _samples.NewOutputDirectory("crop");

        var request = new VideoCropRequest(
            source,
            string.Empty,
            new CropRect(40, 30, 160, 120),
            320,
            240,
            "mp4",
            HasAudio: true);

        var output = await VideoToolRunner.CropAsync(
            _samples.Ffmpeg, request, outputDirectory, "sample Cropped", default);

        Assert.Equal("sample Cropped.mp4", Path.GetFileName(output));
        var probe = await ProbeAsync(output);
        Assert.Equal(160, probe.Width);
        Assert.Equal(120, probe.Height);
        Assert.True(probe.HasAudio);
        Assert.InRange(Math.Abs(probe.Duration - 4.0), 0.0, OneFrame + ContainerSlack);

        Assert.Single(Directory.GetFiles(outputDirectory, "*", SearchOption.AllDirectories));
        await before.AssertUnchangedAsync(source);
    }

    // ---------------------------------------------------------------- speed

    [FfmpegFact]
    public async Task Speed_2x_halves_the_duration_keeps_audio_and_keeps_the_pitch()
    {
        var source = _samples.Clip;
        var before = await FileSnapshot.TakeAsync(source);
        var outputDirectory = _samples.NewOutputDirectory("speed");

        var request = new VideoSpeedRequest(source, string.Empty, 2.0, "mp4", HasAudio: true, Fps: 30);
        var output = await VideoToolRunner.SpeedAsync(
            _samples.Ffmpeg, request, outputDirectory, "sample 2x", default);

        Assert.Equal("sample 2x.mp4", Path.GetFileName(output));
        var probe = await ProbeAsync(output);
        Assert.True(probe.HasAudio);
        Assert.InRange(Math.Abs(probe.Duration - 2.0), 0.0, OneFrame + ContainerSlack);

        var sourceTone = await MeasureToneAsync(source);
        var outputTone = await MeasureToneAsync(output);
        Assert.InRange(sourceTone, 430.0, 450.0);
        Assert.InRange(outputTone, 430.0, 450.0);
        Assert.InRange(Math.Abs(outputTone - sourceTone), 0.0, 15.0);

        Assert.Single(Directory.GetFiles(outputDirectory, "*", SearchOption.AllDirectories));
        await before.AssertUnchangedAsync(source);
    }

    // ---------------------------------------------------------------- failure paths

    [FfmpegFact]
    public async Task Corrupt_input_reports_a_clear_error_and_leaves_no_partial_file()
    {
        var directory = _samples.NewDirectory("corrupt");
        var source = Path.Combine(directory, "broken.mp4");
        await File.WriteAllTextAsync(source, "this is not a video");
        var outputDirectory = Path.Combine(directory, "out");

        var request = new VideoTrimRequest(
            source, string.Empty, TimeSpan.Zero, TimeSpan.FromSeconds(1),
            "mp4", VideoTrimMode.ReEncode, HasAudio: false);

        var error = await Assert.ThrowsAsync<ConversionException>(() => VideoToolRunner.TrimAsync(
            _samples.Ffmpeg, request, outputDirectory, "broken Trimmed", default));

        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        AssertNoOutput(outputDirectory);
    }

    [FfmpegFact]
    public async Task Missing_ffmpeg_reports_a_clear_error_and_leaves_no_partial_file()
    {
        var outputDirectory = _samples.NewOutputDirectory("missing-ffmpeg");
        var missing = Path.Combine(_samples.Directory, "ffmpeg-does-not-exist.exe");
        var request = new VideoTrimRequest(
            _samples.Clip, string.Empty, TimeSpan.Zero, TimeSpan.FromSeconds(1),
            "mp4", VideoTrimMode.ReEncode, HasAudio: true);

        var error = await Assert.ThrowsAsync<ConversionException>(() => VideoToolRunner.TrimAsync(
            missing, request, outputDirectory, "missing Trimmed", default));

        Assert.Contains("ffmpeg-does-not-exist", error.Message, StringComparison.OrdinalIgnoreCase);
        AssertNoOutput(outputDirectory);
    }

    [FfmpegFact]
    public async Task Read_only_output_folder_reports_an_error_and_leaves_no_partial_file()
    {
        var directory = _samples.NewDirectory("read-only");
        var outputDirectory = Path.Combine(directory, "locked");
        Directory.CreateDirectory(outputDirectory);
        var who = $"{Environment.UserDomainName}\\{Environment.UserName}";

        ConversionException? error = null;
        try
        {
            await IcaclsAsync(outputDirectory, "/deny", $"{who}:(W)").ConfigureAwait(false);

            var request = new VideoTrimRequest(
                _samples.Clip, string.Empty, TimeSpan.Zero, TimeSpan.FromSeconds(1),
                "mp4", VideoTrimMode.ReEncode, HasAudio: true);

            error = await Assert.ThrowsAsync<ConversionException>(() => VideoToolRunner.TrimAsync(
                _samples.Ffmpeg, request, outputDirectory, "locked Trimmed", default));
        }
        finally
        {
            // The deny also blocks listing the folder, so restore access before inspecting it.
            await IcaclsAsync(outputDirectory, "/remove:d", who).ConfigureAwait(false);
        }

        Assert.NotNull(error);
        Assert.False(string.IsNullOrWhiteSpace(error!.Message));
        AssertNoOutput(outputDirectory);
    }

    [FfmpegFact]
    public async Task Cancelling_mid_run_reports_cancellation_and_leaves_no_partial_file()
    {
        var longClip = _samples.LongClip;
        var outputDirectory = _samples.NewOutputDirectory("cancel");
        var request = new VideoTrimRequest(
            longClip, string.Empty, TimeSpan.Zero, TimeSpan.FromSeconds(10),
            "mp4", VideoTrimMode.ReEncode, HasAudio: false);

        using var cancellation = new CancellationTokenSource();
        var work = VideoToolRunner.TrimAsync(
            _samples.Ffmpeg, request, outputDirectory, "long Trimmed", cancellation.Token);

        // Cancel as soon as FFmpeg has opened its temporary output: a fixed delay is flaky because a
        // small testsrc clip can encode faster than the delay on a fast runner.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!work.IsCompleted && DateTime.UtcNow < deadline)
        {
            if (Directory.Exists(outputDirectory)
                && Directory.EnumerateFiles(outputDirectory).Any(file => file.Contains(".part.", StringComparison.Ordinal)))
            {
                break;
            }
            await Task.Delay(10);
        }
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
        AssertNoOutput(outputDirectory);
    }

    // ---------------------------------------------------------------- helpers

    private async Task<ProbeResult> ProbeAsync(string path)
    {
        var json = await ProcessRunner.RunAsync(
            _samples.Ffprobe,
            ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", path],
            default);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var format = root.GetProperty("format");
        var duration = format.TryGetProperty("duration", out var durationElement)
            && double.TryParse(durationElement.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                ? seconds
                : 0;

        var width = 0;
        var height = 0;
        var hasAudio = false;
        var hasVideo = false;
        foreach (var stream in root.GetProperty("streams").EnumerateArray())
        {
            var type = stream.TryGetProperty("codec_type", out var codecType) ? codecType.GetString() : null;
            if (type == "audio")
            {
                hasAudio = true;
            }
            else if (type == "video")
            {
                hasVideo = true;
                width = stream.GetProperty("width").GetInt32();
                height = stream.GetProperty("height").GetInt32();
            }
        }
        return new ProbeResult(duration, width, height, hasAudio, hasVideo);
    }

    /// <summary>Extracts mono 16-bit PCM and measures the dominant tone by zero crossings.</summary>
    private async Task<double> MeasureToneAsync(string video)
    {
        var wav = Path.Combine(_samples.Directory, $"tone-{Guid.NewGuid():N}.wav");
        try
        {
            await ProcessRunner.RunAsync(
                _samples.Ffmpeg,
                ["-hide_banner", "-nostdin", "-y", "-i", video, "-vn", "-ac", "1", "-ar", "44100",
                    "-c:a", "pcm_s16le", wav],
                default);
            return DominantFrequency(wav);
        }
        finally
        {
            File.Delete(wav);
        }
    }

    private static double DominantFrequency(string wav)
    {
        using var reader = new BinaryReader(File.OpenRead(wav));
        var riff = new string(reader.ReadChars(4));
        reader.ReadInt32();
        var wave = new string(reader.ReadChars(4));
        if (riff != "RIFF" || wave != "WAVE")
            throw new InvalidDataException("not a RIFF/WAVE file");

        var channels = 1;
        var sampleRate = 44100;
        var bits = 16;
        byte[]? data = null;
        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            var id = new string(reader.ReadChars(4));
            var size = reader.ReadInt32();
            if (id == "fmt ")
            {
                reader.ReadInt16(); // format, PCM from ffmpeg
                channels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32(); // byte rate
                reader.ReadInt16(); // block align
                bits = reader.ReadInt16();
                var extra = size - 16;
                if (extra > 0)
                    reader.BaseStream.Seek(extra, SeekOrigin.Current);
            }
            else if (id == "data")
            {
                data = reader.ReadBytes(size);
                break;
            }
            else
            {
                reader.BaseStream.Seek(size + (size % 2), SeekOrigin.Current);
            }
        }

        if (data is null || channels != 1 || bits != 16)
            throw new InvalidDataException("expected mono 16-bit PCM");

        const double start = 0.25;
        const double length = 1.0;
        var first = (int)(start * sampleRate);
        var samples = Math.Min((int)(length * sampleRate), data.Length / 2 - first);
        var crossings = 0;
        var previous = 0;
        for (var index = 0; index < samples; index++)
        {
            var sample = BitConverter.ToInt16(data, (first + index) * 2);
            var sign = sample > 100 ? 1 : sample < -100 ? -1 : 0;
            if (sign == 0)
                continue;
            if (previous != 0 && sign != previous)
                crossings++;
            previous = sign;
        }
        return crossings / (2.0 * samples / sampleRate);
    }

    private static async Task IcaclsAsync(string directory, string option, string permission)
    {
        var startInfo = new ProcessStartInfo("icacls")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(directory);
        startInfo.ArgumentList.Add(option);
        startInfo.ArgumentList.Add(permission);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("icacls could not be started");
        var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        var error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"icacls {option} failed ({process.ExitCode}): {error}{output}");
    }

    private static void AssertNoOutput(string directory)
    {
        if (!Directory.Exists(directory))
            return;
        Assert.Empty(Directory.GetFiles(directory, "*", SearchOption.AllDirectories));
    }

    internal sealed record ProbeResult(double Duration, int Width, int Height, bool HasAudio, bool HasVideo);

    /// <summary>Generates the shared 4 s sample once per test class and deletes everything afterwards.</summary>
    public sealed class Samples : IDisposable
    {
        private readonly string _longClipPath;

        public Samples()
        {
            Ffmpeg = RealFfmpeg.Path
                ?? throw new InvalidOperationException("FFmpeg is not installed; the FfmpegFact attribute should have skipped.");
            Ffprobe = ResolveFfprobe(Ffmpeg);
            Directory = Path.Combine(
                Path.GetTempPath(), "DropWheelVideoTests", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);

            Clip = Path.Combine(Directory, "sample.mp4");
            FfmpegRun(
                "-f", "lavfi", "-i", "testsrc=size=320x240:rate=30:duration=4",
                "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100:duration=4",
                "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p",
                "-c:a", "aac", "-b:a", "128k", "-shortest", Clip);

            _longClipPath = Path.Combine(Directory, "long.mp4");
        }

        public string Directory { get; }

        public string Clip { get; }

        public string Ffmpeg { get; }

        public string Ffprobe { get; }

        /// <summary>A 10 s 1280x720 clip generated once for the cancellation test.</summary>
        public string LongClip
        {
            get
            {
                if (!File.Exists(_longClipPath))
                {
                    FfmpegRun(
                        "-f", "lavfi", "-i", "testsrc=size=1280x720:rate=30:duration=10",
                        "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p",
                        _longClipPath);
                }
                return _longClipPath;
            }
        }

        public string NewDirectory(string name)
        {
            var path = Path.Combine(Directory, name);
            System.IO.Directory.CreateDirectory(path);
            return path;
        }

        public string NewOutputDirectory(string name)
        {
            var path = Path.Combine(Directory, "out", name);
            System.IO.Directory.CreateDirectory(path);
            return path;
        }

        private void FfmpegRun(params string[] arguments)
        {
            var args = new List<string> { "-hide_banner", "-nostdin", "-y" };
            args.AddRange(arguments);
            ProcessRunner.RunAsync(Ffmpeg, args, default).GetAwaiter().GetResult();
        }

        private static string ResolveFfprobe(string ffmpeg)
        {
            var name = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
            var beside = Path.Combine(Path.GetDirectoryName(ffmpeg) ?? string.Empty, name);
            return File.Exists(beside) ? beside : "ffprobe";
        }

        public void Dispose()
        {
            try
            {
                if (System.IO.Directory.Exists(Directory))
                    System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch (Exception)
            {
                // A locked temp file must not turn a green run red.
            }
        }
    }

    /// <summary>Snapshot of the source file: content, length and timestamp must not change.</summary>
    private sealed record FileSnapshot(byte[] Hash, long Length, DateTime LastWriteTimeUtc)
    {
        public static async Task<FileSnapshot> TakeAsync(string path)
        {
            await using var stream = File.OpenRead(path);
            var hash = await SHA256.HashDataAsync(stream);
            var info = new FileInfo(path);
            return new FileSnapshot(hash, info.Length, info.LastWriteTimeUtc);
        }

        public async Task AssertUnchangedAsync(string path)
        {
            var after = await TakeAsync(path);
            Assert.Equal(Convert.ToHexString(Hash), Convert.ToHexString(after.Hash));
            Assert.Equal(Length, after.Length);
            Assert.Equal(LastWriteTimeUtc, after.LastWriteTimeUtc);
        }
    }
}

/// <summary>Runs only when FFmpeg is installed; otherwise xUnit reports the test as skipped with this reason.</summary>
public sealed class FfmpegFactAttribute : FactAttribute
{
    public FfmpegFactAttribute()
    {
        if (RealFfmpeg.Path is null)
            Skip = "ffmpeg is not installed; real-FFmpeg video function tests are skipped.";
    }
}

internal static class RealFfmpeg
{
    /// <summary>
    /// ToolLocator covers PATH, next-to-the-app and the WinGet link; CI installs FFmpeg with Chocolatey,
    /// whose shim directory is checked explicitly because the Test step is a new process.
    /// </summary>
    public static readonly string? Path = Resolve();

    private static string? Resolve()
    {
        var located = new ToolLocator().Ffmpeg;
        if (located is not null)
            return located;

        var chocolatey = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "chocolatey", "bin", "ffmpeg.exe");
        return File.Exists(chocolatey) ? chocolatey : null;
    }
}
