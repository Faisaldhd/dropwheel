using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Video;

/// <summary>
/// Crop a video frame: a still from FFmpeg with a draggable rectangle (corner and edge handles, dimmed
/// outside), aspect chips, and W/H boxes that always produce even pixel values. The rectangle is kept in
/// original pixels; only the preview is scaled. Apply re-encodes the video and copies the audio.
/// </summary>
internal sealed class VideoCropView
{
    private const int StripFrames = 9;

    // The four ratios are technical notation and stay identical in both languages; only "Free" is translated.
    private static readonly (string Label, double? Ratio)[] Ratios =
    [
        (string.Empty, null),
        ("1:1", 1.0),
        ("16:9", 16.0 / 9.0),
        ("9:16", 9.0 / 16.0),
        ("4:3", 4.0 / 3.0),
    ];

    private readonly FileItem _item;
    private readonly DropTargets? _targets;
    private readonly VideoPreview _preview;
    private readonly VideoTimeline _timeline = new(compact: true);
    private readonly Canvas _canvas = new() { ClipToBounds = true, Background = Brushes.Transparent, Visibility = Visibility.Collapsed };
    private readonly Border _frame = new()
    {
        BorderBrush = Theme.Accent,
        BorderThickness = new Thickness(2),
        IsHitTestVisible = false,
    };
    private readonly Rectangle[] _dims =
    [
        new() { Fill = Brushes.Black, Opacity = 0.55, IsHitTestVisible = false },
        new() { Fill = Brushes.Black, Opacity = 0.55, IsHitTestVisible = false },
        new() { Fill = Brushes.Black, Opacity = 0.55, IsHitTestVisible = false },
        new() { Fill = Brushes.Black, Opacity = 0.55, IsHitTestVisible = false },
    ];
    private readonly Border _moveSurface = new()
    {
        Background = Brushes.Transparent,
        Cursor = Cursors.SizeAll,
    };
    private readonly List<(CropHandle Handle, Border Element)> _handles = [];
    private readonly StackPanel _chipRow = new() { Orientation = Orientation.Horizontal };
    private readonly TextBox _widthBox = new() { Width = 62 };
    private readonly TextBox _heightBox = new() { Width = 62 };
    private readonly TextBlock _sizeText;
    private readonly TextBlock _status;
    private readonly TaskCompletionSource<MediaSize?> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<CropHandle, Point> _dragOrigin = [];
    private readonly Dictionary<CropHandle, CropRect> _dragStart = [];
    private Point _moveOrigin;
    private CropRect _moveStart;

    private CropRect _rect;
    private double? _ratio;
    private int _width;
    private int _height;
    private double _scale = 1;
    private double _originX;
    private double _originY;
    private bool _syncing;
    private bool _busy;
    private VideoInfo? _info;
    private Grid? _root;
    private VideoPalette _palette;

    public VideoCropView(FileItem item, DropTargets? targets)
    {
        _item = item;
        _targets = targets;
        _preview = new VideoPreview(item, "tool.videocrop");
        _sizeText = VideoControls.Hint(string.Empty);
        _sizeText.Foreground = Theme.Text;
        _status = VideoControls.Hint(string.Empty);

        _timeline.SeekRequested += position => _ = _preview.SeekAsync(position);

        _canvas.Children.Add(_dims[0]);
        _canvas.Children.Add(_dims[1]);
        _canvas.Children.Add(_dims[2]);
        _canvas.Children.Add(_dims[3]);
        _canvas.Children.Add(_frame);
        _canvas.Children.Add(_moveSurface);
        BuildHandles();
        _canvas.SizeChanged += (_, _) => Layout();

        _moveSurface.MouseLeftButtonDown += BeginMove;
        _moveSurface.MouseMove += ContinueMove;
        _moveSurface.MouseLeftButtonUp += (_, _) =>
        {
            _moveSurface.ReleaseMouseCapture();
            Commit();
        };
    }

