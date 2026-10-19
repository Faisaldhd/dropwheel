using System.Globalization;
using System.Text.RegularExpressions;
using FileAction.Core.Backends;
using static FileAction.Core.Loc;

namespace FileAction.Core;

/// <summary>
/// One part of a split: the half-open range [<see cref="Start"/>, <see cref="End"/>) in seconds on the
/// source timeline. Parts are produced by <see cref="VideoSplitJoin.Ranges"/> /
/// <see cref="VideoSplitJoin.EqualParts"/>, so a part can never start before the previous one ends.
/// </summary>
public readonly record struct SplitSegment(double Start, double End)
{
    /// <summary>Length of the part in seconds; never negative.</summary>
    public double Duration => Math.Max(0, End - Start);
}

/// <summary>
/// Everything a split needs before ffmpeg runs: the new folder's name, the output container and the
/// part ranges. Building a plan touches nothing, so the names can be shown in the window before the
/// user presses Apply, and every rule below is unit tested without ffmpeg.
/// </summary>
public sealed record SplitPlan(
    string SourcePath,
    string BaseName,
    string FolderName,
    string Extension,
    bool CanCopy,
    IReadOnlyList<SplitSegment> Segments)
{
    public int Count => Segments.Count;

    /// <summary><c>&lt;name&gt; 01.ext</c> for <paramref name="number"/> (1-based).</summary>
    public string FileName(int number) => $"{VideoSplitJoin.SegmentName(BaseName, number, Count)}.{Extension}";

    public IReadOnlyList<string> FileNames =>
        [.. Enumerable.Range(1, Count).Select(FileName)];
}

/// <summary>
/// What the join filter needs to know about one clip. <see cref="IsUsable"/> is false when the probe
/// found no video or no length; the window then explains which clip cannot be read.
/// </summary>
public sealed record ClipInfo(string Path, double Duration, int Width, int Height, double Fps, bool HasAudio)
{
    public bool IsUsable => Duration > 0 && Width > 0 && Height > 0;
}

/// <summary>The concat filter graph plus the labels the encoder maps to the output file.</summary>
public sealed record JoinFilter(string Graph, string VideoLabel, string AudioLabel);

/// <summary>
/// The pure half of the Split and Join tools (2.4.0): split-point math, the equal-parts rule, output
/// names, the ffmpeg argument builders and the concat filter that gives every clip the first clip's
/// frame size. No ffmpeg process, no files: the runners in the app only execute what these builders
/// describe, so the exact commands are covered by ordinary unit tests.
///
/// Segments are written with input-side <c>-ss</c>/<c>-t</c> (the trim happens while the source is
/// read, so the output starts at zero) and <c>-c copy</c> when the source container accepts a stream
/// copy; the caller falls back to <see cref="SplitArgs"/> with <c>copy: false</c> when that fails.
/// </summary>
public static partial class VideoSplitJoin
{
    /// <summary>Shortest part worth writing; points closer than this to an edge or each other are dropped.</summary>
    public const double MinSegment = 0.05;

    /// <summary>Fewest equal parts the window offers.</summary>
    public const int MinParts = 2;

    /// <summary>Most equal parts the window offers (parts are named with at least two digits).</summary>
    public const int MaxParts = 99;

    /// <summary>Sample rate every clip's audio is converted to before the concat filter.</summary>
    public const int AudioRate = 48000;

    /// <summary>Output name for a join, next to the first clip.</summary>
    public const string JoinedName = "Joined";

    /// <summary>Container of a joined file; also the only re-encode target of the join.</summary>
    public const string JoinedExtension = "mp4";

    /// <summary>
    /// How far a stream-copied part may be from its requested length before it is re-encoded. A copy
    /// can only start at a keyframe, so an MKV/WebM part may begin earlier than asked; its measured
    /// length then exposes that and the runner re-encodes the part instead of keeping extra content.
    /// </summary>
    public const double CopyLengthTolerance = 0.4;

    /// <summary>True when a copied part is close enough to the requested length to keep it.</summary>
    public static bool CopyLengthMatches(double expected, double actual) =>
        expected > 0 && actual > 0 && Math.Abs(actual - expected) <= CopyLengthTolerance;

