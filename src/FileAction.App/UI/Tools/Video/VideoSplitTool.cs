using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FileAction.App.Shell;
using FileAction.Core;
using static FileAction.Core.Loc;
using WpfImage = System.Windows.Controls.Image;

namespace FileAction.App.UI.Tools.Video;

/// <summary>
/// Split one video: a frame preview, a timeline where clicking adds a split point (dragging moves it,
/// double-clicking removes it) and "N equal parts". Apply writes <c>&lt;name&gt; 01..NN</c> into a new
/// <c>&lt;name&gt; Parts</c> folder next to the source; the source itself is only read.
/// </summary>
internal sealed class VideoSplitTool : IToolWindowSpec
{
    public static readonly VideoSplitTool Instance = new();

    /// <summary>Title, bottom bar, timeline, controls and the part chips; the preview takes the rest.</summary>
    private static readonly ToolSpecLayout LayoutSpec = new(
        ChromeWidth: 36,
        ChromeHeight: 270,
        PreviewMinWidth: 560,
        PreviewMinHeight: 250,
        PreviewMaxWidth: 820,
        PreviewMaxHeight: 520,
        DefaultPreviewWidth: 684,
        DefaultPreviewHeight: 370);

    public string Id => "tools.videosplit";

    public string TitleKey => "tool.videosplit.title";

    /// <summary>Bottom-right button: the split verb, not the generic "Apply".</summary>
    public string PrimaryLabelKey => "tool.videosplit.primary";

    public ToolSpecLayout Layout => LayoutSpec;

    public bool Applies(IReadOnlyList<FileItem> items) =>
        items.Count == 1 && !items[0].IsDirectory && Formats.Video.Contains(items[0].Format);

    public ToolContent Create(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new VideoSplitView(items[0]).Build();
}

/// <summary>Registers the tool from its own file, so no shared list has to be edited.</summary>
internal static class VideoSplitToolRegistration
{
    [ModuleInitializer]
    internal static void Register() => ToolRegistry.Register(VideoSplitTool.Instance);
}

/// <summary>The split window's content: preview, timeline with markers, equal-parts controls, part chips.</summary>
internal sealed class VideoSplitView
{
    private const int PreviewWidth = 960;

    private readonly FileItem _item;
    private readonly ToolLocator _tools = new();
    private readonly TaskCompletionSource<MediaSize?> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<double> _points = [];
    private readonly List<Border> _markers = [];

    private readonly Grid _preview = new() { ClipToBounds = true };
    private readonly WpfImage _image = new() { Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };
    private readonly Border _placeholder = new() { CornerRadius = new CornerRadius(10) };
    private readonly Canvas _canvas = new();
    private readonly TextBlock _position = new();
    private readonly TextBlock _length = new();
    private readonly TextBox _partsBox = new();
    private readonly TextBlock _countLabel = new();
    private readonly WrapPanel _chips = new();
    private readonly TextBlock _status = new();
    private readonly ProgressBar _progress = new();

    private Grid? _root;
    private ThemeWatcher? _theme;
    private Border? _track;
    private Border? _removeButton;
    private string? _ffmpeg;
    private string? _problem;
    private double _duration;
    private int _selected = -1;
    private int _dragging = -1;
    private CancellationTokenSource? _frameCts;
    private CancellationTokenSource? _applyCts;
    private bool _busy;
    private bool _statusIsError;

    public VideoSplitView(FileItem item)
    {
        _item = item;
        _preview.Children.Add(_image);
        _preview.Children.Add(_placeholder);
        _canvas.SizeChanged += (_, _) => Paint();

        _partsBox.Width = 46;
        _partsBox.Text = VideoSplitJoin.MinParts.ToString(CultureInfo.InvariantCulture);
        _partsBox.Padding = new Thickness(6, 4, 6, 4);
        _partsBox.HorizontalContentAlignment = HorizontalAlignment.Center;
        _partsBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                MakeEqualParts();
        };

        _position.FontSize = 12;
        _length.FontSize = 12;
        _length.HorizontalAlignment = HorizontalAlignment.Right;
        _countLabel.FontSize = 12;
        _countLabel.VerticalAlignment = VerticalAlignment.Center;
        _countLabel.Margin = new Thickness(0, 0, 12, 0);
        _chips.Margin = new Thickness(0, 0, 0, 2);

        _status.FontSize = 11.5;
        _status.TextWrapping = TextWrapping.Wrap;
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.Margin = new Thickness(0, 0, 100, 0);
        _progress.Width = 90;
        _progress.Height = 4;
        _progress.Visibility = Visibility.Collapsed;
        _progress.HorizontalAlignment = HorizontalAlignment.Right;
        _progress.VerticalAlignment = VerticalAlignment.Center;

