using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using FileAction.Core;

namespace FileAction.Tests;

/// <summary>
/// The pure compression rules: kind detection, the quality tables, the image resize plan (including the
/// downscale that target-size and percentage mode need), the bitrate math that feeds the video two-pass
/// encode, and the estimates the window shows. No file, no window and no external tool is involved.
/// </summary>
public class CompressionPlanTests
{
    private static CompressionRequest Request(
        MediaKind kind,
        string format,
        CompressionMode mode = CompressionMode.Preset,
        double targetMegabytes = 1.0,
        int percentage = 50,
        CompressionPreset preset = CompressionPreset.Medium,
        int maxLongSide = 0) =>
        new(kind, format, mode, targetMegabytes, percentage, preset, maxLongSide);

    [Fact]
    public void KindOf_maps_the_shipped_formats()
    {
        Assert.Equal(MediaKind.Image, Compression.KindOf("jpg"));
        Assert.Equal(MediaKind.Image, Compression.KindOf("jpeg"));
        Assert.Equal(MediaKind.Image, Compression.KindOf("png"));
        Assert.Equal(MediaKind.Audio, Compression.KindOf("mp3"));
        Assert.Equal(MediaKind.Audio, Compression.KindOf("flac"));
        Assert.Equal(MediaKind.Video, Compression.KindOf("mp4"));
        Assert.Equal(MediaKind.Video, Compression.KindOf("mkv"));
        Assert.Equal(MediaKind.Pdf, Compression.KindOf("pdf"));
        Assert.Equal(MediaKind.None, Compression.KindOf("docx"));
        Assert.Equal(MediaKind.None, Compression.KindOf(""));
        Assert.Equal(MediaKind.None, Compression.KindOf(null));
    }

    [Fact]
    public void Lossless_audio_is_recognized()
    {
        Assert.True(Compression.IsLosslessAudio("wav"));
        Assert.True(Compression.IsLosslessAudio("aiff"));
        Assert.True(Compression.IsLosslessAudio("flac"));
        Assert.False(Compression.IsLosslessAudio("mp3"));
        Assert.False(Compression.IsLosslessAudio("m4a"));
    }

    [Fact]
    public void Preset_tables_are_ordered()
    {
        Assert.True(Compression.QualityFor(CompressionPreset.High) > Compression.QualityFor(CompressionPreset.Medium));
        Assert.True(Compression.QualityFor(CompressionPreset.Medium) > Compression.QualityFor(CompressionPreset.Low));
        Assert.True(Compression.CrfFor(CompressionPreset.High) < Compression.CrfFor(CompressionPreset.Medium));
        Assert.True(Compression.CrfFor(CompressionPreset.Medium) < Compression.CrfFor(CompressionPreset.Low));
        Assert.True(Compression.AudioKbpsFor(CompressionPreset.High) > Compression.AudioKbpsFor(CompressionPreset.Low));
    }

    [Fact]
    public void A_megabyte_is_1024_squared_and_targets_are_clamped()
    {
        Assert.Equal(1048576, Compression.BytesFromMegabytes(1.0));
        Assert.Equal(Compression.BytesFromMegabytes(Compression.MinTargetMegabytes), Compression.BytesFromMegabytes(0));
        Assert.Equal(Compression.BytesFromMegabytes(Compression.MaxTargetMegabytes), Compression.BytesFromMegabytes(1e12));
        Assert.Equal(2.0, Compression.MegabytesFromBytes(2 * 1024 * 1024), 6);
    }

    [Fact]
    public void Sizes_and_durations_keep_western_digits()
    {
        Assert.Equal("0 B", Compression.FormatSize(0));
        Assert.Equal("1023 B", Compression.FormatSize(1023));
        Assert.Equal("1 KB", Compression.FormatSize(1024));
        Assert.Equal("1.5 KB", Compression.FormatSize(1536));
        Assert.Equal("1 MB", Compression.FormatSize(1024 * 1024));
        Assert.Equal("1.5 MB", Compression.FormatSize(1024 * 1024 + 512 * 1024));
        Assert.Equal("1 GB", Compression.FormatSize(1024L * 1024 * 1024));

        Assert.Equal("0:42", Compression.FormatDuration(42));
        Assert.Equal("12:05", Compression.FormatDuration(725));
        Assert.Equal("1:02:03", Compression.FormatDuration(3723));
        Assert.Equal("1600×900", Compression.FormatPixels(1600, 900));
    }

