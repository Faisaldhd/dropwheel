using System.Globalization;

namespace FileAction.Core;

/// <summary>Which channel layout the channels tool writes.</summary>
public enum AudioChannelMode
{
    Mono,
    Stereo,
}

/// <summary>Monitoring choice for the channels tool: keep both sides or isolate one.</summary>
public enum AudioChannelSolo
{
    Both,
    Left,
    Right,
}

/// <summary>The shape of a visualizer video.</summary>
public enum AudioVisualizerOrientation
{
    Landscape,
    Portrait,
    Square,
}

/// <summary>How the visualizer draws the audio.</summary>
public enum AudioVisualizerStyle
{
    Waveform,
    Scope,
}

/// <summary>One slice of time, in seconds, that the bleep tool replaces with a tone.</summary>
/// <param name="Start">Start offset from the beginning of the audio, in seconds.</param>
/// <param name="End">End offset, in seconds.</param>
public readonly record struct AudioRange(double Start, double End)
{
    /// <summary>Length in seconds; never negative, so an invalid range has length 0.</summary>
    public double Length => Math.Max(0, End - Start);
}

/// <summary>
/// The audio-effect maths shared by the three audio tool windows and by the ffmpeg command line:
/// channel-mode pan filters, the bleep replacement filtergraph (ranges, tone, volume), range editing
/// and the visualizer argument builder. Everything here is a pure string/number function, so the exact
/// filters and arguments are unit tested without starting ffmpeg.
/// </summary>
public static class AudioEffects
{
    /// <summary>Default bleep tone.</summary>
    public const double DefaultToneHz = 1000;

    /// <summary>Default bleep tone level, in percent.</summary>
    public const int DefaultToneVolume = 70;

    /// <summary>Sample rate the bleep graph works at; both branches of the graph use it.</summary>
    public const int DefaultSampleRate = 48000;

    /// <summary>Shorter ranges are treated as an accidental click and dropped.</summary>
    public const double MinRangeSeconds = 0.05;

    /// <summary>Overlapping (or touching) ranges are merged so the filter does not repeat itself.</summary>
    public const double MergeGapSeconds = 0.001;

    /// <summary>Visualizer quality (x264 constant rate factor).</summary>
    public const int DefaultCrf = 23;

    /// <summary>Visualizer frame rate.</summary>
    public const int VisualizerFps = 25;

    /// <summary>Waveform colour: the DropWheel accent, as an ffmpeg colour literal.</summary>
    public const string VisualizerColor = "0x8224D4";

    /// <summary>Volume percent (0–200) to a linear gain.</summary>
    public static double Gain(int volumePercent) => Math.Clamp(volumePercent, 0, 200) / 100.0;

    /// <summary>A stable, invariant number for ffmpeg: no exponent, no trailing zeros.</summary>
    public static string Number(double value) =>
        Math.Round(value, 3, MidpointRounding.AwayFromZero).ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// The pan filter for one channel mode. Mono folds both inputs (0.5 each at full volume) so a loud
    /// source cannot clip; stereo keeps the two sides apart. <paramref name="solo"/> mutes the other side
    /// so "solo left" both previews and writes only the left channel.
    /// </summary>
    public static string ChannelFilter(
        AudioChannelMode mode,
        int leftVolumePercent = 100,
        int rightVolumePercent = 100,
        AudioChannelSolo solo = AudioChannelSolo.Both)
    {
        var left = Gain(leftVolumePercent) * (solo == AudioChannelSolo.Right ? 0 : 1);
        var right = Gain(rightVolumePercent) * (solo == AudioChannelSolo.Left ? 0 : 1);

        return mode == AudioChannelMode.Mono
            ? $"pan=mono|c0={Number(0.5 * left)}*c0+{Number(0.5 * right)}*c1"
            : $"pan=stereo|c0={Number(left)}*c0|c1={Number(right)}*c1";
    }

    /// <summary>The ffmpeg output arguments for the channels tool: the pan filter plus the channel count.</summary>
    public static IReadOnlyList<string> ChannelArguments(
        AudioChannelMode mode,
        int leftVolumePercent = 100,
        int rightVolumePercent = 100,
        AudioChannelSolo solo = AudioChannelSolo.Both) =>
    [
        "-af", ChannelFilter(mode, leftVolumePercent, rightVolumePercent, solo),
        "-ac", mode == AudioChannelMode.Mono ? "1" : "2",
    ];

