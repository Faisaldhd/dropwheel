using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using FileAction.Core;
using WpfImage = System.Windows.Controls.Image;

namespace FileAction.App.UI.Tools.Video;

/// <summary>
/// The shared timeline: a filmstrip (when FFmpeg produced one), a playhead the user can drag, and, for
/// the trim window, a highlighted range with two draggable start/end handles. Times are always left to
/// right even in Arabic, because a timeline reads as a ruler, not as a paragraph.
/// </summary>
internal sealed class VideoTimeline : Grid
{
    private const double CompactHeight = 54;
    private const double TrackHeight = 82;
    private const double HandleWidth = 12;
    private const double RulerHeight = 16;

    private readonly bool _showRange;
    private readonly Canvas _track = new() { ClipToBounds = true };
    private readonly Grid _ruler = new() { Height = RulerHeight };
    private readonly List<WpfImage> _frames = [];
    private readonly Rectangle _dimLeft = new() { Fill = Brushes.Black, Opacity = 0.55, IsHitTestVisible = false };
    private readonly Rectangle _dimRight = new() { Fill = Brushes.Black, Opacity = 0.55, IsHitTestVisible = false };
    private readonly Border _selection = new()
    {
        BorderBrush = Theme.Accent,
        Background = Theme.AccentSoft,
        BorderThickness = new Thickness(2),
        CornerRadius = new CornerRadius(5),
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
    };
    private readonly Border _startHandle;
    private readonly Border _endHandle;
    private readonly Rectangle _playhead = new() { Width = 2, Fill = Brushes.White, Opacity = 0.92, IsHitTestVisible = false };
    private readonly TextBlock[] _rulerLabels = new TextBlock[5];

    private TimeSpan _duration;
    private TimeSpan _start;
    private TimeSpan _end;
    private TimeSpan _position;
    private double _fps = 30;
    private bool _draggingStart;
    private bool _draggingEnd;
    private bool _scrubbing;

    public VideoTimeline(bool compact = false, bool showRange = false)
    {
        _showRange = showRange;
        FlowDirection = FlowDirection.LeftToRight;
        ClipToBounds = true;
        Margin = new Thickness(0, 4, 0, 2);

        _track.Height = compact ? CompactHeight : TrackHeight;
        _track.Background = Theme.Card;
        _track.MouseLeftButtonDown += OnTrackDown;
        _track.MouseMove += OnTrackMove;
        _track.MouseLeftButtonUp += OnTrackUp;

        _startHandle = BuildHandle();
        _endHandle = BuildHandle();
        _startHandle.Visibility = Visibility.Collapsed;
        _endHandle.Visibility = Visibility.Collapsed;

        _track.Children.Add(_dimLeft);
        _track.Children.Add(_dimRight);
        _track.Children.Add(_selection);
        _track.Children.Add(_startHandle);
        _track.Children.Add(_endHandle);
        _track.Children.Add(_playhead);
        _track.SizeChanged += (_, _) => LayoutVisuals();

        _startHandle.MouseLeftButtonDown += (_, e) => BeginHandleDrag(start: true, e);
        _startHandle.MouseMove += (_, e) => ContinueHandleDrag(start: true, e);
        _startHandle.MouseLeftButtonUp += (_, _) => EndHandleDrag(start: true);
        _endHandle.MouseLeftButtonDown += (_, e) => BeginHandleDrag(start: false, e);
        _endHandle.MouseMove += (_, e) => ContinueHandleDrag(start: false, e);
        _endHandle.MouseLeftButtonUp += (_, _) => EndHandleDrag(start: false);

        BuildRuler();
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Children.Add(_track);
        Children.Add(_ruler);
        Grid.SetRow(_track, 0);
        Grid.SetRow(_ruler, 1);
        _ruler.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;

        LayoutVisuals();
    }

    /// <summary>Raised when the user clicks or drags the playhead.</summary>
    public event Action<TimeSpan>? SeekRequested;

    /// <summary>Raised while a start/end handle is dragged.</summary>
    public event Action<TimeSpan, TimeSpan>? RangeChanged;

    public TimeSpan Start => _start;

    public TimeSpan End => _end;

    public void SetFrameRate(double fps) => _fps = fps is >= 1 and <= 240 ? fps : 30;

