using FileAction.Core;
using Xunit;

namespace FileAction.Tests;

/// <summary>
/// The pure audio-tool core: the loudnorm JSON parser and both pass builders, the silencedetect parser,
/// the edge-trim math, the waveform envelope and the time boxes. No FFmpeg and no files are involved.
/// </summary>
public class AudioToolsTests
{
    private const string LoudnormLog = """
        [Parsed_loudnorm_0 @ 000001f449eab840] 
        {
        	"input_i" : "-28.35",
        	"input_tp" : "-27.09",
        	"input_lra" : "1.90",
        	"input_thresh" : "-38.50",
        	"output_i" : "-15.20",
        	"output_tp" : "-13.52",
        	"output_lra" : "2.00",
        	"output_thresh" : "-25.33",
        	"normalization_type" : "dynamic",
        	"target_offset" : "-0.80"
        }
        [out#0/null @ 000001f447f8e840] video:0KiB audio:3750KiB subtitle:0KiB other streams:0KiB global headers:0KiB muxing
        size=N/A time=00:00:05.00 bitrate=N/A speed=  26x
        """;

    private const string SilenceLog = """
        [Parsed_silencedetect_0 @ 000001e86d8cf300] silence_start: 0
        [Parsed_silencedetect_0 @ 000001e86d8cf300] silence_end: 1.000295 | silence_duration: 1.000295
        [Parsed_silencedetect_0 @ 000001e86d8cf300] silence_start: 3.999728
        [Parsed_silencedetect_0 @ 000001e86d8cf300] silence_end: 5 | silence_duration: 1.000272
        """;

    // ------------------------------------------------------------------ loudness

    [Fact]
    public void Loudness_parser_reads_the_ffmpeg_json_block()
    {
        var measurement = AudioLoudness.Parse(LoudnormLog);

        Assert.NotNull(measurement);
        var value = measurement!;
        Assert.Equal(-28.35, value.Integrated, 3);
        Assert.Equal(-27.09, value.TruePeak, 3);
        Assert.Equal(1.90, value.LoudnessRange, 3);
        Assert.Equal(-38.50, value.Threshold, 3);
        Assert.Equal(-15.20, value.PredictedIntegrated, 3);
        Assert.Equal(-13.52, value.PredictedTruePeak, 3);
        Assert.Equal(2.00, value.PredictedRange, 3);
        Assert.Equal(-25.33, value.PredictedThreshold, 3);
        Assert.Equal("dynamic", value.NormalizationType);
        Assert.Equal(-0.80, value.TargetOffset, 3);
        Assert.False(value.IsSilent);
        Assert.False(value.IsLinear);
        Assert.True(value.CanApplyMeasured);
    }

    [Fact]
    public void Loudness_parser_accepts_numbers_and_an_absent_offset()
    {
        const string log = """
            {"input_i": -23, "input_tp": -2.5, "input_lra": 7, "input_thresh": -33,
             "output_i": -16, "output_tp": -1.5, "output_lra": 6, "output_thresh": -26,
             "normalization_type": "linear"}
            """;

        var measurement = AudioLoudness.Parse(log);

        Assert.NotNull(measurement);
        Assert.Equal(-23, measurement!.Integrated, 3);
        Assert.Equal(0, measurement.TargetOffset, 3);
        Assert.True(measurement.IsLinear);
        Assert.True(measurement.CanApplyMeasured);
    }

