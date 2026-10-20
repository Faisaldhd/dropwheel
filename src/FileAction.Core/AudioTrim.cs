using System.Globalization;
using System.Text.RegularExpressions;

namespace FileAction.Core;

/// <summary>One stretch of silence ffmpeg's <c>silencedetect</c> reported. <see cref="End"/> can be +inf.</summary>
public readonly record struct SilenceSpan(double Start, double End)
{
    public double Duration => End - Start;

    /// <summary>True when the file ended while the silence was still running.</summary>
    public bool IsOpenEnded => double.IsPositiveInfinity(End);
}

/// <summary>The kept part of a file: where it starts and how long it is.</summary>
public readonly record struct TrimRange(double Start, double Duration)
{
    public double End => Start + Duration;
}

/// <summary>
/// Pure audio-trim helpers: parse <c>silencedetect</c> log lines, turn the detected edges into a trim
/// range, and reduce decoded PCM to a waveform envelope. The ffmpeg argument builders live here too so the
/// filter strings and the sample math are unit tested without a process.
/// </summary>
public static partial class AudioTrim
{
    /// <summary>Shorter selections are refused: ffmpeg and the encoders need at least a few frames.</summary>
    public const double MinimumDuration = 0.05;

    /// <summary>Silence kept at each edge so the first/last audible sample is never clipped.</summary>
    public const double DefaultPadding = 0.05;

    /// <summary>A span that starts this close to the edge counts as edge silence.</summary>
    public const double EdgeTolerance = 0.05;

    /// <summary>Mono PCM rate used for the waveform envelope; enough detail, small files.</summary>
    public const int WaveformSampleRate = 8000;

    private const double MaxTimeSeconds = 359_999.999;

