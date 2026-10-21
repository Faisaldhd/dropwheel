using FileAction.Core;
using Xunit;

namespace FileAction.Tests;

/// <summary>
/// The pure audio-effect builders: the exact pan filters, the bleep filtergraph (ranges, tone, volume),
/// range editing and the visualizer arguments. No ffmpeg is started here; the strings are what the tool
/// windows hand to <c>FfmpegBackend</c>, so a change in either place breaks this suite.
/// </summary>
public class AudioEffectsTests
{
    // ------------------------------------------------------------ channels

    [Fact]
    public void Mono_folds_both_channels_at_half_gain()
    {
        Assert.Equal(
            "pan=mono|c0=0.5*c0+0.5*c1",
            AudioEffects.ChannelFilter(AudioChannelMode.Mono));
    }

    [Fact]
    public void Stereo_keeps_the_two_sides_apart()
    {
        Assert.Equal(
            "pan=stereo|c0=1*c0|c1=1*c1",
            AudioEffects.ChannelFilter(AudioChannelMode.Stereo));
    }

    [Fact]
    public void Mono_source_is_duplicated_into_both_stereo_channels_at_full_level()
    {
        // A mono source has no c1: the old expression read it as silence and wrote a dead right channel.
        Assert.Equal(
            "pan=stereo|c0=1*c0|c1=1*c0",
            AudioEffects.ChannelFilter(AudioChannelMode.Stereo, sourceIsMono: true));

        Assert.Equal(
            "pan=stereo|c0=0.5*c0|c1=1*c0",
            AudioEffects.ChannelFilter(AudioChannelMode.Stereo, 50, 100, sourceIsMono: true));

        Assert.Equal(
            new[] { "-af", "pan=stereo|c0=1*c0|c1=1*c0", "-ac", "2" },
            AudioEffects.ChannelArguments(AudioChannelMode.Stereo, sourceIsMono: true));
    }

    [Fact]
    public void Mono_source_stays_at_full_level_when_written_as_mono()
    {
        // 0.5*c0 would lose 6 dB; a mono source needs no downmix attenuation.
        Assert.Equal(
            "pan=mono|c0=1*c0",
            AudioEffects.ChannelFilter(AudioChannelMode.Mono, sourceIsMono: true));

        Assert.Equal(
            "pan=mono|c0=0.5*c0",
            AudioEffects.ChannelFilter(AudioChannelMode.Mono, 50, 100, sourceIsMono: true));
    }

    [Fact]
    public void Volume_sliders_become_pan_gains()
    {
        Assert.Equal(
            "pan=stereo|c0=0.5*c0|c1=1*c1",
            AudioEffects.ChannelFilter(AudioChannelMode.Stereo, leftVolumePercent: 50));

        Assert.Equal(
            "pan=mono|c0=0.25*c0+0.25*c1",
            AudioEffects.ChannelFilter(AudioChannelMode.Mono, 50, 50));
    }

    [Fact]
    public void Solo_mutes_the_other_side()
    {
        Assert.Equal(
            "pan=stereo|c0=1*c0|c1=0*c1",
            AudioEffects.ChannelFilter(AudioChannelMode.Stereo, 100, 100, AudioChannelSolo.Left));

        Assert.Equal(
            "pan=mono|c0=0*c0+0.5*c1",
            AudioEffects.ChannelFilter(AudioChannelMode.Mono, 100, 100, AudioChannelSolo.Right));
    }

    [Fact]
    public void Volumes_are_clamped_to_0_200_percent()
    {
        Assert.Equal(
            "pan=stereo|c0=2*c0|c1=0*c1",
            AudioEffects.ChannelFilter(AudioChannelMode.Stereo, 250, -10));
    }

    [Fact]
    public void Channel_arguments_add_the_output_channel_count()
    {
        Assert.Equal(
            new[] { "-af", "pan=stereo|c0=1*c0|c1=1*c1", "-ac", "2" },
            AudioEffects.ChannelArguments(AudioChannelMode.Stereo));

        Assert.Equal(
            new[] { "-af", "pan=mono|c0=0.5*c0+0.5*c1", "-ac", "1" },
            AudioEffects.ChannelArguments(AudioChannelMode.Mono));
    }

    // ------------------------------------------------------------ ranges

    [Fact]
    public void Ranges_are_sorted_clamped_and_trimmed()
    {
        Assert.Equal(
            new[] { new AudioRange(1, 2), new AudioRange(3, 4) },
            AudioEffects.NormalizeRanges(new[] { new AudioRange(3, 4), new AudioRange(1, 2) }, 10));

        Assert.Equal(
            new[] { new AudioRange(0, 10) },
            AudioEffects.NormalizeRanges(new[] { new AudioRange(0, 20) }, 10));

        Assert.Equal(
            new[] { new AudioRange(0, 1) },
            AudioEffects.NormalizeRanges(new[] { new AudioRange(-2, 1) }, 10));

        // A click shorter than the minimum, and an inverted range, both disappear.
        Assert.Empty(AudioEffects.NormalizeRanges(
            new[] { new AudioRange(1, 1.01), new AudioRange(5, 3) }, 10));
    }