    [Fact]
    public void Loudness_parser_keeps_silence_as_negative_infinity()
    {
        const string log = """
            {"input_i": "-inf", "input_tp": "-inf", "input_lra": "0.00", "input_thresh": "-inf",
             "output_i": "-16.00", "output_tp": "-1.50", "output_lra": "0.00", "output_thresh": "-26.00",
             "normalization_type": "dynamic", "target_offset": "0.00"}
            """;

        var measurement = AudioLoudness.Parse(log);

        Assert.NotNull(measurement);
        Assert.True(measurement!.IsSilent);
        Assert.False(measurement.CanApplyMeasured);
        Assert.Equal(double.NegativeInfinity, measurement.Integrated);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ffmpeg version 9.0.2\n  Duration: 00:00:05.00")]
    [InlineData("""{"input_i" : "-23.02",""")]
    [InlineData("""{"input_i" : "-23.02"}""")]
    [InlineData("{ not json at all }")]
    public void Loudness_parser_returns_null_for_incomplete_input(string? log)
    {
        Assert.Null(AudioLoudness.Parse(log));
    }

    [Fact]
    public void Measure_filter_is_invariant_and_uses_the_target()
    {
        Assert.Equal(
            "loudnorm=I=-16:TP=-1.5:LRA=11:print_format=json",
            AudioLoudness.MeasureFilter(LoudnessTarget.Default));

        var filter = AudioLoudness.MeasureFilter(new LoudnessTarget(-16.5, -1.25, 9.75));
        Assert.Contains("I=-16.5", filter, StringComparison.Ordinal);
        Assert.Contains("TP=-1.25", filter, StringComparison.Ordinal);
        Assert.Contains("LRA=9.75", filter, StringComparison.Ordinal);
        Assert.DoesNotContain(',', filter);
    }

    [Fact]
    public void Apply_filter_feeds_the_measured_source_values_into_the_second_pass()
    {
        var measurement = AudioLoudness.Parse(LoudnormLog)!;
        var filter = AudioLoudness.ApplyFilter(LoudnessTarget.Default, measurement);

        Assert.Contains("measured_I=-28.35", filter, StringComparison.Ordinal);
        Assert.Contains("measured_TP=-27.09", filter, StringComparison.Ordinal);
        Assert.Contains("measured_LRA=1.9", filter, StringComparison.Ordinal);
        Assert.Contains("measured_thresh=-38.5", filter, StringComparison.Ordinal);
        Assert.Contains("offset=-0.8", filter, StringComparison.Ordinal);
        Assert.Contains("linear=true", filter, StringComparison.Ordinal);
        Assert.EndsWith("print_format=summary", filter, StringComparison.Ordinal);
        Assert.StartsWith("loudnorm=I=-16:TP=-1.5:LRA=11", filter, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_filter_falls_back_to_one_pass_for_silence_or_no_measurement()
    {
        const string silent = """
            {"input_i": "-inf", "input_tp": "-inf", "input_lra": "0.00", "input_thresh": "-inf",
             "output_i": "-16.00", "output_tp": "-1.50", "output_lra": "0.00", "output_thresh": "-26.00",
             "normalization_type": "dynamic", "target_offset": "0.00"}
            """;

        var silentFilter = AudioLoudness.ApplyFilter(LoudnessTarget.Default, AudioLoudness.Parse(silent));
        Assert.DoesNotContain("measured_", silentFilter, StringComparison.Ordinal);
        Assert.EndsWith("print_format=summary", silentFilter, StringComparison.Ordinal);

        var emptyFilter = AudioLoudness.ApplyFilter(LoudnessTarget.Default, null);
        Assert.DoesNotContain("measured_", emptyFilter, StringComparison.Ordinal);
        Assert.Equal("loudnorm=I=-16:TP=-1.5:LRA=11:print_format=summary", emptyFilter);
    }

    [Fact]
    public void Measure_arguments_read_the_audio_and_write_nothing()
    {
        const string input = @"C:\audio\tone.wav";
        var arguments = AudioLoudness.MeasureArguments(input, LoudnessTarget.Default);

        Assert.Equal("-i", arguments[3]);
        Assert.Equal(input, arguments[4]);
        Assert.Equal("-map", arguments[5]);
        Assert.Equal("0:a:0", arguments[6]);
        Assert.Equal("-af", arguments[7]);
        Assert.Equal(AudioLoudness.MeasureFilter(LoudnessTarget.Default), arguments[8]);
        Assert.Equal(new[] { "-f", "null", "-" }, arguments.Skip(9).ToArray());
    }

    [Fact]
    public void Apply_arguments_put_the_encoder_options_before_the_output()
    {
        const string input = @"C:\audio\tone.wav";
        const string output = @"C:\audio\tone Normalized.wav";
        var measurement = AudioLoudness.Parse(LoudnormLog);
        var arguments = AudioLoudness.ApplyArguments(input, output, LoudnessTarget.Default, measurement, ["-c:a", "pcm_s16le"]);

        Assert.Equal(output, arguments[^1]);
        Assert.Equal("-c:a", arguments[^3]);
        Assert.Equal("pcm_s16le", arguments[^2]);
        Assert.Contains("-af", arguments);
        Assert.Equal(AudioLoudness.ApplyFilter(LoudnessTarget.Default, measurement), arguments[arguments.ToList().IndexOf("-af") + 1]);
        Assert.Single(arguments, argument => argument == output);
    }

    [Theory]
    [InlineData(-2, -1.5, 11)]
    [InlineData(-16, 3, 11)]
    [InlineData(-16, -1.5, 0)]
    [InlineData(double.NaN, -1.5, 11)]
    [InlineData(-16, double.PositiveInfinity, 11)]
    public void Loudness_target_rejects_values_loudnorm_cannot_use(double lufs, double peak, double range)
    {
        Assert.False(LoudnessTarget.TryCreate(lufs, peak, range, out var target));
        Assert.False(target.IsValid);
    }

    [Fact]
    public void Loudness_target_accepts_the_documented_bounds()
    {
        Assert.True(LoudnessTarget.TryCreate(-70, -9, 1, out var lowest));
        Assert.True(lowest.IsValid);
        Assert.True(LoudnessTarget.TryCreate(-5, 0, 50, out var highest));
        Assert.True(highest.IsValid);
        Assert.True(LoudnessTarget.Default.IsValid);
    }

    // ------------------------------------------------------------------ silencedetect

    [Fact]
    public void Silence_parser_pairs_start_and_end_times()
    {
        var spans = AudioTrim.ParseSilence(SilenceLog);

        Assert.Equal(2, spans.Count);
        Assert.Equal(0, spans[0].Start, 6);
        Assert.Equal(1.000295, spans[0].End, 6);
        Assert.False(spans[0].IsOpenEnded);
        Assert.Equal(3.999728, spans[1].Start, 6);
        Assert.Equal(5, spans[1].End, 6);
        Assert.Equal(1.000272, spans[1].Duration, 6);
    }

    [Fact]
    public void Silence_parser_keeps_an_unmatched_start_open_ended()
    {
        var spans = AudioTrim.ParseSilence("[Parsed_silencedetect_0 @ 0x1] silence_start: 4.5");

        Assert.Single(spans);
        Assert.Equal(4.5, spans[0].Start, 6);
        Assert.True(spans[0].IsOpenEnded);
        Assert.Equal(double.PositiveInfinity, spans[0].End);
    }

    [Fact]
    public void Silence_parser_ignores_a_reversed_pair()
    {
        var spans = AudioTrim.ParseSilence("silence_start: 3\nsilence_end: 1");

        Assert.Empty(spans);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ffmpeg version 9.0.2")]
    [InlineData("  Duration: 00:00:05.00, bitrate: 1411 kb/s")]
    [InlineData("[Parsed_silencedetect_0 @ 0x1] silence_duration: 1.5")]
    public void Silence_parser_ignores_text_without_markers(string? log)
    {
        Assert.Empty(AudioTrim.ParseSilence(log));
    }

    [Fact]
    public void Trim_silence_trims_only_the_edges_and_keeps_padding()
    {
        var spans = AudioTrim.ParseSilence(SilenceLog);

        var range = AudioTrim.TrimSilence(spans, 5);

        Assert.Equal(0.950295, range.Start, 6);
        Assert.Equal(4.049728, range.End, 6);
        Assert.Equal(3.099433, range.Duration, 6);
    }

    [Fact]
    public void Trim_silence_with_zero_padding_cuts_exactly_at_the_detected_times()
    {
        var spans = AudioTrim.ParseSilence(SilenceLog);

        var range = AudioTrim.TrimSilence(spans, 5, padding: 0);

        Assert.Equal(1.000295, range.Start, 6);
        Assert.Equal(3.999728, range.End, 6);
    }

    [Fact]
    public void Trim_silence_without_spans_keeps_the_whole_file()
    {
        var range = AudioTrim.TrimSilence(Array.Empty<SilenceSpan>(), 5);

        Assert.Equal(0, range.Start, 6);
        Assert.Equal(5, range.Duration, 6);
        Assert.Equal(AudioTrim.Full(5), range);
    }

    [Fact]
    public void Trim_silence_of_a_fully_silent_file_keeps_the_file()
    {
        var closed = new[] { new SilenceSpan(0, 5) };
        var open = new[] { new SilenceSpan(0, double.PositiveInfinity) };

        Assert.True(AudioTrim.IsEntirelySilent(closed, 5));
        Assert.True(AudioTrim.IsEntirelySilent(open, 5));
        Assert.Equal(AudioTrim.Full(5), AudioTrim.TrimSilence(closed, 5));
        Assert.Equal(AudioTrim.Full(5), AudioTrim.TrimSilence(open, 5));
    }

    [Fact]
    public void Trim_silence_handles_an_open_ended_trailing_silence()
    {
        var spans = new[] { new SilenceSpan(0, 1), new SilenceSpan(4, double.PositiveInfinity) };

        var range = AudioTrim.TrimSilence(spans, 5);

        Assert.Equal(0.95, range.Start, 6);
        Assert.Equal(4.05, range.End, 6);
    }

    [Fact]
    public void Entirely_silent_needs_coverage_from_start_to_end()
    {
        Assert.True(AudioTrim.IsEntirelySilent([new SilenceSpan(0, 2), new SilenceSpan(2, 4)], 4));
        Assert.False(AudioTrim.IsEntirelySilent([new SilenceSpan(0, 2)], 4));
        Assert.False(AudioTrim.IsEntirelySilent([new SilenceSpan(0.5, 4)], 4));
        Assert.False(AudioTrim.IsEntirelySilent([], 4));
    }

    [Fact]
    public void Trim_range_helpers_stay_inside_the_file()
    {
        Assert.Equal(new TrimRange(0, 3), AudioTrim.Clamp(new TrimRange(-1, 3), 5));
        Assert.Equal(new TrimRange(4, 1), AudioTrim.Clamp(new TrimRange(4, 5), 5));
        Assert.Equal(new TrimRange(0, 5), AudioTrim.Clamp(new TrimRange(0, 10), 5));
        Assert.Equal(default, AudioTrim.Clamp(new TrimRange(0, 1), 0));

        Assert.Equal(new TrimRange(1, 2), AudioTrim.FromTimes(1, 3, 5));
        Assert.Equal(new TrimRange(3, 0), AudioTrim.FromTimes(3, 1, 5));
        Assert.Equal(new TrimRange(0, 5), AudioTrim.FromTimes(-2, 10, 5));
        Assert.Equal(default, AudioTrim.FromTimes(0, 1, 0));
    }

    // ------------------------------------------------------------------ waveform

    [Fact]
    public void Peaks_of_a_constant_amplitude_are_flat()
    {
        var samples = new short[8000];
        Array.Fill(samples, (short)16384);

        var peaks = AudioTrim.BuildPeaks(samples, 8);

        Assert.Equal(8, peaks.Length);
        Assert.All(peaks, peak => Assert.Equal(0.5, peak, 3));
    }

    [Fact]
    public void Peaks_handle_silence_full_scale_and_more_buckets_than_samples()
    {
        Assert.All(AudioTrim.BuildPeaks(new short[100], 4), peak => Assert.Equal(0, peak, 3));

        var peaks = AudioTrim.BuildPeaks(new[] { short.MinValue, (short)0, short.MaxValue, (short)0 }, 4);
        Assert.Equal(1f, peaks[0], 3);
        Assert.Equal(0f, peaks[1], 3);
        Assert.Equal(1f, peaks[2], 3);
        Assert.Equal(0f, peaks[3], 3);

        var stretched = AudioTrim.BuildPeaks(new short[] { 100, 200, 300 }, 6);
        Assert.Equal(6, stretched.Length);

        Assert.Empty(AudioTrim.BuildPeaks(Array.Empty<short>(), 0));
        Assert.Equal(new float[4], AudioTrim.BuildPeaks(Array.Empty<short>(), 4));
    }

    [Fact]
    public void Byte_peaks_match_short_peaks()
    {
        var samples = new short[] { 0, 16384, -16384, 32767, -32768, 0 };
        var bytes = new byte[samples.Length * 2];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);

        Assert.Equal(AudioTrim.BuildPeaks(samples, 3), AudioTrim.BuildPeaks(bytes, 3));
    }

    [Fact]
    public void Chunked_peak_accumulation_matches_a_single_pass()
    {
        var samples = new short[1001];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = (short)(i % 2 == 0 ? i * 3 : -i * 5);
        var bytes = new byte[samples.Length * 2];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);

        var expected = AudioTrim.BuildPeaks(samples, 17);
        var chunked = new float[17];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var length = Math.Min(64, bytes.Length - offset);
            AudioTrim.AccumulatePeaks(bytes.AsSpan(offset, length), offset / 2, samples.Length, chunked);
            offset += length;
        }

        Assert.Equal(expected, chunked);
    }

    [Fact]
    public void Resampling_keeps_the_loudest_value_per_column()
    {
        var peaks = new[] { 0f, 0.25f, 0.5f, 1f, 0.5f, 0.25f, 0f, 0f };

        Assert.Equal(new[] { 1f, 0.5f }, AudioTrim.ResamplePeaks(peaks, 2));
        Assert.Equal(peaks, AudioTrim.ResamplePeaks(peaks, peaks.Length));
        Assert.Equal(new float[3], AudioTrim.ResamplePeaks(Array.Empty<float>(), 3));
        Assert.Empty(AudioTrim.ResamplePeaks(peaks, 0));
    }

    [Fact]
    public void Pcm_duration_is_samples_over_the_sample_rate()
    {
        Assert.Equal(1, AudioTrim.PcmDuration(16_000), 6);
        Assert.Equal(0.5, AudioTrim.PcmDuration(8_000), 6);
        Assert.Equal(0, AudioTrim.PcmDuration(0), 6);
        Assert.Equal(1, AudioTrim.PcmDuration(16_000, 8_000), 6);
    }

    // ------------------------------------------------------------------ time boxes

    [Theory]
    [InlineData("90", 90)]
    [InlineData("1:30", 90)]
    [InlineData("01:02:03.5", 3723.5)]
    [InlineData("0:01.5", 1.5)]
    [InlineData("0,5", 0.5)]
    [InlineData("٠:٠١٫٥", 1.5)]
    [InlineData(" 2:00 ", 120)]
    public void Time_parser_accepts_seconds_colons_commas_and_arabic_digits(string text, double expected)
    {
        Assert.True(AudioTrim.TryParseTime(text, out var seconds));
        Assert.Equal(expected, seconds, 6);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1:xx")]
    [InlineData("1:2:3:4")]
    [InlineData("-1")]
    [InlineData("1:-2")]
    [InlineData(":")]
    [InlineData("1::2")]
    public void Time_parser_rejects_garbage(string? text)
    {
        Assert.False(AudioTrim.TryParseTime(text, out _));
    }

    [Theory]
    [InlineData(0, "0:00.000")]
    [InlineData(1.5, "0:01.500")]
    [InlineData(90, "1:30.000")]
    [InlineData(3723.25, "1:02:03.250")]
    [InlineData(-5, "0:00.000")]
    [InlineData(59.9999, "1:00.000")]
    public void Time_formatter_rounds_to_milliseconds(double seconds, string expected)
    {
        Assert.Equal(expected, AudioTrim.FormatTime(seconds));
    }

    [Fact]
    public void Formatted_times_parse_back_to_the_same_second()
    {
        foreach (var seconds in new[] { 0.0, 1.5, 90.0, 3723.25, 3599.999 })
        {
            Assert.True(AudioTrim.TryParseTime(AudioTrim.FormatTime(seconds), out var parsed));
            Assert.Equal(seconds, parsed, 2);
        }
        Assert.Equal("0:00.000", AudioTrim.FormatTime(double.NaN));
    }

    // ------------------------------------------------------------------ ffmpeg arguments

    [Fact]
    public void Silence_filter_and_arguments_use_the_detector()
    {
        Assert.Equal("silencedetect=noise=-30dB:d=0.5", AudioTrim.SilenceFilter());
        Assert.Equal("silencedetect=noise=-35.5dB:d=1.25", AudioTrim.SilenceFilter(-35.5, 1.25));

        var arguments = AudioTrim.DetectSilenceArguments(@"C:\audio\tone.wav").ToList();
        Assert.Contains(AudioTrim.SilenceFilter(), arguments);
        Assert.Equal(new[] { "-f", "null", "-" }, arguments.Skip(arguments.Count - 3).ToArray());
    }

    [Fact]
    public void Waveform_arguments_decode_mono_16_bit_at_8_khz()
    {
        const string output = @"C:\temp\waveform.pcm";
        var arguments = AudioTrim.WaveformArguments(@"C:\audio\tone.wav", output);
        var list = arguments.ToList();

        Assert.Equal(output, arguments[^1]);
        Assert.Equal("1", list[list.IndexOf("-ac") + 1]);
        Assert.Equal("8000", list[list.IndexOf("-ar") + 1]);
        Assert.Equal("s16le", list[list.IndexOf("-f") + 1]);
    }

    [Fact]
    public void Trim_arguments_seek_before_the_input_and_limit_the_duration()
    {
        const string input = @"C:\audio\tone.wav";
        const string output = @"C:\audio\tone Trimmed.wav";

        var arguments = AudioTrim.TrimArguments(input, output, new TrimRange(1.5, 2.25), ["-c:a", "pcm_s16le"]).ToList();

        Assert.Equal(output, arguments[^1]);
        Assert.True(arguments.IndexOf("-ss") < arguments.IndexOf("-i"));
        Assert.Equal("1.5", arguments[arguments.IndexOf("-ss") + 1]);
        Assert.Equal("2.25", arguments[arguments.IndexOf("-t") + 1]);
        Assert.Equal("pcm_s16le", arguments[^2]);
    }

    [Fact]
    public void Trim_arguments_omit_the_seek_for_a_selection_that_starts_at_zero()
    {
        var arguments = AudioTrim.TrimArguments(@"C:\audio\tone.wav", @"C:\audio\out.wav", new TrimRange(0, 2), []);

        Assert.DoesNotContain("-ss", arguments);
        Assert.Contains("-t", arguments);
    }
}
