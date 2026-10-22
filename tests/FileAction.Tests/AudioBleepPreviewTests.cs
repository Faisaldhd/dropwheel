using FileAction.App.UI.Tools.Audio;
using FileAction.Core;
using Xunit;

namespace FileAction.Tests;

/// <summary>
/// The pure half of the bleep window's A/B playback: the normalized preview plan, the cache key that keeps a
/// rendered comparison from being rebuilt for every frame, and the exact arguments for the temporary file.
/// No FFmpeg and no files are involved.
/// </summary>
public class AudioBleepPreviewTests
{
    [Fact]
    public void Preview_plan_merges_touching_ranges_and_drops_clicks()
    {
        var plan = BleepPreviewPlan.Create(
            [
                new AudioRange(3, 4),
                new AudioRange(1, 2),
                new AudioRange(2, 2.2),
                new AudioRange(2.5, 2.6),
                new AudioRange(8, 8.01),
            ],
            duration: 10,
            toneHz: 1000,
            volumePercent: 70);

        Assert.NotNull(plan);
        Assert.Equal(
            new[] { new AudioRange(1, 2.2), new AudioRange(2.5, 2.6), new AudioRange(3, 4) },
            plan.Value.Ranges);
        Assert.Equal(1000, plan.Value.ToneHz, 3);
        Assert.Equal(70, plan.Value.VolumePercent);
    }

    [Fact]
    public void Preview_plan_returns_null_when_there_is_nothing_audible_to_compare()
    {
        Assert.Null(BleepPreviewPlan.Create([], duration: 10, toneHz: 1000, volumePercent: 70));
        Assert.Null(BleepPreviewPlan.Create([new AudioRange(4, 4.01)], 10, 1000, 70));
        Assert.Null(BleepPreviewPlan.Create([new AudioRange(0, 1)], duration: 0, toneHz: 1000, volumePercent: 70));
    }

    [Fact]
    public void Preview_key_is_stable_for_the_same_audio_and_changes_with_the_tone()
    {
        var ordered = BleepPreviewPlan.Create([new AudioRange(1, 2), new AudioRange(3, 4)], 10, 1000, 70);
        var reordered = BleepPreviewPlan.Create([new AudioRange(3, 4), new AudioRange(1, 2)], 10, 1000, 70);
        var adjacent = BleepPreviewPlan.Create([new AudioRange(1, 3), new AudioRange(3, 4)], 10, 1000, 70);
        var merged = BleepPreviewPlan.Create([new AudioRange(1, 4)], 10, 1000, 70);

        Assert.NotNull(ordered);
        Assert.Equal(ordered.Value.Key, reordered!.Value.Key);
        Assert.Equal(merged!.Value.Key, adjacent!.Value.Key);
        Assert.NotEqual(ordered.Value.Key, merged.Value.Key);

        Assert.NotEqual(ordered.Value.Key, BleepPreviewPlan.Create([new AudioRange(1, 2.5)], 10, 1000, 70)!.Value.Key);
        Assert.NotEqual(ordered.Value.Key, BleepPreviewPlan.Create([new AudioRange(1, 2)], 10, 1200, 70)!.Value.Key);
        Assert.NotEqual(ordered.Value.Key, BleepPreviewPlan.Create([new AudioRange(1, 2)], 10, 1000, 40)!.Value.Key);
    }

    [Fact]
    public void Preview_arguments_reuse_the_save_filter_and_encode_a_small_m4a()
    {
        var plan = BleepPreviewPlan.Create([new AudioRange(1, 2)], 10, 1000, 70);
        Assert.NotNull(plan);

        var arguments = AudioBleepPreview.Arguments(plan.Value);

        Assert.Equal("-filter_complex", arguments[0]);
        Assert.Contains("between(t,1,2)", arguments[1], StringComparison.Ordinal);
        Assert.Equal("-map", arguments[2]);
        Assert.Equal("[out]", arguments[3]);
        Assert.Contains("-c:a", arguments);
        Assert.Contains("aac", arguments);
        Assert.Contains("-b:a", arguments);
        Assert.Contains("128k", arguments);
        Assert.Contains("+faststart", arguments);
        Assert.Equal("m4a", BleepPreviewPlan.Extension);
    }
}
