using System.Globalization;
using System.Text.Json;

namespace FileAction.Core;

/// <summary>
/// A loudness target in the units ffmpeg's <c>loudnorm</c> filter uses: integrated loudness in LUFS,
/// true peak in dBFS (0 or below) and loudness range in LU. The bounds are loudnorm's practical range,
/// so the UI can reject a value before a long analysis pass starts.
/// </summary>
public readonly record struct LoudnessTarget(double IntegratedLufs, double TruePeakDb, double LoudnessRangeLu)
{
    public const double MinIntegratedLufs = -70;
    public const double MaxIntegratedLufs = -5;
    public const double MinTruePeakDb = -9;
    public const double MaxTruePeakDb = 0;
    public const double MinLoudnessRangeLu = 1;
    public const double MaxLoudnessRangeLu = 50;

    /// <summary>Streaming-friendly default: -16 LUFS, -1.5 dBTP, 11 LU.</summary>
    public static LoudnessTarget Default => new(-16, -1.5, 11);

    /// <summary>True when every value is finite and inside the range <c>loudnorm</c> accepts.</summary>
    public bool IsValid =>
        InRange(IntegratedLufs, MinIntegratedLufs, MaxIntegratedLufs)
        && InRange(TruePeakDb, MinTruePeakDb, MaxTruePeakDb)
        && InRange(LoudnessRangeLu, MinLoudnessRangeLu, MaxLoudnessRangeLu);

    /// <summary>
    /// Builds a target and reports whether it can be sent to <c>loudnorm</c>. <paramref name="target"/>
    /// always carries the values that were supplied, so the caller can echo them in a validation message.
    /// </summary>
    public static bool TryCreate(double integratedLufs, double truePeakDb, double loudnessRangeLu, out LoudnessTarget target)
    {
        target = new LoudnessTarget(integratedLufs, truePeakDb, loudnessRangeLu);
        return target.IsValid;
    }

    private static bool InRange(double value, double min, double max) =>
        double.IsFinite(value) && value >= min && value <= max;
}