    [Fact]
    public void Box_values_are_parsed_and_clamped()
    {
        Assert.True(Compression.TryParseMegabytes("1.5", out var megabytes));
        Assert.Equal(1.5, megabytes, 6);
        Assert.True(Compression.TryParseMegabytes(" 2 ", out var two));
        Assert.Equal(2.0, two, 6);
        Assert.False(Compression.TryParseMegabytes("0.001", out _));
        Assert.False(Compression.TryParseMegabytes("abc", out _));
        Assert.False(Compression.TryParseMegabytes("", out _));

        Assert.True(Compression.TryParsePercentage("60", out var percentage));
        Assert.Equal(60, percentage);
        Assert.True(Compression.TryParsePercentage("100", out var full));
        Assert.Equal(100, full);
        Assert.False(Compression.TryParsePercentage("4", out _));
        Assert.False(Compression.TryParsePercentage("", out _));

        Assert.True(Compression.TryParseLongSide("", out var none));
        Assert.Equal(0, none);
        Assert.True(Compression.TryParseLongSide("1920", out var side));
        Assert.Equal(1920, side);
        Assert.False(Compression.TryParseLongSide("63", out _));
        Assert.False(Compression.TryParseLongSide("20001", out _));
    }

    [Fact]
    public void Image_plan_never_upscales_and_honours_the_max_long_side()
    {
        var unchanged = Compression.PlanImage(800, 600, 1024 * 1024, "jpg", CompressionMode.Preset, 1, 50,
            CompressionPreset.Medium, maxLongSide: 1920);
        Assert.Equal(800, unchanged.Width);
        Assert.Equal(600, unchanged.Height);
        Assert.False(unchanged.Resized);

        var resized = Compression.PlanImage(4000, 2000, 8 * 1024 * 1024, "jpg", CompressionMode.Preset, 1, 50,
            CompressionPreset.Medium, maxLongSide: 1000);
        Assert.Equal(1000, resized.Width);
        Assert.Equal(500, resized.Height);
        Assert.True(resized.Resized);
    }

    [Fact]
    public void Target_size_downscales_the_image_to_reach_the_target()
    {
        var plan = Compression.PlanImage(4000, 3000, 8 * 1024 * 1024, "jpg", CompressionMode.TargetSize, 1.0, 50,
            CompressionPreset.Medium, maxLongSide: 0);

        Assert.True(plan.Resized);
        Assert.True(plan.Width < 4000);
        Assert.True(plan.Height < 3000);

        var estimate = Compression.EstimateImageBytes(8 * 1024 * 1024, "jpg", plan);
        Assert.InRange(estimate, 900_000, 1_100_000);
    }

    [Fact]
    public void Percentage_mode_keeps_the_quality_and_scales_the_pixels()
    {
        var plan = Compression.PlanImage(1000, 1000, 2 * 1024 * 1024, "jpg", CompressionMode.Percentage, 1.0, 50,
            CompressionPreset.Medium, maxLongSide: 0);

        Assert.Equal(Compression.QualityFor(CompressionPreset.High), plan.Quality);
        Assert.True(plan.Width < 1000);
        Assert.Equal(plan.Width, plan.Height);

        var estimate = Compression.EstimateImageBytes(2 * 1024 * 1024, "jpg", plan);
        Assert.InRange(estimate, 950_000, 1_100_000);
    }

    [Fact]
    public void Preset_mode_estimates_a_smaller_jpeg()
    {
        var plan = Compression.PlanImage(1000, 1000, 2 * 1024 * 1024, "jpg", CompressionMode.Preset, 1.0, 50,
            CompressionPreset.Low, maxLongSide: 0);

        Assert.Equal(Compression.QualityFor(CompressionPreset.Low), plan.Quality);
        Assert.False(plan.Resized);
        var estimate = Compression.EstimateImageBytes(2 * 1024 * 1024, "jpg", plan);
        Assert.True(estimate < 1024 * 1024, $"estimate {estimate}");
    }

    [Fact]
    public void An_unknown_image_size_still_plans_a_quality()
    {
        var plan = Compression.PlanImage(0, 0, 1024, "png", CompressionMode.Preset, 1.0, 50,
            CompressionPreset.High, maxLongSide: 1920);
        Assert.Equal(0, plan.Width);
        Assert.Equal(0, plan.Height);
        Assert.Equal(Compression.QualityFor(CompressionPreset.High), plan.Quality);
    }