    [GeneratedRegex(@"silence_(start|end):\s*(-?[0-9]+(?:\.[0-9]+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex SilenceRegex();

    /// <summary>
    /// Parses every <c>silence_start</c>/<c>silence_end</c> pair in an ffmpeg log. An unmatched start is
    /// returned with <see cref="double.PositiveInfinity"/> as its end (the file ended in silence).
    /// </summary>
    public static IReadOnlyList<SilenceSpan> ParseSilence(string? ffmpegLog)
    {
        var spans = new List<SilenceSpan>();
        if (string.IsNullOrWhiteSpace(ffmpegLog))
            return spans;

        double? pending = null;
        foreach (Match match in SilenceRegex().Matches(ffmpegLog))
        {
            if (!double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || !double.IsFinite(value))
            {
                continue;
            }

            if (match.Groups[1].Value == "start")
            {
                pending = Math.Max(0, value);
            }
            else if (pending is double start)
            {
                if (value >= start)
                    spans.Add(new SilenceSpan(start, value));
                pending = null;
            }
        }

        if (pending is double open)
            spans.Add(new SilenceSpan(open, double.PositiveInfinity));

        return spans;
    }

    /// <summary>Whole file, or an empty range when the duration is unknown.</summary>
    public static TrimRange Full(double sourceDuration) =>
        double.IsFinite(sourceDuration) && sourceDuration > 0
            ? new TrimRange(0, sourceDuration)
            : default;

    /// <summary>Clamps a range into the file; never returns a negative duration.</summary>
    public static TrimRange Clamp(TrimRange range, double sourceDuration)
    {
        var duration = Full(sourceDuration).Duration;
        if (duration <= 0)
            return default;

        var start = Math.Clamp(double.IsFinite(range.Start) ? range.Start : 0, 0, duration);
        var length = double.IsFinite(range.Duration) ? range.Duration : 0;
        length = Math.Clamp(length, 0, duration - start);
        return new TrimRange(start, length);
    }

    /// <summary>
    /// The selection described by two times as the user typed them. An end before the start collapses to
    /// a zero-length range (the caller reports that); both values are clamped to the file.
    /// </summary>
    public static TrimRange FromTimes(double start, double end, double sourceDuration)
    {
        var duration = Full(sourceDuration).Duration;
        if (duration <= 0)
            return default;

        var clampedStart = Math.Clamp(double.IsFinite(start) ? start : 0, 0, duration);
        var clampedEnd = Math.Clamp(double.IsFinite(end) ? end : duration, clampedStart, duration);
        return new TrimRange(clampedStart, clampedEnd - clampedStart);
    }

    /// <summary>
    /// True when the detected spans cover the file from start to finish, so there is no audible part left.
    /// </summary>
    public static bool IsEntirelySilent(IReadOnlyList<SilenceSpan> spans, double sourceDuration)
    {
        if (spans is null || spans.Count == 0 || !double.IsFinite(sourceDuration) || sourceDuration <= 0)
            return false;

        var coveredUntil = 0.0;
        foreach (var span in spans)
        {
            if (span.Start > coveredUntil + EdgeTolerance)
                return false;
            coveredUntil = Math.Max(coveredUntil, span.IsOpenEnded ? sourceDuration : span.End);
            if (coveredUntil >= sourceDuration - EdgeTolerance)
                return true;
        }
        return false;
    }

    /// <summary>
    /// The range that removes only leading and trailing silence, keeping <paramref name="padding"/> seconds
    /// of the silence at each edge. Falls back to the whole file when nothing usable remains.
    /// </summary>
    public static TrimRange TrimSilence(
        IReadOnlyList<SilenceSpan> spans,
        double sourceDuration,
        double padding = DefaultPadding)
    {
        var full = Full(sourceDuration);
        if (full.Duration <= 0 || spans is null || spans.Count == 0)
            return full;

        if (IsEntirelySilent(spans, sourceDuration))
            return full;

        padding = double.IsFinite(padding) ? Math.Max(0, padding) : DefaultPadding;
        var start = 0.0;
        var end = full.Duration;

        foreach (var span in spans)
        {
            if (span.Start > EdgeTolerance)
                break;
            if (span.End > span.Start)
            {
                start = Math.Clamp(span.End - padding, 0, full.Duration);
                break;
            }
        }

        // Only the last span can reach the end of the file; earlier ones end before the audible tail.
        var last = spans[spans.Count - 1];
        if ((last.IsOpenEnded || last.End >= full.Duration - EdgeTolerance)
            && last.Start > start + MinimumDuration)
        {
            end = Math.Clamp(last.Start + padding, start, full.Duration);
        }

        if (end - start < MinimumDuration)
            return full;

        return new TrimRange(start, end - start);
    }

    /// <summary>
    /// Peak envelope of mono PCM: one value in 0..1 per bucket, taken from the largest absolute sample.
    /// Empty buckets (more buckets than samples) are zero, so the shape stays stable while resizing.
    /// </summary>
    public static float[] BuildPeaks(ReadOnlySpan<short> samples, int buckets)
    {
        if (buckets <= 0)
            return [];

        var peaks = new float[buckets];
        for (var i = 0; i < samples.Length; i++)
        {
            var bucket = (int)((long)i * buckets / samples.Length);
            var value = Math.Abs((int)samples[i]) / 32768f;
            if (value > peaks[bucket])
                peaks[bucket] = value;
        }
        return peaks;
    }

    /// <summary>Same envelope from little-endian 16-bit PCM as ffmpeg's <c>s16le</c> muxer writes it.</summary>
    public static float[] BuildPeaks(ReadOnlySpan<byte> pcm16LittleEndian, int buckets)
    {
        if (buckets <= 0)
            return [];

        var peaks = new float[buckets];
        AccumulatePeaks(pcm16LittleEndian, 0, pcm16LittleEndian.Length / 2, peaks);
        return peaks;
    }

    /// <summary>
    /// Adds one chunk of little-endian 16-bit PCM to an envelope that is filled across several reads, so a
    /// long file never has to be held in memory. <paramref name="firstSampleIndex"/> is the sample offset of
    /// the chunk in the whole stream and <paramref name="totalSamples"/> is the stream length.
    /// </summary>
    public static void AccumulatePeaks(
        ReadOnlySpan<byte> pcm16LittleEndian,
        long firstSampleIndex,
        long totalSamples,
        Span<float> peaks)
    {
        if (peaks.Length == 0 || totalSamples <= 0 || firstSampleIndex < 0)
            return;

        var samples = pcm16LittleEndian.Length / 2;
        for (var i = 0; i < samples; i++)
        {
            var index = firstSampleIndex + i;
            if (index >= totalSamples)
                break;

            var offset = i * 2;
            var sample = (short)(pcm16LittleEndian[offset] | (pcm16LittleEndian[offset + 1] << 8));
            var bucket = (int)(index * peaks.Length / totalSamples);
            var value = Math.Abs((int)sample) / 32768f;
            if (value > peaks[bucket])
                peaks[bucket] = value;
        }
    }

    /// <summary>Stretches or squeezes an envelope to the number of pixel columns the control has.</summary>
    public static float[] ResamplePeaks(IReadOnlyList<float> peaks, int columns)
    {
        if (columns <= 0)
            return [];
        if (peaks is null || peaks.Count == 0)
            return new float[columns];

        var result = new float[columns];
        var count = peaks.Count;
        for (var column = 0; column < columns; column++)
        {
            var start = (int)((long)column * count / columns);
            var end = (int)((long)(column + 1) * count / columns);
            if (end <= start)
            {
                result[column] = peaks[Math.Min(start, count - 1)];
                continue;
            }

            var peak = 0f;
            for (var i = start; i < end; i++)
                peak = Math.Max(peak, peaks[i]);
            result[column] = peak;
        }
        return result;
    }

    /// <summary>Duration of a mono 16-bit PCM buffer, the format <see cref="WaveformArguments"/> writes.</summary>
    public static double PcmDuration(long bytes, int sampleRate = WaveformSampleRate) =>
        bytes <= 0 || sampleRate <= 0 ? 0 : bytes / 2.0 / sampleRate;

    /// <summary>"h:mm:ss.mmm" (or "m:ss.mmm" under an hour) with Western digits; negative input is 0.</summary>
    public static string FormatTime(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
            seconds = 0;

        var milliseconds = (long)Math.Round(Math.Min(seconds, MaxTimeSeconds) * 1000, MidpointRounding.AwayFromZero);
        var hours = milliseconds / 3_600_000;
        var minutes = milliseconds / 60_000 % 60;
        var wholeSeconds = milliseconds / 1000 % 60;
        var fraction = milliseconds % 1000;

        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{wholeSeconds:00}.{fraction:000}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}:{wholeSeconds:00}.{fraction:000}");
    }

    /// <summary>
    /// Parses "90", "1:30", "1:02:03.5" and "0:01٫5" into seconds. Arabic-Indic and full-width digits are
    /// accepted and the Arabic decimal separator is treated like ".".
    /// </summary>
    public static bool TryParseTime(string? text, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = NormalizeDigits(text)
            .Trim()
            .Replace("\u066B", ".", StringComparison.Ordinal)
            .Replace("\u066C", "", StringComparison.Ordinal)
            .Replace(',', '.');

        var parts = normalized.Split(':');
        if (parts.Length is 0 or > 3)
            return false;

        var total = 0.0;
        foreach (var part in parts)
        {
            if (part.Length == 0)
                return false;
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return false;
            if (!double.IsFinite(value) || value < 0)
                return false;
            total = total * 60 + value;
        }

        if (!double.IsFinite(total) || total > MaxTimeSeconds)
            return false;

        seconds = total;
        return true;
    }

    /// <summary>ffmpeg filter that reports silence stretches at least <paramref name="minimumSilence"/> long.</summary>
    public static string SilenceFilter(double noiseDb = -30, double minimumSilence = 0.5) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"silencedetect=noise={noiseDb:0.#}dB:d={minimumSilence:0.###}");

