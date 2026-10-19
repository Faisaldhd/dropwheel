using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using FileAction.Core;
using FileAction.Core.Backends;

namespace FileAction.App.UI.Tools.Video;

/// <summary>Shared helpers for the Split and Join windows: probing, frame thumbnails and time labels.</summary>
internal static partial class VideoToolCommon
{
    [GeneratedRegex(@"\btime=\s*(\d+):(\d+):(\d+(?:\.\d+)?)")]
    private static partial Regex TimePattern();

    /// <summary>"0:07.5" under an hour and "1:02:03" above it; Western digits in both languages.</summary>
    public static string Time(double seconds)
    {
        var value = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : value.ToString(@"m\:ss\.f", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reads length, video size, frame rate and audio presence from what <c>ffmpeg -i</c> prints.
    /// Uses the same parser the unit tests cover; a file ffmpeg cannot read comes back unusable.
    /// </summary>
    public static async Task<ClipInfo> ProbeAsync(string ffmpeg, string path, CancellationToken ct)
    {
        var lines = new List<string>();
        await ProcessRunner.RunAsync(
            ffmpeg,
            ["-hide_banner", "-nostdin", "-i", path],
            ct,
            line =>
            {
                lock (lines)
                    lines.Add(line);
            },
            // "-i file" without an output exits with 1 by design; only its banner is wanted.
            throwOnError: false).ConfigureAwait(false);

        string[] snapshot;
        lock (lines)
            snapshot = [.. lines];
        return VideoSplitJoin.ParseProbe(path, snapshot);
    }

    /// <summary>The seconds an ffmpeg progress line reports ("time=00:00:12.34"), or null.</summary>
    public static double? ProgressSeconds(string line)
    {
        if (TimePattern().Match(line) is not { Success: true } match)
            return null;
        return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 3600
            + int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * 60
            + double.Parse(match.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>One frame of a clip as a frozen bitmap, or null when ffmpeg could not decode it.</summary>
    public static async Task<BitmapSource?> FrameAsync(
        string ffmpeg, string path, double seconds, int width, CancellationToken ct)
    {
        var folder = OutputPaths.TempDirectory();
        try
        {
            var file = Path.Combine(folder, "frame.jpg");
            if (!await FfmpegBackend.ExtractFrameAsync(ffmpeg, path, seconds, width, file, ct).ConfigureAwait(false))
                return null;
            return Decode(file);
        }
        catch (ConversionException)
        {
            return null;
        }
        finally
        {
            OutputPaths.TryDelete(folder);
        }
    }

    private static BitmapSource? Decode(string file)
    {
        try
        {
            using var stream = File.OpenRead(file);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            // A frame that cannot be decoded is simply not shown; it is never a failure of the tool.
            return null;
        }
    }
}