    [Fact]
    public void Video_target_size_uses_a_two_pass_bitrate()
    {
        var plan = Compression.Plan(
            Request(MediaKind.Video, "mp4", CompressionMode.TargetSize, targetMegabytes: 2.0),
            sourceBytes: 20 * 1024 * 1024, sourceWidth: 1920, sourceHeight: 1080, durationSeconds: 60);

        Assert.True(plan.TwoPass);
        Assert.Equal(Compression.AudioKbpsFor(CompressionPreset.Medium), plan.AudioKbps);
        Assert.InRange(plan.VideoKbps, 140, 165);
        Assert.InRange(plan.EstimatedBytes, 2_000_000, 2_200_000);
    }

    [Fact]
    public void Video_percentage_uses_the_source_bitrate_share()
    {
        var plan = Compression.Plan(
            Request(MediaKind.Video, "mp4", CompressionMode.Percentage, percentage: 50),
            sourceBytes: 20 * 1024 * 1024, sourceWidth: 1920, sourceHeight: 1080, durationSeconds: 60);

        Assert.True(plan.TwoPass);
        Assert.InRange(plan.VideoKbps, 1200, 1330);
        Assert.InRange(plan.EstimatedBytes, 10_000_000, 10_900_000);
    }

    [Fact]
    public void Video_preset_uses_crf_and_a_single_pass()
    {
        var plan = Compression.Plan(
            Request(MediaKind.Video, "mp4", preset: CompressionPreset.Low),
            sourceBytes: 10 * 1024 * 1024, sourceWidth: 1920, sourceHeight: 1080, durationSeconds: 60);

        Assert.False(plan.TwoPass);
        Assert.Equal(Compression.CrfFor(CompressionPreset.Low), plan.Crf);
        Assert.Equal(0, plan.VideoKbps);
        Assert.InRange(plan.EstimatedBytes, 3_000_000, 3_300_000);
    }

    [Fact]
    public void A_target_below_the_minimum_bitrate_is_reported_honestly()
    {
        var plan = Compression.Plan(
            Request(MediaKind.Audio, "mp3", CompressionMode.TargetSize, targetMegabytes: 0.5),
            sourceBytes: 10 * 1024 * 1024, sourceWidth: 0, sourceHeight: 0, durationSeconds: 300);

        Assert.Equal(Compression.MinAudioKbps, plan.AudioKbps);
        // The bitrate floor needs ~1.2 MB for five minutes, so the estimate says so instead of promising 0.5 MB.
        Assert.InRange(plan.EstimatedBytes, 1_100_000, 1_300_000);
    }

    [Fact]
    public void Lossless_audio_ignores_the_bitrate_target()
    {
        var plan = Compression.Plan(
            Request(MediaKind.Audio, "flac", CompressionMode.TargetSize, targetMegabytes: 1.0),
            sourceBytes: 5 * 1024 * 1024, sourceWidth: 0, sourceHeight: 0, durationSeconds: 100);

        Assert.True(plan.LosslessAudio);
        Assert.True(plan.EstimatedBytes < 5 * 1024 * 1024);
        Assert.True(plan.EstimatedBytes > 4 * 1024 * 1024);
    }

    [Fact]
    public void Pdf_estimates_use_the_preset_factor()
    {
        var plan = Compression.Plan(
            Request(MediaKind.Pdf, "pdf", preset: CompressionPreset.Medium),
            sourceBytes: 4 * 1024 * 1024, sourceWidth: 0, sourceHeight: 0, durationSeconds: 0);

        Assert.InRange(plan.EstimatedBytes, 3_200_000, 3_300_000);
    }

    [Fact]
    public void Percent_smaller_never_goes_below_zero()
    {
        Assert.Equal(75, Compression.PercentSmaller(1000, 250));
        Assert.Equal(0, Compression.PercentSmaller(1000, 1000));
        Assert.Equal(0, Compression.PercentSmaller(0, 0));
    }

    [Fact]
    public void Duration_is_only_needed_for_bitrate_modes()
    {
        Assert.True(Compression.NeedsDuration(MediaKind.Video, CompressionMode.TargetSize));
        Assert.True(Compression.NeedsDuration(MediaKind.Audio, CompressionMode.Percentage));
        Assert.False(Compression.NeedsDuration(MediaKind.Video, CompressionMode.Preset));
        Assert.False(Compression.NeedsDuration(MediaKind.Image, CompressionMode.TargetSize));
    }

