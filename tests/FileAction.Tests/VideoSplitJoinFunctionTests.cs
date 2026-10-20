using System.Globalization;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using FileAction.App.UI.Tools.Video;
using FileAction.Core;
using Xunit;

namespace FileAction.Tests;

/// <summary>
/// Function tests for the Split and Join runners: they drive the real <see cref="VideoSplitRunner"/> and
/// <see cref="VideoJoinRunner"/> against real FFmpeg, then check the written files with ffprobe (lengths,
/// canvas, frame rate, audio) and check the failure paths on disk. Samples are generated with FFmpeg's
/// testsrc2 and sine sources; when FFmpeg or ffprobe is not installed the tests skip, like the other
/// integration samples in this suite.
/// </summary>
[Trait("Category", "Integration")]
public class VideoSplitJoinFunctionTests : IClassFixture<VideoSplitJoinFunctionTests.Samples>
{
    private readonly Samples _samples;

    public VideoSplitJoinFunctionTests(Samples samples) => _samples = samples;

    public sealed class Samples : IDisposable
    {
        public string Dir { get; } = OutputPaths.TempDirectory();
        public string MediaDir { get; }
        public string? Ffmpeg { get; }
        public string? Ffprobe { get; }
        public string? Trip { get; private set; }
        public string? Wide { get; private set; }
        public string? Silent { get; private set; }
        public string? Heavy { get; private set; }

        public bool Available => Ffmpeg is not null && Ffprobe is not null;

        public Samples()
        {
            MediaDir = Path.Combine(Dir, "media");
            Directory.CreateDirectory(MediaDir);
            Ffmpeg = new ToolLocator().Find(ExternalTool.Ffmpeg);
            Ffprobe = LocateFfprobe(Ffmpeg);
            if (!Available)
                return;

            // 4 s, 320x240, 30 fps with a tone: the split sample the task asks for.
            Trip = Create("trip.mp4", 320, 240, 30, 4, tone: 440);
            // Different size and frame rate, and one clip without audio at all.
            Wide = Create("wide.mp4", 480, 360, 24, 3, tone: 660);
            Silent = Create("silent.mp4", 640, 360, 25, 2.5, tone: null);
            // Legacy container for the re-encode path (AVI cannot be stream-copied into MP4 parts).
            Heavy = Create("heavy.avi", 1280, 720, 30, 10, tone: 440);
        }

        public void Dispose() => OutputPaths.TryDelete(Dir);

        private string Create(string name, int width, int height, int fps, double seconds, int? tone)
        {
            var path = Path.Combine(MediaDir, name);
            var args = new List<string>
            {
                "-hide_banner", "-nostdin", "-y",
                "-f", "lavfi", "-i",
                FormattableString.Invariant($"testsrc2=size={width}x{height}:rate={fps}:duration={seconds}"),
            };
            if (tone is int frequency)
            {
                args.AddRange(
                [
                    "-f", "lavfi", "-i",
                    FormattableString.Invariant($"sine=frequency={frequency}:sample_rate=44100:duration={seconds}"),
                ]);
            }

            if (name.EndsWith(".avi", StringComparison.OrdinalIgnoreCase))
                args.AddRange(["-c:v", "mpeg4", "-q:v", "5"]);
            else
                args.AddRange(["-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p"]);
            if (tone is not null)
            {
                // AVI predates AAC; MP3 audio keeps the legacy sample playable by its own muxer.
                if (name.EndsWith(".avi", StringComparison.OrdinalIgnoreCase))
                    args.AddRange(["-c:a", "libmp3lame", "-q:a", "5"]);
                else
                    args.AddRange(["-c:a", "aac"]);
                args.Add("-shortest");
            }
            args.Add(path);

            ProcessRunner.RunAsync(Ffmpeg!, args, CancellationToken.None).GetAwaiter().GetResult();
            return path;
        }