    public void SetDuration(TimeSpan duration)
    {
        _duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        _start = TimeSpan.Zero;
        _end = _duration;
        _position = VideoEdit.Clamp(_position, _duration);
        UpdateRuler();
        LayoutVisuals();
    }

    public void SetRange(TimeSpan start, TimeSpan end)
    {
        if (_duration <= TimeSpan.Zero)
        {
            _start = TimeSpan.Zero;
            _end = TimeSpan.Zero;
            LayoutVisuals();
            return;
        }

        var minimum = VideoEdit.FrameStep(_fps);
        start = VideoEdit.Clamp(start, _duration);
        end = VideoEdit.Clamp(end, _duration);
        if (end <= start)
            end = VideoEdit.Clamp(start + minimum, _duration);
        if (end <= start)
            start = VideoEdit.Clamp(end - minimum, _duration);

        _start = start;
        _end = end;
        LayoutVisuals();
    }

    public void SetPosition(TimeSpan position)
    {
        _position = VideoEdit.Clamp(position, _duration);
        LayoutVisuals();
    }

    public void SetStrip(IReadOnlyList<BitmapSource> frames)
    {
        foreach (var image in _frames)
            _track.Children.Remove(image);
        _frames.Clear();

        // Strip frames sit behind the dimming, handles and playhead (index 0…).
        for (var index = 0; index < frames.Count; index++)
        {
            var image = new WpfImage
            {
                Source = frames[index],
                Stretch = Stretch.UniformToFill,
                IsHitTestVisible = false,
            };
            _track.Children.Insert(index, image);
            _frames.Add(image);
        }
        LayoutVisuals();
    }

    private Border BuildHandle()
    {
        var handle = new Border
        {
            Width = HandleWidth,
            Height = TrackHeight + 4,
            CornerRadius = new CornerRadius(6),
            Background = Theme.Accent,
            BorderBrush = Theme.TextOnAccent,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.SizeWE,
            Child = new Border
            {
                Width = 2,
                Height = 18,
                CornerRadius = new CornerRadius(1),
                Background = Theme.TextOnAccent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        handle.MouseEnter += (_, _) => handle.Opacity = 0.85;
        handle.MouseLeave += (_, _) => handle.Opacity = 1;
        return handle;
    }

    private void BuildRuler()
    {
        for (var index = 0; index < _rulerLabels.Length; index++)
        {
            _ruler.ColumnDefinitions.Add(new ColumnDefinition());
            var label = new TextBlock
            {
                FontSize = 10.5,
                Foreground = Theme.Muted,
                HorizontalAlignment = index switch
                {
                    0 => HorizontalAlignment.Left,
                    _ when index == _rulerLabels.Length - 1 => HorizontalAlignment.Right,
                    _ => HorizontalAlignment.Center,
                },
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 2, 2, 0),
            };
            _rulerLabels[index] = label;
            Grid.SetColumn(label, index);
            _ruler.Children.Add(label);
        }
    }

    private void UpdateRuler()
    {
        for (var index = 0; index < _rulerLabels.Length; index++)
        {
            var seconds = _duration.TotalSeconds * index / (_rulerLabels.Length - 1);
            _rulerLabels[index].Text = VideoEdit.FormatClock(TimeSpan.FromSeconds(seconds));
        }
    }

    private void LayoutVisuals()
    {
        var width = Math.Max(1, _track.ActualWidth);
        var height = Math.Max(1, _track.ActualHeight);

        var count = _frames.Count;
        if (count > 0)
        {
            var slot = width / count;
            for (var index = 0; index < count; index++)
            {
                var image = _frames[index];
                image.Width = slot + 1; // one pixel of overlap hides seams between frames
                image.Height = height;
                Canvas.SetLeft(image, index * slot);
                Canvas.SetTop(image, 0);
            }
        }

        var positionX = TimeToX(_position);
        _playhead.Height = height;
        Canvas.SetLeft(_playhead, positionX - 1);
        Canvas.SetTop(_playhead, 0);

        var showRange = _showRange && _duration > TimeSpan.Zero;
        _selection.Visibility = showRange ? Visibility.Visible : Visibility.Collapsed;
        _startHandle.Visibility = showRange ? Visibility.Visible : Visibility.Collapsed;
        _endHandle.Visibility = showRange ? Visibility.Visible : Visibility.Collapsed;
        _dimLeft.Visibility = showRange ? Visibility.Visible : Visibility.Collapsed;
        _dimRight.Visibility = showRange ? Visibility.Visible : Visibility.Collapsed;
        if (!showRange)
            return;

        var startX = TimeToX(_start);
        var endX = TimeToX(_end);

        _selection.Height = height;
        _selection.Width = Math.Max(2, endX - startX);
        Canvas.SetLeft(_selection, startX);
        Canvas.SetTop(_selection, 0);

        _dimLeft.Width = Math.Max(0, startX);
        _dimLeft.Height = height;
        Canvas.SetLeft(_dimLeft, 0);
        Canvas.SetTop(_dimLeft, 0);
        _dimRight.Width = Math.Max(0, width - endX);
        _dimRight.Height = height;
        Canvas.SetLeft(_dimRight, endX);
        Canvas.SetTop(_dimRight, 0);

        PlaceHandle(_startHandle, startX, height);
        PlaceHandle(_endHandle, endX, height);
    }

    private void PlaceHandle(Border handle, double x, double height)
    {
        handle.Height = Math.Max(24, height + 4);
        Canvas.SetLeft(handle, Math.Clamp(x - HandleWidth / 2, 0, Math.Max(0, _track.ActualWidth - HandleWidth)));
        Canvas.SetTop(handle, (height - handle.Height) / 2);
    }

    private double TimeToX(TimeSpan time)
    {
        if (_duration <= TimeSpan.Zero)
            return 0;
        var ratio = Math.Clamp(time.TotalSeconds / _duration.TotalSeconds, 0, 1);
        return ratio * Math.Max(1, _track.ActualWidth);
    }

    private TimeSpan XToTime(double x)
    {
        if (_duration <= TimeSpan.Zero)
            return TimeSpan.Zero;
        var width = Math.Max(1, _track.ActualWidth);
        var ratio = Math.Clamp(x / width, 0, 1);
        return TimeSpan.FromTicks((long)(_duration.Ticks * ratio));
    }

    // ---------------------------------------------------------------- seeking

    private void OnTrackDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;
        _scrubbing = true;
        _track.CaptureMouse();
        SeekAt(e.GetPosition(_track).X);
        e.Handled = true;
    }