    [Fact]
    public void Defaults_are_the_balanced_preset_with_no_size_goal()
    {
        Assert.Equal(CompressionMode.Preset, CompressionDefaults.Mode);
        Assert.Equal(CompressionPreset.Medium, CompressionDefaults.Preset);
        Assert.False(CompressionDefaults.TargetSizeEnabled);
        Assert.False(CompressionDefaults.PercentageEnabled);
        Assert.Equal(40, CompressionDefaults.PercentageReduction);
        Assert.Equal(60, CompressionDefaults.KeepPercentage);
        Assert.Equal(0, CompressionDefaults.KeepOriginalDimensions);
        Assert.NotEmpty(CompressionDefaults.ImageSizePresets);
        Assert.DoesNotContain(CompressionDefaults.KeepOriginalDimensions, CompressionDefaults.ImageSizePresets);
    }

    [Fact]
    public void Reduction_maps_onto_the_kept_percentage()
    {
        Assert.Equal(60, Compression.KeepPercentageFromReduction(40));
        Assert.Equal(40, Compression.ReductionFromKeepPercentage(60));
        Assert.Equal(95, Compression.KeepPercentageFromReduction(0));
        Assert.Equal(5, Compression.KeepPercentageFromReduction(100));

        Assert.True(Compression.TryParseReduction("40", out var reduction));
        Assert.Equal(40, reduction);
        Assert.False(Compression.TryParseReduction("4", out _));
        Assert.False(Compression.TryParseReduction("96", out _));
        Assert.False(Compression.TryParseReduction("", out _));
    }
}

/// <summary>
/// The pure metadata rules: kind detection, the chapter list format, coordinate validation, the FFmpeg
/// tag/chapter arguments and the <c>-i</c> banner parser, plus search and grouping of the field list.
/// </summary>
public class MetadataModelTests
{
    [Fact]
    public void Metadata_kind_follows_the_format()
    {
        Assert.Equal(MetadataKind.Image, MetadataModel.KindOf("jpg"));
        Assert.Equal(MetadataKind.Media, MetadataModel.KindOf("mp3"));
        Assert.Equal(MetadataKind.Media, MetadataModel.KindOf("mp4"));
        Assert.Equal(MetadataKind.Pdf, MetadataModel.KindOf("pdf"));
        Assert.Equal(MetadataKind.None, MetadataModel.KindOf("docx"));
    }

    [Fact]
    public void Chapters_parse_from_one_line_per_chapter()
    {
        var chapters = MetadataValidation.ParseChapters("0:00 Intro\n1:30 Main", out var issues);

        Assert.Empty(issues);
        Assert.Equal(2, chapters.Count);
        Assert.Equal(0, chapters[0].StartMs);
        Assert.Equal("Intro", chapters[0].Title);
        Assert.Equal(90000, chapters[1].StartMs);
        Assert.Equal("Main", chapters[1].Title);
    }

    [Fact]
    public void An_hour_part_is_accepted()
    {
        var chapters = MetadataValidation.ParseChapters("01:02:03 Third", out var issues);

        Assert.Empty(issues);
        Assert.Single(chapters);
        Assert.Equal(3723000, chapters[0].StartMs);
    }

    [Fact]
    public void A_bad_chapter_line_reports_its_line_number()
    {
        var chapters = MetadataValidation.ParseChapters("0:00 Intro\nnot a chapter", out var issues);

        Assert.Single(chapters);
        Assert.Single(issues);
        Assert.Equal("tool.metadata.error.chapter", issues[0].MessageKey);
        Assert.Equal("2", issues[0].Arguments[0]);
    }

    [Fact]
    public void Chapter_times_must_increase()
    {
        var chapters = MetadataValidation.ParseChapters("1:00 First\n0:30 Second", out var issues);

        Assert.Single(chapters);
        Assert.Single(issues);
        Assert.Equal("tool.metadata.error.chapterOrder", issues[0].MessageKey);
    }

    [Fact]
    public void At_most_one_hundred_chapters_are_kept()
    {
        var text = string.Join("\n", Enumerable.Range(0, 101).Select(i => $"{i}:00 Part {i}"));
        var chapters = MetadataValidation.ParseChapters(text, out var issues);

        Assert.Equal(MetadataValidation.MaxChapters, chapters.Count);
        var issue = Assert.Single(issues);
        Assert.Equal("tool.metadata.error.chapterCount", issue.MessageKey);
        Assert.Equal("100", issue.Arguments[0]);
    }

    [Fact]
    public void Chapters_round_trip_through_the_editor_text()
    {
        var chapters = MetadataValidation.ParseChapters("0:00 Intro\n1:30 Main", out _);
        Assert.Equal("0:00 Intro\n1:30 Main", MetadataValidation.ChaptersToText(chapters));
        Assert.Equal("1:02:03", MetadataValidation.FormatChapterTime(3723000));
    }

