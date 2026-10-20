using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FileAction.Core;

namespace FileAction.App.UI.Tools.Audio;

/// <summary>
/// The trim waveform: the shared read-only waveform plus the dimmed kept region, the orange start/end
/// handles and the drag handling. Handles are dragged with the mouse; typing exact times in the view moves
/// the same selection.
/// </summary>
internal sealed class WaveformStrip : WaveformChart
{
    private const double HandleRadius = 7;
    private const double HitSlop = 14;

    private TrimRange _selection;
    private int _drag;

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

    protected override bool IsHighlighted(double x) =>
        DurationSeconds > 0 && x >= TimeToX(_selection.Start) && x <= TimeToX(_selection.End);

    protected override void DrawOverlay(DrawingContext dc, double center)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        var startX = TimeToX(_selection.Start);
        var endX = TimeToX(_selection.End);

        // Shade everything outside the kept part and outline it in the handle colour.
        dc.DrawRectangle(AudioTheme.Dim, null, new Rect(0, 0, Math.Max(0, startX), height));
        dc.DrawRectangle(AudioTheme.Dim, null, new Rect(Math.Min(width, endX), 0, Math.Max(0, width - endX), height));

        var handleBrush = AudioTheme.EndHandle;
        var outline = new Pen(handleBrush, 2);
        dc.DrawLine(outline, new Point(startX, 0), new Point(startX, height));
        dc.DrawLine(outline, new Point(endX, 0), new Point(endX, height));

        var handlePen = new Pen(Theme.Text, 2);
        dc.DrawEllipse(handleBrush, handlePen, new Point(startX, center), HandleRadius, HandleRadius);
        dc.DrawEllipse(handleBrush, handlePen, new Point(endX, center), HandleRadius, HandleRadius);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (DurationSeconds <= 0 || ActualWidth <= 0)
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
        if (DurationSeconds <= 0 || ActualWidth <= 0)
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
        var duration = DurationSeconds;
        var time = Math.Clamp(XToTime(x), 0, duration);
        var minimum = Math.Min(AudioTrim.MinimumDuration, duration);
        var start = _selection.Start;
        var end = _selection.End;

        if (_drag == 1)
            start = Math.Clamp(time, 0, Math.Max(0, end - minimum));
        else
            end = Math.Clamp(time, Math.Min(duration, start + minimum), duration);

        var next = new TrimRange(start, Math.Max(0, end - start));
        if (next == _selection)
            return;

        _selection = next;
        InvalidateVisual();
        SelectionChanged?.Invoke(next);
    }
}