    private void OnTrackMove(object sender, MouseEventArgs e)
    {
        if (!_scrubbing || e.LeftButton != MouseButtonState.Pressed)
            return;
        SeekAt(e.GetPosition(_track).X);
    }

    private void OnTrackUp(object sender, MouseButtonEventArgs e)
    {
        _scrubbing = false;
        _track.ReleaseMouseCapture();
    }

    private void SeekAt(double x)
    {
        var position = XToTime(x);
        _position = position;
        LayoutVisuals();
        SeekRequested?.Invoke(position);
    }

    // ---------------------------------------------------------------- range handles

    private void BeginHandleDrag(bool start, MouseButtonEventArgs e)
    {
        if (!_showRange || e.ChangedButton != MouseButton.Left)
            return;
        _draggingStart = start;
        _draggingEnd = !start;
        (start ? _startHandle : _endHandle).CaptureMouse();
        e.Handled = true;
    }

    private void ContinueHandleDrag(bool start, MouseEventArgs e)
    {
        if ((start ? !_draggingStart : !_draggingEnd) || e.LeftButton != MouseButtonState.Pressed)
            return;

        var time = XToTime(e.GetPosition(_track).X);
        var minimum = VideoEdit.FrameStep(_fps);
        if (start)
            _start = VideoEdit.Clamp(time, _end - minimum);
        else
            _end = VideoEdit.Clamp(time, _duration);

        if (!start && _end < _start + minimum)
            _end = VideoEdit.Clamp(_start + minimum, _duration);
        if (start && _start > _end - minimum)
            _start = VideoEdit.Clamp(_end - minimum, _duration);

        LayoutVisuals();
        RangeChanged?.Invoke(_start, _end);
    }

    private void EndHandleDrag(bool start)
    {
        if (start)
        {
            _draggingStart = false;
            _startHandle.ReleaseMouseCapture();
        }
        else
        {
            _draggingEnd = false;
            _endHandle.ReleaseMouseCapture();
        }
    }
}
