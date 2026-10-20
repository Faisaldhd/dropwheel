using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Audio;

/// <summary>
/// The editable "Bleep" layer above the audio: drag across empty space to add a section, drag a clip to
/// move it and drag either of its edges to resize it. The clamping rules are the pure
/// <see cref="AudioEffects"/> ones, so the model can be tested without a window.
/// </summary>
internal sealed class BleepLaneView : FrameworkElement
{
    private const double EdgeHitPixels = 9;
    private const double HandleWidth = 3;
    private const double LabelClearance = 54;

    private static readonly Brush HitBrush = CreateBrush(Color.FromArgb(1, 0, 0, 0));

    private readonly Brush _trackBrush = Theme.Track;
    private readonly Brush _clipBrush = Theme.Accent;
    private readonly Brush _labelBrush = Theme.TextOnAccent;
    private readonly Brush _handleBrush = CreateBrush(Color.FromArgb(0xD8, 0xFF, 0xFF, 0xFF));
    private readonly Pen _selectedPen = CreateSelectedPen();
    private readonly Typeface _typeface = new(Theme.UiFont, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private readonly List<AudioRange> _ranges = [];

    private int _selected = -1;
    private DragMode _mode;
    private int _dragIndex = -1;
    private double _anchor;

    private enum DragMode
    {
        None,
        NewSection,
        Move,
        StartEdge,
        EndEdge,
    }

    public BleepLaneView()
    {
        Focusable = true;
        Cursor = Cursors.Cross;
    }

    public double Duration { get; set; }

    /// <summary>Text drawn inside every clip, e.g. "Bleep".</summary>
    public string ClipLabel { get; set; } = string.Empty;

    public IReadOnlyList<AudioRange> Ranges => _ranges;

    public int Selected => _selected;

    /// <summary>Ranges or the selection changed; repaint the count, the audio lane and the toolbar.</summary>
    public event Action? Changed;

    /// <summary>Replaces the model without raising <see cref="Changed"/> (callers refresh separately).</summary>
    public void SetModel(IReadOnlyList<AudioRange> ranges, int selected)
    {
        _ranges.Clear();
        _ranges.AddRange(ranges);
        _selected = selected >= 0 && selected < _ranges.Count ? selected : -1;
        InvalidateVisual();
    }

    public void UpdateSelected(AudioRange range)
    {
        if (_selected < 0 || _selected >= _ranges.Count)
            return;
        _ranges[_selected] = range;
        InvalidateVisual();
        Changed?.Invoke();
    }

    public void RemoveSelected()
    {
        if (_selected < 0 || _selected >= _ranges.Count)
            return;
        _ranges.RemoveAt(_selected);
        _selected = _ranges.Count == 0 ? -1 : Math.Min(_selected, _ranges.Count - 1);
        InvalidateVisual();
        Changed?.Invoke();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = Math.Max(1, ActualWidth);
        var height = Math.Max(1, ActualHeight);

        dc.DrawRectangle(HitBrush, null, new Rect(0, 0, width, height));
        dc.DrawRoundedRectangle(_trackBrush, null, new Rect(0, 0, width, height), 8, 8);

        if (Duration <= 0)
            return;

        for (var i = 0; i < _ranges.Count; i++)
            DrawSection(dc, _ranges[i], i == _selected, width, height);
    }

    private void DrawSection(DrawingContext dc, AudioRange range, bool selected, double width, double height)
    {
        var x0 = XFor(range.Start, width);
        var x1 = XFor(range.End, width);
        var rect = new Rect(x0, 4, Math.Max(3, x1 - x0), Math.Max(6, height - 8));

        dc.DrawRoundedRectangle(_clipBrush, selected ? _selectedPen : null, rect, 6, 6);

        // Edge handles double as the hit targets for resizing.
        var handleHeight = Math.Max(4, rect.Height - 10);
        dc.DrawRoundedRectangle(_handleBrush, null, new Rect(rect.Left + 3, rect.Top + 5, HandleWidth, handleHeight), 1.5, 1.5);
        dc.DrawRoundedRectangle(_handleBrush, null, new Rect(rect.Right - 3 - HandleWidth, rect.Top + 5, HandleWidth, handleHeight), 1.5, 1.5);

        if (rect.Width < LabelClearance || string.IsNullOrEmpty(ClipLabel))
            return;

        var text = new FormattedText(
            ClipLabel,
            CultureInfo.CurrentUICulture,
            IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            _typeface,
            11,
            _labelBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, rect.Width - 18),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
        };
        dc.DrawText(text, new Point(rect.Left + 9, rect.Top + (rect.Height - text.Height) / 2));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Duration <= 0)
            return;

