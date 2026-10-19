using System.Globalization;
using FileAction.App.UI.Tools;
using FileAction.App.UI.Tools.Video;
using FileAction.Core;

namespace FileAction.Tests;

/// <summary>
/// The pure video-tool rules: time parsing/formatting, frame stepping, even crop geometry, output
/// containers and the exact FFmpeg argument lists. No FFmpeg process is started here.
/// </summary>
public class VideoEditTests
{
    // ---------------------------------------------------------------- time

    [Theory]
    [InlineData("01:02:03.004", 3723.004)]
    [InlineData("1:02:03.5", 3723.5)]
    [InlineData("02:03.250", 123.25)]
    [InlineData("90", 90)]
    [InlineData("0", 0)]
    [InlineData("00:00:01,500", 1.5)]
    [InlineData("90:00", 5400)]
    [InlineData("100:00:00.000", 360000)]
    public void TryParseTime_accepts_the_trim_box_forms(string text, double seconds)
    {
        Assert.True(VideoEdit.TryParseTime(text, out var value), text);
        Assert.Equal(seconds, value.TotalSeconds, 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("1:75")]
    [InlineData("1:2:3:4")]
    [InlineData("1::2")]
    [InlineData("1.2.3")]
    [InlineData("1e3")]
    public void TryParseTime_rejects_bad_input(string text)
    {
        Assert.False(VideoEdit.TryParseTime(text, out _), text);
    }

    [Fact]
    public void FormatTime_has_three_decimals_and_grows_the_hour_field()
    {
        Assert.Equal("01:02:03.004", VideoEdit.FormatTime(TimeSpan.FromMilliseconds(3_723_004)));
        Assert.Equal("00:00:00.000", VideoEdit.FormatTime(TimeSpan.Zero));
        Assert.Equal("25:00:00.000", VideoEdit.FormatTime(TimeSpan.FromHours(25)));
        Assert.Equal("00:00:00.000", VideoEdit.FormatTime(TimeSpan.FromSeconds(-5)));
    }

    [Fact]
    public void Format_time_round_trips_through_the_parser()
    {
        foreach (var seconds in new[] { 0.0, 0.001, 1.5, 59.999, 60, 3599.25, 3723.004, 90000.0 })
        {
            var original = TimeSpan.FromSeconds(seconds);
            var text = VideoEdit.FormatTime(original);
            Assert.True(VideoEdit.TryParseTime(text, out var parsed), text);
            Assert.Equal((long)original.TotalMilliseconds, (long)parsed.TotalMilliseconds);
        }
    }

    [Fact]
    public void FormatClock_is_compact()
    {
        Assert.Equal("0:00", VideoEdit.FormatClock(TimeSpan.Zero));
        Assert.Equal("2:07", VideoEdit.FormatClock(TimeSpan.FromSeconds(127)));
        Assert.Equal("1:02:03", VideoEdit.FormatClock(TimeSpan.FromSeconds(3723)));
    }

    [Fact]
    public void Frame_stepping_stays_inside_the_file()
    {
        var duration = TimeSpan.FromSeconds(10);
        var step = VideoEdit.FrameStep(30);
        Assert.Equal(1.0 / 30, step.TotalSeconds, 6);

        var next = VideoEdit.StepFrames(TimeSpan.FromSeconds(1), 30, +1, duration);
        Assert.Equal(1 + 1.0 / 30, next.TotalSeconds, 6);

        var previous = VideoEdit.StepFrames(TimeSpan.FromSeconds(1), 30, -1, duration);
        Assert.Equal(1 - 1.0 / 30, previous.TotalSeconds, 6);

        Assert.Equal(TimeSpan.Zero, VideoEdit.StepFrames(TimeSpan.Zero, 30, -1, duration));
        Assert.Equal(duration, VideoEdit.StepFrames(duration, 30, +1, duration));

        // An unusable frame rate falls back to 30 rather than producing a zero-length step.
        Assert.Equal(1 + 1.0 / 30, VideoEdit.StepFrames(TimeSpan.FromSeconds(1), 0, +1, duration).TotalSeconds, 6);
    }

    [Fact]
    public void Clamp_keeps_zero_to_duration()
    {
        var duration = TimeSpan.FromSeconds(5);
        Assert.Equal(TimeSpan.Zero, VideoEdit.Clamp(TimeSpan.FromSeconds(-1), duration));
        Assert.Equal(TimeSpan.FromSeconds(3), VideoEdit.Clamp(TimeSpan.FromSeconds(3), duration));
        Assert.Equal(duration, VideoEdit.Clamp(TimeSpan.FromSeconds(9), duration));
        Assert.Equal(TimeSpan.Zero, VideoEdit.Clamp(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(-1)));
    }

    // ---------------------------------------------------------------- probe parsing

    private static readonly string[] Banner =
    [
        "Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'C:\\videos\\holiday 2024.mp4':",
        "  Metadata:",
        "    major_brand     : isom",
        "  Duration: 00:01:02.50, start: 0.000000, bitrate: 2500 kb/s",
        "  Stream #0:0[0x1](und): Video: h264 (High) (avc1 / 0x31637661), yuv420p(tv, bt709), 1920x1080 [SAR 1:1 DAR 16:9], 2200 kb/s, 29.97 fps, 29.97 tbr, 90k tbn (default)",
        "  Stream #0:1[0x2](und): Audio: aac (LC) (mp4a / 0x6134706D), 48000 Hz, stereo, fltp, 128 kb/s (default)",
    ];

    [Fact]
    public void Probe_reads_duration_size_fps_and_audio()
    {
        var info = VideoProbe.Parse(Banner);
        Assert.NotNull(info);
        Assert.Equal(62.5, info!.Duration.TotalSeconds, 3);
        Assert.Equal(1920, info.Width);
        Assert.Equal(1080, info.Height);
        Assert.Equal(29.97, info.Fps, 3);
        Assert.True(info.HasVideo);
        Assert.True(info.HasAudio);
        Assert.Equal(0.0, info.Rotation);

        // The codec tag "0x31637661" must never be read as a size.
        Assert.NotEqual(31637661, info.Height);
    }

    [Fact]
    public void Probe_swaps_a_rotated_video_to_its_display_size()
    {
        var info = VideoProbe.Parse(
        [
            "  Duration: 00:00:10.00, start: 0.000000, bitrate: 2000 kb/s",
            "  Stream #0:0[0x1](und): Video: h264 (High) (avc1 / 0x31637661), yuv420p, 1920x1080 [SAR 1:1 DAR 16:9], 30 fps, 30 tbr",
            "      displaymatrix: rotation of -90.00 degrees",
        ]);

        Assert.NotNull(info);
        Assert.Equal(1080, info!.Width);
        Assert.Equal(1920, info.Height);
        Assert.Equal(270.0, info.Rotation);
    }

    [Fact]
    public void Probe_reads_a_legacy_rotate_metadata_tag()
    {
        var info = VideoProbe.Parse(
        [
            "  Duration: 00:00:10.00, start: 0.000000, bitrate: 2000 kb/s",
            "    rotate          : 90",
            "  Stream #0:0[0x1](und): Video: h264 (High) (avc1 / 0x31637661), yuv420p, 1920x1080, 30 fps, 30 tbr",
        ]);

        Assert.NotNull(info);
        Assert.Equal(1080.0, info!.Width);
        Assert.Equal(1920.0, info.Height);
        Assert.Equal(90.0, info.Rotation);
    }

    [Fact]
    public void Probe_defaults_to_30_fps_and_no_audio()
    {
        var info = VideoProbe.Parse(
        [
            "  Duration: 00:00:05.00, start: 0.000000, bitrate: 1000 kb/s",
            "  Stream #0:0[0x1](und): Video: vp9, yuv420p, 1280x720, 800 kb/s, 24 tbr, 1k tbn",
        ]);

        Assert.NotNull(info);
        Assert.Equal(30.0, info!.Fps);
        Assert.False(info.HasAudio);
        Assert.True(info.HasVideo);
    }

    [Fact]
    public void Probe_rejects_audio_only_and_unknown_durations()
    {
        Assert.Null(VideoProbe.Parse(
        [
            "  Duration: 00:00:05.00, start: 0.000000, bitrate: 128 kb/s",
            "  Stream #0:0[0x1](und): Audio: mp3, 48000 Hz, stereo, fltp, 128 kb/s",
        ]));

        Assert.Null(VideoProbe.Parse(
        [
            "  Duration: N/A, start: 0.000000, bitrate: N/A",
            "  Stream #0:0[0x1](und): Video: h264, yuv420p, 640x360, 25 fps, 25 tbr",
        ]));

        Assert.Null(VideoProbe.Parse([]));
    }

    // ---------------------------------------------------------------- containers

    [Theory]
    [InlineData("mp4", "mp4")]
    [InlineData("mov", "mov")]
    [InlineData("mkv", "mkv")]
    [InlineData("webm", "webm")]
    [InlineData("avi", "mp4")]
    [InlineData("wmv", "mp4")]
    [InlineData("mts", "mp4")]
    [InlineData("m4v", "mp4")]
    [InlineData(".MP4", "mp4")]
    public void Output_container_normalizes_legacy_formats(string format, string expected) =>
        Assert.Equal(expected, VideoEdit.OutputContainerFor(format));

    [Theory]
    [InlineData("mp4", true)]
    [InlineData("mov", true)]
    [InlineData("mkv", true)]
    [InlineData("webm", true)]
    [InlineData("avi", false)]
    [InlineData("wmv", false)]
    [InlineData("3gp", false)]
    public void Stream_copy_is_only_offered_for_copy_safe_containers(string container, bool expected) =>
        Assert.Equal(expected, VideoEdit.SupportsStreamCopy(container));

    // ---------------------------------------------------------------- trim

    [Fact]
    public void Trim_stream_copy_args_use_start_duration_and_copy()
    {
        var args = VideoEdit.BuildTrimArgs(new VideoTrimRequest(
            @"C:\in\clip.mp4", @"C:\out\clip Trimmed.mp4",
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), "mp4", VideoTrimMode.StreamCopy, HasAudio: true));

        AssertPair(args, "-ss", "00:00:05.000");
        AssertPair(args, "-i", @"C:\in\clip.mp4");
        AssertPair(args, "-t", "00:00:10.000");
        AssertPair(args, "-c", "copy");
        Assert.DoesNotContain("-avoid_negative_ts", args);
        AssertPair(args, "-map", "0:v:0");
        AssertPair(args, "-map", "0:a?");
        AssertPair(args, "-movflags", "+faststart");
        Assert.Equal(@"C:\out\clip Trimmed.mp4", args[^1]);

        Assert.DoesNotContain("-c:v", args);
        Assert.DoesNotContain("libx264", args);
        Assert.True(Index(args, "-ss") < Index(args, "-i"), "the seek must be an input option");

        // The duration is passed after the input, as an output option.
        Assert.True(Index(args, "-t") > Index(args, "-i"));
    }

