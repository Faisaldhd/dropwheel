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
}