    [Fact]
    public void Coordinates_are_range_checked()
    {
        Assert.True(MetadataValidation.TryCoordinate("24.7136", latitude: true, out var latitude));
        Assert.Equal(24.7136, latitude, 4);
        Assert.True(MetadataValidation.TryCoordinate("24,7136", latitude: true, out var comma));
        Assert.Equal(24.7136, comma, 4);
        Assert.False(MetadataValidation.TryCoordinate("95", latitude: true, out _));
        Assert.False(MetadataValidation.TryCoordinate("-95", latitude: true, out _));
        Assert.True(MetadataValidation.TryCoordinate("180", latitude: false, out _));
        Assert.False(MetadataValidation.TryCoordinate("181", latitude: false, out _));
        Assert.False(MetadataValidation.TryCoordinate("abc", latitude: true, out _));
        Assert.False(MetadataValidation.TryCoordinate("", latitude: true, out _));
        Assert.Equal("24.7136", MetadataValidation.FormatCoordinate(24.7136));
    }

    [Fact]
    public void Supported_edit_kinds_are_declared()
    {
        Assert.True(MetadataValidation.SupportsCamera(MetadataKind.Image));
        Assert.False(MetadataValidation.SupportsCamera(MetadataKind.Media));
        Assert.True(MetadataValidation.SupportsChapters(MetadataKind.Media));
        Assert.False(MetadataValidation.SupportsChapters(MetadataKind.Pdf));
        Assert.True(MetadataValidation.SupportsTags(MetadataKind.Media));
        Assert.True(MetadataValidation.SupportsTags(MetadataKind.Pdf));
        Assert.False(MetadataValidation.SupportsTags(MetadataKind.Image));
    }

    [Fact]
    public void Validation_rejects_a_bad_latitude_for_images()
    {
        var edit = new MetadataEdit { Latitude = "95" };
        var issue = Assert.Single(MetadataValidation.Validate(MetadataKind.Image, edit));

        Assert.Equal("latitude", issue.Field);
        Assert.Equal("tool.metadata.error.latitude", issue.MessageKey);
    }

    [Fact]
    public void Validation_reports_a_too_long_tag_with_its_label()
    {
        var edit = new MetadataEdit { Title = new string('x', MetadataValidation.MaxTextLength + 1) };
        var issue = Assert.Single(MetadataValidation.Validate(MetadataKind.Media, edit));

        Assert.Equal("title", issue.Field);
        Assert.Equal("tool.metadata.error.text", issue.MessageKey);
        Assert.Equal("tool.metadata.field.title", issue.Arguments[0]);
    }

    [Fact]
    public void Removing_everything_needs_no_validation()
    {
        var edit = new MetadataEdit { RemoveAll = true, Title = new string('x', 400) };
        Assert.Empty(MetadataValidation.Validate(MetadataKind.Media, edit));
    }

    [Fact]
    public void The_edit_knows_when_it_has_something_to_write()
    {
        Assert.False(new MetadataEdit().HasEdits);
        Assert.True(new MetadataEdit { Title = "Song" }.HasEdits);
        Assert.True(new MetadataEdit { ChapterText = "0:00 Intro" }.HasEdits);
        Assert.True(new MetadataEdit { RemoveAll = true }.HasEdits);
    }

    [Fact]
    public void Ffmpeg_tag_arguments_skip_empty_values()
    {
        var args = MetadataFfmpeg.TagArgs(new MetadataEdit { Title = "New", Artist = "  " });

        Assert.Equal(2, args.Count);
        Assert.Equal("-metadata", args[0]);
        Assert.Equal("title=New", args[1]);
    }

    [Fact]
    public void Removing_all_metadata_drops_tags_and_chapters()
    {
        var args = MetadataFfmpeg.EditArgs(new MetadataEdit { RemoveAll = true }, "in.mp4",
            Array.Empty<MetadataChapter>(), null, "out.mp4");

        Assert.Contains("-map_metadata", args);
        Assert.Contains("-1", args);
        Assert.DoesNotContain("-metadata", args);
        Assert.Equal("out.mp4", args[^1]);
    }

    [Fact]
    public void A_chapter_edit_adds_the_sidecar_input()
    {
        var chapters = new[] { new MetadataChapter(0, "Intro") };
        var args = MetadataFfmpeg.EditArgs(new MetadataEdit { Title = "New" }, "in.mp4", chapters, "chapters.ffmeta", "out.mp4");

        Assert.Contains("-i", args);
        Assert.Contains("chapters.ffmeta", args);
        Assert.Contains("-map_chapters", args);
        Assert.Contains("1", args);
        Assert.Contains("-c", args);
        Assert.Contains("copy", args);
    }