    [GeneratedRegex(@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)")]
    private static partial Regex DurationPattern();

    [GeneratedRegex(@"(\d{2,5})x(\d{2,5})(?![0-9])")]
    private static partial Regex SizePattern();

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*fps\b")]
    private static partial Regex FpsPattern();

    // ---------------------------------------------------------------- split points

    /// <summary>Seconds rounded to the millisecond, the resolution the timeline and the arguments use.</summary>
    public static double RoundMs(double seconds) => Math.Round(seconds, 3, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The split points that actually create a part: clamped into the clip, rounded to the millisecond,
    /// sorted, and with points that would make a part shorter than <see cref="MinSegment"/> dropped.
    /// A point on either edge is not a split, so it is dropped too.
    /// </summary>
    public static IReadOnlyList<double> CleanPoints(double duration, IEnumerable<double> points)
    {
        if (!(duration > 0))
            throw new ConversionException(T("tool.videosplit.error.probe"));
        ArgumentNullException.ThrowIfNull(points);

        var limit = RoundMs(duration);
        var clean = new List<double>();
        // The timeline can hand the points over in any order; the parts are always built low to high.
        foreach (var point in points
                     .Where(double.IsFinite)
                     .Select(raw => RoundMs(Math.Clamp(raw, 0, limit)))
                     .OrderBy(point => point))
        {
            if (point <= MinSegment || point > limit - MinSegment)
                continue;
            // Points closer than MinSegment to the previous one would produce an unusable part.
            if (clean.Count > 0 && point - clean[^1] < MinSegment)
                continue;
            clean.Add(point);
        }
        return clean;
    }

    /// <summary>The parts a set of split points creates, covering the whole clip from 0 to its length.</summary>
    public static IReadOnlyList<SplitSegment> Ranges(double duration, IEnumerable<double> points)
    {
        var clean = CleanPoints(duration, points);
        var limit = RoundMs(duration);
        var segments = new List<SplitSegment>(clean.Count + 1);
        var start = 0d;
        foreach (var point in clean)
        {
            segments.Add(new SplitSegment(start, point));
            start = point;
        }
        segments.Add(new SplitSegment(start, limit));
        return segments;
    }

    /// <summary>
    /// The interior points of <paramref name="parts"/> equal parts. The last part takes the remainder,
    /// so the parts always add up to exactly the clip length.
    /// </summary>
    public static IReadOnlyList<double> PointsForEqualParts(double duration, int parts)
    {
        if (!(duration > 0))
            throw new ConversionException(T("tool.videosplit.error.probe"));

        var count = Math.Clamp(parts, 1, MaxParts);
        var limit = RoundMs(duration);
        var points = new List<double>(Math.Max(0, count - 1));
        for (var i = 1; i < count; i++)
        {
            var point = RoundMs(limit * i / count);
            if (point <= MinSegment || point > limit - MinSegment)
                continue;
            if (points.Count > 0 && point - points[^1] < MinSegment)
                continue;
            points.Add(point);
        }
        return points;
    }

    /// <summary><paramref name="parts"/> equal parts; a very short clip yields fewer usable parts.</summary>
    public static IReadOnlyList<SplitSegment> EqualParts(double duration, int parts) =>
        Ranges(duration, PointsForEqualParts(duration, parts));

    // ---------------------------------------------------------------- names and plan

    /// <summary>
    /// The container a stream copy is safe into. Everything else (legacy AVI/WMV/MPG, 3GP, TS) is
    /// re-encoded to MP4 instead, because copying its codecs into MP4 is rejected by the muxer.
    /// </summary>
    public static string? CopyContainer(string format) => format switch
    {
        "mp4" or "mov" or "mkv" or "webm" => format,
        _ => null,
    };

    /// <summary>The container a part is written with: the copy-safe source container, or MP4.</summary>
    public static string ContainerFor(string format) => CopyContainer(format) ?? JoinedExtension;

    /// <summary><c>&lt;name&gt; 01</c>: at least two digits, and enough digits for the highest part.</summary>
    public static string SegmentName(string baseName, int number, int total)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);
        var digits = Math.Max(2, Math.Max(1, total).ToString(CultureInfo.InvariantCulture).Length);
        var text = Math.Max(0, number).ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
        return $"{baseName} {text}";
    }

