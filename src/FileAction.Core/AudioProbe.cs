using System.Globalization;
using System.Text.RegularExpressions;

namespace FileAction.Core;

/// <summary>
/// The first audio stream FFmpeg reports for a file: its sample rate and channel count. The audio tools
/// use these to keep a mono source unattenuated and to write a normalized file at the source rate instead
/// of loudnorm's internal 192 kHz.
/// </summary>
public readonly record struct AudioProbeInfo(int SampleRate, int Channels)
{
    /// <summary>True for a single-channel source, which the pan filters have to duplicate by hand.</summary>
    public bool IsMono => Channels == 1;
}

/// <summary>
/// Reads the stream banner FFmpeg prints for <c>-i &lt;file&gt;</c> (the same banner a user sees). It is a
/// pure parser, so the channel/sample-rate rules are tested without starting FFmpeg.
/// </summary>
public static partial class AudioProbe
{
    [GeneratedRegex(@"Audio:\s*[^,\r\n]+,\s*(?<rate>\d+)\s*Hz,\s*(?<layout>[^,\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex AudioStreamRegex();

    [GeneratedRegex(@"^(?<count>\d+)\s+channels?$", RegexOptions.IgnoreCase)]
    private static partial Regex ChannelCountRegex();

    /// <summary>
    /// The first audio stream's rate and layout, or null when the log has no audio stream. A layout that
    /// cannot be mapped still yields its sample rate, with two channels assumed (the common case).
    /// </summary>
    public static AudioProbeInfo? Parse(string? ffmpegLog)
    {
        if (string.IsNullOrWhiteSpace(ffmpegLog))
            return null;

        var match = AudioStreamRegex().Match(ffmpegLog);
        if (!match.Success
            || !int.TryParse(match.Groups["rate"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rate)
            || rate <= 0)
        {
            return null;
        }

        return new AudioProbeInfo(rate, ChannelCount(match.Groups["layout"].Value));
    }

    /// <summary>The channel count for an FFmpeg channel-layout name; unknown layouts use two channels.</summary>
    public static int ChannelCount(string layout)
    {
        var name = layout.Trim();
        var count = ChannelCountRegex().Match(name);
        if (count.Success
            && int.TryParse(count.Groups["count"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && parsed > 0)
        {
            return parsed;
        }

        return name.ToLowerInvariant() switch
        {
            "mono" => 1,
            "stereo" or "downmix" => 2,
            "2.1" or "3.0" => 3,
            "4.0" or "quad" => 4,
            "5.0" => 5,
            "5.1" or "5.1(side)" => 6,
            "6.1" => 7,
            "7.0" => 7,
            "7.1" or "7.1(wide)" => 8,
            _ => 2,
        };
    }
}
