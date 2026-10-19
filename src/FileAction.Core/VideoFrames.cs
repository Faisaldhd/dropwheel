using System.Globalization;
using System.Text;

namespace FileAction.Core;

/// <summary>
/// Frame-index arithmetic shared by the two video tools. A frame index is zero based, exactly like the
/// <c>n</c> in FFmpeg's <c>select</c> filter: frame 0 is the first picture, frame 123 is shown and saved
/// as <c>00123</c>. Nothing here touches a file, so the UI math and the file names can be unit tested
/// without an FFmpeg run.
/// </summary>
public static class FrameMath
{
    /// <summary>Frame rate used when the probe could not read one.</summary>
    public const double DefaultFps = 30.0;

    /// <summary>Width of the zero-padded frame number in capture file names.</summary>
    public const int FrameDigits = 5;

    private const double MaxFps = 1000.0;

    /// <summary>A usable frame rate, falling back to <see cref="DefaultFps"/> for missing/garbage input.</summary>
    public static double SafeFps(double fps) =>
        double.IsFinite(fps) && fps is > 0 and <= MaxFps ? fps : DefaultFps;

    /// <summary>How many frames a clip has, at least one even when the duration is unknown.</summary>
    public static int TotalFrames(double durationSeconds, double fps)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0)
            return 1;
        var frames = Math.Round(durationSeconds * SafeFps(fps), MidpointRounding.AwayFromZero);
        return (int)Math.Clamp(frames, 1, int.MaxValue - 1);
    }

    /// <summary>The timestamp of a frame index, in seconds.</summary>
    public static double TimeOfFrame(int frame, double fps) => Math.Max(0, frame) / SafeFps(fps);

    /// <summary>The frame index shown at <paramref name="seconds"/>, clamped to <paramref name="totalFrames"/>.</summary>
    public static int FrameAt(double seconds, double fps, int totalFrames)
    {
        var total = Math.Max(1, totalFrames);
        if (!double.IsFinite(seconds) || seconds <= 0)
            return 0;
        var index = (int)Math.Floor(seconds * SafeFps(fps) + 1e-6);
        return Math.Clamp(index, 0, total - 1);
    }

    /// <summary>Moves a frame index by <paramref name="delta"/> without leaving the clip.</summary>
    public static int Step(int frame, int delta, int totalFrames) =>
        Math.Clamp(frame + delta, 0, Math.Max(0, totalFrames - 1));

    /// <summary>"00000", "00123", "123456": invariant Western digits, never truncated.</summary>
    public static string FrameNumber(int frame) =>
        Math.Max(0, frame).ToString("D" + FrameDigits, CultureInfo.InvariantCulture);

    /// <summary>"00:00:04.100": hours, minutes, seconds and milliseconds.</summary>
    public static string Timecode(int frame, double fps) => Timecode(TimeOfFrame(frame, fps));

    /// <summary>"00:00:04.100": hours never wrap at 24, milliseconds are rounded.</summary>
    public static string Timecode(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
            seconds = 0;
        var totalMilliseconds = (long)Math.Round(seconds * 1000, MidpointRounding.AwayFromZero);
        var hours = totalMilliseconds / 3_600_000;
        var minutes = totalMilliseconds / 60_000 % 60;
        var wholeSeconds = totalMilliseconds / 1000 % 60;
        var milliseconds = totalMilliseconds % 1000;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{hours:D2}:{minutes:D2}:{wholeSeconds:D2}.{milliseconds:D3}");
    }

    /// <summary>"0:05" / "1:02:03": compact clock for lists, hours only when the clip needs them.</summary>
    public static string ShortTimecode(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
            seconds = 0;
        var total = (long)Math.Round(seconds, MidpointRounding.AwayFromZero);
        var hours = total / 3600;
        var minutes = total / 60 % 60;
        var wholeSeconds = total % 60;
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:D2}:{wholeSeconds:D2}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}:{wholeSeconds:D2}");
    }

    /// <summary>"clip frame 00123.png": the file name every captured frame is saved under.</summary>
    public static string CaptureFileName(string baseName, int frame) =>
        $"{baseName} frame {FrameNumber(frame)}.png";

    /// <summary>"clip frames": the folder next to the source that holds the captured stills.</summary>
    public static string FramesFolderName(string baseName) => $"{baseName} frames";

    /// <summary>
    /// Parses "90", "1:30" or "01:02:03.5" (and the comma decimal form) into seconds. The clock parts
    /// are base 60; only the last one may carry a fraction. Negative, empty and malformed text fails.
    /// </summary>
    public static bool TryParseTime(string? text, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Trim().Replace(',', '.').Split(':');
        if (parts.Length is 0 or > 3)
            return false;

        var total = 0.0;
        foreach (var part in parts)
        {
            var valueText = part.Trim();
            if (valueText.Length == 0)
                return false;
            if (!double.TryParse(valueText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
                return false;
            if (!double.IsFinite(value) || value < 0)
                return false;
            total = total * 60 + value;
        }

        if (!double.IsFinite(total) || total > 1_000_000_000)
            return false;
        seconds = total;
        return true;
    }
}

/// <summary>
/// The frames the user captured, kept sorted and without duplicates so the export order and the file
/// names are stable no matter in which order the clip was scrubbed.
/// </summary>
public sealed class CaptureList
{
    private readonly List<int> _frames = [];

    /// <summary>Captured frame indices, ascending.</summary>
    public IReadOnlyList<int> Frames => _frames;

    public int Count => _frames.Count;

    public bool IsEmpty => _frames.Count == 0;

    public bool Contains(int frame) => _frames.Contains(frame);

    /// <summary>Adds a frame; false for a negative index or one that is already in the list.</summary>
    public bool Add(int frame)
    {
        if (frame < 0 || _frames.Contains(frame))
            return false;
        var index = _frames.BinarySearch(frame);
        _frames.Insert(index < 0 ? ~index : index, frame);
        return true;
    }

    public bool Remove(int frame) => _frames.Remove(frame);

    public void Clear() => _frames.Clear();
}

/// <summary>How a redaction area hides what is under it.</summary>
public enum RedactionStyle
{
    /// <summary>A filled box in the chosen colour.</summary>
    Solid,

    /// <summary>A box whose pixels are blurred.</summary>
    Blur,
}

/// <summary>
/// One rectangle to hide between two timestamps. Coordinates are in the video's original pixels, so the
/// filter graph never depends on how large the preview happened to be.
/// </summary>
public sealed record RedactionArea(
    int X,
    int Y,
    int Width,
    int Height,
    double StartSeconds,
    double EndSeconds,
    RedactionStyle Style,
    string Color = "black")
{
    /// <summary>How long the area stays hidden.</summary>
    public double Duration => EndSeconds - StartSeconds;
}

/// <summary>
/// Builds the FFmpeg filter graph that hides every area only during its own time range. A solid area is a
/// single <c>drawbox</c>; a blur area splits the frame, crops the region, blurs the crop with
/// <c>boxblur</c> and overlays it back, gated by <c>enable='between(t,a,b)'</c>. Areas are applied in list
/// order, so overlapping ranges simply stack. The graph is pure text and is covered by exact tests.
/// </summary>
public static class RedactionGraph
{
    /// <summary>Areas shorter than a millisecond are treated as malformed.</summary>
    public const double MinDuration = 0.001;

    /// <summary>Radius and power of the crop-and-overlay blur.</summary>
    public const string BlurFilter = "boxblur=10:2";

    /// <summary>The output label the caller maps into the encoder.</summary>
    public const string OutputLabel = "[vout]";

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>
    /// The value for <c>-filter_complex</c>, ending in <see cref="OutputLabel"/>, or an empty string when
    /// there is nothing valid to hide. Clamping keeps a rectangle inside the frame; invalid areas
    /// (zero size, reversed or missing time range) are skipped.
    /// </summary>
    public static string Build(IReadOnlyList<RedactionArea>? areas, int frameWidth, int frameHeight)
    {
        if (areas is null || areas.Count == 0 || frameWidth <= 0 || frameHeight <= 0)
            return string.Empty;

        var valid = new List<(int X, int Y, int Width, int Height, double Start, double End, RedactionStyle Style, string Color)>();
        foreach (var area in areas)
        {
            if (area is null || area.Width <= 0 || area.Height <= 0)
                continue;
            if (!double.IsFinite(area.StartSeconds) || !double.IsFinite(area.EndSeconds))
                continue;

            var start = Math.Max(0, area.StartSeconds);
            var end = area.EndSeconds;
            if (end - start < MinDuration)
                continue;

            var x = Math.Clamp(area.X, 0, frameWidth - 1);
            var y = Math.Clamp(area.Y, 0, frameHeight - 1);
            var width = Math.Clamp(area.Width, 1, frameWidth - x);
            var height = Math.Clamp(area.Height, 1, frameHeight - y);
            valid.Add((x, y, width, height, start, end, area.Style, NormalizeColor(area.Color)));
        }

        if (valid.Count == 0)
            return string.Empty;

        var graph = new StringBuilder();
        var input = "[0:v]";
        for (var i = 0; i < valid.Count; i++)
        {
            var area = valid[i];
            var output = i == valid.Count - 1 ? OutputLabel : $"[v{i + 1}]";
            graph.Append(input);
            if (area.Style == RedactionStyle.Blur)
            {
                graph.Append($"split[b{i}][s{i}];[s{i}]crop={area.Width}:{area.Height}:{area.X}:{area.Y},{BlurFilter}[k{i}];");
                graph.Append($"[b{i}][k{i}]overlay={area.X}:{area.Y}:{Enable(area.Start, area.End)}");
            }
            else
            {
                graph.Append(
                    $"drawbox=x={area.X}:y={area.Y}:w={area.Width}:h={area.Height}:color={area.Color}:t=fill:{Enable(area.Start, area.End)}");
            }
            graph.Append(output);
            if (i < valid.Count - 1)
                graph.Append(';');
            input = output;
        }
        return graph.ToString();
    }

    /// <summary>"1.500": three invariant decimals, the precision the enable expression uses.</summary>
    public static string FormatSeconds(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
            seconds = 0;
        return seconds.ToString("0.000", Invariant);
    }

    /// <summary><c>enable='between(t,1.500,2.500)'</c>, inclusive at both ends.</summary>
    public static string Enable(double startSeconds, double endSeconds) =>
        $"enable='between(t,{FormatSeconds(startSeconds)},{FormatSeconds(endSeconds)})'";

    /// <summary>FFmpeg colour token: <c>#RRGGBB</c> becomes <c>0xRRGGBB</c>; junk falls back to black.</summary>
    private static string NormalizeColor(string? color)
    {
        var value = (color ?? string.Empty).Trim();
        if (value.Length == 0)
            return "black";
        if (value[0] == '#')
            value = "0x" + value[1..];
        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('@' or '.'))
                return "black";
        }
        return value;
    }
}
