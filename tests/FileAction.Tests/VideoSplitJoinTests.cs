using FileAction.Core;
using Xunit;

namespace FileAction.Tests;

/// <summary>
/// The pure half of the Split and Join tools: partition math (points and N equal parts), output
/// naming, the exact ffmpeg arguments and the concat filter that scales and pads every clip to the
/// first one's frame. No ffmpeg runs here; the strings below are the whole contract the runners use.
/// </summary>
public class VideoSplitJoinTests
{
    private static readonly Settings Defaults = new();

    private static ClipInfo Clip(
        string path, double duration = 10, int width = 1920, int height = 1080, double fps = 30, bool audio = true) =>
        new(path, duration, width, height, fps, audio);

    // ---------------------------------------------------------------- split points

    [Fact]
    public void Ranges_partitions_the_whole_clip()
    {
        var parts = VideoSplitJoin.Ranges(10, [4, 2]);

        Assert.Equal(3, parts.Count);
        Assert.Equal(new SplitSegment(0, 2), parts[0]);
        Assert.Equal(new SplitSegment(2, 4), parts[1]);
        Assert.Equal(new SplitSegment(4, 10), parts[2]);
        Assert.Equal(10, parts.Sum(p => p.Duration), 3);
    }

    [Fact]
    public void Ranges_without_points_is_one_part_of_the_whole_clip()
    {
        var part = Assert.Single(VideoSplitJoin.Ranges(12.5, []));

        Assert.Equal(new SplitSegment(0, 12.5), part);
    }

    [Fact]
    public void Ranges_drops_points_on_the_edges_and_points_that_would_make_a_tiny_part()
    {
        // 0 and 10 are not splits, 0.01 is too close to the start, 9.99 too close to the end, and
        // 4.51 is too close to the 4.5 point that was already accepted.
        var parts = VideoSplitJoin.Ranges(10, [0, 10, 0.01, 9.99, 4, 4.5, 4.51]);

        Assert.Equal(3, parts.Count);
        Assert.Equal(new SplitSegment(0, 4), parts[0]);
        Assert.Equal(new SplitSegment(4, 4.5), parts[1]);
        Assert.Equal(new SplitSegment(4.5, 10), parts[2]);
        Assert.All(parts, part => Assert.True(part.Duration >= VideoSplitJoin.MinSegment));
        Assert.Equal(10, parts[^1].End, 3);
    }

    [Fact]
    public void Ranges_clamps_points_outside_the_clip_and_rounds_to_milliseconds()
    {
        var parts = VideoSplitJoin.Ranges(10, [-5, 3.14159, 40]);

        Assert.Equal(2, parts.Count);
        Assert.Equal(new SplitSegment(0, 3.142), parts[0]);
        Assert.Equal(new SplitSegment(3.142, 10), parts[1]);
    }