    [Fact]
    public void Overlapping_and_touching_ranges_are_merged()
    {
        Assert.Equal(
            new[] { new AudioRange(1, 3) },
            AudioEffects.NormalizeRanges(new[] { new AudioRange(1, 2), new AudioRange(1.5, 3) }, 10));

        Assert.Equal(
            new[] { new AudioRange(1, 3) },
            AudioEffects.NormalizeRanges(new[] { new AudioRange(1, 2), new AudioRange(2, 3) }, 10));

        // The merged range keeps the furthest end, not the last one seen.
        Assert.Equal(
            new[] { new AudioRange(1, 5) },
            AudioEffects.NormalizeRanges(new[] { new AudioRange(1, 5), new AudioRange(2, 3) }, 10));
    }

    [Fact]
    public void Range_start_is_nudged_within_the_range()
    {
        Assert.Equal(0.5, AudioEffects.NudgeStart(new AudioRange(1, 2), -0.5, 10).Start, 3);
        Assert.Equal(0, AudioEffects.NudgeStart(new AudioRange(1, 2), -5, 10).Start, 3);
        Assert.Equal(1.95, AudioEffects.NudgeStart(new AudioRange(1, 2), 5, 10).Start, 3);
    }

    [Fact]
    public void Range_end_is_nudged_within_the_audio()
    {
        Assert.Equal(7, AudioEffects.NudgeEnd(new AudioRange(1, 2), 5, 10).End, 3);
        // Never past the end of the audio, and never before its own start.
        Assert.Equal(10, AudioEffects.NudgeEnd(new AudioRange(1, 2), 20, 10).End, 3);
        Assert.Equal(1.05, AudioEffects.NudgeEnd(new AudioRange(1, 2), -5, 10).End, 3);
        Assert.Equal(2.01, AudioEffects.NudgeEnd(new AudioRange(1, 2), 0.01, 10).End, 3);
    }

    // ------------------------------------------------------------ bleep

    [Fact]
    public void Bleep_filter_mutes_the_ranges_and_adds_the_tone_there()
    {
        var filter = AudioEffects.BleepFilter(
            new[] { new AudioRange(1, 2), new AudioRange(3.5, 4.25) },
            toneHz: 1000,
            volumePercent: 70);

        const string gate = "between(t,1,2)+between(t,3.5,4.25)";
        Assert.Equal(
            "[0:a]aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo,"
            + $"volume=volume=0:enable='{gate}'[duck];"
            + $"aevalsrc='sin(2*PI*1000*t)*0.7*({gate})':s=48000:c=stereo[tone];"
            + "[duck][tone]amix=inputs=2:duration=first:normalize=0[out]",
            filter);
    }

    [Fact]
    public void Bleep_filter_sorts_ranges_and_clamps_tone_settings()
    {
        var filter = AudioEffects.BleepFilter(
            new[] { new AudioRange(3, 4), new AudioRange(1, 2) },
            toneHz: 5,
            volumePercent: 150,
            sampleRate: 96000);

        const string gate = "between(t,1,2)+between(t,3,4)";
        Assert.Equal(
            "[0:a]aformat=sample_fmts=fltp:sample_rates=96000:channel_layouts=stereo,"
            + $"volume=volume=0:enable='{gate}'[duck];"
            + $"aevalsrc='sin(2*PI*20*t)*1*({gate})':s=96000:c=stereo[tone];"
            + "[duck][tone]amix=inputs=2:duration=first:normalize=0[out]",
            filter);
    }

    [Fact]
    public void Bleep_filter_needs_a_range()
    {
        Assert.Throws<ArgumentException>(() => AudioEffects.BleepFilter(Array.Empty<AudioRange>()));
        Assert.Throws<ArgumentException>(() => AudioEffects.BleepFilter(new[] { new AudioRange(2, 2) }));
    }

    [Fact]
    public void Bleep_arguments_map_the_filtergraph_output()
    {
        var range = new[] { new AudioRange(0.5, 1.5) };
        var arguments = AudioEffects.BleepArguments(range, 1000, 70);

        Assert.Equal(4, arguments.Count);
        Assert.Equal("-filter_complex", arguments[0]);
        Assert.Equal(AudioEffects.BleepFilter(range, 1000, 70), arguments[1]);
        Assert.Equal("-map", arguments[2]);
        Assert.Equal("[out]", arguments[3]);
    }

    // ------------------------------------------------------------ visualizer