    [Fact]
    public void The_chapter_sidecar_uses_milliseconds_and_escapes_titles()
    {
        var chapters = new[] { new MetadataChapter(0, "a=b"), new MetadataChapter(90000, "Main") };
        var text = MetadataFfmpeg.ChapterFile(chapters, totalSeconds: 300);

        Assert.StartsWith(";FFMETADATA1", text);
        Assert.Equal(2, text.Split("[CHAPTER]").Length - 1);
        Assert.Contains("TIMEBASE=1/1000", text);
        Assert.Contains("START=0", text);
        Assert.Contains("END=90000", text);
        Assert.Contains("START=90000", text);
        Assert.Contains("END=300000", text);
        Assert.Contains(@"title=a\=b", text);
        Assert.Contains("title=Main", text);
    }

    [Fact]
    public void The_ffmpeg_banner_becomes_grouped_fields()
    {
        const string banner =
            "Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'clip.mp4':\n" +
            "  Metadata:\n" +
            "    major_brand     : isom\n" +
            "    title           : Hello\n" +
            "  Duration: 00:01:30.00, start: 0.000000, bitrate: 1234 kb/s\n" +
            "  Chapter #0:0: start 0.000000, end 60.000000\n" +
            "    Metadata:\n" +
            "      title           : Intro\n" +
            "  Stream #0:0[0x1](und): Video: h264 (High) (avc1 / 0x31637661), yuv420p, 1920x1080 [SAR 1:1 DAR 16:9], 5000 kb/s, 30 fps\n" +
            "  Stream #0:1[0x2](und): Audio: aac (LC) (mp4a / 0x6134706D), 48000 Hz, stereo, fltp, 128 kb/s\n";

        var probe = MetadataFfmpeg.ParseProbe(banner);

        Assert.Equal(90, probe.DurationSeconds, 3);
        Assert.Equal(1920, probe.Width);
        Assert.Equal(1080, probe.Height);
        Assert.True(probe.Document.Count > 5);

        Assert.Contains(probe.Document.Fields, f => f.GroupKey == MetadataGroups.FileKey && f.Label == "title" && f.Value == "Hello");
        Assert.Contains(probe.Document.Fields, f => f.GroupKey == MetadataGroups.ChaptersKey && f.Label == "title" && f.Value == "Intro");
        Assert.Contains(probe.Document.Fields, f => f.GroupKey == MetadataGroups.VideoKey && f.Label == "resolution" && f.Value == "1920x1080");
        Assert.Contains(probe.Document.Fields, f => f.GroupKey == MetadataGroups.AudioKey && f.Label == "sample_rate" && f.Value == "48000 Hz");
        Assert.Contains(probe.Document.Fields, f => f.GroupKey == MetadataGroups.AudioKey && f.Label == "channels" && f.Value == "stereo");
    }

    [Fact]
    public void The_field_list_is_searchable_and_grouped_in_display_order()
    {
        var document = new MetadataDocument(MetadataKind.Image,
        [
            new MetadataField(MetadataGroups.FileKey, "format", "JPEG"),
            new MetadataField(MetadataGroups.ExifKey, "Make", "Canon"),
            new MetadataField(MetadataGroups.ExifKey, "Model", "EOS R6"),
        ]);

        Assert.Equal(3, document.Count);
        Assert.Equal(3, document.Search("").Count);
        Assert.Single(document.Search("canon"));
        Assert.Single(document.Search("eos"));

        var groups = document.Grouped("");
        Assert.True(groups.Select(g => g.GroupKey).SequenceEqual([MetadataGroups.FileKey, MetadataGroups.ExifKey]));
        Assert.Empty(document.Grouped("nothing matches this"));
    }

    [Fact]
    public void Group_keys_are_localization_keys_in_display_order()
    {
        Assert.Equal("tool.metadata.group.file", MetadataGroups.KeyOf(MetadataGroup.File));
        Assert.Equal("tool.metadata.group.chapters", MetadataGroups.KeyOf(MetadataGroup.Chapters));
        Assert.True(MetadataGroups.OrderOf(MetadataGroups.FileKey) < MetadataGroups.OrderOf(MetadataGroups.ExifKey));
        Assert.True(MetadataGroups.OrderOf(MetadataGroups.ChaptersKey) > MetadataGroups.OrderOf(MetadataGroups.FileKey));
    }