        Focus();
        var x = e.GetPosition(this).X;
        var time = SecondsFor(x);
        var index = -1;
        var mode = DragMode.NewSection;

        for (var i = 0; i < _ranges.Count; i++)
        {
            if (Math.Abs(x - XFor(_ranges[i].Start, ActualWidth)) <= EdgeHitPixels)
            {
                index = i;
                mode = DragMode.StartEdge;
                break;
            }
            if (Math.Abs(x - XFor(_ranges[i].End, ActualWidth)) <= EdgeHitPixels)
            {
                index = i;
                mode = DragMode.EndEdge;
                break;
            }
        }

        if (index < 0)
        {
            for (var i = 0; i < _ranges.Count; i++)
            {
                if (x < XFor(_ranges[i].Start, ActualWidth) || x > XFor(_ranges[i].End, ActualWidth))
                    continue;
                index = i;
                mode = DragMode.Move;
                break;
            }
        }

        _mode = mode;
        _dragIndex = index;
        _selected = index;
        _anchor = mode == DragMode.Move
            ? time - _ranges[index].Start
            : time;
        CaptureMouse();
        e.Handled = true;
        InvalidateVisual();
        Changed?.Invoke();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Duration <= 0)
            return;

        var x = e.GetPosition(this).X;
        if (_mode == DragMode.None)
        {
            Cursor = CursorFor(x);
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndDrag();
            return;
        }

        var time = SecondsFor(x);

        if (_mode == DragMode.NewSection)
        {
            var section = AudioEffects.NewSection(Duration, _anchor, time);
            if (section is null)
            {
                if (_dragIndex >= 0)
                {
                    _ranges.RemoveAt(_dragIndex);
                    _dragIndex = -1;
                    _selected = -1;
                }
            }
            else if (_dragIndex < 0)
            {
                _ranges.Add(section.Value);
                _dragIndex = _ranges.Count - 1;
                _selected = _dragIndex;
            }
            else
            {
                _ranges[_dragIndex] = section.Value;
            }
        }
        else if (_dragIndex >= 0 && _dragIndex < _ranges.Count)
        {
            _ranges[_dragIndex] = _mode switch
            {
                DragMode.Move => AudioEffects.MoveSection(_ranges[_dragIndex], time - _anchor, Duration),
                DragMode.StartEdge => AudioEffects.ResizeSection(_ranges[_dragIndex], startEdge: true, time, Duration),
                DragMode.EndEdge => AudioEffects.ResizeSection(_ranges[_dragIndex], startEdge: false, time, Duration),
                _ => _ranges[_dragIndex],
            };
        }

        InvalidateVisual();
        Changed?.Invoke();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        EndDrag();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Delete or Key.Back)
        {
            RemoveSelected();
            e.Handled = true;
        }
    }

    private void EndDrag()
    {
        if (_mode == DragMode.None)
            return;

        _mode = DragMode.None;
        _dragIndex = -1;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        Cursor = Cursors.Cross;
        InvalidateVisual();
        Changed?.Invoke();
    }

    private Cursor CursorFor(double x)
    {
        foreach (var range in _ranges)
        {
            if (Math.Abs(x - XFor(range.Start, ActualWidth)) <= EdgeHitPixels
                || Math.Abs(x - XFor(range.End, ActualWidth)) <= EdgeHitPixels)
                return Cursors.SizeWE;
            if (x >= XFor(range.Start, ActualWidth) && x <= XFor(range.End, ActualWidth))
                return Cursors.Hand;
        }
        return Cursors.Cross;
    }

    private double XFor(double seconds, double width) =>
        Duration <= 0 ? 0 : Math.Clamp(seconds / Duration, 0, 1) * width;

    private double SecondsFor(double x) =>
        Duration <= 0 ? 0 : Math.Clamp(x / Math.Max(1, ActualWidth), 0, 1) * Duration;

    private static Brush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen CreateSelectedPen()
    {
        var pen = new Pen(CreateBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)), 1.5);
        pen.Freeze();
        return pen;
    }
}