    /// <summary>
    /// Clamps ranges to the media, drops empty ones, sorts them and merges overlapping ones. The result is
    /// what the filtergraph and the waveform overlay both use, so the preview shows exactly what is written.
    /// </summary>
    public static IReadOnlyList<AudioRange> NormalizeRanges(IEnumerable<AudioRange> ranges, double duration)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        var limit = Math.Max(0, duration);
        var clean = new List<AudioRange>();
        foreach (var range in ranges)
        {
            var start = Math.Clamp(range.Start, 0, limit);
            var end = Math.Clamp(range.End, 0, limit);
            if (end - start < MinRangeSeconds)
                continue;
            clean.Add(new AudioRange(start, end));
        }

        clean.Sort((a, b) => a.Start.CompareTo(b.Start));
        var merged = new List<AudioRange>();
        foreach (var range in clean)
        {
            if (merged.Count > 0 && range.Start <= merged[^1].End + MergeGapSeconds)
            {
                merged[^1] = new AudioRange(merged[^1].Start, Math.Max(merged[^1].End, range.End));
                continue;
            }
            merged.Add(range);
        }
        return merged;
    }

    /// <summary>Moves a range start by <paramref name="delta"/> seconds, never past its end.</summary>
    public static AudioRange NudgeStart(AudioRange range, double delta, double duration)
    {
        var latest = Math.Max(0, range.End - MinRangeSeconds);
        var start = Math.Clamp(range.Start + delta, 0, Math.Max(0, Math.Min(duration, latest)));
        return range with { Start = Math.Round(start, 3, MidpointRounding.AwayFromZero) };
    }

    /// <summary>Moves a range end by <paramref name="delta"/> seconds, never before its start.</summary>
    public static AudioRange NudgeEnd(AudioRange range, double delta, double duration)
    {
        var earliest = Math.Min(duration, range.Start + MinRangeSeconds);
        var end = Math.Clamp(range.End + delta, Math.Max(0, earliest), Math.Max(0, duration));
        return range with { End = Math.Round(end, 3, MidpointRounding.AwayFromZero) };
    }

    /// <summary>
    /// A section created by dragging across the bleep layer: the two times are sorted, clamped to the
    /// audio and rounded to milliseconds. Null when the drag is shorter than <see cref="MinRangeSeconds"/>
    /// (a click, not a section) or when the audio has no length yet.
    /// </summary>
    public static AudioRange? NewSection(double duration, double from, double to)
    {
        if (duration <= 0 || double.IsNaN(from) || double.IsNaN(to))
            return null;

        var start = Math.Clamp(Math.Min(from, to), 0, duration);
        var end = Math.Clamp(Math.Max(from, to), 0, duration);
        return end - start < MinRangeSeconds ? null : new AudioRange(Round3(start), Round3(end));
    }

    /// <summary>
    /// Moves a whole section so its start lands on <paramref name="toStart"/>: the length never changes
    /// and the clip cannot leave the audio (a clip longer than the audio fills it).
    /// </summary>
    public static AudioRange MoveSection(AudioRange range, double toStart, double duration)
    {
        var limit = Math.Max(0, duration);
        var length = Math.Min(Math.Max(0, range.Length), limit);
        var start = Math.Clamp(toStart, 0, Math.Max(0, limit - length));
        return new AudioRange(Round3(start), Round3(start + length));
    }

    /// <summary>
    /// Resizes one edge of a section to <paramref name="to"/>. The start can never pass the end (and the
    /// other way round), and neither edge leaves the audio.
    /// </summary>
    public static AudioRange ResizeSection(AudioRange range, bool startEdge, double to, double duration)
    {
        var limit = Math.Max(0, duration);
        if (startEdge)
        {
            var latest = Math.Max(0, Math.Min(limit, range.End - MinRangeSeconds));
            return range with { Start = Round3(Math.Clamp(to, 0, latest)) };
        }

        var earliest = Math.Max(0, Math.Min(limit, range.Start + MinRangeSeconds));
        return range with { End = Round3(Math.Clamp(to, earliest, limit)) };
    }

    /// <summary>
    /// "0:00.00" for the bleep layer's ruler; hours are only shown once the audio is that long. Always
    /// invariant digits, so Arabic shows the same Western numbers as the rest of the technical text.
    /// </summary>
    public static string FormatTimecode(double seconds)
    {
        var hundredths = (int)Math.Round(Math.Max(0, seconds) * 100, MidpointRounding.AwayFromZero);
        var hours = hundredths / 360_000;
        hundredths %= 360_000;
        var minutes = hundredths / 6_000;
        hundredths %= 6_000;
        var whole = hundredths / 100;
        var fraction = hundredths % 100;

        return hours > 0
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}.{3:00}", hours, minutes, whole, fraction)
            : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}.{2:00}", minutes, whole, fraction);
    }

    private static double Round3(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The bleep filtergraph: the source is muted inside every range and a sine tone of
    /// <paramref name="toneHz"/> at <paramref name="volumePercent"/> is added exactly there. Both branches
    /// use the same time expression, so the tone and the hole always line up.
    /// </summary>
    public static string BleepFilter(
        IReadOnlyList<AudioRange> ranges,
        double toneHz = DefaultToneHz,
        int volumePercent = DefaultToneVolume,
        int sampleRate = DefaultSampleRate)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        var last = ranges.Count == 0 ? 0 : ranges.Max(r => r.End);
        var normalized = NormalizeRanges(ranges, last);
        if (normalized.Count == 0)
            throw new ArgumentException("The bleep filter needs at least one non-empty range.", nameof(ranges));

        var rate = Math.Clamp(sampleRate, 8000, 192000);
        var hz = Number(Math.Clamp(toneHz, 20, 20000));
        var amplitude = Number(Math.Clamp(volumePercent, 0, 100) / 100.0);
        var gate = string.Join("+", normalized.Select(r => $"between(t,{Number(r.Start)},{Number(r.End)})"));

        return "[0:a]aformat=sample_fmts=fltp:sample_rates=" + rate
            + ":channel_layouts=stereo,volume=volume=0:enable='" + gate + "'[duck];"
            + "aevalsrc='sin(2*PI*" + hz + "*t)*" + amplitude + "*(" + gate + ")':s=" + rate + ":c=stereo[tone];"
            + "[duck][tone]amix=inputs=2:duration=first:normalize=0[out]";
    }

    /// <summary>The ffmpeg output arguments for the bleep tool: the graph plus the stream it produces.</summary>
    public static IReadOnlyList<string> BleepArguments(
        IReadOnlyList<AudioRange> ranges,
        double toneHz = DefaultToneHz,
        int volumePercent = DefaultToneVolume,
        int sampleRate = DefaultSampleRate) =>
    [
        "-filter_complex", BleepFilter(ranges, toneHz, volumePercent, sampleRate),
        "-map", "[out]",
    ];

    /// <summary>The pixel size of a visualizer orientation.</summary>
    public static (int Width, int Height) VisualizerDimensions(AudioVisualizerOrientation orientation) => orientation switch
    {
        AudioVisualizerOrientation.Portrait => (1080, 1920),
        AudioVisualizerOrientation.Square => (1080, 1080),
        _ => (1920, 1080),
    };

    /// <summary>The ffmpeg size expression for a visualizer orientation, e.g. "1920x1080".</summary>
    public static string VisualizerSize(AudioVisualizerOrientation orientation)
    {
        var (width, height) = VisualizerDimensions(orientation);
        return $"{width}x{height}";
    }

    /// <summary>
    /// The complete output arguments of the visualizer: an animated waveform (or vectorscope) drawn on
    /// black, or composited over a still image that the caller supplies as input 0 (audio is input 1 then).
    /// </summary>
    public static IReadOnlyList<string> VisualizerArguments(
        AudioVisualizerOrientation orientation,
        AudioVisualizerStyle style,
        int crf = DefaultCrf,
        bool hasBackgroundImage = false,
        int fps = VisualizerFps)
    {
        var (width, height) = VisualizerDimensions(orientation);
        var rate = Math.Clamp(fps, 1, 120);
        var quality = Math.Clamp(crf, 0, 51);
        var size = $"{width}x{height}";
        var audio = hasBackgroundImage ? "1:a:0" : "0:a:0";

        var visual = style == AudioVisualizerStyle.Scope
            ? $"aformat=channel_layouts=stereo,avectorscope=s={size}:rate={rate}:mode=lissajous:draw=line"
            : $"showwaves=s={size}:mode=cline:rate={rate}:colors={VisualizerColor}";

        var graph = hasBackgroundImage
            ? $"[0:v]scale={width}:{height}:force_original_aspect_ratio=increase,crop={width}:{height},setsar=1[bg];"
              + $"[{audio}]{visual},format=rgba,colorkey=0x000000:0.10:0.02[wave];"
              + "[bg][wave]overlay=0:0:shortest=1[v]"
            : $"[{audio}]{visual}[v]";

        return
        [
            "-filter_complex", graph,
            "-map", "[v]",
            "-map", audio,
            "-r", rate.ToString(CultureInfo.InvariantCulture),
            "-c:v", "libx264", "-preset", "medium",
            "-crf", quality.ToString(CultureInfo.InvariantCulture),
            "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "160k",
            "-movflags", "+faststart",
            "-shortest",
        ];
    }
}