/// <summary>
/// What one <c>loudnorm</c> measurement pass reported: the source loudness (<c>input_*</c>) and the
/// loudness the filter predicts for the chosen target (<c>output_*</c>), plus the values the second pass
/// needs (<c>measured_*</c> and <c>offset</c>).
/// <para>
/// A silent source measures <c>-inf</c>, which is kept as <see cref="double.NegativeInfinity"/> so the UI
/// can say "silent" instead of showing a meaningless number.
/// </para>
/// </summary>
public sealed record LoudnessMeasurement(
    double Integrated,
    double TruePeak,
    double LoudnessRange,
    double Threshold,
    double PredictedIntegrated,
    double PredictedTruePeak,
    double PredictedRange,
    double PredictedThreshold,
    string NormalizationType,
    double TargetOffset)
{
    /// <summary>True when ffmpeg reported <c>linear</c> for this pass.</summary>
    public bool IsLinear => string.Equals(NormalizationType, "linear", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the source had no measurable level (-inf integrated loudness).</summary>
    public bool IsSilent => !double.IsFinite(Integrated);

    /// <summary>True when the measured values can be fed into a second pass.</summary>
    public bool CanApplyMeasured =>
        double.IsFinite(Integrated)
        && double.IsFinite(TruePeak)
        && double.IsFinite(LoudnessRange)
        && double.IsFinite(Threshold)
        && double.IsFinite(TargetOffset);
}

/// <summary>
/// Pure EBU R128 / ITU-R BS.1770 helpers for the audio normalizer: parse the JSON that
/// <c>loudnorm=…:print_format=json</c> prints, and build both ffmpeg passes from it. Nothing here touches
/// a process, so the parser and the argument lists are unit tested without FFmpeg.
/// </summary>
public static class AudioLoudness
{
    private static readonly string[] RequiredKeys =
    [
        "input_i", "input_tp", "input_lra", "input_thresh",
        "output_i", "output_tp", "output_lra", "output_thresh",
        "normalization_type",
    ];

    /// <summary>
    /// Reads the last loudnorm JSON object in an ffmpeg log (the filter prints it to stderr after the
    /// pass). Returns null when the log has no complete object or a value cannot be read.
    /// </summary>
    public static LoudnessMeasurement? Parse(string? ffmpegLog)
    {
        if (string.IsNullOrWhiteSpace(ffmpegLog))
            return null;

        // The filter prints exactly one object per run; other log lines only surround it. Taking the last
        // braces keeps a truncated earlier attempt from hiding a complete one.
        var start = ffmpegLog.LastIndexOf('{');
        var end = ffmpegLog.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;

        try
        {
            using var document = JsonDocument.Parse(ffmpegLog[start..(end + 1)]);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var key in RequiredKeys)
            {
                if (!root.TryGetProperty(key, out _))
                    return null;
            }

            if (!TryReadNumber(root, "input_i", out var integrated)
                || !TryReadNumber(root, "input_tp", out var truePeak)
                || !TryReadNumber(root, "input_lra", out var range)
                || !TryReadNumber(root, "input_thresh", out var threshold)
                || !TryReadNumber(root, "output_i", out var predictedIntegrated)
                || !TryReadNumber(root, "output_tp", out var predictedTruePeak)
                || !TryReadNumber(root, "output_lra", out var predictedRange)
                || !TryReadNumber(root, "output_thresh", out var predictedThreshold))
            {
                return null;
            }

            var type = root.GetProperty("normalization_type").ValueKind == JsonValueKind.String
                ? root.GetProperty("normalization_type").GetString()
                : null;
            if (string.IsNullOrWhiteSpace(type))
                return null;

            // Older logs omit target_offset; zero is the neutral offset.
            if (!TryReadNumber(root, "target_offset", out var offset))
                offset = 0;

            return new LoudnessMeasurement(
                integrated, truePeak, range, threshold,
                predictedIntegrated, predictedTruePeak, predictedRange, predictedThreshold,
                type.Trim(), offset);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The first pass: measure the file and print the JSON the second pass needs.</summary>
    public static string MeasureFilter(LoudnessTarget target) => Filter(target, "json");

    /// <summary>
    /// The second pass: normalize to the target using the first pass's measured values. Without usable
    /// measurements (silence, or a failed pass) this falls back to loudnorm's single-pass dynamic mode.
    /// </summary>
    public static string ApplyFilter(LoudnessTarget target, LoudnessMeasurement? measurement)
    {
        var filter = Filter(target, "summary");
        if (measurement is null || !measurement.CanApplyMeasured)
            return filter;

        return filter + string.Create(
            CultureInfo.InvariantCulture,
            $":measured_I={measurement.Integrated:0.###}:measured_TP={measurement.TruePeak:0.###}:measured_LRA={measurement.LoudnessRange:0.###}:measured_thresh={measurement.Threshold:0.###}:offset={measurement.TargetOffset:0.###}:linear=true");
    }

    /// <summary>ffmpeg arguments for the measurement pass (output is the null muxer, nothing is written).</summary>
    public static IReadOnlyList<string> MeasureArguments(string inputPath, LoudnessTarget target) =>
    [
        "-hide_banner", "-nostdin", "-nostats",
        "-i", inputPath,
        "-map", "0:a:0",
        "-af", MeasureFilter(target),
        "-f", "null", "-",
    ];

    /// <summary>
    /// ffmpeg arguments for the apply pass: the filter, the caller's encoder arguments and the output path.
    /// The output path is last so the encoder options apply to it.
    /// </summary>
    public static IReadOnlyList<string> ApplyArguments(
        string inputPath,
        string outputPath,
        LoudnessTarget target,
        LoudnessMeasurement? measurement,
        IEnumerable<string> encoderArgs)
    {
        ArgumentNullException.ThrowIfNull(encoderArgs);

        var arguments = new List<string>
        {
            "-hide_banner", "-nostdin", "-nostats", "-y",
            "-i", inputPath,
            "-map", "0:a:0",
            "-af", ApplyFilter(target, measurement),
        };
        arguments.AddRange(encoderArgs);
        arguments.Add(outputPath);
        return arguments;
    }

    private static string Filter(LoudnessTarget target, string printFormat) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"loudnorm=I={target.IntegratedLufs:0.###}:TP={target.TruePeakDb:0.###}:LRA={target.LoudnessRangeLu:0.###}:print_format={printFormat}");

    private static bool TryReadNumber(JsonElement root, string name, out double value)
    {
        value = double.NaN;
        if (!root.TryGetProperty(name, out var element))
            return false;

        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetDouble(out value) && double.IsFinite(value),
            JsonValueKind.String => TryParseNumber(element.GetString(), out value),
            _ => false,
        };
    }

    /// <summary>
    /// loudnorm prints its numbers as strings ("-23.02"). Silence is <c>-inf</c>; anything that is not a
    /// finite number or a signed infinity is rejected.
    /// </summary>
    private static bool TryParseNumber(string? text, out double value)
    {
        value = double.NaN;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        if (trimmed.Equals("inf", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("+inf", StringComparison.OrdinalIgnoreCase))
        {
            value = double.PositiveInfinity;
            return true;
        }
        if (trimmed.Equals("-inf", StringComparison.OrdinalIgnoreCase))
        {
            value = double.NegativeInfinity;
            return true;
        }

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value);
    }
}