    [Fact]
    public void Trim_stream_copy_without_audio_keeps_only_the_video()
    {
        var args = VideoEdit.BuildTrimArgs(new VideoTrimRequest(
            "in.mkv", "out.mkv", TimeSpan.Zero, TimeSpan.FromSeconds(2), "mkv", VideoTrimMode.StreamCopy, false));

        AssertPair(args, "-c", "copy");
        Assert.DoesNotContain("0:a?", args);
        Assert.DoesNotContain("-movflags", args);
    }

    [Fact]
    public void Trim_reencode_args_use_h264_and_aac()
    {
        var args = VideoEdit.BuildTrimArgs(new VideoTrimRequest(
            "in.mp4", "out.mp4", TimeSpan.Zero, TimeSpan.FromSeconds(2), "mp4", VideoTrimMode.ReEncode, HasAudio: true));

        AssertPair(args, "-c:v", "libx264");
        AssertPair(args, "-c:a", "aac");
        AssertPair(args, "-movflags", "+faststart");
        Assert.DoesNotContain("copy", args);
        Assert.Equal("out.mp4", args[^1]);
    }

    [Fact]
    public void Trim_reencode_webm_uses_vp9_and_opus()
    {
        var args = VideoEdit.BuildTrimArgs(new VideoTrimRequest(
            "in.webm", "out.webm", TimeSpan.Zero, TimeSpan.FromSeconds(2), "webm", VideoTrimMode.ReEncode, true));

        AssertPair(args, "-c:v", "libvpx-vp9");
        AssertPair(args, "-c:a", "libopus");
        Assert.DoesNotContain("-an", args);
        Assert.DoesNotContain("-movflags", args);
    }

