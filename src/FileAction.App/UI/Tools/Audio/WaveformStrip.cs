using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FileAction.Core;

namespace FileAction.App.UI.Tools.Audio;

/// <summary>
/// The waveform and the two trim handles, drawn by hand so no extra dependency is needed. Time always runs
/// left to right (a timeline is not mirrored in an Arabic UI); the rest of the window follows RTL normally.
/// </summary>
internal sealed class WaveformStrip : FrameworkElement
{
    private const double HandleRadius = 7;
    private const double HitSlop = 14;
    private const double ColumnWidth = 2;

    private static readonly Brush Dim = Freeze(new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)));

    private float[] _peaks = [];
    private float[] _columns = [];
    private int _columnsFor = -1;
    private double _duration;
    private TrimRange _selection;
    private double? _playhead;
    private int _drag;

    public WaveformStrip()
    {
        ClipToBounds = true;
        Focusable = false;
        // A waveform is a time axis: start is on the left even in an RTL window.
        FlowDirection = FlowDirection.LeftToRight;
        Cursor = Cursors.Arrow;
    }

    /// <summary>Raised while a handle moves; the view mirrors the times into its boxes.</summary>
    public event Action<TrimRange>? SelectionChanged;

    /// <summary>Raised once the mouse is released after a drag.</summary>
    public event Action? SelectionCommitted;

    public TrimRange Selection
    {
        get => _selection;
        set
        {
            if (_selection == value)
                return;
            _selection = value;
            InvalidateVisual();
        }
    }

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
        var startX = TimeToX(_selection.Start);
        var endX = TimeToX(_selection.End);
        for (var i = 0; i < _columns.Length; i++)
        {
            var peak = _columns[i];
            if (peak <= 0)
                continue;

            var barHeight = Math.Max(1.0, peak * amplitude);
            var x = i * ColumnWidth;
            var inside = x + ColumnWidth / 2 >= startX && x + ColumnWidth / 2 <= endX;
            dc.DrawRectangle(
                inside ? Theme.Accent : Theme.Muted,
                null,
                new Rect(x, center - barHeight, ColumnWidth, barHeight * 2));
        }

        if (_duration <= 0)
            return;

        // Shade everything outside the kept part and outline it.
        dc.DrawRectangle(Dim, null, new Rect(0, 0, Math.Max(0, startX), height));
        dc.DrawRectangle(Dim, null, new Rect(Math.Min(width, endX), 0, Math.Max(0, width - endX), height));

        var accentPen = new Pen(Theme.Accent, 2);
        dc.DrawLine(accentPen, new Point(startX, 0), new Point(startX, height));
        dc.DrawLine(accentPen, new Point(endX, 0), new Point(endX, height));

        var handlePen = new Pen(Theme.Text, 2);
        dc.DrawEllipse(Theme.Accent, handlePen, new Point(startX, center), HandleRadius, HandleRadius);
        dc.DrawEllipse(Theme.Accent, handlePen, new Point(endX, center), HandleRadius, HandleRadius);

        if (_playhead is { } playhead && playhead >= 0 && playhead <= _duration)
        {
            var playX = TimeToX(playhead);
            dc.DrawLine(new Pen(Theme.Text, 1.5), new Point(playX, 0), new Point(playX, height));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_duration <= 0 || ActualWidth <= 0)
            return;

        var x = e.GetPosition(this).X;
        var toStart = Math.Abs(x - TimeToX(_selection.Start));
        var toEnd = Math.Abs(x - TimeToX(_selection.End));
        _drag = toStart <= toEnd ? 1 : 2;

        // Grabbing a handle keeps it in place; clicking elsewhere moves the nearer handle there.
        if (toStart > HitSlop && toEnd > HitSlop)
            MoveHandle(x);

        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_duration <= 0 || ActualWidth <= 0)
            return;

        var x = e.GetPosition(this).X;
        if (_drag != 0 && IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
        {
            MoveHandle(x);
            return;
        }

        var nearStart = Math.Abs(x - TimeToX(_selection.Start)) <= HitSlop;
        var nearEnd = Math.Abs(x - TimeToX(_selection.End)) <= HitSlop;
        Cursor = nearStart || nearEnd ? Cursors.SizeWE : Cursors.Arrow;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_drag == 0)
            return;
        _drag = 0;
        ReleaseMouseCapture();
        SelectionCommitted?.Invoke();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _drag = 0;
    }

    private void MoveHandle(double x)
    {
        var time = Math.Clamp(XToTime(x), 0, _duration);
        var minimum = Math.Min(AudioTrim.MinimumDuration, _duration);
        var start = _selection.Start;
        var end = _selection.End;

        if (_drag == 1)
            start = Math.Clamp(time, 0, Math.Max(0, end - minimum));
        else
            end = Math.Clamp(time, Math.Min(_duration, start + minimum), _duration);

        var next = new TrimRange(start, Math.Max(0, end - start));
        if (next == _selection)
            return;

        _selection = next;
        InvalidateVisual();
        SelectionChanged?.Invoke(next);
    }

    private void BuildColumns(int columns)
    {
        if (columns == _columnsFor)
            return;
        _columnsFor = columns;
        _columns = AudioTrim.ResamplePeaks(_peaks, columns);
    }

    private double TimeToX(double seconds) =>
        _duration <= 0 || ActualWidth <= 0
            ? 0
            : Math.Clamp(seconds / _duration, 0, 1) * ActualWidth;

    private double XToTime(double x) =>
        _duration <= 0 || ActualWidth <= 0
            ? 0
            : Math.Clamp(x / ActualWidth, 0, 1) * _duration;

    private static T Freeze<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
