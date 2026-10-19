using FileAction.Core;

namespace FileAction.Tests;

/// <summary>
/// Pure math and filter-text tests for the two video tools. Nothing here starts FFmpeg: the frame
/// arithmetic, the capture list, the redaction model and the exact <c>-filter_complex</c> text are
/// covered as plain functions.
/// </summary>
public class VideoFramesTests
{
    // ---------------------------------------------------------------- frame math

    [Fact]
    public void Frame_numbers_are_five_padded_western_digits()
    {
        Assert.Equal("00000", FrameMath.FrameNumber(0));
        Assert.Equal("00001", FrameMath.FrameNumber(1));
        Assert.Equal("00123", FrameMath.FrameNumber(123));
        Assert.Equal("12345", FrameMath.FrameNumber(12345));
        Assert.Equal("123456", FrameMath.FrameNumber(123456));
        Assert.Equal("00000", FrameMath.FrameNumber(-4));
    }

    [Fact]
    public void Capture_names_and_folder_match_the_promise()
    {
        Assert.Equal("clip frame 00123.png", FrameMath.CaptureFileName("clip", 123));
        Assert.Equal("clip frame 00000.png", FrameMath.CaptureFileName("clip", 0));
        Assert.Equal("holiday 2025 frame 12345.png", FrameMath.CaptureFileName("holiday 2025", 12345));
        Assert.Equal("clip frames", FrameMath.FramesFolderName("clip"));
    }

    [Fact]
    public void Total_frames_rounds_the_duration_and_never_returns_zero()
    {
        Assert.Equal(300, FrameMath.TotalFrames(10, 30));
        Assert.Equal(300, FrameMath.TotalFrames(10, 29.97));
        Assert.Equal(1, FrameMath.TotalFrames(0.01, 30));
        Assert.Equal(1, FrameMath.TotalFrames(0, 30));
        Assert.Equal(1, FrameMath.TotalFrames(-5, 30));
        // A missing frame rate falls back to 30 rather than dividing by zero.
        Assert.Equal(300, FrameMath.TotalFrames(10, 0));
        Assert.Equal(300, FrameMath.TotalFrames(10, double.NaN));
    }

    [Fact]
    public void Frame_time_and_back_are_stable_at_the_boundaries()
    {
        Assert.Equal(4.1, FrameMath.TimeOfFrame(123, 30), 10);
        Assert.Equal(123, FrameMath.FrameAt(4.1, 30, 300));
        Assert.Equal(122, FrameMath.FrameAt(4.099, 30, 300));
        Assert.Equal(0, FrameMath.FrameAt(-1, 30, 300));
        Assert.Equal(299, FrameMath.FrameAt(1_000, 30, 300));
        Assert.Equal(0, FrameMath.TimeOfFrame(-2, 30));
    }

    [Fact]
    public void Stepping_clamps_at_both_ends()
    {
        Assert.Equal(0, FrameMath.Step(0, -1, 300));
        Assert.Equal(1, FrameMath.Step(0, 1, 300));
        Assert.Equal(299, FrameMath.Step(299, 1, 300));
        Assert.Equal(0, FrameMath.Step(0, -1, 1));
    }

    [Fact]
    public void Timecodes_use_hours_minutes_seconds_and_milliseconds()
    {
        Assert.Equal("00:00:00.000", FrameMath.Timecode(0, 30));
        Assert.Equal("00:00:04.100", FrameMath.Timecode(123, 30));
        Assert.Equal("01:02:03.500", FrameMath.Timecode(3723.5));
        Assert.Equal("25:00:00.000", FrameMath.Timecode(90_000));
        Assert.Equal("00:00:00.000", FrameMath.Timecode(-3));
    }

    [Fact]
    public void Short_timecodes_drop_the_padded_hours()
    {
        Assert.Equal("0:00", FrameMath.ShortTimecode(0));
        Assert.Equal("0:05", FrameMath.ShortTimecode(5));
        Assert.Equal("1:05", FrameMath.ShortTimecode(65));
        Assert.Equal("1:02:03", FrameMath.ShortTimecode(3723));
        Assert.Equal("25:00:00", FrameMath.ShortTimecode(90_000));
        Assert.Equal("0:00", FrameMath.ShortTimecode(-3));
    }

