using FileAction.Core;

namespace FileAction.App.UI.Tools.Audio;

/// <summary>
/// The immutable description of a bleeped preview: the normalized ranges, the tone and a key that lets the
/// bleep window reuse its temporary file until one of them changes. Building a plan is pure, so the cache
/// rules and the ffmpeg arguments are tested without starting a process.
/// </summary>
/// <param name="Ranges">Ranges after <see cref="AudioEffects.NormalizeRanges"/>: sorted, merged and clamped.</param>
/// <param name="ToneHz">Tone frequency the preview was rendered with.</param>
/// <param name="VolumePercent">Tone level the preview was rendered with.</param>
/// <param name="Key">Changes whenever the audible result changes.</param>
internal readonly record struct BleepPreviewPlan(
    IReadOnlyList<AudioRange> Ranges,
    double ToneHz,
    int VolumePercent,
    string Key)
{
    /// <summary>Small compressed copy, so an hour-long recording does not leave a huge PCM file behind.</summary>
    public const string Extension = "m4a";

    /// <summary>
    /// Normalizes the live layer model and returns a playable plan. Null when there is nothing to hear:
    /// the ranges are empty, only clicks remain, or the audio has not loaded yet.
    /// </summary>
    public static BleepPreviewPlan? Create(
        IReadOnlyList<AudioRange> ranges,
        double duration,
        double toneHz,
        int volumePercent)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        var normalized = AudioEffects.NormalizeRanges(ranges, duration);
        if (normalized.Count == 0)
            return null;

        var tone = Math.Clamp(toneHz, 20, 20000);
        var volume = Math.Clamp(volumePercent, 0, 100);
        var key = string.Join(';', normalized.Select(range =>
                $"{AudioEffects.Number(range.Start)}-{AudioEffects.Number(range.End)}"))
            + $"|{AudioEffects.Number(tone)}|{volume}";
        return new BleepPreviewPlan(normalized, tone, volume, key);
    }
}

/// <summary>
/// The ffmpeg arguments for the temporary bleeped preview. It is the same filtergraph the save uses, encoded
/// as a small AAC/M4A file that WPF's MediaPlayer can play back for the original/bleeped comparison.
/// </summary>
internal static class AudioBleepPreview
{
    public static IReadOnlyList<string> Arguments(BleepPreviewPlan plan) =>
    [
        .. AudioEffects.BleepArguments(plan.Ranges, plan.ToneHz, plan.VolumePercent),
        "-vn",
        "-c:a", "aac",
        "-b:a", "128k",
        "-movflags", "+faststart",
    ];
}
