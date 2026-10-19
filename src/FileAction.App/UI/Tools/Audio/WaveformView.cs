using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FileAction.Core;

namespace FileAction.App.UI.Tools.Audio;

/// <summary>
/// A decoded waveform drawn by hand: one bar per two pixels, a centre line and an optional playhead. The
/// trim strip adds the handles on top; the normalize window only needs this read-only view.
/// </summary>
internal class WaveformView : FrameworkElement
{
    protected const double ColumnWidth = 2;

    private float[] _peaks = [];
    private float[] _columns = [];
    private int _columnsFor = -1;
    private double _duration;
    private double? _playhead;

    public WaveformView()
    {
        ClipToBounds = true;
        Focusable = false;
        // A waveform is a time axis: start is on the left even in an RTL window.
        FlowDirection = FlowDirection.LeftToRight;
        Cursor = Cursors.Arrow;
    }

    /// <summary>Decoded length of the file; zero until the waveform pass finished.</summary>
    public double DurationSeconds => _duration;

    public double? Playhead
    {
        get => _playhead;
        set
        {
            if (_playhead == value)
                return;
            _playhead = value;
            InvalidateVisual();
        }
    }

    public void SetWaveform(float[]? peaks, double duration)
    {
        _peaks = peaks ?? [];
        _duration = double.IsFinite(duration) && duration > 0 ? duration : 0;
        _columnsFor = -1;
        _columns = [];
        InvalidateVisual();
    }

    /// <summary>True when the bar at <paramref name="x"/> is inside the kept region (always for a preview).</summary>
    protected virtual bool IsHighlighted(double x) => true;

    /// <summary>Drawing between the bars and the playhead: the trim strip draws its dimming and handles here.</summary>
    protected virtual void DrawOverlay(DrawingContext dc, double center)
    {
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        var center = height / 2;
        var amplitude = Math.Max(2.0, center - 9);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
        dc.DrawRectangle(Theme.Track, null, new Rect(0, center - 0.5, width, 1));

        BuildColumns((int)Math.Max(1, Math.Round(width / ColumnWidth)));
        for (var i = 0; i < _columns.Length; i++)
        {
            var peak = _columns[i];
            if (peak <= 0)
                continue;

            var barHeight = Math.Max(1.0, peak * amplitude);
            var x = i * ColumnWidth;
            dc.DrawRectangle(
                IsHighlighted(x + ColumnWidth / 2) ? Theme.Accent : Theme.Muted,
                null,
                new Rect(x, center - barHeight, ColumnWidth, barHeight * 2));
        }

        if (_duration > 0)
            DrawOverlay(dc, center);

        if (_duration > 0 && _playhead is { } playhead && playhead >= 0 && playhead <= _duration)
        {
            var playX = TimeToX(playhead);
            dc.DrawLine(new Pen(Theme.Text, 1.5), new Point(playX, 0), new Point(playX, height));
        }
    }

    protected double TimeToX(double seconds) =>
        _duration <= 0 || ActualWidth <= 0
            ? 0
            : Math.Clamp(seconds / _duration, 0, 1) * ActualWidth;

    protected double XToTime(double x) =>
        _duration <= 0 || ActualWidth <= 0
            ? 0
            : Math.Clamp(x / ActualWidth, 0, 1) * _duration;

    private void BuildColumns(int columns)
    {
        if (columns == _columnsFor)
            return;
        _columnsFor = columns;
        _columns = AudioTrim.ResamplePeaks(_peaks, columns);
    }
}