    [Fact]
    public void Editable_keys_map_onto_the_edit()
    {
        var edit = new MetadataEdit();
        edit.Set(MetadataEditKeys.MediaTitle, "Song");
        Assert.Equal("Song", edit.Title);
        Assert.Equal("Song", edit.Get(MetadataEditKeys.MediaTitle));

        edit.Remove(MetadataEditKeys.MediaTitle);
        Assert.Equal(string.Empty, edit.Title);
        Assert.True(edit.IsRemoved(MetadataEditKeys.MediaTitle));
        Assert.True(edit.HasEdits);

        edit.Set(MetadataEditKeys.MediaTitle, "Again");
        Assert.False(edit.IsRemoved(MetadataEditKeys.MediaTitle));
        Assert.Equal("Again", edit.Get(MetadataEditKeys.MediaTitle));

        Assert.Equal("title", MetadataEditKeys.FfmpegTag(MetadataEditKeys.MediaTitle));
        Assert.Null(MetadataEditKeys.FfmpegTag(MetadataEditKeys.PdfTitle));
    }

    [Fact]
    public void Removed_media_tags_are_written_as_empty_metadata()
    {
        var edit = new MetadataEdit();
        edit.Remove(MetadataEditKeys.MediaTitle);
        edit.Set(MetadataEditKeys.MediaArtist, "Artist");

        var args = MetadataFfmpeg.TagArgs(edit);
        Assert.Equal(4, args.Count);
        Assert.Equal("-metadata", args[0]);
        Assert.Equal("title=", args[1]);
        Assert.Equal("artist=Artist", args[3]);
    }

    [Fact]
    public void Removing_chapters_drops_the_chapter_track()
    {
        var edit = new MetadataEdit();
        edit.Remove(MetadataEditKeys.Chapters);

        var args = MetadataFfmpeg.EditArgs(edit, "in.mp4", Array.Empty<MetadataChapter>(), null, "out.mp4");
        Assert.Contains("-map_chapters", args);
        Assert.Contains("-1", args);
    }

    [Fact]
    public void The_probe_seeds_the_editable_rows_and_the_chapter_text()
    {
        const string banner =
            "Input #0, mov,mp4, from 'x.mp4':\n" +
            "  Duration: 00:00:10.00, start: 0, bitrate: 100 kb/s\n" +
            "  Chapter #0:0: start 5.000000, end 9.000000\n" +
            "    Metadata:\n" +
            "      title           : Intro\n";

        var probe = MetadataFfmpeg.ParseProbe(banner);

        Assert.Contains(probe.Document.Fields, f => f.EditKey == MetadataEditKeys.MediaTitle);
        Assert.Contains(probe.Document.Fields, f => f.EditKey == MetadataEditKeys.MediaArtist);
        Assert.Contains(probe.Document.Fields, f => f.EditKey == MetadataEditKeys.MediaAlbum);
        Assert.Contains(probe.Document.Fields, f => f.EditKey == MetadataEditKeys.Chapters && f.Value == "0:05 Intro");
    }

    [Fact]
    public void Editable_rows_come_first_inside_their_group()
    {
        var document = new MetadataDocument(MetadataKind.Media,
        [
            new MetadataField(MetadataGroups.FileKey, "major_brand", "isom"),
            new MetadataField(MetadataGroups.FileKey, "title", "Song", MetadataEditKeys.MediaTitle),
        ]);

        var file = document.Grouped("")[0];
        Assert.Equal("title", file.Fields[0].Label);
        Assert.True(file.Fields[0].Editable);
        Assert.False(file.Fields[1].Editable);
    }

    [Fact]
    public void Metadata_defaults_are_an_unfiltered_list_without_removal()
    {
        Assert.Equal(string.Empty, MetadataDefaults.Search);
        Assert.False(MetadataDefaults.RemoveAll);
        Assert.Equal("tool.metadata.search", MetadataDefaults.SearchKey);
        Assert.Equal("tool.metadata.removeAll", MetadataDefaults.RemoveAllKey);
        Assert.Equal("tool.metadata.save", MetadataDefaults.SaveKey);
        Assert.Equal("tool.metadata.note.removeAll", MetadataDefaults.NoteKey);
        Assert.Equal("tool.metadata.removeField", MetadataDefaults.RemoveFieldKey);
    }
}

/// <summary>
/// The shipped string tables: every tool.compress.* and tool.metadata.* key exists in both languages with
/// the same placeholders, and the Arabic is a real translation. Values are read from the committed files,
/// so the test covers exactly what ships.
/// </summary>
public class CompressMetadataStringTests
{
    private static readonly Regex Placeholder = new(@"\{(\d+)\}", RegexOptions.Compiled);