    [Theory]
    [InlineData("90", 90)]
    [InlineData("1:30", 90)]
    [InlineData("01:02:03.5", 3723.5)]
    [InlineData(" 2,5 ", 2.5)]
    [InlineData("0:00", 0)]
    public void Time_text_parses_plain_clock_and_comma_forms(string text, double expected)
    {
        Assert.True(FrameMath.TryParseTime(text, out var seconds));
        Assert.Equal(expected, seconds, 10);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("1:")]
    [InlineData("1:2:3:4")]
    [InlineData("1:-2")]
    [InlineData("1e3")]
    public void Bad_time_text_is_rejected(string text)
    {
        Assert.False(FrameMath.TryParseTime(text, out _));
    }

    // ---------------------------------------------------------------- capture list

    [Fact]
    public void Capture_list_keeps_frames_sorted_and_unique()
    {
        var captures = new CaptureList();
        Assert.True(captures.IsEmpty);

        Assert.True(captures.Add(5));
        Assert.True(captures.Add(1));
        Assert.True(captures.Add(3));
        Assert.False(captures.Add(3));
        Assert.False(captures.Add(-1));
        Assert.False(captures.Add(-1));

        Assert.Equal(new[] { 1, 3, 5 }, captures.Frames);
        Assert.Equal(3, captures.Count);
        Assert.True(captures.Contains(3));
        Assert.False(captures.Contains(4));
    }

    [Fact]
    public void Capture_list_removes_and_clears()
    {
        var captures = new CaptureList();
        foreach (var frame in new[] { 1, 5 })
            Assert.True(captures.Add(frame));

        Assert.True(captures.Remove(1));
        Assert.False(captures.Remove(1));
        Assert.Equal(new[] { 5 }, captures.Frames);

        captures.Clear();
        Assert.True(captures.IsEmpty);
        Assert.Empty(captures.Frames);
    }

    // ---------------------------------------------------------------- redaction model

    [Fact]
    public void Redaction_area_reports_its_duration()
    {
        var area = new RedactionArea(0, 0, 10, 10, 1.5, 4, RedactionStyle.Solid);
        Assert.Equal(2.5, area.Duration, 10);
        Assert.Equal("black", area.Color);
    }

    // ---------------------------------------------------------------- filter graph

    [Fact]
    public void One_solid_area_is_one_drawbox()
    {
        var graph = RedactionGraph.Build(
            [new RedactionArea(10, 20, 100, 50, 1, 2.5, RedactionStyle.Solid, "black")],
            1920,
            1080);

        Assert.Equal(
            "[0:v]drawbox=x=10:y=20:w=100:h=50:color=black:t=fill:enable='between(t,1.000,2.500)'[vout]",
            graph);
    }

    [Fact]
    public void One_blur_area_crops_blurs_and_overlays_it()
    {
        var graph = RedactionGraph.Build(
            [new RedactionArea(200, 100, 80, 40, 1, 3, RedactionStyle.Blur)],
            1920,
            1080);

        Assert.Equal(
            "[0:v]split[b0][s0];[s0]crop=80:40:200:100,boxblur=10:2[k0];"
            + "[b0][k0]overlay=200:100:enable='between(t,1.000,3.000)'[vout]",
            graph);
    }

    [Fact]
    public void Two_areas_chain_and_the_last_one_produces_vout()
    {
        var graph = RedactionGraph.Build(
            [
                new RedactionArea(10, 20, 100, 50, 1, 2.5, RedactionStyle.Solid, "black"),
                new RedactionArea(200, 100, 80, 40, 1.5, 3, RedactionStyle.Blur),
            ],
            1920,
            1080);

        Assert.Equal(
            "[0:v]drawbox=x=10:y=20:w=100:h=50:color=black:t=fill:enable='between(t,1.000,2.500)'[v1];"
            + "[v1]split[b1][s1];[s1]crop=80:40:200:100,boxblur=10:2[k1];"
            + "[b1][k1]overlay=200:100:enable='between(t,1.500,3.000)'[vout]",
            graph);
    }

