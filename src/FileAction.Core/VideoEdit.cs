using System.Globalization;
using System.Text.RegularExpressions;

namespace FileAction.Core;

/// <summary>What FFmpeg's input banner says about a video: duration, display size, frame rate and audio.</summary>
/// <param name="Duration">Full length of the file.</param>
/// <param name="Width">Display width after applying the rotation matrix (portrait phone clips report 1080×1920).</param>
/// <param name="Height">Display height after applying the rotation matrix.</param>
/// <param name="Fps">Nominal frame rate; 30 when FFmpeg did not report a usable one.</param>
/// <param name="Rotation">Rotation reported by the display matrix, normalized to 0–359 degrees.</param>
public sealed record VideoInfo(
    TimeSpan Duration,
    int Width,
    int Height,
    double Fps,
    bool HasAudio,
    bool HasVideo,
    double Rotation);

/// <summary>How a trim is produced.</summary>
public enum VideoTrimMode
{
    /// <summary>Copy every packet: fast and lossless, but the cut snaps to the nearest keyframe.</summary>
    StreamCopy,

    /// <summary>Decode and encode again: frame accurate, slower.</summary>
    ReEncode,
}

/// <summary>A trim request in original-file coordinates; paths are already resolved.</summary>
public sealed record VideoTrimRequest(
    string InputPath,
    string OutputPath,
    TimeSpan Start,
    TimeSpan End,
    string Container,
    VideoTrimMode Mode,
    bool HasAudio,
    int Crf = 23);

/// <summary>A crop request. <see cref="Rect"/> is in original pixels; the builder rounds it to even values.</summary>
public sealed record VideoCropRequest(
    string InputPath,
    string OutputPath,
    CropRect Rect,
    int SourceWidth,
    int SourceHeight,
    string Container,
    bool HasAudio,
    bool CopyAudio = true,
    int Crf = 23);

/// <summary>
/// A speed request. Speed is clamped to <see cref="VideoEdit.MinSpeed"/>–<see cref="VideoEdit.MaxSpeed"/>;
/// <see cref="Fps"/> is the source frame rate, used to force constant output frames after setpts.
/// </summary>
public sealed record VideoSpeedRequest(
    string InputPath,
    string OutputPath,
    double Speed,
    string Container,
    bool HasAudio,
    double Fps = 30,
    int Crf = 23);

/// <summary>
/// The rules behind the three video tools, with no window, no process and no file-system access:
/// time parsing/formatting, frame stepping, even crop geometry, output container choice and the exact
/// FFmpeg argument lists for trim (stream copy or re-encode), crop and speed (atempo chain). Pure
/// functions so every case is unit tested without FFmpeg; <see cref="VideoProbe"/> reads the banner.
/// </summary>
public static class VideoEdit
{
    public const double MinSpeed = 0.25;
    public const double MaxSpeed = 4.0;
    public const int DefaultCrf = 23;

    private const double Epsilon = 0.000000001;

    // ---------------------------------------------------------------- time