    /// <summary><c>&lt;name&gt; Parts</c>; the suffix is localized by the caller.</summary>
    public static string FolderName(string baseName, string suffix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);
        var trimmed = suffix?.Trim() ?? "";
        return trimmed.Length == 0 ? baseName : $"{baseName} {trimmed}";
    }

    /// <summary>The plan for a split at the given points (the ones the timeline shows).</summary>
    public static SplitPlan Plan(string sourcePath, string folderSuffix, double duration, IEnumerable<double> points)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var baseName = Formats.BaseNameOf(sourcePath);
        var format = Formats.KeyOf(sourcePath);
        return new SplitPlan(
            sourcePath,
            baseName,
            FolderName(baseName, folderSuffix),
            ContainerFor(format),
            CopyContainer(format) is not null,
            Ranges(duration, points));
    }

    /// <summary>The plan for "N equal parts".</summary>
    public static SplitPlan PlanEqualParts(string sourcePath, string folderSuffix, double duration, int parts) =>
        Plan(sourcePath, folderSuffix, duration, PointsForEqualParts(duration, parts));

    // ---------------------------------------------------------------- split arguments

    /// <summary>
    /// Complete ffmpeg arguments for one part: the segment is expressed as an input-side start and
    /// duration (<c>-ss &lt;start&gt; -t &lt;length&gt;</c> before <c>-i</c>), which is what makes both
    /// the copy and the re-encode write exactly the requested range. <c>-to</c> cannot be used here:
    /// ffmpeg treats it as an output stop even when it appears before <c>-i</c>, so each part would be
    /// as long as its end time (measured with ffmpeg 9: 2 s–5 s of a 10 s file became 5 s).
    /// A stream copy is attempted first when the plan says the container allows it; the caller builds
    /// the same command with <paramref name="copy"/> = false when the copy fails.
    /// </summary>
    public static IReadOnlyList<string> SplitArgs(
        string input, SplitSegment segment, string output, bool copy, string container, Settings? settings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(output);

        var args = new List<string>
        {
            "-hide_banner", "-nostdin", "-y",
            "-ss", Seconds(segment.Start),
            "-t", Seconds(segment.Duration),
            "-i", input,
            // Optional maps: a clip without audio still splits, and subtitles/data never break the part.
            "-map", "0:v:0?",
            "-map", "0:a?",
        };
        if (copy)
        {
            // No -avoid_negative_ts here: measured with ffmpeg 9 it stops the muxer trimming the
            // leading keyframe, so a 2 s–5 s part became 5 s. MP4/MOV keep the requested length
            // through their edit list; the runner verifies MKV/WebM and re-encodes when the copy
            // came out longer (see CopyLengthMatches).
            args.AddRange(["-c", "copy"]);
        }
        else
        {
            args.AddRange(FfmpegBackend.CodecArgs(ContainerFor(container), settings ?? new Settings()));
        }
        args.Add(output);
        return args;
    }

    // ---------------------------------------------------------------- join filter

    /// <summary>
    /// The concat graph for <paramref name="clips"/>: the first clip's size and frame rate are the
    /// output's, every other clip is scaled down (or up) inside that frame and padded with black, and
    /// a clip without audio gets silence of exactly its own length so the streams stay in step.
    /// </summary>
    public static JoinFilter BuildJoinFilter(IReadOnlyList<ClipInfo> clips)
    {
        ArgumentNullException.ThrowIfNull(clips);
        if (clips.Count < 2)
            throw new ConversionException(T("tool.videojoin.noClips"));

        var first = clips[0];
        if (!first.IsUsable)
            throw new ConversionException(T("tool.videojoin.error.probe"));

        var width = Even(first.Width);
        var height = Even(first.Height);
        var fps = first.Fps > 0 ? first.Fps.ToString("0.###", CultureInfo.InvariantCulture) : "30";

        var chains = new List<string>(clips.Count * 2 + 1);
        for (var i = 0; i < clips.Count; i++)
        {
            var clip = clips[i];
            if (!(clip.Duration > 0))
                throw new ConversionException(T("tool.videojoin.error.probe"));

            chains.Add(
                $"[{i}:v]scale={width}:{height}:force_original_aspect_ratio=decrease,"
                + $"pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps={fps},format=yuv420p[v{i}]");

            var duration = Seconds(clip.Duration);
            chains.Add(clip.HasAudio
                // apad + atrim: the audio is exactly as long as the video, whether it was shorter or longer.
                ? $"[{i}:a]aresample={AudioRate},aformat=sample_fmts=fltp:channel_layouts=stereo,"
                  + $"apad,atrim=duration={duration},asetpts=N/SR/TB[a{i}]"
                : $"anullsrc=channel_layout=stereo:sample_rate={AudioRate},"
                  + $"atrim=duration={duration},asetpts=N/SR/TB[a{i}]");
        }

        var inputs = string.Concat(Enumerable.Range(0, clips.Count).Select(i => $"[v{i}][a{i}]"));
        chains.Add($"{inputs}concat=n={clips.Count}:v=1:a=1[outv][outa]");
        return new JoinFilter(string.Join(";", chains), "[outv]", "[outa]");
    }

    /// <summary>Complete ffmpeg arguments for a join: every clip as an input, the concat graph, H.264/AAC.</summary>
    public static IReadOnlyList<string> JoinArgs(
        IReadOnlyList<ClipInfo> clips, string output, Settings? settings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        settings ??= new Settings();
        var filter = BuildJoinFilter(clips);

        var args = new List<string> { "-hide_banner", "-nostdin", "-y" };
        foreach (var clip in clips)
        {
            args.Add("-i");
            args.Add(clip.Path);
        }
        args.AddRange(
        [
            "-filter_complex", filter.Graph,
            "-map", filter.VideoLabel,
            "-map", filter.AudioLabel,
            "-c:v", "libx264",
            "-preset", "medium",
            "-crf", settings.VideoCrf.ToString(CultureInfo.InvariantCulture),
            "-pix_fmt", "yuv420p",
            "-c:a", "aac",
            "-b:a", "160k",
            "-movflags", "+faststart",
            output,
        ]);
        return args;
    }

    /// <summary>H.264 needs even dimensions; the first clip's odd size is rounded down by one pixel.</summary>
    public static int Even(int value) => Math.Max(2, value - (value % 2));

    // ---------------------------------------------------------------- order model

    /// <summary>A copy of <paramref name="items"/> with the item at <paramref name="from"/> moved to <paramref name="to"/>.</summary>
    public static IReadOnlyList<T> Move<T>(IReadOnlyList<T> items, int from, int to)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0 || from < 0 || from >= items.Count)
            return [.. items];
        var target = Math.Clamp(to, 0, items.Count - 1);
        if (target == from)
            return [.. items];

        var copy = items.ToList();
        var moved = copy[from];
        copy.RemoveAt(from);
        copy.Insert(target, moved);
        return copy;
    }

    /// <summary>A copy of <paramref name="items"/> without the item at <paramref name="index"/>.</summary>
    public static IReadOnlyList<T> RemoveAt<T>(IReadOnlyList<T> items, int index)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0 || index < 0 || index >= items.Count)
            return [.. items];
        return [.. items.Where((_, i) => i != index)];
    }

    // ---------------------------------------------------------------- probe parsing

    /// <summary>
    /// Reads the facts the join filter needs out of what <c>ffmpeg -i &lt;file&gt;</c> prints: length,
    /// video size, frame rate and whether there is an audio stream. Pure, so the banner of an unusual
    /// file can be replayed in a test without ffmpeg.
    /// </summary>
    public static ClipInfo ParseProbe(string path, IEnumerable<string> bannerLines)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(bannerLines);

        double duration = 0;
        var width = 0;
        var height = 0;
        double fps = 0;
        var hasAudio = false;

        foreach (var line in bannerLines)
        {
            if (line is null)
                continue;

            if (duration <= 0 && DurationPattern().Match(line) is { Success: true } time)
            {
                duration =
                    int.Parse(time.Groups[1].Value, CultureInfo.InvariantCulture) * 3600
                    + int.Parse(time.Groups[2].Value, CultureInfo.InvariantCulture) * 60
                    + double.Parse(time.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            if (line.Contains("Video:", StringComparison.Ordinal))
            {
                if (width == 0 && SizePattern().Match(line) is { Success: true } size)
                {
                    width = int.Parse(size.Groups[1].Value, CultureInfo.InvariantCulture);
                    height = int.Parse(size.Groups[2].Value, CultureInfo.InvariantCulture);
                }
                if (fps <= 0 && FpsPattern().Match(line) is { Success: true } rate)
                    fps = double.Parse(rate.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            if (!hasAudio && line.Contains("Audio:", StringComparison.Ordinal))
                hasAudio = true;
        }

        return new ClipInfo(path, RoundMs(duration), width, height, fps, hasAudio);
    }

    private static string Seconds(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