    public ToolContent Build()
    {
        // The canvas lives inside the preview surface, so its coordinates match the displayed image
        // exactly (the preview border's padding is already outside it).
        _preview.Surface.Children.Add(_canvas);
        var host = _preview.View;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var chips = BuildChips();
        var boxes = BuildBoxes();
        var footer = BuildFooter();
        root.Children.Add(host);
        root.Children.Add(_timeline);
        root.Children.Add(chips);
        root.Children.Add(boxes);
        root.Children.Add(footer);
        Grid.SetRow(host, 0);
        Grid.SetRow(_timeline, 1);
        Grid.SetRow(chips, 2);
        Grid.SetRow(boxes, 3);
        Grid.SetRow(footer, 4);

        _root = root;
        _palette = VideoPalette.Capture();
        VideoThemeWatcher.Start();
        VideoThemeWatcher.Changed += OnThemeChanged;

        root.Unloaded += (_, _) =>
        {
            VideoThemeWatcher.Changed -= OnThemeChanged;
            _lifetime.Cancel();
            _preview.Dispose();
        };

        return new ToolContent(root, Reset, ApplyAsync, _ready.Task, ActivateAsync);
    }

    private void BuildHandles()
    {
        foreach (var handle in Enum.GetValues<CropHandle>().Where(h => h != CropHandle.None))
        {
            var element = new Border
            {
                Width = 13,
                Height = 13,
                CornerRadius = new CornerRadius(7),
                Background = Theme.Accent,
                BorderBrush = Theme.TextOnAccent,
                BorderThickness = new Thickness(1),
                Cursor = CursorFor(handle),
                Visibility = Visibility.Collapsed,
            };
            element.MouseLeftButtonDown += (s, e) => BeginHandleDrag(handle, (FrameworkElement)s, e);
            element.MouseMove += (_, e) => ContinueHandleDrag(handle, e);
            element.MouseLeftButtonUp += (_, _) => element.ReleaseMouseCapture();
            _handles.Add((handle, element));
            _canvas.Children.Add(element);
        }
    }

    private FrameworkElement BuildChips()
    {
        _chipRow.Margin = new Thickness(0, 8, 0, 6);
        foreach (var (label, ratio) in Ratios)
        {
            var text = label.Length == 0 ? T("tool.videocrop.aspect.free") : label;
            _chipRow.Children.Add(VideoControls.Chip(text, () => SelectRatio(ratio)));
        }
        PaintChips();
        return _chipRow;
    }

