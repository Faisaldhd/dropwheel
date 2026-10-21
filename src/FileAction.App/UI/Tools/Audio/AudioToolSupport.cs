using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FileAction.Core;

namespace FileAction.App.UI.Tools.Audio;

/// <summary>
/// A tiny PCM copy of the source, bucketed into peaks so the tool windows can draw a waveform without
/// decoding the whole file or depending on a second library. FFmpeg (found by <see cref="ToolLocator"/>)
/// writes 4 kHz stereo s16le into the temp folder; that is quick even for hour-long files.
/// </summary>
internal sealed class AudioWaveform
{
    /// <summary>4 kHz keeps an hour of stereo near 57 MB of temporary PCM and is plenty for an envelope.</summary>
    private const int SampleRate = 4000;
    private const int Buckets = 1200;

    private AudioWaveform(double duration, float[] left, float[] right)
    {
        Duration = duration;
        Left = left;
        Right = right;
    }

    public double Duration { get; }

    /// <summary>Peak amplitude per bucket, 0–1, for the left channel.</summary>
    public float[] Left { get; }

    /// <summary>Peak amplitude per bucket, 0–1, for the right channel.</summary>
    public float[] Right { get; }

    /// <summary>Decodes a small PCM copy and reduces it to peaks. Throws when ffmpeg fails.</summary>
    public static async Task<AudioWaveform> LoadAsync(string ffmpeg, string path, CancellationToken ct)
    {
        var directory = OutputPaths.TempDirectory();
        var pcm = Path.Combine(directory, "waveform.pcm");
        try
        {
            await ProcessRunner.RunAsync(
                ffmpeg,
                [
                    "-hide_banner", "-nostdin", "-y",
                    "-i", path,
                    "-vn",
                    "-ac", "2",
                    "-ar", SampleRate.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "-f", "s16le", pcm,
                ],
                ct).ConfigureAwait(false);

            // Streamed in chunks: the temporary PCM never has to fit in memory.
            var totalFrames = new FileInfo(pcm).Length / 4;
            var left = new float[Buckets];
            var right = new float[Buckets];
            var buffer = new byte[1 << 20];
            long frame = 0;
            await using (var stream = new FileStream(pcm, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true))
            {
                int read;
                while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    var count = read / 4;
                    for (var i = 0; i < count; i++)
                    {
                        var bucket = (int)Math.Min(Buckets - 1, (frame + i) * Buckets / Math.Max(1, totalFrames));
                        var l = Math.Abs(BitConverter.ToInt16(buffer, i * 4)) / 32768f;
                        var r = Math.Abs(BitConverter.ToInt16(buffer, i * 4 + 2)) / 32768f;
                        if (l > left[bucket])
                            left[bucket] = l;
                        if (r > right[bucket])
                            right[bucket] = r;
                    }
                    frame += count;
                }
            }
            return new AudioWaveform(totalFrames / (double)SampleRate, left, right);
        }
        finally
        {
            OutputPaths.TryDelete(directory);
        }
    }

    /// <summary>The louder of the two channels, used by the single-lane bleep preview.</summary>
    public float[] Combined()
    {
        var combined = new float[Buckets];
        for (var i = 0; i < Buckets; i++)
            combined[i] = Math.Max(Left[i], Right[i]);
        return combined;
    }
}

/// <summary>
/// Draws the audio envelope: one lane per side in the channels tool, one combined lane under the bleep
/// layer. The bleep tool paints the replaced ranges here for context; editing lives in the layer above.
/// </summary>
internal sealed class WaveformView : FrameworkElement
{
    private const double LaneGap = 6;

    private static readonly Brush HitBrush = AudioTheme.HitBrush;

    private readonly Brush _waveBrush = Theme.Accent;
    private readonly Brush _laneBrush = Theme.Track;
    private readonly Brush _mutedBrush = Theme.Muted;
    private readonly Brush _rangeFill = Theme.AccentSoft;

    private double _leftGain = 1;
    private double _rightGain = 1;
    private bool _showTone;
    private double _toneAmplitude = AudioEffects.DefaultToneVolume / 100.0;

    public AudioWaveform? Waveform { get; set; }

    /// <summary>Two lanes (left/right) instead of one combined envelope.</summary>
    public bool Stereo { get; set; }

    /// <summary>Ranges the bleep tool is replacing; drawn as a translucent block for context.</summary>
    public IReadOnlyList<AudioRange> Ranges { get; set; } = [];

    public void ShowChannels(double leftGain, double rightGain)
    {
        _leftGain = Math.Max(0, leftGain);
        _rightGain = Math.Max(0, rightGain);
        InvalidateVisual();
    }