        ShowHint();
    }

    public ToolContent Build()
    {
        _root = new Grid();
        Populate();
        _theme = new ThemeWatcher(_root, Repaint);
        _root.Loaded += (_, _) => _theme.Start();
        _root.Unloaded += (_, _) =>
        {
            _theme?.Stop();
            Cancel();
        };
        return new ToolContent(_root, Reset, ApplyAsync, _ready.Task, ActivateAsync);
    }

    /// <summary>Rebuilds the view with the current palette; the model (points, selection) is untouched.</summary>
    private void Repaint()
    {
        Populate();
        if (_duration > 0)
        {
            Paint();
            RefreshParts();
        }
        UpdateLabels();
    }

    private void Populate()
    {
        if (_root is null)
            return;

        // The state-bearing controls move into a freshly themed layout instead of being re-created.
        VideoToolCommon.Detach(_image);
        VideoToolCommon.Detach(_placeholder);
        VideoToolCommon.Detach(_canvas);
        VideoToolCommon.Detach(_position);
        VideoToolCommon.Detach(_length);
        VideoToolCommon.Detach(_partsBox);
        VideoToolCommon.Detach(_countLabel);
        VideoToolCommon.Detach(_chips);
        VideoToolCommon.Detach(_status);
        VideoToolCommon.Detach(_progress);

        _root.Children.Clear();
        _root.RowDefinitions.Clear();
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        for (var index = 0; index < 4; index++)
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        PaintStaticSurfaces();

        var preview = BuildPreview();
        var timeline = BuildTimeline();
        var controls = BuildControls();
        var chips = BuildChips();
        var status = BuildStatus();
        Grid.SetRow(preview, 0);
        Grid.SetRow(timeline, 1);
        Grid.SetRow(controls, 2);
        Grid.SetRow(chips, 3);
        Grid.SetRow(status, 4);
        _root.Children.Add(preview);
        _root.Children.Add(timeline);
        _root.Children.Add(controls);
        _root.Children.Add(chips);
        _root.Children.Add(status);
    }

    /// <summary>Applies the current palette to the controls that outlive a rebuild (they hold old brushes).</summary>
    private void PaintStaticSurfaces()
    {
        _placeholder.Background = Theme.Card;
        _position.Foreground = Theme.Muted;
        _length.Foreground = Theme.Muted;
        _partsBox.Background = Theme.Card;
        _partsBox.Foreground = Theme.Text;
        _partsBox.BorderBrush = Theme.PanelBorder;
        _partsBox.BorderThickness = new Thickness(1);
        _countLabel.Foreground = Theme.Muted;
        _status.Foreground = _statusIsError ? Theme.Error : Theme.Muted;
        _progress.Foreground = Theme.Accent;
        _progress.Background = Theme.Track;
        RenderPlaceholder();
    }

    /// <summary>Reads the clip once, then shows its first frame; the window sizes itself from the video.</summary>
    public async Task ActivateAsync()
    {
        try
        {
            _ffmpeg = _tools.Require(ExternalTool.Ffmpeg);
            var info = await VideoToolCommon.ProbeAsync(_ffmpeg, _item.Path, _lifetime.Token);
            if (!info.IsUsable)
                throw new ConversionException(T("tool.videosplit.error.probe"));

            _duration = info.Duration;
            _placeholder.Visibility = Visibility.Collapsed;
            UpdateLabels();
            Paint();
            RefreshParts();
            _ready.TrySetResult(new MediaSize(info.Width, info.Height));
            QueueFrame(0);
        }
        catch (OperationCanceledException)
        {
            _ready.TrySetResult(null);
        }
        catch (Exception ex)
        {
            ShowProblem(ex.Message);
            _ready.TrySetResult(null);
        }
    }

    private FrameworkElement BuildPreview()
    {
        _preview.Margin = new Thickness(0, 2, 0, 8);
        return new Border
        {
            Background = Theme.Card,
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(6),
            Child = _preview,
        };
    }