    private static readonly string[] Prefixes = ["tool.compress.", "tool.metadata."];

    /// <summary>The strings the UI-fidelity pass fixed; the English text is asserted verbatim.</summary>
    private static readonly (string Key, string English)[] AddendumKeys =
    [
        ("tool.compress.apply", "Compress"),
        ("tool.compress.originalSize", "Original size: {0}"),
        ("tool.compress.preset.balanced", "Balanced"),
        ("tool.compress.preset.strong", "Strong"),
        ("tool.compress.preset.helper", "Smaller files with less quality loss."),
        ("tool.compress.imageSize", "Image size"),
        ("tool.compress.imageSize.keep", "Keep original dimensions"),
        ("tool.compress.imageSize.entry", "{0} px"),
        ("tool.compress.options", "Size options"),
        ("tool.compress.target.toggle", "Compress to target file size"),
        ("tool.compress.percentage.toggle", "Reduce by percentage"),
        ("tool.compress.error.percentage", "Enter a reduction from 5 to 95."),
        ("tool.metadata.search", "Search metadata"),
        ("tool.metadata.save", "Save"),
        ("tool.metadata.removeAll", "Remove All"),
        ("tool.metadata.removeField", "Remove this field"),
        ("tool.metadata.note.removeAll", "For photos, Remove All also clears hidden camera and processing data. HDR and portrait effects may change."),
    ];

    [Fact]
    public void Both_tables_have_the_same_tool_keys()
    {
        var (en, ar) = LoadTables();

        var english = ToolKeys(en);
        var arabic = ToolKeys(ar);

        Assert.True(english.Length >= 70, $"only {english.Length} tool keys");
        Assert.True(english.SequenceEqual(arabic), "tool keys differ between en.json and ar.json");
    }

    [Fact]
    public void Placeholders_match_for_every_tool_key()
    {
        var (en, ar) = LoadTables();

        foreach (var key in ToolKeys(en))
        {
            var english = Placeholders(en[key]);
            var arabic = Placeholders(ar[key]);
            Assert.True(english.SequenceEqual(arabic), $"placeholders differ for '{key}'");
        }
    }

    [Fact]
    public void Arabic_is_not_a_copy_of_english()
    {
        var (en, ar) = LoadTables();

        foreach (var key in ToolKeys(en))
            Assert.NotEqual(en[key], ar[key]);
    }

    [Fact]
    public void Every_tool_value_is_present()
    {
        var (en, ar) = LoadTables();

        foreach (var key in ToolKeys(en))
        {
            Assert.False(string.IsNullOrWhiteSpace(en[key]), $"en: '{key}' is empty");
            Assert.False(string.IsNullOrWhiteSpace(ar[key]), $"ar: '{key}' is empty");
            Assert.DoesNotContain('\uFFFD', en[key]);
            Assert.DoesNotContain('\uFFFD', ar[key]);
        }
    }

    [Fact]
    public void Addendum_keys_exist_in_both_languages_with_the_fixed_english()
    {
        var (en, ar) = LoadTables();

        foreach (var (key, english) in AddendumKeys)
        {
            Assert.True(en.ContainsKey(key), $"en.json is missing '{key}'");
            Assert.True(ar.ContainsKey(key), $"ar.json is missing '{key}'");
            Assert.Equal(english, en[key]);
        }
    }

    [Fact]
    public void No_tool_key_looks_like_a_plural_group()
    {
        // Loc.PluralBaseKeys treats every "*.other" key as a plural group; a plain label must never
        // end that way or the localization test that enumerates plural groups fails.
        var (en, _) = LoadTables();
        var offenders = en.Keys
            .Where(key => key.StartsWith("tool.", StringComparison.Ordinal))
            .Where(key => key.EndsWith(".other", StringComparison.Ordinal))
            .ToArray();
        Assert.Empty(offenders);
    }

    private static string[] ToolKeys(Dictionary<string, string> table) =>
        table.Keys
            .Where(key => Prefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string[] Placeholders(string value) =>
        Placeholder.Matches(value)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static (Dictionary<string, string> English, Dictionary<string, string> Arabic) LoadTables()
    {
        var strings = Path.Combine(RepoRoot(), "src", "FileAction.Core", "Strings");
        return (Load(Path.Combine(strings, "en.json")), Load(Path.Combine(strings, "ar.json")));
    }

    private static Dictionary<string, string> Load(string path)
    {
        Assert.True(File.Exists(path), $"Missing string table: {path}");
        var table = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
        Assert.NotNull(table);
        return table!;
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