    /// <summary>
    /// Parses "hh:mm:ss.mmm", "mm:ss", "90" or "1:02:03.5" (comma decimals too) into a duration.
    /// Only the left-most part may exceed 59, so "1:75" is rejected but "90:00" means 90 minutes.
    /// </summary>
    public static bool TryParseTime(string? text, out TimeSpan value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var cleaned = text.Trim().Replace(',', '.');
        var parts = cleaned.Split(':');
        if (parts.Length is < 1 or > 3)
            return false;

        var total = 0.0;
        var multiplier = 1.0;
        for (var index = parts.Length - 1; index >= 0; index--)
        {
            var part = parts[index].Trim();
            if (part.Length == 0
                || !double.TryParse(part, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
                return false;
            if (!double.IsFinite(number) || number < 0)
                return false;
            if (index != 0 && number >= 60)
                return false;
            total += number * multiplier;
            multiplier *= 60;
        }

        if (!double.IsFinite(total) || total > TimeSpan.MaxValue.TotalSeconds)
            return false;
        value = TimeSpan.FromSeconds(total);
        return true;
    }

    /// <summary>Parses like <see cref="TryParseTime"/> and throws <see cref="FormatException"/> when it cannot.</summary>
    public static TimeSpan ParseTime(string text) =>
        TryParseTime(text, out var value) ? value : throw new FormatException($"Invalid time: {text}");

    /// <summary>"hh:mm:ss.mmm", always three decimals and at least two hour digits.</summary>
    public static string FormatTime(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
            time = TimeSpan.Zero;
        var hours = (long)time.TotalHours;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{hours:00}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}");
    }

    /// <summary>Compact length for labels: "1:02:03" over an hour, "2:07" below.</summary>
    public static string FormatClock(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
            time = TimeSpan.Zero;
        return time.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(long)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{time.Minutes}:{time.Seconds:00}");
    }

    /// <summary>Keeps a time inside 0…<paramref name="duration"/>.</summary>
    public static TimeSpan Clamp(TimeSpan time, TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
            duration = TimeSpan.Zero;
        if (time < TimeSpan.Zero)
            return TimeSpan.Zero;
        return time > duration ? duration : time;
    }

    /// <summary>One frame at <paramref name="fps"/>; 30 fps is assumed when the rate is unusable.</summary>
    public static TimeSpan FrameStep(double fps)
    {
        var rate = fps is >= 1 and <= 240 ? fps : 30;
        return TimeSpan.FromSeconds(1 / rate);
    }

    /// <summary>Moves <paramref name="frames"/> frames (negative goes back) inside the file.</summary>
    public static TimeSpan StepFrames(TimeSpan time, double fps, int frames, TimeSpan duration)
    {
        var ticks = FrameStep(fps).Ticks * frames;
        return Clamp(time + TimeSpan.FromTicks(ticks), duration);
    }

    // ---------------------------------------------------------------- speed values

    public static double ClampSpeed(double speed) =>
        !double.IsFinite(speed) ? 1.0 : Math.Clamp(speed, MinSpeed, MaxSpeed);

    /// <summary>"2x", "0.5x", "1.25x": the file-name suffix and the preset labels.</summary>
    public static string SpeedSuffix(double speed) =>
        ClampSpeed(speed).ToString("0.###", CultureInfo.InvariantCulture) + "x";

    /// <summary>"2×", "0.5×": the label next to the slider.</summary>
    public static string SpeedText(double speed) =>
        ClampSpeed(speed).ToString("0.##", CultureInfo.InvariantCulture) + "×";

    /// <summary>
    /// The atempo chain for one speed. One filter handles 0.5×–2×, so larger changes are split and
    /// every factor stays inside that range; the product is the requested speed and pitch is kept.
    /// </summary>
    public static IReadOnlyList<double> AtempoFactors(double speed)
    {
        if (!double.IsFinite(speed) || speed <= 0)
            throw new ArgumentOutOfRangeException(nameof(speed));

        var remaining = speed;
        var factors = new List<double>();
        while (remaining > 2.0 + Epsilon)
        {
            factors.Add(2.0);
            remaining /= 2.0;
        }
        while (remaining < 0.5 - Epsilon)
        {
            factors.Add(0.5);
            remaining /= 0.5;
        }
        factors.Add(Math.Round(remaining, 6));
        return factors;
    }

    // ---------------------------------------------------------------- containers and geometry

    /// <summary>Containers our encoder arguments target; legacy containers become MP4.</summary>
    public static string OutputContainerFor(string? format)
    {
        var key = (format ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant();
        return key switch
        {
            "avi" or "wmv" or "flv" or "mpg" or "mpeg" or "3gp" or "ts" or "mts" or "m4v" => "mp4",
            _ => key,
        };
    }

    /// <summary>True when a source container can be trimmed by copying packets instead of encoding again.</summary>
    public static bool SupportsStreamCopy(string? sourceFormat)
    {
        var key = (sourceFormat ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant();
        return key is "mp4" or "mov" or "mkv" or "webm";
    }

    /// <summary>Largest even number not above <paramref name="value"/>, never below 2.</summary>
    public static int Even(int value) => value >= 2 ? value & ~1 : 2;

    /// <summary>Even offset (0, 2, 4…) for crop x/y.</summary>
    public static int EvenOffset(int value) => Math.Max(0, value & ~1);

    /// <summary>
    /// Rounds a rectangle to whole even pixels, keeps it inside the frame and leaves at least 2×2.
    /// Even width and height are required by yuv420p encoders.
    /// </summary>
    public static CropRect EvenCrop(CropRect rect, int sourceWidth, int sourceHeight)
    {
        var maxWidth = Even(sourceWidth);
        var maxHeight = Even(sourceHeight);
        var width = Math.Clamp(Even((int)Math.Round(rect.Width)), 2, maxWidth);
        var height = Math.Clamp(Even((int)Math.Round(rect.Height)), 2, maxHeight);
        var x = Math.Clamp(EvenOffset((int)Math.Round(rect.X)), 0, maxWidth - width);
        var y = Math.Clamp(EvenOffset((int)Math.Round(rect.Y)), 0, maxHeight - height);
        return new CropRect(x, y, width, height);
    }

    // ---------------------------------------------------------------- FFmpeg arguments

    /// <summary>Trim arguments: a fast stream copy or a frame-accurate re-encode.</summary>
    public static IReadOnlyList<string> BuildTrimArgs(VideoTrimRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var start = request.Start < TimeSpan.Zero ? TimeSpan.Zero : request.Start;
        if (request.End <= start)
            throw new ArgumentException("The trim end must come after its start.", nameof(request));

        var container = OutputContainerFor(request.Container);
        var args = new List<string>
        {
            "-hide_banner", "-nostdin", "-y",
            "-ss", FormatTime(start),
            "-i", request.InputPath,
            "-t", FormatTime(request.End - start),
            "-map", "0:v:0",
        };

        if (request.Mode == VideoTrimMode.StreamCopy)
        {
            if (request.HasAudio)
                args.AddRange(["-map", "0:a?"]);
            // No -avoid_negative_ts: it makes the cut include the pre-roll before the requested start
            // and the output then lasts start+duration instead of duration. MP4's edit list trims the
            // pre-keyframe frames while the file still starts at the requested time.
            args.AddRange(["-c", "copy"]);
        }
        else
        {
            if (request.HasAudio)
                args.AddRange(["-map", "0:a:0"]);
            AddVideoEncoder(args, container, request.Crf);
            if (request.HasAudio)
                AddAudioEncoder(args, container);
            else
                args.Add("-an");
        }

        AddFastStart(args, container);
        args.Add(request.OutputPath);
        return args;
    }

    /// <summary>Crop arguments: the rectangle is rounded to even pixels and audio is copied by default.</summary>
    public static IReadOnlyList<string> BuildCropArgs(VideoCropRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SourceWidth < 2 || request.SourceHeight < 2)
            throw new ArgumentOutOfRangeException(nameof(request), "The source size must be positive.");

        var rect = EvenCrop(request.Rect, request.SourceWidth, request.SourceHeight);
        var container = OutputContainerFor(request.Container);
        var width = (int)rect.Width;
        var height = (int)rect.Height;
        var x = (int)rect.X;
        var y = (int)rect.Y;

        var args = new List<string>
        {
            "-hide_banner", "-nostdin", "-y",
            "-i", request.InputPath,
            "-vf", string.Create(CultureInfo.InvariantCulture, $"crop={width}:{height}:{x}:{y}"),
            "-map", "0:v:0",
        };
        if (request.HasAudio)
            args.AddRange(["-map", "0:a:0"]);

        AddVideoEncoder(args, container, request.Crf);
        if (!request.HasAudio)
            args.Add("-an");
        else if (request.CopyAudio)
            args.AddRange(["-c:a", "copy"]);
        else
            AddAudioEncoder(args, container);

        AddFastStart(args, container);
        args.Add(request.OutputPath);
        return args;
    }

    /// <summary>Speed arguments: setpts for the picture and an atempo chain for the audio (none when silent).</summary>
    public static IReadOnlyList<string> BuildSpeedArgs(VideoSpeedRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var speed = ClampSpeed(request.Speed);
        var container = OutputContainerFor(request.Container);
        var fps = double.IsFinite(request.Fps) && request.Fps is >= 1 and <= 240 ? request.Fps : 30;
        var factor = (1 / speed).ToString("0.######", CultureInfo.InvariantCulture);
        // fps forces constant-rate output after setpts: without it x264 keeps 62 frames/2.066 s for a
        // 4 s 30 fps input instead of the expected 60 frames/2 s.
        var filter = string.Create(
            CultureInfo.InvariantCulture,
            $"setpts=(PTS-STARTPTS)*{factor},fps={fps:0.###}");

        var args = new List<string>
        {
            "-hide_banner", "-nostdin", "-y",
            "-i", request.InputPath,
            "-vf", filter,
            "-map", "0:v:0",
        };

        if (request.HasAudio)
        {
            var atempo = string.Join(",", AtempoFactors(speed)
                .Select(part => "atempo=" + part.ToString("0.######", CultureInfo.InvariantCulture)));
            args.AddRange(["-af", atempo, "-map", "0:a:0"]);
        }
        else
        {
            args.Add("-an");
        }

        AddVideoEncoder(args, container, request.Crf);
        if (request.HasAudio)
            AddAudioEncoder(args, container);
        AddFastStart(args, container);
        args.Add(request.OutputPath);
        return args;
    }

    /// <summary>
    /// Arguments for a filmstrip: one FFmpeg call samples <paramref name="count"/> frames evenly across
    /// the file and writes small JPEGs to <paramref name="outputPattern"/> ("…/strip%02d.jpg").
    /// </summary>
    public static IReadOnlyList<string> BuildStripArgs(
        string inputPath,
        string outputPattern,
        TimeSpan duration,
        int count,
        int width)
    {
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count));
        var totalSeconds = Math.Max(0.1, duration.TotalSeconds);
        var rate = count / totalSeconds;
        var filter = string.Create(
            CultureInfo.InvariantCulture,
            $"fps={rate:0.####},scale={Math.Max(16, width)}:-2");
        return
        [
            "-hide_banner", "-nostdin", "-y",
            "-i", inputPath,
            "-vf", filter,
            "-frames:v", count.ToString(CultureInfo.InvariantCulture),
            "-q:v", "3",
            outputPattern,
        ];
    }