    private FrameworkElement BuildBoxes()
    {
        foreach (var box in new[] { _widthBox, _heightBox })
        {
            box.Padding = new Thickness(6, 4, 6, 4);
            box.Background = Theme.Card;
            box.Foreground = Theme.Text;
            box.BorderBrush = Theme.PanelBorder;
            box.BorderThickness = new Thickness(1);
            box.HorizontalContentAlignment = HorizontalAlignment.Center;
            box.FlowDirection = FlowDirection.LeftToRight;
            box.TextAlignment = TextAlignment.Center;
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    CommitBoxes();
                    e.Handled = true;
                }
            };
            box.LostFocus += (_, _) => CommitBoxes();
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        row.Children.Add(VideoControls.FieldLabel(T("tool.videocrop.width")));
        row.Children.Add(_widthBox);
        var heightLabel = VideoControls.FieldLabel(T("tool.videocrop.height"));
        heightLabel.Margin = new Thickness(18, 0, 8, 0);
        row.Children.Add(heightLabel);
        row.Children.Add(_heightBox);
        var sizeLabel = VideoControls.FieldLabel(T("tool.videocrop.size"));
        sizeLabel.Margin = new Thickness(20, 0, 8, 0);
        row.Children.Add(sizeLabel);
        row.Children.Add(_sizeText);
        return row;
    }

    private FrameworkElement BuildFooter()
    {
        var footer = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
        var hint = VideoControls.Hint(
            T("tool.videocrop.hint") + "  ·  " + T("tool.videocrop.evenNote") + "  ·  " + T("tool.videocrop.keepAudio"));
        footer.Children.Add(hint);
        footer.Children.Add(_status);
        return footer;
    }

    public async Task ActivateAsync()
    {
        try
        {
            if (!await _preview.LoadAsync())
            {
                _ready.TrySetResult(null);
                return;
            }

            _info = _preview.Info!;
            _width = _info.Width;
            _height = _info.Height;
            _rect = VideoEdit.EvenCrop(CropMath.Full(_width, _height), _width, _height);
            _timeline.SetFrameRate(_info.Fps);
            _timeline.SetDuration(_info.Duration);
            _timeline.SetPosition(TimeSpan.Zero);
            _canvas.Visibility = Visibility.Visible;
            SyncBoxes();
            Layout();
            _ready.TrySetResult(new MediaSize(_width, _height));

            _ = LoadStripAsync();
        }
        catch (OperationCanceledException)
        {
            _ready.TrySetResult(null);
        }
        catch (Exception)
        {
            _ready.TrySetResult(null);
        }
    }

    private async Task LoadStripAsync()
    {
        var frames = await _preview.LoadStripAsync(StripFrames, 150);
        if (frames.Count > 0 && !_lifetime.IsCancellationRequested)
            _timeline.SetStrip(frames);
    }

    private void SelectRatio(double? ratio)
    {
        _ratio = ratio;
        if (_width > 0 && ratio is double value)
            _rect = VideoEdit.EvenCrop(CropMath.SnapToRatio(_rect, _width, _height, value), _width, _height);
        SyncBoxes();
        Layout();
        PaintChips();
    }

    private void PaintChips()
    {
        for (var index = 0; index < _chipRow.Children.Count && index < Ratios.Length; index++)
        {
            if (_chipRow.Children[index] is Border chip)
                VideoControls.PaintChip(chip, Equals(Ratios[index].Ratio, _ratio));
        }
    }

    private void BeginMove(object sender, MouseButtonEventArgs e)
    {
        if (_width == 0 || e.ChangedButton != MouseButton.Left)
            return;
        _moveOrigin = e.GetPosition(_canvas);
        _moveStart = _rect;
        _moveSurface.CaptureMouse();
        e.Handled = true;
    }

    private void ContinueMove(object sender, MouseEventArgs e)
    {
        if (!_moveSurface.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed)
            return;
        var point = e.GetPosition(_canvas);
        var dx = (point.X - _moveOrigin.X) / _scale;
        var dy = (point.Y - _moveOrigin.Y) / _scale;
        _rect = VideoEdit.EvenCrop(
            new CropRect(_moveStart.X + dx, _moveStart.Y + dy, _moveStart.Width, _moveStart.Height),
            _width, _height);
        SyncBoxes();
        Layout();
    }

    private void BeginHandleDrag(CropHandle handle, FrameworkElement element, MouseButtonEventArgs e)
    {
        if (_width == 0 || e.ChangedButton != MouseButton.Left)
            return;
        _dragOrigin[handle] = e.GetPosition(_canvas);
        _dragStart[handle] = _rect;
        element.CaptureMouse();
        e.Handled = true;
    }

    private void ContinueHandleDrag(CropHandle handle, MouseEventArgs e)
    {
        if (!_dragStart.TryGetValue(handle, out var start) || !_dragOrigin.TryGetValue(handle, out var origin))
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        var point = e.GetPosition(_canvas);
        var dx = (point.X - origin.X) / _scale;
        var dy = (point.Y - origin.Y) / _scale;
        _rect = VideoEdit.EvenCrop(
            CropMath.Drag(handle, start, dx, dy, _width, _height, _ratio),
            _width, _height);
        SyncBoxes();
        Layout();
    }

    private void CommitBoxes()
    {
        if (_syncing || _width == 0)
            return;
        if (!int.TryParse(_widthBox.Text, out var width) || !int.TryParse(_heightBox.Text, out var height))
        {
            SyncBoxes();
            return;
        }

        width = VideoEdit.Even(Math.Max(2, width));
        height = VideoEdit.Even(Math.Max(2, height));
        if (_ratio is double ratio)
            height = VideoEdit.Even(Math.Max(2, (int)Math.Round(width / ratio)));

        _rect = VideoEdit.EvenCrop(new CropRect(_rect.X, _rect.Y, width, height), _width, _height);
        SyncBoxes();
        Layout();
    }

    private void Commit()
    {
        _sizeText.Text = T(
            "tool.videocrop.sizeValue",
            ((int)_rect.Width).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((int)_rect.Height).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private void SyncBoxes()
    {
        _syncing = true;
        _widthBox.Text = ((int)_rect.Width).ToString(System.Globalization.CultureInfo.InvariantCulture);
        _heightBox.Text = ((int)_rect.Height).ToString(System.Globalization.CultureInfo.InvariantCulture);
        _syncing = false;
        Commit();
    }

    private void Layout()
    {
        if (_width == 0 || _canvas.ActualWidth <= 0 || _canvas.ActualHeight <= 0)
            return;

        var availableWidth = Math.Max(1, _canvas.ActualWidth);
        var availableHeight = Math.Max(1, _canvas.ActualHeight);
        _scale = Math.Min(availableWidth / _width, availableHeight / _height);
        if (_scale <= 0 || double.IsInfinity(_scale))
            _scale = 1;

        var displayWidth = _width * _scale;
        var displayHeight = _height * _scale;
        _originX = (availableWidth - displayWidth) / 2;
        _originY = (availableHeight - displayHeight) / 2;

        var left = _originX + _rect.X * _scale;
        var top = _originY + _rect.Y * _scale;
        var width = _rect.Width * _scale;
        var height = _rect.Height * _scale;

        Canvas.SetLeft(_frame, left);
        Canvas.SetTop(_frame, top);
        _frame.Width = width;
        _frame.Height = height;

        Canvas.SetLeft(_moveSurface, left);
        Canvas.SetTop(_moveSurface, top);
        _moveSurface.Width = Math.Max(1, width);
        _moveSurface.Height = Math.Max(1, height);

        Dim(_dims[0], _originX, _originY, displayWidth, top - _originY);
        Dim(_dims[1], _originX, top + height, displayWidth, _originY + displayHeight - (top + height));
        Dim(_dims[2], _originX, top, left - _originX, height);
        Dim(_dims[3], left + width, top, _originX + displayWidth - (left + width), height);

        foreach (var (handle, element) in _handles)
        {
            var point = HandlePoint(handle, left, top, width, height);
            Canvas.SetLeft(element, point.X - element.Width / 2);
            Canvas.SetTop(element, point.Y - element.Height / 2);
            element.Visibility = Visibility.Visible;
        }
    }

    private static void Dim(Rectangle rectangle, double left, double top, double width, double height)
    {
        Canvas.SetLeft(rectangle, Math.Max(0, left));
        Canvas.SetTop(rectangle, Math.Max(0, top));
        rectangle.Width = Math.Max(0, width);
        rectangle.Height = Math.Max(0, height);
    }

    private static Point HandlePoint(CropHandle handle, double left, double top, double width, double height) => handle switch
    {
        CropHandle.TopLeft => new Point(left, top),
        CropHandle.Top => new Point(left + width / 2, top),
        CropHandle.TopRight => new Point(left + width, top),
        CropHandle.Right => new Point(left + width, top + height / 2),
        CropHandle.BottomRight => new Point(left + width, top + height),
        CropHandle.Bottom => new Point(left + width / 2, top + height),
        CropHandle.BottomLeft => new Point(left, top + height),
        CropHandle.Left => new Point(left, top + height / 2),
        _ => new Point(left, top),
    };

    private static Cursor CursorFor(CropHandle handle) => handle switch
    {
        CropHandle.TopLeft or CropHandle.BottomRight => Cursors.SizeNWSE,
        CropHandle.TopRight or CropHandle.BottomLeft => Cursors.SizeNESW,
        CropHandle.Top or CropHandle.Bottom => Cursors.SizeNS,
        _ => Cursors.SizeWE,
    };

    private void Reset()
    {
        _ratio = null;
        if (_width > 0)
            _rect = VideoEdit.EvenCrop(CropMath.Full(_width, _height), _width, _height);
        SyncBoxes();
        Layout();
        PaintChips();
    }

    private async Task<string?> ApplyAsync()
    {
        var info = _info;
        if (info is null)
            throw new ConversionException(T("tool.videocrop.probeFailed", Bidi.Value(_item.Name)));
        var ffmpeg = _preview.Ffmpeg
            ?? throw new ConversionException(T("tool.videocrop.noFfmpeg"));
        if (_busy)
            return null;

        var container = VideoEdit.OutputContainerFor(_item.Format);
        var directory = VideoOutput.DirectoryFor(_item, _targets);
        var name = VideoOutput.NameFor(_item, T("tool.videocrop.suffix"));
        var request = new VideoCropRequest(
            _item.Path,
            string.Empty,
            VideoEdit.EvenCrop(_rect, _width, _height),
            _width,
            _height,
            container,
            info.HasAudio,
            CopyAudio: true);

        SetBusy(true);
        try
        {
            return await VideoToolRunner.CropAsync(ffmpeg, request, directory, name, _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _status.Text = busy ? T("tool.videocrop.applyBusy") : string.Empty;
    }

    private void OnThemeChanged()
    {
        if (_root is null)
            return;
        var next = VideoPalette.Capture();
        var target = (DependencyObject?)Window.GetWindow(_root) ?? _root;
        VideoThemeTint.Apply(target, _palette, next);
        _palette = next;
    }
}