    private FrameworkElement BuildTimeline()
    {
        var track = new Border
        {
            Height = 38,
            CornerRadius = new CornerRadius(9),
            Background = Theme.Track,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Cursor = Cursors.Cross,
            Focusable = true,
            Child = _canvas,
        };
        track.SizeChanged += (_, _) => Paint();
        track.MouseLeftButtonDown += TrackDown;
        track.MouseMove += TrackMove;
        track.MouseLeftButtonUp += TrackUp;
        track.LostMouseCapture += (_, _) => _dragging = -1;
        track.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete)
                RemoveSelected();
        };
        _track = track;

        var labels = new Grid { Margin = new Thickness(2, 4, 2, 0) };
        labels.Children.Add(_position);
        labels.Children.Add(_length);

        var block = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        block.Children.Add(track);
        block.Children.Add(labels);
        return block;
    }

    private FrameworkElement BuildControls()
    {
        var make = Controls.Button(T("tool.videosplit.makeEqual"), MakeEqualParts);
        make.Margin = new Thickness(8, 0, 0, 0);

        var start = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        start.Children.Add(new TextBlock
        {
            Text = T("tool.videosplit.equalParts"),
            FontSize = 12.5,
            Foreground = Theme.Muted,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        start.Children.Add(_partsBox);
        start.Children.Add(make);

        var remove = Controls.Button(T("tool.videosplit.removePoint"), RemoveSelected);
        remove.Opacity = _selected >= 0 ? 1 : 0.45;
        _removeButton = remove;

        var end = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        end.Children.Add(_countLabel);
        end.Children.Add(remove);

        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.Children.Add(start);
        row.Children.Add(end);
        return row;
    }

    private FrameworkElement BuildChips() => new ScrollViewer
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        MaxHeight = 46,
        Content = _chips,
    };

    private FrameworkElement BuildStatus()
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        row.Children.Add(_status);
        row.Children.Add(_progress);
        return row;
    }

    // ---------------------------------------------------------------- timeline

    private void Paint()
    {
        _canvas.Children.Clear();
        _markers.Clear();
        var width = _canvas.ActualWidth;
        var height = _canvas.ActualHeight;
        if (_duration <= 0 || width <= 1 || height <= 1)
            return;

        // Alternate the parts so the timeline reads as a set of pieces, not one long bar.
        var segments = VideoSplitJoin.Ranges(_duration, _points);
        for (var index = 1; index < segments.Count; index += 2)
        {
            var left = Position(segments[index].Start, width);
            var right = Position(segments[index].End, width);
            var shade = new Border
            {
                Background = Theme.AccentSoft,
                CornerRadius = new CornerRadius(6),
                Height = Math.Max(0, height - 10),
                IsHitTestVisible = false,
            };
            shade.Width = Math.Max(0, right - left);
            Canvas.SetLeft(shade, left);
            Canvas.SetTop(shade, 5);
            _canvas.Children.Add(shade);
        }

        for (var index = 0; index < _points.Count; index++)
        {
            var handle = new Border
            {
                Width = 13,
                Height = height,
                Background = VideoPalette.Transparent,
                Cursor = Cursors.SizeWE,
                Tag = index,
                ToolTip = T("tool.videosplit.pointAt", Bidi.Value(VideoToolCommon.Time(_points[index]))),
            };
            handle.Child = new Border
            {
                Width = 3,
                Height = Math.Max(0, height - 8),
                CornerRadius = new CornerRadius(2),
                Background = index == _selected ? Theme.Accent : Theme.Text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Canvas.SetLeft(handle, Math.Clamp(Position(_points[index], width) - handle.Width / 2, 0, Math.Max(0, width - handle.Width)));
            Canvas.SetTop(handle, 0);
            handle.MouseLeftButtonDown += MarkerDown;
            _markers.Add(handle);
            _canvas.Children.Add(handle);
        }
    }

    private void TrackDown(object sender, MouseButtonEventArgs e)
    {
        if (_busy || _duration <= 0 || e.ChangedButton != MouseButton.Left)
            return;
        if (sender is Border track)
            track.Focus();
        var seconds = TimeAt(e.GetPosition(_canvas));
        var near = Nearest(seconds);
        if (near >= 0)
        {
            Select(near);
            return;
        }
        AddPoint(seconds);
    }

    private void MarkerDown(object sender, MouseButtonEventArgs e)
    {
        if (_busy || sender is not Border handle || handle.Tag is not int index)
            return;
        e.Handled = true;
        if (e.ClickCount >= 2)
        {
            RemovePoint(index);
            return;
        }
        _dragging = index;
        Select(index);
        _track?.CaptureMouse();
    }

    private void TrackMove(object sender, MouseEventArgs e)
    {
        if (_dragging < 0 || _duration <= 0 || e.LeftButton != MouseButtonState.Pressed)
            return;

        var seconds = VideoSplitJoin.RoundMs(TimeAt(e.GetPosition(_canvas)));
        var minimum = _dragging > 0 ? _points[_dragging - 1] + VideoSplitJoin.MinSegment : VideoSplitJoin.MinSegment;
        var maximum = _dragging < _points.Count - 1
            ? _points[_dragging + 1] - VideoSplitJoin.MinSegment
            : _duration - VideoSplitJoin.MinSegment;
        var point = Math.Clamp(seconds, minimum, Math.Max(minimum, maximum));
        if (Math.Abs(point - _points[_dragging]) < 0.001)
            return;

        _points[_dragging] = point;
        Paint();
        UpdateLabels();
        QueueFrame(point);
    }

    private void TrackUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging < 0)
            return;
        var point = _points[_dragging];
        _dragging = -1;
        if (sender is Border track && track.IsMouseCaptured)
            track.ReleaseMouseCapture();
        Paint();
        UpdateLabels();
        RefreshParts();
        QueueFrame(point);
    }

    private void AddPoint(double seconds)
    {
        if (_points.Count >= VideoSplitJoin.MaxParts - 1)
        {
            ShowError(T("tool.videosplit.error.count", Bidi.Number(VideoSplitJoin.MaxParts)));
            return;
        }

        var point = VideoSplitJoin.RoundMs(Math.Clamp(seconds, 0, _duration));
        if (point <= VideoSplitJoin.MinSegment || point >= _duration - VideoSplitJoin.MinSegment)
            return;
        foreach (var existing in _points)
        {
            if (Math.Abs(existing - point) < VideoSplitJoin.MinSegment)
                return;
        }

        var index = 0;
        while (index < _points.Count && _points[index] < point)
            index++;
        _points.Insert(index, point);
        Select(index);
        RefreshParts();
    }

    private void RemoveSelected()
    {
        if (_selected >= 0)
            RemovePoint(_selected);
    }

    private void RemovePoint(int index)
    {
        if (index < 0 || index >= _points.Count)
            return;
        _points.RemoveAt(index);
        _selected = -1;
        _dragging = -1;
        if (_track?.IsMouseCaptured == true)
            _track.ReleaseMouseCapture();
        UpdateRemoveButton();
        Paint();
        UpdateLabels();
        RefreshParts();
        QueueFrame(0);
    }

    private void Select(int index)
    {
        _selected = index >= 0 && index < _points.Count ? index : -1;
        UpdateRemoveButton();
        Paint();
        UpdateLabels();
        QueueFrame(_selected >= 0 ? _points[_selected] : 0);
    }

    private void UpdateRemoveButton()
    {
        if (_removeButton is not null)
            _removeButton.Opacity = _selected >= 0 ? 1 : 0.45;
    }

    private void MakeEqualParts()
    {
        if (_busy || _duration <= 0)
            return;

        if (!int.TryParse(_partsBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            || count < VideoSplitJoin.MinParts
            || count > VideoSplitJoin.MaxParts)
        {
            ShowError(T("tool.videosplit.error.count", Bidi.Number(VideoSplitJoin.MaxParts)));
            return;
        }

        _points.Clear();
        _points.AddRange(VideoSplitJoin.PointsForEqualParts(_duration, count));
        _selected = _points.Count > 0 ? 0 : -1;
        UpdateRemoveButton();
        ShowHint();
        Paint();
        UpdateLabels();
        RefreshParts();
        QueueFrame(_selected >= 0 ? _points[0] : 0);
    }

    private void QueueFrame(double seconds)
    {
        _frameCts?.Cancel();
        _frameCts?.Dispose();
        var cts = new CancellationTokenSource();
        _frameCts = cts;
        _ = ShowFrameAsync(seconds, cts.Token);
    }

    private async Task ShowFrameAsync(double seconds, CancellationToken ct)
    {
        if (_ffmpeg is null)
            return;
        try
        {
            // A short pause coalesces the flood of frames a marker drag would otherwise request.
            await Task.Delay(140, ct);
            var bitmap = await VideoToolCommon.FrameAsync(_ffmpeg, _item.Path, seconds, PreviewWidth, ct);
            if (bitmap is null || ct.IsCancellationRequested)
                return;
            _image.Source = bitmap;
            _image.Visibility = Visibility.Visible;
            _placeholder.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer position.
        }
        catch (Exception)
        {
            // The frame is a preview; Apply reports the real failures.
        }
    }

    private double Position(double seconds, double width) =>
        Math.Clamp(seconds / _duration, 0, 1) * width;

    private double TimeAt(Point point)
    {
        var width = Math.Max(1, _canvas.ActualWidth);
        return Math.Clamp(point.X / width, 0, 1) * _duration;
    }

    private int Nearest(double seconds)
    {
        var width = _canvas.ActualWidth;
        if (_duration <= 0 || width <= 1)
            return -1;
        var tolerance = 10 / width * _duration;
        for (var index = 0; index < _points.Count; index++)
        {
            if (Math.Abs(_points[index] - seconds) <= tolerance)
                return index;
        }
        return -1;
    }

    // ---------------------------------------------------------------- labels, status and apply

    private void UpdateLabels()
    {
        _length.Text = _duration > 0
            ? T("tool.videosplit.duration", Bidi.Value(VideoToolCommon.Time(_duration)))
            : string.Empty;
        _position.Text = _selected >= 0 && _selected < _points.Count
            ? T("tool.videosplit.pointAt", Bidi.Value(VideoToolCommon.Time(_points[_selected])))
            : string.Empty;
    }

    private void RefreshParts()
    {
        _chips.Children.Clear();
        if (_duration <= 0)
        {
            _countLabel.Text = string.Empty;
            return;
        }

        var segments = VideoSplitJoin.Ranges(_duration, _points);
        _countLabel.Text = T("tool.videosplit.parts", Bidi.Number(segments.Count));
        for (var index = 0; index < segments.Count; index++)
        {
            var range = Bidi.Value(
                $"{VideoToolCommon.Time(segments[index].Start)} – {VideoToolCommon.Time(segments[index].End)}");
            _chips.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(9, 3, 9, 4),
                Margin = new Thickness(0, 0, 6, 4),
                Background = Theme.Card,
                BorderBrush = Theme.PanelBorder,
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = T("tool.videosplit.segment", Bidi.Number(index + 1), range),
                    FontSize = 11.5,
                    Foreground = Theme.Text,
                },
            });
        }
    }

    private void ShowProblem(string message)
    {
        _problem = message;
        _placeholder.Visibility = Visibility.Visible;
        RenderPlaceholder();
        ShowError(message);
    }

    /// <summary>The progress bar, or the reason the clip could not be read.</summary>
    private void RenderPlaceholder()
    {
        _placeholder.Child = _problem is { } problem
            ? new TextBlock
            {
                Text = problem,
                Foreground = Theme.Error,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(18),
            }
            : new ProgressBar
            {
                IsIndeterminate = true,
                Width = 120,
                Height = 4,
                Foreground = Theme.Accent,
                Background = Theme.Track,
            };
    }

    private void ShowHint()
    {
        _statusIsError = false;
        _status.Foreground = Theme.Muted;
        _status.Text = T("tool.videosplit.hint");
    }

    private void ShowError(string message)
    {
        _statusIsError = true;
        _status.Foreground = Theme.Error;
        _status.Text = message;
    }

    private void ShowProgress(double fraction)
    {
        var value = Math.Clamp(fraction, 0, 1);
        _progress.Visibility = Visibility.Visible;
        _progress.Value = value * 100;
        _statusIsError = false;
        _status.Foreground = Theme.Muted;
        _status.Text = T("tool.videosplit.working") + " "
            + Bidi.Value(Math.Round(value * 100).ToString(CultureInfo.InvariantCulture) + "%");
    }

    private void Reset()
    {
        if (_busy)
            return;
        _points.Clear();
        _selected = -1;
        _dragging = -1;
        _partsBox.Text = VideoSplitJoin.MinParts.ToString(CultureInfo.InvariantCulture);
        UpdateRemoveButton();
        ShowHint();
        _progress.Visibility = Visibility.Collapsed;
        UpdateLabels();
        Paint();
        RefreshParts();
        QueueFrame(0);
    }

    private async Task<string?> ApplyAsync()
    {
        // The window is already interactive while the clip is probed, so Apply waits for that probe.
        await _ready.Task;
        if (_busy)
            return null;
        if (_duration <= 0 || _ffmpeg is null)
            throw new ConversionException(T("tool.videosplit.error.probe"));

        var plan = VideoSplitJoin.Plan(_item.Path, T("tool.videosplit.folderSuffix"), _duration, _points);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _applyCts = cts;
        _busy = true;
        ShowProgress(0);

        try
        {
            var progress = new Progress<double>(ShowProgress);
            var folder = await VideoSplitRunner.RunAsync(_ffmpeg, plan, progress, cts.Token);
            Notifications.Show(
                T("tool.videosplit.done.title"),
                T("tool.videosplit.done", Bidi.Value(Path.GetFileName(folder))));
            // The view shows its own toast, so the shared window does not repeat a single file name.
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            _busy = false;
            _applyCts = null;
            cts.Dispose();
            _progress.Visibility = Visibility.Collapsed;
            if (_duration > 0 && !_statusIsError)
                ShowHint();
        }
    }

    private void Cancel()
    {
        _lifetime.Cancel();
        _frameCts?.Cancel();
        _applyCts?.Cancel();
    }
}