    [Fact]
    public void Ranges_ignores_not_a_number_and_infinity()
    {
        var parts = VideoSplitJoin.Ranges(10, [double.NaN, double.PositiveInfinity, double.NegativeInfinity, 5]);

        Assert.Equal(2, parts.Count);
        Assert.Equal(new SplitSegment(0, 5), parts[0]);
        Assert.Equal(new SplitSegment(5, 10), parts[1]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_clip_without_length_cannot_be_split(double duration)
    {
        Assert.Throws<ConversionException>(() => VideoSplitJoin.Ranges(duration, [1]));
        Assert.Throws<ConversionException>(() => VideoSplitJoin.EqualParts(duration, 3));
    }

    [Fact]
    public void EqualParts_uses_the_requested_count_and_the_last_part_takes_the_remainder()
    {
        var parts = VideoSplitJoin.EqualParts(10, 3);

        Assert.Equal(3, parts.Count);
        Assert.Equal(new SplitSegment(0, 3.333), parts[0]);
        Assert.Equal(new SplitSegment(3.333, 6.667), parts[1]);
        Assert.Equal(new SplitSegment(6.667, 10), parts[2]);
        Assert.Equal(10, parts.Sum(p => p.Duration), 3);
    }

    [Fact]
    public void EqualParts_are_contiguous_and_ordered()
    {
        var parts = VideoSplitJoin.EqualParts(7.5, 5);

        for (var i = 1; i < parts.Count; i++)
            Assert.Equal(parts[i - 1].End, parts[i].Start, 3);
        Assert.Equal(0, parts[0].Start, 3);
        Assert.Equal(7.5, parts[^1].End, 3);
        Assert.All(parts, part => Assert.True(part.Duration > 0));
    }

    [Fact]
    public void EqualParts_of_a_very_short_clip_keeps_only_usable_parts()
    {
        var parts = VideoSplitJoin.EqualParts(0.1, 10);

        Assert.All(parts, part => Assert.True(part.Duration >= VideoSplitJoin.MinSegment));
        Assert.Equal(0.1, parts.Sum(p => p.Duration), 3);
    }

    [Fact]
    public void EqualParts_clamps_to_the_offered_range()
    {
        var whole = Assert.Single(VideoSplitJoin.EqualParts(10, 1));
        Assert.Equal(new SplitSegment(0, 10), whole);
        Assert.Equal(VideoSplitJoin.MaxParts, VideoSplitJoin.EqualParts(10, 1000).Count);
    }

    [Fact]
    public void PointsForEqualParts_are_the_interior_boundaries()
    {
        Assert.Equal(new[] { 2.5, 5, 7.5 }, VideoSplitJoin.PointsForEqualParts(10, 4));
        Assert.Empty(VideoSplitJoin.PointsForEqualParts(10, 1));
    }

    // ---------------------------------------------------------------- names and plan

    [Theory]
    [InlineData(1, 9, "Trip 01")]
    [InlineData(9, 9, "Trip 09")]
    [InlineData(10, 12, "Trip 10")]
    [InlineData(12, 12, "Trip 12")]
    [InlineData(1, 105, "Trip 001")]
    [InlineData(105, 105, "Trip 105")]
    public void SegmentName_pads_to_at_least_two_digits_and_to_the_highest_part(int number, int total, string expected)
    {
        Assert.Equal(expected, VideoSplitJoin.SegmentName("Trip", number, total));
    }

    [Fact]
    public void A_plan_names_the_new_folder_after_the_source()
    {
        var plan = VideoSplitJoin.Plan(@"C:\videos\Trip.mp4", "Parts", 10, [5]);

        Assert.Equal("Trip Parts", plan.FolderName);
        Assert.Equal(@"C:\videos\Trip.mp4", plan.SourcePath);
        Assert.Equal("Trip", plan.BaseName);
        Assert.Equal("mp4", plan.Extension);
        Assert.True(plan.CanCopy);
        Assert.Equal(new[] { "Trip 01.mp4", "Trip 02.mp4" }, plan.FileNames);
    }

    [Theory]
    [InlineData("clip.mkv", "mkv", true)]
    [InlineData("clip.mov", "mov", true)]
    [InlineData("clip.webm", "webm", true)]
    [InlineData("clip.avi", "mp4", false)]
    [InlineData("clip.wmv", "mp4", false)]
    [InlineData("clip.mpe", "mp4", false)]
    public void Legacy_containers_are_re_encoded_to_mp4_instead_of_copied(string file, string expected, bool canCopy)
    {
        var plan = VideoSplitJoin.Plan($@"C:\videos\{file}", "Parts", 10, []);

        Assert.Equal(expected, plan.Extension);
        Assert.Equal(canCopy, plan.CanCopy);
    }

    [Fact]
    public void A_plan_for_equal_parts_has_the_requested_number_of_names()
    {
        var plan = VideoSplitJoin.PlanEqualParts(@"C:\videos\Trip.mkv", "Parts", 60, 4);

        Assert.Equal(4, plan.Count);
        Assert.Equal("mkv", plan.Extension);
        Assert.Equal(new[] { "Trip 01.mkv", "Trip 02.mkv", "Trip 03.mkv", "Trip 04.mkv" }, plan.FileNames);
    }

    // ---------------------------------------------------------------- split arguments

    [Fact]
    public void A_copied_part_seeks_and_stops_inside_the_input_and_never_re_encodes()
    {
        var args = VideoSplitJoin.SplitArgs(
            @"C:\in\clip.mp4", new SplitSegment(2, 5), @"C:\out\part.mp4", copy: true, "mp4", Defaults);

        Assert.Equal("-ss", args[3]);
        Assert.Equal("2", args[4]);
        Assert.Equal("-t", args[5]);
        Assert.Equal("3", args[6]);
        Assert.Equal("-i", args[7]);
        Assert.Equal(@"C:\in\clip.mp4", args[8]);

        var copy = Array.IndexOf(args.ToArray(), "-c");
        Assert.True(copy > 8, "the copy flags belong after the input");
        Assert.Equal("copy", args[copy + 1]);
        // Measured with ffmpeg 9: -avoid_negative_ts make_zero inflates a copied part to its own end time.
        Assert.DoesNotContain("-avoid_negative_ts", args);
        Assert.DoesNotContain("libx264", args);
        Assert.Equal(@"C:\out\part.mp4", args[^1]);
    }

    [Theory]
    [InlineData(3, 3.0, true)]     // an exact copy
    [InlineData(3, 3.02, true)]    // audio priming
    [InlineData(3, 3.35, true)]   // a keyframe up to the tolerance early
    [InlineData(3, 5.0, false)]    // the MKV copy started at the previous keyframe
    [InlineData(3, 1.0, false)]    // truncated
    [InlineData(3, 0, false)]      // no length at all
    [InlineData(0, 3, false)]      // nothing was requested
    public void A_copied_part_is_kept_only_when_its_length_matches(double expected, double actual, bool matches)
    {
        Assert.Equal(matches, VideoSplitJoin.CopyLengthMatches(expected, actual));
    }

    [Fact]
    public void A_re_encoded_part_uses_the_encoder_for_the_container()
    {
        var args = VideoSplitJoin.SplitArgs(
            @"C:\in\clip.webm", new SplitSegment(2, 5), @"C:\out\part.webm", copy: false, "webm", Defaults);

        Assert.Contains("libvpx-vp9", args);
        Assert.DoesNotContain("copy", args);
        Assert.Equal(@"C:\out\part.webm", args[^1]);
    }

    [Fact]
    public void A_part_without_audio_is_mapped_optionally()
    {
        var args = VideoSplitJoin.SplitArgs(
            @"C:\in\clip.mp4", new SplitSegment(0, 5), @"C:\out\part.mp4", copy: true, "mp4", Defaults);

        Assert.Contains("0:v:0?", args);
        Assert.Contains("0:a?", args);
    }

    // ---------------------------------------------------------------- join filter

    [Fact]
    public void The_join_filter_uses_the_first_clips_size_and_frame_rate()
    {
        var filter = VideoSplitJoin.BuildJoinFilter(
            [Clip(@"C:\a.mp4", 10, 1280, 720, 30), Clip(@"C:\b.mov", 8, 640, 480, 25)]);

        Assert.Contains("scale=1280:720:force_original_aspect_ratio=decrease", filter.Graph);
        Assert.Contains("pad=1280:720:(ow-iw)/2:(oh-ih)/2", filter.Graph);
        Assert.Contains("fps=30", filter.Graph);
        Assert.DoesNotContain("fps=25", filter.Graph);
        Assert.Equal("[outv]", filter.VideoLabel);
        Assert.Equal("[outa]", filter.AudioLabel);
        Assert.EndsWith("concat=n=2:v=1:a=1[outv][outa]", filter.Graph);
    }

    [Fact]
    public void A_clip_without_audio_gets_silence_of_its_own_length()
    {
        var filter = VideoSplitJoin.BuildJoinFilter(
            [Clip(@"C:\a.mp4", 10, 1920, 1080, 30, audio: true), Clip(@"C:\b.mp4", 4.5, 1920, 1080, 30, audio: false)]);

        Assert.Contains("anullsrc=channel_layout=stereo:sample_rate=48000,atrim=duration=4.5", filter.Graph);
        Assert.Contains("[0:a]aresample=48000", filter.Graph);
        // Exactly one silent source, and the silent clip contributes no [1:a] input label.
        Assert.Equal(1, SplitOccurrences(filter.Graph, "anullsrc="));
        Assert.DoesNotContain("[1:a]", filter.Graph);
        Assert.Contains("[a1]", filter.Graph);
    }

    [Fact]
    public void Audio_is_padded_and_trimmed_to_the_video_length_so_clips_stay_in_step()
    {
        var filter = VideoSplitJoin.BuildJoinFilter(
            [Clip(@"C:\a.mp4", 10, 1920, 1080), Clip(@"C:\b.mp4", 3, 1920, 1080)]);

        Assert.Contains("apad,atrim=duration=10,asetpts=N/SR/TB", filter.Graph);
        Assert.Contains("apad,atrim=duration=3,asetpts=N/SR/TB", filter.Graph);
        Assert.Contains("aformat=sample_fmts=fltp:channel_layouts=stereo", filter.Graph);
    }

    [Fact]
    public void Odd_dimensions_are_rounded_down_because_h264_needs_even_ones()
    {
        var filter = VideoSplitJoin.BuildJoinFilter(
            [Clip(@"C:\a.mp4", 10, 1921, 1081), Clip(@"C:\b.mp4", 3, 640, 480)]);

        Assert.Contains("scale=1920:1080", filter.Graph);
    }

    [Fact]
    public void Every_clip_contributes_one_video_and_one_audio_chain()
    {
        var filter = VideoSplitJoin.BuildJoinFilter(
            [Clip("a.mp4"), Clip("b.mp4", audio: false), Clip("c.mkv", audio: true)]);

        foreach (var index in new[] { 0, 1, 2 })
        {
            Assert.Contains($"[{index}:v]scale=", filter.Graph);
            Assert.Contains($"[v{index}]", filter.Graph);
            Assert.Contains($"[a{index}]", filter.Graph);
        }
        Assert.EndsWith("[v0][a0][v1][a1][v2][a2]concat=n=3:v=1:a=1[outv][outa]", filter.Graph);
    }

    [Fact]
    public void Joining_fewer_than_two_clips_is_refused()
    {
        Assert.Throws<ConversionException>(() => VideoSplitJoin.BuildJoinFilter([Clip("a.mp4")]));
        Assert.Throws<ConversionException>(() => VideoSplitJoin.BuildJoinFilter([]));
    }

    [Fact]
    public void A_clip_the_probe_could_not_read_blocks_the_join()
    {
        Assert.Throws<ConversionException>(() =>
            VideoSplitJoin.BuildJoinFilter([Clip("a.mp4", duration: 0), Clip("b.mp4")]));
        Assert.Throws<ConversionException>(() =>
            VideoSplitJoin.BuildJoinFilter([Clip("a.mp4", width: 0), Clip("b.mp4")]));
    }

    // ---------------------------------------------------------------- join arguments

    [Fact]
    public void JoinArgs_lists_every_clip_as_an_input_and_encodes_h264_aac()
    {
        var args = VideoSplitJoin.JoinArgs(
            [Clip(@"C:\a.mp4"), Clip(@"C:\b.mov"), Clip(@"C:\c.mkv")], @"C:\out\Joined.mp4", Defaults);

        var inputs = args
            .Select((value, index) => (value, index))
            .Where(pair => pair.value == "-i")
            .Select(pair => pair.index)
            .ToList();
        Assert.Equal(3, inputs.Count);
        Assert.Equal(@"C:\a.mp4", args[inputs[0] + 1]);
        Assert.Equal(@"C:\b.mov", args[inputs[1] + 1]);
        Assert.Equal(@"C:\c.mkv", args[inputs[2] + 1]);

        Assert.Contains("-filter_complex", args);
        Assert.Contains("[outv]", args);
        Assert.Contains("[outa]", args);
        Assert.Contains("libx264", args);
        Assert.Contains("aac", args);
        Assert.Contains("+faststart", args);
        Assert.Equal(@"C:\out\Joined.mp4", args[^1]);
    }

    // ---------------------------------------------------------------- ordering

    [Fact]
    public void The_initial_order_is_the_selection_order()
    {
        var clips = new List<ClipInfo> { Clip("a"), Clip("b"), Clip("c") };

        Assert.Equal(new[] { "a", "b", "c" }, clips.Select(c => c.Path));
    }

    [Fact]
    public void Move_reorders_without_losing_a_clip()
    {
        var clips = new[] { "a", "b", "c", "d" };

        Assert.Equal(new[] { "b", "c", "a", "d" }, VideoSplitJoin.Move(clips, 0, 2));
        Assert.Equal(new[] { "d", "a", "b", "c" }, VideoSplitJoin.Move(clips, 3, 0));
        Assert.Equal(new[] { "a", "c", "d", "b" }, VideoSplitJoin.Move(clips, 1, 3));
        Assert.Equal(clips, VideoSplitJoin.Move(clips, 2, 2));
        Assert.Equal(clips, VideoSplitJoin.Move(clips, -1, 0));
        Assert.Equal(clips, VideoSplitJoin.Move(clips, 9, 0));
        Assert.Empty(VideoSplitJoin.Move<string>([], 0, 1));
    }

    [Fact]
    public void RemoveAt_removes_exactly_one_clip()
    {
        var clips = new[] { "a", "b", "c" };

        Assert.Equal(new[] { "a", "c" }, VideoSplitJoin.RemoveAt(clips, 1));
        Assert.Equal(clips, VideoSplitJoin.RemoveAt(clips, 5));
        Assert.Equal(clips, VideoSplitJoin.RemoveAt(clips, -1));
    }

    [Fact]
    public void Move_and_RemoveAt_leave_the_input_list_untouched()
    {
        var clips = new List<string> { "a", "b", "c" };

        VideoSplitJoin.Move(clips, 0, 2);
        VideoSplitJoin.RemoveAt(clips, 0);

        Assert.Equal(new[] { "a", "b", "c" }, clips);
    }

    // ---------------------------------------------------------------- probe parsing

    [Fact]
    public void ParseProbe_reads_size_frame_rate_audio_and_length_from_the_ffmpeg_banner()
    {
        string[] banner =
        [
            "Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'clip.mp4':",
            "  Metadata:",
            "    major_brand     : isom",
            "  Duration: 00:01:23.45, start: 0.000000, bitrate: 1234 kb/s",
            "  Stream #0:0[0x1](und): Video: h264 (High) (avc1 / 0x31637661), yuv420p(progressive), 1920x1080 [SAR 1:1 DAR 16:9], 1200 kb/s, 29.97 fps, 29.97 tbr, 90k tbn (default)",
            "  Stream #0:1[0x2](und): Audio: aac (LC) (mp4a / 0x6134706D), 48000 Hz, stereo, fltp, 160 kb/s (default)",
        ];

        var info = VideoSplitJoin.ParseProbe(@"C:\videos\clip.mp4", banner);

        Assert.Equal(1920, info.Width);
        Assert.Equal(1080, info.Height);
        Assert.Equal(29.97, info.Fps, 3);
        Assert.True(info.HasAudio);
        Assert.Equal(83.45, info.Duration, 3);
        Assert.True(info.IsUsable);
    }

    [Fact]
    public void ParseProbe_marks_a_clip_without_video_or_length_as_unusable()
    {
        var noVideo = VideoSplitJoin.ParseProbe("audio.mp3",
        [
            "Input #0, mp3, from 'audio.mp3':",
            "  Duration: 00:00:10.00, start: 0.000000, bitrate: 128 kb/s",
            "  Stream #0:0: Audio: mp3, 44100 Hz, stereo, fltp, 128 kb/s",
        ]);
        Assert.False(noVideo.IsUsable);
        Assert.True(noVideo.HasAudio);

        var noDuration = VideoSplitJoin.ParseProbe("still.mp4",
        [
            "Input #0, mov,mp4,m4a, from 'still.mp4':",
            "  Duration: N/A, start: 0.000000, bitrate: N/A",
            "  Stream #0:0: Video: h264, yuv420p, 640x480, 90k tbr",
        ]);
        Assert.False(noDuration.IsUsable);
        Assert.Equal(640, noDuration.Width);
        Assert.Equal(480, noDuration.Height);
        Assert.False(noDuration.HasAudio);
    }

    [Fact]
    public void ParseProbe_keeps_the_path_it_was_given()
    {
        var info = VideoSplitJoin.ParseProbe(@"C:\videos\clip.mkv", []);

        Assert.Equal(@"C:\videos\clip.mkv", info.Path);
        Assert.False(info.IsUsable);
    }

    private static int SplitOccurrences(string text, string token) =>
        text.Split(token, StringSplitOptions.None).Length - 1;
}