        /// <summary>ffprobe ships beside FFmpeg in the official builds; the PATH lookup is the fallback.</summary>
        private static string? LocateFfprobe(string? ffmpeg)
        {
            if (ffmpeg is not null)
            {
                var folder = Path.GetDirectoryName(ffmpeg);
                if (folder is not null)
                {
                    var sibling = Path.Combine(folder, "ffprobe.exe");
                    if (File.Exists(sibling))
                        return sibling;
                }
            }

            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            path += Path.PathSeparator + Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User)
                + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine);
            foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var candidate = Path.Combine(folder.Trim('"'), "ffprobe.exe");
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch (ArgumentException)
                {
                    // A malformed PATH entry is not a reason to stop looking.
                }
            }
            return null;
        }
    }

    // ---------------------------------------------------------------- split

    [Fact]
    public async Task Split_writes_three_parts_into_one_new_folder_with_the_requested_ranges()
    {
        if (!_samples.Available)
            return;

        var source = _samples.Trip!;
        var folder = Path.GetDirectoryName(source)!;
        var before = Directory.GetFileSystemEntries(folder).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sourceHash = Hash(source);
        var duration = Probe(source).Duration;

        var plan = VideoSplitJoin.Plan(source, "Parts", duration, [1.0, 2.0]);
        Assert.Equal(3, plan.Count);

        var reported = new List<double>();
        var partsFolder = await VideoSplitRunner.RunAsync(
            _samples.Ffmpeg!, plan, new InlineProgress(reported.Add), CancellationToken.None);

        // One new folder named "<name> Parts", exactly three numbered parts, no stray files.
        Assert.True(Directory.Exists(partsFolder), "the parts folder was not created");
        Assert.Equal("trip Parts", Path.GetFileName(partsFolder));
        Assert.Empty(Directory.GetDirectories(partsFolder));
        var files = Directory.GetFiles(partsFolder).OrderBy(Path.GetFileName, StringComparer.Ordinal).ToList();
        Assert.Equal(3, files.Count);
        Assert.Equal(
            new[] { "trip 01.mp4", "trip 02.mp4", "trip 03.mp4" },
            files.Select(file => Path.GetFileName(file)!).ToArray());

        // Each part lasts its range within one source frame; the parts add up to the source.
        var frame = 1.0 / 30;
        var tolerance = frame + 0.01; // an AAC stream copy keeps a few milliseconds of priming
        var total = 0d;
        for (var index = 0; index < plan.Segments.Count; index++)
        {
            var part = Probe(files[index]);
            AssertClose(plan.Segments[index].Duration, part.Duration, tolerance, $"part {index + 1} length");
            Assert.Equal(320, part.Width);
            Assert.Equal(240, part.Height);
            Assert.True(part.HasAudio, $"part {index + 1} lost its audio");
            total += part.Duration;
        }
        AssertClose(duration, total, 0.05 * plan.Count, "parts total");

        // The source is only read: same bytes, and the only new entry is the parts folder.
        Assert.Equal(sourceHash, Hash(source));
        Assert.Single(Directory.GetFileSystemEntries(folder).Except(before));
        Assert.Equal(3, reported.Count);
        AssertClose(1.0, reported[^1], 0.001, "progress");
    }

    // ---------------------------------------------------------------- join

    [Fact]
    public async Task Join_uses_the_first_clip_canvas_and_keeps_a_silent_clip_silent()
    {
        if (!_samples.Available)
            return;

        var clips = new List<ClipInfo>
        {
            ToClip(_samples.Trip!),
            ToClip(_samples.Wide!),
            ToClip(_samples.Silent!),
        };
        Assert.True(clips[0].HasAudio);
        Assert.False(clips[2].HasAudio);
        Assert.NotEqual(clips[0].Width, clips[1].Width);
        Assert.NotEqual(clips[0].Fps, clips[1].Fps);

        var expected = clips.Sum(clip => clip.Duration);
        var joined = await VideoJoinRunner.RunAsync(
            _samples.Ffmpeg!, clips, new InlineProgress(_ => { }), CancellationToken.None);

        Assert.True(File.Exists(joined), "Joined.mp4 was not written");
        Assert.Equal("Joined.mp4", Path.GetFileName(joined));
        Assert.Equal(Path.GetDirectoryName(_samples.Trip!), Path.GetDirectoryName(joined));

        var output = Probe(joined);
        Assert.Equal(clips[0].Width, output.Width);
        Assert.Equal(clips[0].Height, output.Height);
        AssertClose(clips[0].Fps, output.Fps, 0.01, "output frame rate");
        Assert.True(output.HasAudio, "the joined file has no audio stream");
        // One frame of the output, plus the few milliseconds the source files carry as AAC priming.
        AssertClose(expected, output.Duration, (1.0 / clips[0].Fps) + 0.01, "joined length");

        // The silent clip's window is silent, and the first clip's window still carries its tone.
        var silentStart = clips[0].Duration + clips[1].Duration + 0.3;
        var silentLength = Math.Max(0.5, clips[2].Duration - 0.6);
        var silentLevel = MeanVolumeDb(joined, silentStart, silentLength);
        Assert.True(silentLevel < -60, FormattableString.Invariant($"the silent clip is audible: {silentLevel} dB"));
        var toneLevel = MeanVolumeDb(joined, 0.3, 2.0);
        Assert.True(toneLevel > -40, FormattableString.Invariant($"the first clip's tone is missing: {toneLevel} dB"));
    }

    // ---------------------------------------------------------------- failure paths

    [Fact]
    public async Task A_corrupt_input_fails_with_an_error_and_leaves_no_part_folder()
    {
        if (!_samples.Available)
            return;

        var folder = Path.Combine(_samples.Dir, "corrupt");
        Directory.CreateDirectory(folder);
        var broken = Path.Combine(folder, "broken.mp4");
        File.WriteAllBytes(broken, [.. Enumerable.Range(0, 2048).Select(value => (byte)(value * 37))]);

        var plan = VideoSplitJoin.Plan(broken, "Parts", 4, [1.5]);
        var error = await Assert.ThrowsAsync<ConversionException>(() =>
            VideoSplitRunner.RunAsync(_samples.Ffmpeg!, plan, new InlineProgress(_ => { }), CancellationToken.None));

        Assert.False(string.IsNullOrWhiteSpace(error.Message), "the failure had no message");
        Assert.False(Directory.Exists(Path.Combine(folder, "broken Parts")), "a part folder was left behind");
        Assert.Equal(new[] { broken }, Directory.GetFiles(folder));
    }

    [Fact]
    public async Task A_missing_ffmpeg_fails_with_an_error_and_leaves_no_part_folder()
    {
        if (!_samples.Available)
            return;

        var folder = Path.Combine(_samples.Dir, "missing");
        Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, "trip.mp4");
        File.Copy(_samples.Trip!, source);
        var missing = Path.Combine(_samples.Dir, "no-such-tool", "ffmpeg.exe");

        var plan = VideoSplitJoin.Plan(source, "Parts", 4, [2]);
        var error = await Assert.ThrowsAsync<ConversionException>(() =>
            VideoSplitRunner.RunAsync(missing, plan, new InlineProgress(_ => { }), CancellationToken.None));

        Assert.Contains("ffmpeg", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(folder, "trip Parts")), "a part folder was left behind");
        Assert.Equal(new[] { source }, Directory.GetFiles(folder));
    }

    [Fact]
    public async Task A_read_only_folder_fails_with_the_write_error_and_leaves_nothing()
    {
        if (!_samples.Available)
            return;

        var folder = Path.Combine(_samples.Dir, "readonly");
        Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, "trip.mp4");
        File.Copy(_samples.Trip!, source);
        var plan = VideoSplitJoin.Plan(source, "Parts", 4, [2]);

        var restore = DenyCreatingEntries(folder);
        try
        {
            var error = await Assert.ThrowsAsync<ConversionException>(() =>
                VideoSplitRunner.RunAsync(_samples.Ffmpeg!, plan, new InlineProgress(_ => { }), CancellationToken.None));

            Assert.Equal(Loc.T("error.output.notWritable"), error.Message);
            Assert.False(Directory.Exists(Path.Combine(folder, "trip Parts")), "a part folder was left behind");
            Assert.Equal(new[] { source }, Directory.GetFiles(folder));
        }
        finally
        {
            restore();
        }
    }

    [Fact]
    public async Task A_join_into_a_read_only_folder_fails_with_the_write_error_and_leaves_nothing()
    {
        if (!_samples.Available)
            return;

        var folder = Path.Combine(_samples.Dir, "join-readonly");
        Directory.CreateDirectory(folder);
        var first = Path.Combine(folder, "trip.mp4");
        var second = Path.Combine(folder, "silent.mp4");
        File.Copy(_samples.Trip!, first);
        File.Copy(_samples.Silent!, second);
        var clips = new List<ClipInfo> { ToClip(first), ToClip(second) };

        var restore = DenyCreatingEntries(folder);
        try
        {
            var error = await Assert.ThrowsAsync<ConversionException>(() =>
                VideoJoinRunner.RunAsync(_samples.Ffmpeg!, clips, new InlineProgress(_ => { }), CancellationToken.None));

            Assert.Equal(Loc.T("error.output.notWritable"), error.Message);
            Assert.Equal(new[] { first, second }.OrderBy(path => path, StringComparer.Ordinal),
                Directory.GetFiles(folder).OrderBy(path => path, StringComparer.Ordinal));
        }
        finally
        {
            restore();
        }
    }

    [Fact]
    public async Task Cancelling_mid_run_removes_the_part_folder_it_created()
    {
        if (!_samples.Available)
            return;

        var folder = Path.Combine(_samples.Dir, "cancel");
        Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, "heavy.avi");
        File.Copy(_samples.Heavy!, source);
        var plan = VideoSplitJoin.Plan(source, "Parts", 10, [1, 2, 3]);
        Assert.False(plan.CanCopy); // AVI parts are re-encoded, so the run is still busy when it is cancelled

        using var cts = new CancellationTokenSource();
        // The progress callback runs inside the runner, so cancellation lands between the first and second part.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            VideoSplitRunner.RunAsync(_samples.Ffmpeg!, plan, new InlineProgress(_ => cts.Cancel()), cts.Token));

        Assert.False(Directory.Exists(Path.Combine(folder, "heavy Parts")), "a partial part folder was left behind");
        Assert.Equal(new[] { source }, Directory.GetFiles(folder));
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Synchronous progress, so a test can cancel at an exact point in the runner.</summary>
    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    private sealed record Probed(int Width, int Height, double Fps, double Duration, bool HasAudio);

    private ClipInfo ToClip(string path)
    {
        var probed = Probe(path);
        return new ClipInfo(path, probed.Duration, probed.Width, probed.Height, probed.Fps, probed.HasAudio);
    }

    private Probed Probe(string path) => ProbeFile(path, _samples.Ffprobe!);

    private static Probed ProbeFile(string path, string ffprobe)
    {
        var json = ProcessRunner.RunAsync(
            ffprobe,
            ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", path],
            CancellationToken.None).GetAwaiter().GetResult();

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var duration = 0d;
        if (root.TryGetProperty("format", out var format)
            && format.TryGetProperty("duration", out var length))
        {
            duration = length.ValueKind == JsonValueKind.String
                ? double.Parse(length.GetString() ?? "0", CultureInfo.InvariantCulture)
                : length.GetDouble();
        }

        var width = 0;
        var height = 0;
        var fps = 0d;
        var hasAudio = false;
        foreach (var stream in root.GetProperty("streams").EnumerateArray())
        {
            var type = stream.GetProperty("codec_type").GetString();
            if (type == "video" && width == 0)
            {
                width = stream.GetProperty("width").GetInt32();
                height = stream.GetProperty("height").GetInt32();
                fps = Rate(stream.GetProperty("r_frame_rate").GetString());
            }
            else if (type == "audio")
            {
                hasAudio = true;
            }
        }

        return new Probed(width, height, fps, duration, hasAudio);
    }

    /// <summary>Mean volume of one window of the file, as ffmpeg's volumedetect reports it.</summary>
    private double MeanVolumeDb(string path, double start, double length)
    {
        var lines = new List<string>();
        ProcessRunner.RunAsync(
            _samples.Ffmpeg!,
            [
                "-hide_banner", "-nostdin", "-v", "info",
                "-ss", start.ToString("0.###", CultureInfo.InvariantCulture),
                "-t", length.ToString("0.###", CultureInfo.InvariantCulture),
                "-i", path, "-vn", "-af", "volumedetect", "-f", "null", "-",
            ],
            CancellationToken.None,
            line =>
            {
                lock (lines)
                    lines.Add(line);
            },
            throwOnError: false).GetAwaiter().GetResult();

        string text;
        lock (lines)
            text = string.Join("\n", lines);
        var match = Regex.Match(text, @"mean_volume:\s*(-?[\d.]+)\s*dB");
        Assert.True(match.Success, "ffmpeg did not report a mean volume for the window");
        return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static double Rate(string? rate)
    {
        if (string.IsNullOrEmpty(rate))
            return 0;
        var parts = rate.Split('/');
        if (parts.Length != 2
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator)
            || denominator == 0)
        {
            return double.TryParse(rate, NumberStyles.Float, CultureInfo.InvariantCulture, out var single) ? single : 0;
        }
        return numerator / denominator;
    }

    [Fact]
    public async Task Split_honours_the_detected_drop_folder()
    {
        if (!_samples.Available)
            return;

        var source = _samples.Trip!;
        var drop = Path.Combine(Path.GetTempPath(), "dropwheel-split-drop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(drop);
        try
        {
            var plan = VideoSplitJoin.Plan(source, "Parts", Probe(source).Duration, [1.0]);
            var folder = await VideoSplitRunner.RunAsync(
                _samples.Ffmpeg!, plan, new InlineProgress(_ => { }), CancellationToken.None,
                outputDirectory: drop);

            Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(drop)), Path.GetDirectoryName(folder));
            Assert.True(File.Exists(Path.Combine(folder, "trip 02.mp4")), "the second part was not written into the drop folder");
        }
        finally
        {
            Directory.Delete(drop, recursive: true);
        }
    }

    [Fact]
    public async Task Join_honours_the_detected_drop_folder()
    {
        if (!_samples.Available)
            return;

        var drop = Path.Combine(Path.GetTempPath(), "dropwheel-join-drop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(drop);
        try
        {
            var clips = new List<ClipInfo> { ToClip(_samples.Trip!), ToClip(_samples.Wide!) };
            var joined = await VideoJoinRunner.RunAsync(
                _samples.Ffmpeg!, clips, new InlineProgress(_ => { }), CancellationToken.None,
                outputDirectory: drop);

            Assert.Equal(Path.Combine(drop, "Joined.mp4"), joined);
            Assert.True(File.Exists(joined));
        }
        finally
        {
            Directory.Delete(drop, recursive: true);
        }
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static void AssertClose(double expected, double actual, double tolerance, string what) =>
        Assert.True(
            Math.Abs(expected - actual) <= tolerance,
            FormattableString.Invariant($"{what}: expected {expected:0.###} ± {tolerance:0.###}, got {actual:0.###}"));

    /// <summary>
    /// Denies creating entries in a folder for the current user and returns the undo action. A deny ACE
    /// applies to administrators too, so the folder behaves like a read-only target on any CI runner.
    /// </summary>
    private static Action DenyCreatingEntries(string folder)
    {
        var directory = new DirectoryInfo(folder);
        var security = directory.GetAccessControl();
        var identity = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("the current Windows identity has no user SID");
        var rule = new FileSystemAccessRule(
            identity,
            FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Deny);
        security.AddAccessRule(rule);
        directory.SetAccessControl(security);

        return () =>
        {
            var restore = directory.GetAccessControl();
            restore.RemoveAccessRule(rule);
            directory.SetAccessControl(restore);
        };
    }
}