    private static void AddVideoEncoder(List<string> args, string container, int crf)
    {
        if (container == "webm")
        {
            args.AddRange(["-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0",
                "-row-mt", "1", "-cpu-used", "4", "-pix_fmt", "yuv420p"]);
            return;
        }

        var quality = Math.Clamp(crf, 0, 51).ToString(CultureInfo.InvariantCulture);
        args.AddRange(["-c:v", "libx264", "-preset", "medium", "-crf", quality, "-pix_fmt", "yuv420p"]);
    }

    private static void AddAudioEncoder(List<string> args, string container)
    {
        if (container == "webm")
            args.AddRange(["-c:a", "libopus", "-b:a", "128k"]);
        else
            args.AddRange(["-c:a", "aac", "-b:a", "160k"]);
    }

    private static void AddFastStart(List<string> args, string container)
    {
        if (container is "mp4" or "mov")
            args.AddRange(["-movflags", "+faststart"]);
    }
}

/// <summary>
/// Reads the FFmpeg input banner into a <see cref="VideoInfo"/>. The parsing is pure and unit tested with
/// captured banner lines; <see cref="ReadAsync"/> only runs <c>ffmpeg -i file</c> (which exits with code 1
/// and is expected to) and never decodes the video.
/// </summary>
public static class VideoProbe
{
    // "Duration: 00:01:02.50, start: …". Hours may exceed two digits.
    private static readonly Regex DurationPattern = new(
        @"Duration:\s*(\d+):(\d{2}):(\d{2}(?:\.\d+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // "1920x1080"; the guards keep codec tags such as "0x31637661" out.
    private static readonly Regex DimensionsPattern = new(
        @"(?<![0-9A-Fa-fx])(\d{2,5})x(\d{2,5})(?![0-9A-Fa-f])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // "30 fps", "29.97 fps".
    private static readonly Regex FrameRatePattern = new(
        @"(?<!\d)(\d{1,3}(?:\.\d+)?)\s*fps(?![A-Za-z])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // "displaymatrix: rotation of -90.00 degrees".
    private static readonly Regex RotationPattern = new(
        @"rotation of\s+(-?\d+(?:\.\d+)?)\s+degrees",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Older MOV/MP4 files carry "rotate : 90" in the metadata block instead of a display matrix.
    private static readonly Regex RotateTagPattern = new(
        @"^\s*rotate\s*:\s*(-?\d+(?:\.\d+)?)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Parses the captured stderr lines; null when this is not a readable video with a duration.</summary>
    public static VideoInfo? Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var duration = TimeSpan.Zero;
        var width = 0;
        var height = 0;
        var fps = 0.0;
        var rotation = 0.0;
        var hasVideo = false;
        var hasAudio = false;

        foreach (var line in lines)
        {
            if (string.IsNullOrEmpty(line))
                continue;

            if (duration <= TimeSpan.Zero && DurationPattern.Match(line) is { Success: true } clock)
            {
                var hours = long.Parse(clock.Groups[1].Value, CultureInfo.InvariantCulture);
                var minutes = int.Parse(clock.Groups[2].Value, CultureInfo.InvariantCulture);
                var seconds = double.Parse(clock.Groups[3].Value, CultureInfo.InvariantCulture);
                duration = TimeSpan.FromSeconds(hours * 3600 + minutes * 60 + seconds);
            }

            var isStream = line.Contains("Stream #", StringComparison.Ordinal);
            if (isStream && line.Contains("Video:", StringComparison.Ordinal))
            {
                hasVideo = true;
                if (width == 0 && DimensionsPattern.Match(line) is { Success: true } size)
                {
                    width = int.Parse(size.Groups[1].Value, CultureInfo.InvariantCulture);
                    height = int.Parse(size.Groups[2].Value, CultureInfo.InvariantCulture);
                }
                if (fps <= 0 && FrameRatePattern.Match(line) is { Success: true } rate)
                    fps = double.Parse(rate.Groups[1].Value, CultureInfo.InvariantCulture);
            }
            else if (isStream && line.Contains("Audio:", StringComparison.Ordinal))
            {
                hasAudio = true;
            }

            if (RotationPattern.Match(line) is { Success: true } rotationMatch)
                rotation = double.Parse(rotationMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            else if (RotateTagPattern.Match(line) is { Success: true } rotateTag)
                rotation = double.Parse(rotateTag.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        if (!hasVideo || width < 2 || height < 2 || duration <= TimeSpan.Zero)
            return null;

        var normalized = ((rotation % 360) + 360) % 360;
        if (Math.Abs(normalized - 90) < 45 || Math.Abs(normalized - 270) < 45)
            (width, height) = (height, width);

        if (!double.IsFinite(fps) || fps is < 1 or > 240)
            fps = 30;

        return new VideoInfo(duration, width, height, fps, hasAudio, hasVideo, normalized);
    }

    /// <summary>Runs <c>ffmpeg -i</c> and parses its banner; null when the file cannot be read.</summary>
    public static async Task<VideoInfo?> ReadAsync(string ffmpeg, string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(ffmpeg);
        ArgumentException.ThrowIfNullOrEmpty(path);

        var lines = new List<string>();
        await ProcessRunner.RunAsync(
            ffmpeg,
            ["-hide_banner", "-nostdin", "-i", path],
            cancellationToken,
            line =>
            {
                lock (lines)
                    lines.Add(line);
            },
            throwOnError: false).ConfigureAwait(false);

        string[] captured;
        lock (lines)
            captured = [.. lines];
        return Parse(captured);
    }
}