    [Fact]
    public void Visualizer_dimensions_follow_the_orientation()
    {
        Assert.Equal((1920, 1080), AudioEffects.VisualizerDimensions(AudioVisualizerOrientation.Landscape));
        Assert.Equal((1080, 1920), AudioEffects.VisualizerDimensions(AudioVisualizerOrientation.Portrait));
        Assert.Equal((1080, 1080), AudioEffects.VisualizerDimensions(AudioVisualizerOrientation.Square));

        Assert.Equal("1920x1080", AudioEffects.VisualizerSize(AudioVisualizerOrientation.Landscape));
        Assert.Equal("1080x1920", AudioEffects.VisualizerSize(AudioVisualizerOrientation.Portrait));
        Assert.Equal("1080x1080", AudioEffects.VisualizerSize(AudioVisualizerOrientation.Square));
    }

    [Fact]
    public void Visualizer_waveform_arguments_encode_an_mp4()
    {
        var arguments = AudioEffects.VisualizerArguments(
            AudioVisualizerOrientation.Landscape,
            AudioVisualizerStyle.Waveform);

        Assert.Equal(
            new[]
            {
                "-filter_complex",
                "[0:a:0]showwaves=s=1920x1080:mode=cline:rate=25:colors=0x8224D4[v]",
                "-map", "[v]",
                "-map", "0:a:0",
                "-r", "25",
                "-c:v", "libx264", "-preset", "medium", "-crf", "23",
                "-pix_fmt", "yuv420p",
                "-c:a", "aac", "-b:a", "160k",
                "-movflags", "+faststart",
                "-shortest",
            },
            arguments);
    }

    [Fact]
    public void Visualizer_scope_arguments_use_the_portrait_size()
    {
        var arguments = AudioEffects.VisualizerArguments(
            AudioVisualizerOrientation.Portrait,
            AudioVisualizerStyle.Scope);

        Assert.Equal(
            "[0:a:0]aformat=channel_layouts=stereo,avectorscope=s=1080x1920:rate=25:mode=lissajous:draw=line[v]",
            arguments[1]);
    }

    [Fact]
    public void Visualizer_background_image_is_input_zero()
    {
        var arguments = AudioEffects.VisualizerArguments(
            AudioVisualizerOrientation.Square,
            AudioVisualizerStyle.Waveform,
            crf: 20,
            hasBackgroundImage: true);

        Assert.Equal(
            "[0:v]scale=1080:1080:force_original_aspect_ratio=increase,crop=1080:1080,setsar=1[bg];"
            + "[1:a:0]showwaves=s=1080x1080:mode=cline:rate=25:colors=0x8224D4,format=rgba,colorkey=0x000000:0.10:0.02[wave];"
            + "[bg][wave]overlay=0:0:shortest=1[v]",
            arguments[1]);
        Assert.Equal("1:a:0", arguments[5]);
        Assert.Equal("-crf", arguments[12]);
        Assert.Equal("20", arguments[13]);
        Assert.Equal("-shortest", arguments[^1]);
    }

    // ------------------------------------------------------------ bleep layer editing

    /// <summary>The section a drag would create, or a failure when the model rejects it.</summary>
    private static AudioRange Section(double duration, double from, double to) =>
        AudioEffects.NewSection(duration, from, to) ?? throw new Xunit.Sdk.XunitException("expected a section");

    [Fact]
    public void New_section_sorts_clamps_and_rejects_a_click()
    {
        Assert.Equal(new AudioRange(2, 5), Section(10, 2, 5));
        // Dragged right to left, the earlier time still becomes the start.
        Assert.Equal(new AudioRange(2, 5), Section(10, 5, 2));
        // Past either end of the audio, the section is clamped to it.
        Assert.Equal(new AudioRange(0, 10), Section(10, -3, 12));
        // A click or a hair-length drag is not a section, and nothing can be added before the audio loads.
        Assert.Null(AudioEffects.NewSection(10, 1, 1.01));
        Assert.Null(AudioEffects.NewSection(0, 0, 5));
    }

    [Fact]
    public void Moving_a_section_keeps_its_length_inside_the_audio()
    {
        Assert.Equal(new AudioRange(5, 7), AudioEffects.MoveSection(new AudioRange(2, 4), 5, 10));
        Assert.Equal(new AudioRange(0, 2), AudioEffects.MoveSection(new AudioRange(2, 4), -3, 10));
        Assert.Equal(new AudioRange(8, 10), AudioEffects.MoveSection(new AudioRange(2, 4), 99, 10));
        // A clip longer than the audio fills it instead of leaving the lane.
        Assert.Equal(new AudioRange(0, 6), AudioEffects.MoveSection(new AudioRange(2, 8), 1, 6));
    }