    /// <summary>ffmpeg arguments for the silence analysis pass (nothing is written).</summary>
    public static IReadOnlyList<string> DetectSilenceArguments(
        string inputPath,
        double noiseDb = -30,
        double minimumSilence = 0.5) =>
    [
        "-hide_banner", "-nostdin", "-nostats",
        "-i", inputPath,
        "-map", "0:a:0",
        "-af", SilenceFilter(noiseDb, minimumSilence),
        "-f", "null", "-",
    ];

    /// <summary>
    /// ffmpeg arguments that decode the audio to mono 16-bit PCM for the waveform envelope. The output path
    /// must end up being read as little-endian shorts (see <see cref="BuildPeaks(ReadOnlySpan{byte}, int)"/>).
    /// </summary>
    public static IReadOnlyList<string> WaveformArguments(
        string inputPath,
        string outputPath,
        int sampleRate = WaveformSampleRate) =>
    [
        "-hide_banner", "-nostdin", "-nostats", "-y",
        "-i", inputPath,
        "-map", "0:a:0",
        "-vn",
        "-ac", "1",
        "-ar", sampleRate.ToString(CultureInfo.InvariantCulture),
        "-f", "s16le",
        outputPath,
    ];

    /// <summary>
    /// ffmpeg arguments that keep only <paramref name="range"/>: the input seeks first (fast, accurate for
    /// audio), then the exact duration and the caller's encoder arguments are written to the output.
    /// </summary>
    public static IReadOnlyList<string> TrimArguments(
        string inputPath,
        string outputPath,
        TrimRange range,
        IEnumerable<string> encoderArgs)
    {
        ArgumentNullException.ThrowIfNull(encoderArgs);

        var arguments = new List<string> { "-hide_banner", "-nostdin", "-nostats", "-y" };
        if (range.Start > 0)
        {
            arguments.Add("-ss");
            arguments.Add(range.Start.ToString("0.###", CultureInfo.InvariantCulture));
        }
        arguments.AddRange(["-i", inputPath]);
        arguments.AddRange(["-t", Math.Max(MinimumDuration, range.Duration).ToString("0.###", CultureInfo.InvariantCulture)]);
        arguments.AddRange(["-map", "0:a:0"]);
        arguments.AddRange(encoderArgs);
        arguments.Add(outputPath);
        return arguments;
    }

    private static string NormalizeDigits(string text)
    {
        char[]? buffer = null;
        for (var i = 0; i < text.Length; i++)
        {
            var digit = text[i] switch
            {
                >= '\u0660' and <= '\u0669' => text[i] - '\u0660',
                >= '\u06F0' and <= '\u06F9' => text[i] - '\u06F0',
                >= '\uFF10' and <= '\uFF19' => text[i] - '\uFF10',
                _ => -1,
            };
            if (digit < 0)
                continue;

            buffer ??= text.ToCharArray();
            buffer[i] = (char)('0' + digit);
        }
        return buffer is null ? text : new string(buffer);
    }
}