    public void ShowBleep(bool bleeped, double toneAmplitude)
    {
        _showTone = bleeped;
        _toneAmplitude = Math.Clamp(toneAmplitude, 0, 1);
        InvalidateVisual();
    }

    public void SetRanges(IReadOnlyList<AudioRange> ranges)
    {
        Ranges = ranges;
        InvalidateVisual();
    }

    public void Redraw() => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        var width = Math.Max(1, ActualWidth);
        var height = Math.Max(1, ActualHeight);
        // A nearly transparent fill keeps the surface hit-testable without covering the lane behind it.
        drawingContext.DrawRectangle(HitBrush, null, new Rect(0, 0, width, height));

        var waveform = Waveform;
        if (waveform is null)
            return;

        var duration = Math.Max(0, waveform.Duration);
        if (duration > 0)
        {
            foreach (var range in Ranges)
            {
                var x0 = Math.Clamp(range.Start / duration, 0, 1) * width;
                var x1 = Math.Clamp(range.End / duration, 0, 1) * width;
                if (x1 > x0)
                    drawingContext.DrawRectangle(_rangeFill, null, new Rect(x0, 0, x1 - x0, height));
            }
        }

        var lanes = Stereo ? 2 : 1;
        var laneHeight = (height - (lanes - 1) * LaneGap) / lanes;
        for (var lane = 0; lane < lanes; lane++)
        {
            var top = lane * (laneHeight + LaneGap);
            var gain = Stereo ? (lane == 0 ? _leftGain : _rightGain) : Math.Max(_leftGain, _rightGain);
            var peaks = Stereo ? (lane == 0 ? waveform.Left : waveform.Right) : waveform.Combined();
            DrawLane(drawingContext, peaks, top, laneHeight, width, duration, gain);
        }
    }

    private void DrawLane(DrawingContext dc, float[] peaks, double top, double height, double width, double duration, double gain)
    {
        var center = top + height / 2;
        var half = Math.Max(1, height / 2 - 2);
        dc.DrawRectangle(_laneBrush, null, new Rect(0, center, width, 1));

        var step = Math.Max(1, (int)Math.Round(width / 360));
        for (var x = 0; x < (int)width; x += step)
        {
            double amplitude;
            Brush brush;
            if (_showTone && Ranges.Count > 0 && IsInside(x, width, duration))
            {
                amplitude = Math.Clamp(_toneAmplitude, 0, 1);
                brush = _waveBrush;
            }
            else
            {
                var bucket = (int)Math.Min(peaks.Length - 1, (long)x * peaks.Length / Math.Max(1, (int)width));
                amplitude = Math.Clamp(peaks[bucket] * gain, 0, 1);
                brush = gain <= 0.001 ? _mutedBrush : _waveBrush;
            }

            var extent = amplitude * half;
            if (extent < 0.5)
                continue;
            dc.DrawRectangle(brush, null, new Rect(x, center - extent, step, Math.Max(1, extent * 2)));
        }
    }

    private bool IsInside(double x, double width, double duration)
    {
        if (duration <= 0)
            return false;
        var seconds = Math.Clamp(x / Math.Max(1, width), 0, 1) * duration;
        foreach (var range in Ranges)
        {
            if (seconds >= range.Start && seconds <= range.End)
                return true;
        }
        return false;
    }
}

/// <summary>Where an audio tool writes: the folder under the cursor when the drop found one, else next to the source.</summary>
internal static class AudioToolOutput
{
    public static string DirectoryFor(FileItem item, DropTargets? targets)
    {
        var detected = OutputTarget.Resolve(item.Path, targets?.UnderCursor, Directory.Exists, OutputTarget.CanWrite);
        if (!detected.UsesDetectedFolder && targets?.Remembered is { } remembered)
        {
            var fallback = OutputTarget.Resolve(item.Path, remembered, Directory.Exists, OutputTarget.CanWrite);
            if (fallback.UsesDetectedFolder)
                return fallback.Directory!;
        }
        return detected.Directory
            ?? OutputTarget.SourceFolder(item.Path)
            ?? Environment.CurrentDirectory;
    }
}

/// <summary>Small pieces every audio tool window shares (caption, status line, preview surface).</summary>
internal static class AudioToolChrome
{
    public static TextBlock Caption(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = Theme.Muted,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 10, 0),
    };

    public static TextBlock Status() => new()
    {
        FontSize = 11.5,
        Foreground = Theme.Muted,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 6, 0, 0),
    };

    public static Border PreviewSurface(FrameworkElement content) => new()
    {
        Background = Theme.Card,
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(6),
        Margin = new Thickness(0, 4, 0, 10),
        Child = content,
    };
}