    [Fact]
    public void Resizing_an_edge_stays_inside_the_audio_and_the_clip()
    {
        // Start edge: free to move, never past the end, never before zero.
        Assert.Equal(new AudioRange(1, 4), AudioEffects.ResizeSection(new AudioRange(2, 4), startEdge: true, 1, 10));
        Assert.Equal(new AudioRange(0, 4), AudioEffects.ResizeSection(new AudioRange(2, 4), startEdge: true, -5, 10));
        Assert.Equal(new AudioRange(3.95, 4), AudioEffects.ResizeSection(new AudioRange(2, 4), startEdge: true, 9, 10));

        // End edge: never before the start, never past the end of the audio.
        Assert.Equal(new AudioRange(2, 8), AudioEffects.ResizeSection(new AudioRange(2, 4), startEdge: false, 8, 10));
        Assert.Equal(new AudioRange(2, 10), AudioEffects.ResizeSection(new AudioRange(2, 4), startEdge: false, 50, 10));
        Assert.Equal(new AudioRange(2, 2.05), AudioEffects.ResizeSection(new AudioRange(2, 4), startEdge: false, 0, 10));
    }

    [Fact]
    public void Drag_add_move_and_resize_never_leaves_the_audio()
    {
        // Drag out 2.0–5.0, shove the clip past the end, then pull its edges.
        var section = Section(8, 2, 5);
        section = AudioEffects.MoveSection(section, 6, 8);
        Assert.Equal(new AudioRange(5, 8), section);

        section = AudioEffects.ResizeSection(section, startEdge: true, 4, 8);
        Assert.Equal(new AudioRange(4, 8), section);

        section = AudioEffects.ResizeSection(section, startEdge: false, 0, 8);
        Assert.Equal(new AudioRange(4, 4.05), section);
    }

    [Fact]
    public void Timecodes_use_invariant_digits()
    {
        Assert.Equal("0:00.00", AudioEffects.FormatTimecode(0));
        Assert.Equal("0:05.50", AudioEffects.FormatTimecode(5.5));
        Assert.Equal("1:05.25", AudioEffects.FormatTimecode(65.25));
        Assert.Equal("1:02:05.50", AudioEffects.FormatTimecode(3725.5));
        Assert.Equal("0:00.00", AudioEffects.FormatTimecode(-1));
    }

    // ------------------------------------------------------------ bleep window strings

    /// <summary>Every string the reworked bleep window reads through <c>Loc.T</c>.</summary>
    private static readonly string[] BleepKeys =
    [
        "tool.audiobleep.label",
        "tool.audiobleep.title",
        "tool.audiobleep.subtitle",
        "tool.audiobleep.count",
        "tool.audiobleep.lane.bleep",
        "tool.audiobleep.lane.audio",
        "tool.audiobleep.help",
        "tool.audiobleep.preview.original",
        "tool.audiobleep.preview.bleeped",
        "tool.audiobleep.preview.note",
        "tool.audiobleep.save",
        "tool.audiobleep.tone",
        "tool.audiobleep.volume",
        "tool.audiobleep.start",
        "tool.audiobleep.end",
        "tool.audiobleep.remove",
        "tool.audiobleep.suffix",
        "tool.audiobleep.working",
        "tool.audiobleep.error.decode",
        "tool.audiobleep.error.noRanges",
    ];

    [Fact]
    public void Bleep_window_strings_exist_in_both_languages()
    {
        var english = Loc.Table(Language.English);
        var arabic = Loc.Table(Language.Arabic);

        foreach (var key in BleepKeys)
        {
            Assert.StartsWith("tool.audiobleep.", key);
            Assert.True(english.TryGetValue(key, out var en), $"en.json is missing '{key}'.");
            Assert.True(arabic.TryGetValue(key, out var ar), $"ar.json is missing '{key}'.");
            Assert.False(string.IsNullOrWhiteSpace(en), $"'{key}' is empty in en.json.");
            Assert.False(string.IsNullOrWhiteSpace(ar), $"'{key}' is empty in ar.json.");
            Assert.NotEqual(key, en);
            Assert.NotEqual(key, ar);
        }
    }

    [Fact]
    public void Bleep_window_strings_keep_their_placeholders()
    {
        var english = Loc.Table(Language.English);
        var arabic = Loc.Table(Language.Arabic);

        foreach (var key in BleepKeys)
            Assert.Equal(Placeholders(english[key]), Placeholders(arabic[key]));
    }

    [Fact]
    public void Bleep_helper_text_is_the_exact_wording()
    {
        Assert.Equal(
            "Drag across the Bleep layer to add a section, then drag a clip or either edge to adjust it.",
            Loc.Table(Language.English)["tool.audiobleep.help"]);
    }

    private static string[] Placeholders(string value) =>
        System.Text.RegularExpressions.Regex.Matches(value, @"\{(\d+)\}")
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();
}