    [Fact]
    public void Trim_rejects_an_empty_or_reversed_range()
    {
        Assert.Throws<ArgumentException>(() => VideoEdit.BuildTrimArgs(new VideoTrimRequest(
            "in.mp4", "out.mp4", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), "mp4", VideoTrimMode.ReEncode, true)));
        Assert.Throws<ArgumentException>(() => VideoEdit.BuildTrimArgs(new VideoTrimRequest(
            "in.mp4", "out.mp4", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(4), "mp4", VideoTrimMode.ReEncode, true)));
    }

    // ---------------------------------------------------------------- crop

    [Fact]
    public void EvenCrop_rounds_to_even_pixels_inside_the_frame()
    {
        var rect = VideoEdit.EvenCrop(new CropRect(10.4, 20.6, 1001.2, 501.9), 1920, 1080);
        Assert.Equal(10.0, rect.X);
        Assert.Equal(20.0, rect.Y);
        Assert.Equal(1000.0, rect.Width);
        Assert.Equal(502.0, rect.Height);
    }

    [Fact]
    public void EvenCrop_clamps_a_rectangle_that_leaves_the_frame()
    {
        var rect = VideoEdit.EvenCrop(new CropRect(-50, -50, 4000, 4000), 1280, 720);
        Assert.Equal(0.0, rect.X);
        Assert.Equal(0.0, rect.Y);
        Assert.Equal(1280.0, rect.Width);
        Assert.Equal(720.0, rect.Height);

        var oddSource = VideoEdit.EvenCrop(CropMath.Full(1919, 1079), 1919, 1079);
        Assert.Equal(1918.0, oddSource.Width);
        Assert.Equal(1078.0, oddSource.Height);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(4, 4)]
    [InlineData(5, 4)]
    public void Even_returns_the_largest_small_even_value(int input, int expected) =>
        Assert.Equal(expected, VideoEdit.Even(input));

    [Fact]
    public void Crop_args_contain_an_even_crop_filter_and_copy_the_audio()
    {
        var args = VideoEdit.BuildCropArgs(new VideoCropRequest(
            "in.mp4", "out.mp4",
            new CropRect(10.4, 20.6, 1001.2, 501.9),
            1920, 1080, "mp4", HasAudio: true));

        AssertPair(args, "-vf", "crop=1000:502:10:20");
        AssertPair(args, "-c:a", "copy");
        AssertPair(args, "-map", "0:a:0");
        AssertPair(args, "-c:v", "libx264");
        Assert.Equal("out.mp4", args[^1]);
    }

    [Fact]
    public void Crop_can_encode_the_audio_when_copy_is_not_possible()
    {
        var args = VideoEdit.BuildCropArgs(new VideoCropRequest(
            "in.mov", "out.mp4", new CropRect(0, 0, 640, 360), 640, 360, "mp4",
            HasAudio: true, CopyAudio: false));

        AssertPair(args, "-c:a", "aac");
        Assert.DoesNotContain("copy", args);
    }

    [Fact]
    public void Crop_without_audio_is_silent()
    {
        var args = VideoEdit.BuildCropArgs(new VideoCropRequest(
            "in.webm", "out.webm", new CropRect(0, 0, 640, 360), 640, 360, "webm", HasAudio: false));

        Assert.Contains("-an", args);
        Assert.DoesNotContain("0:a:0", args);
        AssertPair(args, "-c:v", "libvpx-vp9");
    }

    [Fact]
    public void Crop_rejects_an_invalid_source_size()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => VideoEdit.BuildCropArgs(new VideoCropRequest(
            "in.mp4", "out.mp4", new CropRect(0, 0, 100, 100), 0, 0, "mp4", true)));
    }

    // ---------------------------------------------------------------- speed

    [Theory]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    [InlineData(3.0)]
    [InlineData(4.0)]
    public void Atempo_chain_multiplies_to_the_requested_speed(double speed)
    {
        var factors = VideoEdit.AtempoFactors(speed);
        Assert.NotEmpty(factors);
        Assert.All(factors, factor => Assert.InRange(factor, 0.5, 2.0));
        Assert.Equal(speed, factors.Aggregate(1.0, (product, factor) => product * factor), 6);
    }

    [Fact]
    public void Atempo_chain_splits_the_extremes()
    {
        Assert.Equal(new[] { 0.5, 0.5 }, VideoEdit.AtempoFactors(0.25));
        Assert.Equal(new[] { 2.0, 2.0 }, VideoEdit.AtempoFactors(4));
        Assert.Equal(new[] { 2.0, 1.5 }, VideoEdit.AtempoFactors(3));
        Assert.Equal(new[] { 1.0 }, VideoEdit.AtempoFactors(1));
    }

    [Fact]
    public void Speed_args_setpts_the_picture_and_atempo_the_audio()
    {
        var args = VideoEdit.BuildSpeedArgs(new VideoSpeedRequest(
            "in.mp4", "out.mp4", 2.0, "mp4", HasAudio: true));

        AssertPair(args, "-vf", "setpts=(PTS-STARTPTS)*0.5,fps=30");
        AssertPair(args, "-af", "atempo=2");
        AssertPair(args, "-c:v", "libx264");
        AssertPair(args, "-c:a", "aac");
        Assert.Equal("out.mp4", args[^1]);
    }

    [Fact]
    public void Speed_args_use_the_source_frame_rate_after_setpts()
    {
        var args = VideoEdit.BuildSpeedArgs(new VideoSpeedRequest(
            "in.mp4", "out.mp4", 2.0, "mp4", HasAudio: true, Fps: 25));

        AssertPair(args, "-vf", "setpts=(PTS-STARTPTS)*0.5,fps=25");
    }

    [Fact]
    public void Speed_args_chain_tempo_for_a_quarter_speed()
    {
        var args = VideoEdit.BuildSpeedArgs(new VideoSpeedRequest(
            "in.mp4", "out.mp4", 0.25, "mp4", HasAudio: true));

        AssertPair(args, "-vf", "setpts=(PTS-STARTPTS)*4,fps=30");
        AssertPair(args, "-af", "atempo=0.5,atempo=0.5");
    }

    [Fact]
    public void Speed_args_for_a_silent_video_skip_the_audio_filter()
    {
        var args = VideoEdit.BuildSpeedArgs(new VideoSpeedRequest(
            "in.mp4", "out.mp4", 1.5, "mp4", HasAudio: false));

        Assert.Contains("-an", args);
        Assert.DoesNotContain("-af", args);
        Assert.DoesNotContain("atempo", string.Join(' ', args));
    }

    [Fact]
    public void Speed_is_clamped_and_labelled()
    {
        Assert.Equal(4.0, VideoEdit.ClampSpeed(10));
        Assert.Equal(0.25, VideoEdit.ClampSpeed(0.1));
        Assert.Equal(1.0, VideoEdit.ClampSpeed(double.NaN));

        Assert.Equal("2x", VideoEdit.SpeedSuffix(2));
        Assert.Equal("0.5x", VideoEdit.SpeedSuffix(0.5));
        Assert.Equal("0.25x", VideoEdit.SpeedSuffix(0.25));
        Assert.Equal("1.25x", VideoEdit.SpeedSuffix(1.25));
        Assert.Equal("1x", VideoEdit.SpeedSuffix(1));
        Assert.Equal("1.5×", VideoEdit.SpeedText(1.5));
    }

    // ---------------------------------------------------------------- filmstrip

    [Fact]
    public void Strip_args_sample_evenly_and_cap_the_frame_count()
    {
        var args = VideoEdit.BuildStripArgs(
            "in.mp4", @"C:\temp\strip%02d.jpg", TimeSpan.FromSeconds(100), count: 10, width: 160);

        AssertPair(args, "-vf", "fps=0.1,scale=160:-2");
        AssertPair(args, "-frames:v", "10");
        Assert.Equal(@"C:\temp\strip%02d.jpg", args[^1]);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VideoEdit.BuildStripArgs("in.mp4", "strip%02d.jpg", TimeSpan.FromSeconds(10), 0, 160));
    }

    // ---------------------------------------------------------------- strings and helpers

    [Fact]
    public void The_three_video_prefixes_exist_in_both_tables()
    {
        var english = Loc.Table(Language.English);
        var arabic = Loc.Table(Language.Arabic);
        var keys = english.Keys
            .Where(key => key.StartsWith("tool.videotrim.", StringComparison.Ordinal)
                || key.StartsWith("tool.videocrop.", StringComparison.Ordinal)
                || key.StartsWith("tool.videospeed.", StringComparison.Ordinal))
            .ToArray();

        Assert.True(keys.Length >= 40, $"only {keys.Length} video-tool strings");
        foreach (var key in keys)
        {
            Assert.True(arabic.ContainsKey(key), $"ar.json is missing {key}");
            Assert.False(string.IsNullOrWhiteSpace(english[key]), key);
            Assert.False(string.IsNullOrWhiteSpace(arabic[key]), key);
            Assert.NotEqual(english[key], arabic[key]);
        }
    }

    // ---------------------------------------------------------------- tool specs

    [Fact]
    public void The_three_video_specs_register_themselves_and_accept_one_video()
    {
        var item = new FileItem(@"C:\videos\clip.mp4");
        IToolWindowSpec[] specs = [VideoTrimTool.Instance, VideoCropTool.Instance, VideoSpeedTool.Instance];

        Assert.Equal(3, specs.Select(spec => spec.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(specs, spec => Assert.True(spec.Applies([item]), spec.Id));
        Assert.All(specs, spec => Assert.True(spec.Layout.PreviewMinWidth >= 560, spec.Id));
        Assert.All(specs, spec => Assert.True(spec.Applies([item]) && !spec.Applies([item, item]), spec.Id));

        Assert.False(VideoTrimTool.Instance.Applies([new FileItem(@"C:\music\song.mp3")]));

        var registered = ToolRegistry.All.Select(spec => spec.Id).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("tools.videotrim", registered);
        Assert.Contains("tools.videocrop", registered);
        Assert.Contains("tools.videospeed", registered);
    }

    [Fact]
    public void The_three_operations_share_the_spec_ids_and_open_windows()
    {
        var item = new FileItem(@"C:\videos\clip.mkv");
        Operation[] operations = [new VideoTrimOperation(), new VideoCropOperation(), new VideoSpeedOperation()];

        Assert.Equal(
            new[] { "tools.videotrim", "tools.videocrop", "tools.videospeed" },
            operations.Select(operation => operation.Id));
        Assert.All(operations, operation => Assert.Equal(WheelKind.Tools, operation.Wheel));
        Assert.All(operations, operation => Assert.True(operation.AppliesTo([item])));
        Assert.All(operations, operation => Assert.IsAssignableFrom<IInteractiveTool>(operation));
    }

    private static int Index(IReadOnlyList<string> args, string value)
    {
        for (var index = 0; index < args.Count; index++)
        {
            if (string.Equals(args[index], value, StringComparison.Ordinal))
                return index;
        }
        return -1;
    }

    private static void AssertPair(IReadOnlyList<string> args, string first, string second)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], first, StringComparison.Ordinal)
                && string.Equals(args[index + 1], second, StringComparison.Ordinal))
                return;
        }

        Assert.Fail($"missing '{first} {second}' in: {string.Join(' ', args)}");
    }
}