    [Fact]
    public void Overlapping_time_ranges_each_keep_their_own_enable()
    {
        var graph = RedactionGraph.Build(
            [
                new RedactionArea(0, 0, 20, 20, 1, 3, RedactionStyle.Solid, "black"),
                new RedactionArea(40, 0, 20, 20, 2, 4, RedactionStyle.Solid, "black"),
            ],
            640,
            360);

        Assert.Equal(
            "[0:v]drawbox=x=0:y=0:w=20:h=20:color=black:t=fill:enable='between(t,1.000,3.000)'[v1];"
            + "[v1]drawbox=x=40:y=0:w=20:h=20:color=black:t=fill:enable='between(t,2.000,4.000)'[vout]",
            graph);
    }

    [Fact]
    public void Times_are_formatted_with_three_invariant_decimals()
    {
        Assert.Equal("0.000", RedactionGraph.FormatSeconds(0));
        Assert.Equal("1.500", RedactionGraph.FormatSeconds(1.5));
        Assert.Equal("1.235", RedactionGraph.FormatSeconds(1.23456));
        Assert.Equal("0.000", RedactionGraph.FormatSeconds(-4));
        Assert.Equal("enable='between(t,0.000,2.000)'", RedactionGraph.Enable(0, 2));
    }

    [Fact]
    public void Colours_are_normalised_for_ffmpeg()
    {
        static string ColorOf(string color) => RedactionGraph.Build(
            [new RedactionArea(0, 0, 10, 10, 0, 1, RedactionStyle.Solid, color)],
            100,
            100);

        Assert.Contains("color=0x112233", ColorOf("#112233"));
        Assert.Contains("color=black", ColorOf(""));
        Assert.Contains("color=red@0.75", ColorOf("red@0.75"));
        // A colon would end the option early, so unsafe text falls back to the default.
        Assert.Contains("color=black", ColorOf("red:enable=x"));
    }

    [Fact]
    public void Rectangles_are_clamped_inside_the_frame()
    {
        var topLeft = RedactionGraph.Build(
            [new RedactionArea(-5, -8, 50, 40, 0, 1, RedactionStyle.Solid, "black")],
            100,
            80);
        Assert.Equal(
            "[0:v]drawbox=x=0:y=0:w=50:h=40:color=black:t=fill:enable='between(t,0.000,1.000)'[vout]",
            topLeft);

        var bottomRight = RedactionGraph.Build(
            [new RedactionArea(95, 75, 50, 40, 0, 1, RedactionStyle.Solid, "black")],
            100,
            80);
        Assert.Equal(
            "[0:v]drawbox=x=95:y=75:w=5:h=5:color=black:t=fill:enable='between(t,0.000,1.000)'[vout]",
            bottomRight);
    }

    [Fact]
    public void Invalid_areas_are_skipped_and_missing_areas_make_no_graph()
    {
        Assert.Equal("", RedactionGraph.Build([], 1920, 1080));
        Assert.Equal("", RedactionGraph.Build(null, 1920, 1080));
        Assert.Equal("", RedactionGraph.Build([new RedactionArea(0, 0, 10, 10, 0, 1, RedactionStyle.Blur)], 0, 1080));

        var skipped = RedactionGraph.Build(
            [
                new RedactionArea(0, 0, 0, 10, 0, 1, RedactionStyle.Solid),
                new RedactionArea(0, 0, 10, 10, 5, 5, RedactionStyle.Solid),
                new RedactionArea(10, 10, 20, 20, 1, 2, RedactionStyle.Solid, "black"),
            ],
            100,
            100);

        Assert.Equal(
            "[0:v]drawbox=x=10:y=10:w=20:h=20:color=black:t=fill:enable='between(t,1.000,2.000)'[vout]",
            skipped);
    }

    [Fact]
    public void Graph_always_ends_with_the_vout_label()
    {
        var graph = RedactionGraph.Build(
            [new RedactionArea(0, 0, 10, 10, 0, 1, RedactionStyle.Blur)],
            100,
            100);
        Assert.EndsWith(RedactionGraph.OutputLabel, graph);
    }
}
