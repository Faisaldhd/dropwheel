using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FileAction.Core;
using static FileAction.Core.Loc;
using WpfImage = System.Windows.Controls.Image;

namespace FileAction.App.UI.Tools.Video;

/// <summary>
/// Redact video: scrub to a frame, drag a rectangle over what must be hidden, give it a time range and
/// choose a solid colour or a blur. Apply builds one FFmpeg filter graph (drawbox / boxblur with a
/// between() gate per area) and writes "&lt;name&gt; Redacted.&lt;ext&gt;" next to the source.
/// </summary>
internal sealed class VideoRedactTool : IToolWindowSpec
{
    public static readonly VideoRedactTool Instance = new();

    public string Id => "tools.videoredact";

    public string TitleKey => "tool.videoredact.title";

    public string PrimaryLabelKey => "tool.videoredact.primary";

    public ToolSpecLayout Layout => LayoutSpec;

    public bool Applies(IReadOnlyList<FileItem> items) =>
        items.Count == 1 && !items[0].IsDirectory && Formats.Video.Contains(items[0].Format);

    public ToolContent Create(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new VideoRedactView(items[0], targets).Build();

    /// <summary>The extra time/colour editor row makes this window slightly taller than Save frames.</summary>
    internal static readonly ToolSpecLayout LayoutSpec = new(
        ChromeWidth: 36,
        ChromeHeight: 330,
        PreviewMinWidth: 560,
        PreviewMinHeight: 315,
        PreviewMaxWidth: 684,
        PreviewMaxHeight: 385,
        DefaultPreviewWidth: 684,
        DefaultPreviewHeight: 385);
}

/// <summary>Registers the tool from its own file; no shared list is edited.</summary>
internal static class VideoRedactRegistration
{
    [ModuleInitializer]
    internal static void Register() => ToolRegistry.Register(VideoRedactTool.Instance);
}

/// <summary>The wheel operation. Areas are drawn and timed before Apply writes anything.</summary>
internal sealed class VideoRedactOperation : Operation, IInteractiveTool
{
    public VideoRedactOperation()
        : base("tools.videoredact", WheelKind.Tools, T("tool.videoredact.label"), Glyphs.Clear, T("tool.videoredact.subtitle"))
    {
        Requires = [ExternalTool.Ffmpeg];
    }

    public override bool AppliesTo(IReadOnlyList<FileItem> items) => VideoRedactTool.Instance.Applies(items);

    public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
        Task.FromResult(OperationResult.Of([]));

    public void Open(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new ToolWindow(VideoRedactTool.Instance, items, targets).Show();
}

/// <summary>The redaction window: scrub strip, drawable preview, area editor and the area list.</summary>
internal sealed class VideoRedactView
{
    private const int PreviewDelayMilliseconds = 70;

    /// <summary>The drawn size of a resize handle, and the pointer distance that hits it.</summary>
    private const double HandleSize = 10;
    private const double HandleHitRadius = 8;

    /// <summary>A new area must be at least this large on screen so a click is not an accidental box.</summary>
    private const double MinimumDraftDips = 8;

    private static readonly RedactionHandle[] Handles =
    [
        RedactionHandle.TopLeft,
        RedactionHandle.Top,
        RedactionHandle.TopRight,
        RedactionHandle.Right,
        RedactionHandle.BottomRight,
        RedactionHandle.Bottom,
        RedactionHandle.BottomLeft,
        RedactionHandle.Left,
    ];

    private readonly FileItem _item;
    private readonly DropTargets? _targets;
    private readonly ToolLocator _tools = new();
    private readonly TaskCompletionSource<MediaSize?> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<VideoRedactionArea> _areas = [];

    private readonly Grid _preview = new() { ClipToBounds = true };
    private readonly Canvas _canvas = new() { Background = VideoPalette.Transparent, ClipToBounds = true };
    private readonly WpfImage _image = new() { Stretch = Stretch.Uniform, IsHitTestVisible = false };
    private readonly Border _placeholder;
    private readonly ProgressBar _spinner = new() { IsIndeterminate = true, Width = 120, Height = 4 };
    private readonly TextBlock _placeholderText = Controls.Text("", 12, brush: Theme.Muted);
    private readonly TextBlock _position = Controls.Text("", 12.5, brush: Theme.Muted, wrap: false);
    private readonly TextBlock _status = Controls.Text("", 11.5, brush: Theme.Muted, wrap: false);
    private readonly TextBlock _header = Controls.Text("", 12, FontWeights.SemiBold);
    private readonly TextBlock _emptyHint = Controls.Text("", 11.5, brush: Theme.Muted);
    private readonly WrapPanel _chips = new();
    private readonly VideoScrubber _scrubber = new();
    private readonly Border _cancel = Controls.Button("", () => { });
    private readonly Border _remove = Controls.Button("", () => { });
    private readonly TextBox _startBox = new();
    private readonly TextBox _endBox = new();
    private readonly Rectangle _dragRectangle = new()
    {
        Stroke = Theme.Accent,
        StrokeThickness = 2,
        Fill = VideoPalette.Tint(Theme.AccentColor, 0x22),
        Visibility = Visibility.Collapsed,
        IsHitTestVisible = false,
    };
    private readonly List<Rectangle> _areaShapes = [];
    private readonly List<Border> _handles = [];
    private PillChoice<VideoRedactionStyle> _styleChoice = null!;
    private ColorChoice _colorChoice = null!;
    private Border? _previewCard;
    private Border? _previousButton;
    private Border? _nextButton;
    private VideoGateGrid? _root;
    private VideoThemeHook? _theme;

    private string? _ffmpeg;
    private VideoInfo? _info;
    private int _total = 1;
    private int _frame;
    private int _selected = -1;
    private VideoRedactionStyle _style = VideoRedactionStyle.Solid;
    private string _color = "black";
    private double _scale = 1;
    private double _originX;
    private double _originY;
    private DragKind _dragKind;
    private VideoRedactionArea? _dragArea;
    private RedactionHandle _dragHandle;
    private Point _dragOrigin;
    private RedactionBox? _draftBox;
    private bool _syncing;
    private CancellationTokenSource? _seek;
    private CancellationTokenSource? _apply;

    public VideoRedactView(FileItem item, DropTargets? targets)
    {
        _item = item;
        _targets = targets;
        _placeholder = new Border
        {
            Background = Theme.Card,
            CornerRadius = new CornerRadius(10),
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    _spinner,
                    _placeholderText,
                },
            },
        };
        _placeholderText.Margin = new Thickness(0, 10, 0, 0);
        _placeholderText.Text = T("tool.videoredact.loading");
        _placeholderText.TextAlignment = TextAlignment.Center;

        foreach (var box in new[] { _startBox, _endBox })
        {
            box.Width = 92;
            box.Padding = new Thickness(8, 6, 8, 6);
            box.Background = Theme.Card;
            box.Foreground = Theme.Text;
            box.BorderBrush = Theme.PanelBorder;
            box.BorderThickness = new Thickness(1);
            box.HorizontalContentAlignment = HorizontalAlignment.Center;
            box.Margin = new Thickness(0, 0, 10, 0);
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                    CommitTimes();
            };
            box.LostFocus += (_, _) => CommitTimes();
            box.IsEnabled = false;
        }

        _scrubber.Scrub += frame =>
        {
            _frame = Math.Clamp(frame, 0, _total - 1);
            UpdatePosition();
            _ = ShowFrameAsync(_frame, immediate: false);
        };
        _scrubber.Committed += () => _ = ShowFrameAsync(_frame, immediate: true);
    }

    public ToolContent Build()
    {
        var root = new VideoGateGrid();
        _root = root;
        for (var i = 0; i < 6; i++)
            root.RowDefinitions.Add(new RowDefinition { Height = i == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });

        var preview = BuildPreview();
        var tools = BuildToolsRow();
        var editor = BuildEditorRow();
        var chips = BuildAreas();
        var position = BuildPositionRow();

        Grid.SetRow(preview, 0);
        Grid.SetRow(position, 1);
        Grid.SetRow(_scrubber, 2);
        Grid.SetRow(tools, 3);
        Grid.SetRow(editor, 4);
        Grid.SetRow(chips, 5);
        root.Children.Add(preview);
        root.Children.Add(position);
        root.Children.Add(_scrubber);
        root.Children.Add(tools);
        root.Children.Add(editor);
        root.Children.Add(chips);

        _scrubber.Margin = new Thickness(2, 0, 2, 6);
        _position.Margin = new Thickness(2, 0, 0, 2);
        _position.TextTrimming = TextTrimming.CharacterEllipsis;

        _theme = new VideoThemeHook(root, RepaintTheme);
        root.Unloaded += (_, _) =>
        {
            _theme?.Dispose();
            _theme = null;
            _seek?.Cancel();
            _apply?.Cancel();
        };

        return new ToolContent(root, Reset, ApplyAsync, _ready.Task, ActivateAsync);
    }

    /// <summary>Probes the clip and shows its first frame; the window then sizes itself around the media.</summary>
    public async Task ActivateAsync()
    {
        try
        {
            _ffmpeg = _tools.Find(ExternalTool.Ffmpeg);
            if (_ffmpeg is null)
            {
                ShowMessage(T("tool.videoredact.needsFfmpeg"));
                _ready.TrySetResult(null);
                return;
            }

            var info = await VideoProbe.ReadAsync(_ffmpeg, _item.Path, CancellationToken.None);
            if (info is null)
            {
                ShowMessage(T("tool.videoredact.unreadable"));
                _ready.TrySetResult(null);
                return;
            }

            _info = info;
            _total = info.TotalFrames;
            _scrubber.Maximum = Math.Max(0, _total - 1);
            _frame = 0;
            _scrubber.SetValue(0);
            UpdatePosition();
            UpdateHeader();
            UpdateEditor();
            PaintAreas();
            PaintChips();
            _ready.TrySetResult(new MediaSize(info.Width, info.Height));

            _spinner.Visibility = Visibility.Visible;
            _placeholder.Visibility = Visibility.Visible;
            _placeholderText.Text = T("tool.videoredact.loading");
            await ShowFrameAsync(0, immediate: true);
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message);
            _ready.TrySetResult(null);
        }
    }

    private FrameworkElement BuildPreview()
    {
        _image.Visibility = Visibility.Collapsed;
        // Area coordinates are original pixels measured from the left; keep the overlay in that frame
        // even when the window itself flows right to left.
        _canvas.FlowDirection = FlowDirection.LeftToRight;
        Panel.SetZIndex(_dragRectangle, 20);
        _canvas.Children.Add(_dragRectangle);
        foreach (var _ in Handles)
        {
            var handle = new Border
            {
                Width = HandleSize,
                Height = HandleSize,
                CornerRadius = new CornerRadius(2),
                Background = Theme.Accent,
                BorderBrush = Theme.TextOnAccent,
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
            };
            Panel.SetZIndex(handle, 15);
            _handles.Add(handle);
            _canvas.Children.Add(handle);
        }
        _canvas.MouseLeftButtonDown += BeginDraw;
        _canvas.MouseMove += ContinueDraw;
        _canvas.MouseLeftButtonUp += EndDraw;
        _canvas.LostMouseCapture += (_, _) => CancelDrag();

        _preview.Children.Add(_image);
        _preview.Children.Add(_canvas);
        _preview.Children.Add(_placeholder);
        _preview.SizeChanged += (_, _) => LayoutOverlay();
        MediaSurface.Round(_preview);

        _previewCard = new Border
        {
            Background = Theme.Card,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 4, 0, 8),
            Child = _preview,
        };
        return _previewCard;
    }

    private FrameworkElement BuildPositionRow()
    {
        _status.TextTrimming = TextTrimming.CharacterEllipsis;
        _status.HorizontalAlignment = HorizontalAlignment.Right;
        _status.MaxWidth = 340;

        var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 2, 2) };
        DockPanel.SetDock(_status, Dock.Right);
        row.Children.Add(_status);
        row.Children.Add(_position);
        return row;
    }

    private FrameworkElement BuildToolsRow()
    {
        var previous = Controls.Button(T("tool.videoredact.previous"), () => SeekTo(_frame - 1), glyph: "\uE76B");
        previous.Margin = new Thickness(0, 0, 6, 0);
        var next = Controls.Button(T("tool.videoredact.next"), () => SeekTo(_frame + 1), glyph: "\uE76C");
        _previousButton = previous;
        _nextButton = next;
        var nav = new StackPanel { Orientation = Orientation.Horizontal };
        nav.Children.Add(previous);
        nav.Children.Add(next);

        _styleChoice = new PillChoice<VideoRedactionStyle>(
            [
                (T("tool.videoredact.solid"), VideoRedactionStyle.Solid),
                (T("tool.videoredact.blur"), VideoRedactionStyle.Blur),
            ],
            _style);
        _styleChoice.Margin = new Thickness(0, 0, 8, 0);
        _styleChoice.Changed += style =>
        {
            _style = style;
            if (_selected >= 0)
                ReplaceSelected(area => area with { Style = style });
        };

        _colorChoice = new ColorChoice(_color);
        _colorChoice.Margin = new Thickness(0, 0, 8, 0);
        _colorChoice.Changed += color =>
        {
            _color = color;
            if (_selected >= 0)
                ReplaceSelected(area => area with { Color = color });
        };

        Controls.SetText(_cancel, T("tool.videoredact.cancel"));
        _cancel.Padding = new Thickness(10, 4, 10, 5);
        _cancel.Margin = new Thickness(6, 0, 0, 0);
        _cancel.Visibility = Visibility.Collapsed;
        _cancel.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _apply?.Cancel();
        };

        var row = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(nav, Dock.Left);
        DockPanel.SetDock(_styleChoice, Dock.Right);
        DockPanel.SetDock(_colorChoice, Dock.Right);
        row.Children.Add(nav);
        row.Children.Add(_colorChoice);
        row.Children.Add(_styleChoice);
        return row;
    }

    private FrameworkElement BuildEditorRow()
    {
        var startLabel = Controls.Text(T("tool.videoredact.start"), 12, brush: Theme.Muted, wrap: false);
        startLabel.VerticalAlignment = VerticalAlignment.Center;
        startLabel.Margin = new Thickness(0, 0, 8, 0);

        var endLabel = Controls.Text(T("tool.videoredact.end"), 12, brush: Theme.Muted, wrap: false);
        endLabel.VerticalAlignment = VerticalAlignment.Center;
        endLabel.Margin = new Thickness(0, 0, 8, 0);

        Controls.SetText(_remove, T("tool.videoredact.remove"));
        _remove.Padding = new Thickness(10, 4, 10, 5);
        _remove.VerticalAlignment = VerticalAlignment.Center;
        _remove.Opacity = 0.45;
        _remove.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            RemoveSelected();
        };

        var fields = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { startLabel, _startBox, endLabel, _endBox, _remove, _cancel },
        };

        _header.VerticalAlignment = VerticalAlignment.Center;
        _header.HorizontalAlignment = HorizontalAlignment.Right;

        var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 2, 6) };
        DockPanel.SetDock(_header, Dock.Right);
        row.Children.Add(_header);
        row.Children.Add(fields);
        return row;
    }

    private FrameworkElement BuildAreas()
    {
        _emptyHint.Text = T("tool.videoredact.empty");
        _chips.Margin = new Thickness(0, 0, 0, 2);
        PaintChips();
        return new ScrollViewer
        {
            MaxHeight = 64,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _chips,
        };
    }

    // ---------------------------------------------------------------- preview layout

    private void LayoutOverlay()
    {
        if (_info is null)
            return;

        var width = Math.Max(1, _preview.ActualWidth);
        var height = Math.Max(1, _preview.ActualHeight);
        _scale = Math.Min(width / _info.Width, height / _info.Height);
        if (!double.IsFinite(_scale) || _scale <= 0)
            _scale = 1;
        _originX = (width - _info.Width * _scale) / 2;
        _originY = (height - _info.Height * _scale) / 2;
        PaintAreas();
        PaintDraft();
    }

    private Rect ImageBounds() => new(
        _originX,
        _originY,
        Math.Max(1, (_info?.Width ?? 1) * _scale),
        Math.Max(1, (_info?.Height ?? 1) * _scale));

    private Point ToImage(Point point) => new(
        (point.X - _originX) / Math.Max(_scale, 0.0001),
        (point.Y - _originY) / Math.Max(_scale, 0.0001));

    /// <summary>
    /// Pressing an existing area selects it and starts a move; pressing one of the selected area's handles
    /// resizes it; pressing empty image starts a new box. Clicking outside the picture clears the selection.
    /// </summary>
    private void BeginDraw(object sender, MouseButtonEventArgs e)
    {
        if (_info is null || e.ChangedButton != MouseButton.Left)
            return;

        var position = e.GetPosition(_canvas);
        if (!ImageBounds().Contains(position))
        {
            if (_selected >= 0)
                Select(-1);
            return;
        }

        var point = ToImage(position);
        var selected = SelectedArea();
        var tolerance = HandleHitRadius / Math.Max(_scale, 0.0001);
        var handle = selected is null
            ? RedactionHandle.None
            : RedactionEditor.HandleAt(VideoRedactionEditor.Box(selected), point.X, point.Y, tolerance);

        if (selected is not null && handle != RedactionHandle.None)
        {
            _dragKind = DragKind.Resize;
            _dragHandle = handle;
            _dragArea = selected;
            _dragOrigin = point;
            _canvas.Cursor = CursorFor(handle);
        }
        else
        {
            var hit = VideoRedactionEditor.HitTest(_areas, point.X, point.Y);
            if (hit >= 0)
            {
                Select(hit);
                _dragKind = DragKind.Move;
                _dragArea = _areas[_selected];
                _dragOrigin = point;
                _canvas.Cursor = Cursors.SizeAll;
            }
            else
            {
                if (_selected >= 0)
                    Select(-1);
                _dragKind = DragKind.New;
                _dragOrigin = point;
                _draftBox = null;
                _canvas.Cursor = Cursors.Cross;
            }
        }

        _canvas.CaptureMouse();
        e.Handled = true;
    }

    private void ContinueDraw(object sender, MouseEventArgs e)
    {
        if (_info is null)
            return;

        var point = ToImage(e.GetPosition(_canvas));
        if (_dragKind == DragKind.None)
        {
            UpdateCursor(point);
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            FinishDrag();
            return;
        }

        var dx = point.X - _dragOrigin.X;
        var dy = point.Y - _dragOrigin.Y;
        switch (_dragKind)
        {
            case DragKind.Move when _dragArea is not null && SelectedArea() is not null:
                _areas[_selected] = VideoRedactionEditor.Move(
                    _dragArea, dx, dy, _info.Width, _info.Height);
                PaintAreas();
                break;

            case DragKind.Resize when _dragArea is not null && SelectedArea() is not null:
                _areas[_selected] = VideoRedactionEditor.Resize(
                    _dragArea, _dragHandle, dx, dy, _info.Width, _info.Height);
                PaintAreas();
                break;

            case DragKind.New:
                _draftBox = RedactionEditor.Create(
                    _dragOrigin.X, _dragOrigin.Y, point.X, point.Y, _info.Width, _info.Height);
                PaintDraft();
                break;
        }
    }

    private void EndDraw(object sender, MouseButtonEventArgs e)
    {
        if (_dragKind == DragKind.None)
            return;
        FinishDrag();
        e.Handled = true;
    }

    private void FinishDrag()
    {
        var kind = _dragKind;
        var draft = _draftBox;
        _dragKind = DragKind.None;
        _dragArea = null;
        _draftBox = null;
        _dragRectangle.Visibility = Visibility.Collapsed;
        if (_canvas.IsMouseCaptured)
            _canvas.ReleaseMouseCapture();

        if (kind == DragKind.New)
        {
            if (draft is { } box && _info is not null &&
                box.Width * _scale >= MinimumDraftDips && box.Height * _scale >= MinimumDraftDips)
            {
                var endSeconds = _info.DurationSeconds > 0 ? _info.DurationSeconds : 1;
                _areas.Add(VideoRedactionEditor.Create(
                    box, 0, endSeconds, _style, _color, _info.Width, _info.Height));
                Select(_areas.Count - 1);
            }
            return;
        }

        if (kind is DragKind.Move or DragKind.Resize)
        {
            PaintAreas();
            PaintChips();
            UpdateEditor();
            UpdateHeader();
        }
    }

    /// <summary>Drops the gesture without undoing the pixels already moved; used when capture is lost.</summary>
    private void CancelDrag()
    {
        if (_dragKind == DragKind.None)
            return;
        _dragKind = DragKind.None;
        _dragArea = null;
        _draftBox = null;
        _dragRectangle.Visibility = Visibility.Collapsed;
    }

    private void PaintDraft()
    {
        if (_draftBox is not { } box || box.IsEmpty)
        {
            _dragRectangle.Visibility = Visibility.Collapsed;
            return;
        }

        _dragRectangle.Visibility = Visibility.Visible;
        Canvas.SetLeft(_dragRectangle, _originX + box.X * _scale);
        Canvas.SetTop(_dragRectangle, _originY + box.Y * _scale);
        _dragRectangle.Width = Math.Max(1, box.Width * _scale);
        _dragRectangle.Height = Math.Max(1, box.Height * _scale);
    }

    private void UpdateCursor(Point point)
    {
        var selected = SelectedArea();
        if (selected is not null)
        {
            var tolerance = HandleHitRadius / Math.Max(_scale, 0.0001);
            var handle = RedactionEditor.HandleAt(
                VideoRedactionEditor.Box(selected), point.X, point.Y, tolerance);
            if (handle != RedactionHandle.None)
            {
                _canvas.Cursor = CursorFor(handle);
                return;
            }
        }

        _canvas.Cursor = VideoRedactionEditor.HitTest(_areas, point.X, point.Y) >= 0
            ? Cursors.SizeAll
            : Cursors.Cross;
    }

    private static Cursor CursorFor(RedactionHandle handle) => handle switch
    {
        RedactionHandle.TopLeft or RedactionHandle.BottomRight => Cursors.SizeNWSE,
        RedactionHandle.TopRight or RedactionHandle.BottomLeft => Cursors.SizeNESW,
        RedactionHandle.Top or RedactionHandle.Bottom => Cursors.SizeNS,
        RedactionHandle.Left or RedactionHandle.Right => Cursors.SizeWE,
        _ => Cursors.Cross,
    };

    private void PaintAreas()
    {
        foreach (var shape in _areaShapes)
            _canvas.Children.Remove(shape);
        _areaShapes.Clear();
        if (_info is null)
            return;

        for (var i = 0; i < _areas.Count; i++)
        {
            var area = _areas[i];
            var selected = i == _selected;
            var fill = area.Style == VideoRedactionStyle.Blur
                ? Theme.AccentColor
                : VideoPalette.PreviewFill(area.Color);
            var rectangle = new Rectangle
            {
                Fill = VideoPalette.Tint(fill, area.Style == VideoRedactionStyle.Blur ? (byte)0x44 : (byte)0x66),
                Stroke = selected ? Theme.Accent : Theme.Muted,
                StrokeThickness = selected ? 2 : 1,
                IsHitTestVisible = false,
            };
            rectangle.ToolTip = $"#{i + 1}  {FrameMath.ShortTimecode(area.StartSeconds)}–{FrameMath.ShortTimecode(area.EndSeconds)}";
            Canvas.SetLeft(rectangle, _originX + area.X * _scale);
            Canvas.SetTop(rectangle, _originY + area.Y * _scale);
            rectangle.Width = Math.Max(1, area.Width * _scale);
            rectangle.Height = Math.Max(1, area.Height * _scale);
            Panel.SetZIndex(rectangle, 10);
            _canvas.Children.Add(rectangle);
            _areaShapes.Add(rectangle);
        }

        var selectedArea = SelectedArea();
        for (var i = 0; i < _handles.Count && i < Handles.Length; i++)
        {
            var handle = _handles[i];
            if (selectedArea is null)
            {
                handle.Visibility = Visibility.Collapsed;
                continue;
            }

            var (x, y) = RedactionEditor.HandlePoint(VideoRedactionEditor.Box(selectedArea), Handles[i]);
            Canvas.SetLeft(handle, _originX + x * _scale - HandleSize / 2);
            Canvas.SetTop(handle, _originY + y * _scale - HandleSize / 2);
            handle.Visibility = Visibility.Visible;
        }
        Panel.SetZIndex(_dragRectangle, 20);
    }

    // ---------------------------------------------------------------- area editing

    private VideoRedactionArea? SelectedArea() =>
        _selected >= 0 && _selected < _areas.Count ? _areas[_selected] : null;

    private void Select(int index)
    {
        _selected = index >= 0 && index < _areas.Count ? index : -1;
        PaintAreas();
        PaintChips();
        UpdateEditor();
        UpdateHeader();
        _status.Text = string.Empty;
    }

    private void ReplaceSelected(Func<VideoRedactionArea, VideoRedactionArea> change)
    {
        if (_selected < 0 || _selected >= _areas.Count)
            return;
        _areas[_selected] = change(_areas[_selected]);
        PaintAreas();
        PaintChips();
        UpdateEditor();
    }

    private void RemoveSelected()
    {
        if (_selected < 0 || _selected >= _areas.Count)
            return;
        _areas.RemoveAt(_selected);
        _selected = -1;
        PaintAreas();
        PaintChips();
        UpdateHeader();
        UpdateEditor();
        _status.Text = string.Empty;
    }

    private void CommitTimes()
    {
        if (_syncing || _selected < 0 || _selected >= _areas.Count)
            return;

        var area = _areas[_selected];
        if (!FrameMath.TryParseTime(_startBox.Text, out var start) || !FrameMath.TryParseTime(_endBox.Text, out var end))
        {
            _status.Text = T("tool.videoredact.invalidTime");
            UpdateEditor();
            return;
        }

        var duration = _info?.DurationSeconds ?? 0;
        if (duration > 0)
        {
            start = Math.Clamp(start, 0, duration);
            end = Math.Clamp(end, 0, duration);
        }
        if (end - start < RedactionGraph.MinDuration)
        {
            _status.Text = T("tool.videoredact.endBeforeStart");
            UpdateEditor();
            return;
        }

        _status.Text = string.Empty;
        _areas[_selected] = area with { StartSeconds = start, EndSeconds = end };
        PaintAreas();
        PaintChips();
        UpdateEditor();
    }

    private void UpdateEditor()
    {
        _syncing = true;
        try
        {
            var hasSelection = _selected >= 0 && _selected < _areas.Count;
            _startBox.IsEnabled = hasSelection;
            _endBox.IsEnabled = hasSelection;
            _remove.Opacity = hasSelection ? 1 : 0.45;
            if (hasSelection)
            {
                var area = _areas[_selected];
                _startBox.Text = FrameMath.Timecode(area.StartSeconds);
                _endBox.Text = FrameMath.Timecode(area.EndSeconds);
                _styleChoice.Set(area.Style);
                _colorChoice.Set(area.Color);
                _style = area.Style;
                _color = area.Color;
            }
            else
            {
                _startBox.Text = string.Empty;
                _endBox.Text = string.Empty;
                _styleChoice.Set(_style);
                _colorChoice.Set(_color);
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private void PaintChips()
    {
        _chips.Children.Clear();
        if (_areas.Count == 0)
        {
            _chips.Children.Add(_emptyHint);
            return;
        }

        for (var i = 0; i < _areas.Count; i++)
        {
            var index = i;
            var area = _areas[i];
            var label = new TextBlock
            {
                Text = $"#{index + 1}  {FrameMath.ShortTimecode(area.StartSeconds)}–{FrameMath.ShortTimecode(area.EndSeconds)}",
                FontSize = 11.5,
                Foreground = Theme.Text,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var remove = new TextBlock
            {
                Text = "\uE711",
                FontFamily = Theme.Icons,
                FontSize = 9,
                Foreground = Theme.Muted,
                Margin = new Thickness(7, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = T("tool.videoredact.remove"),
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(label);
            content.Children.Add(remove);

            var chip = new Border
            {
                Background = index == _selected ? Theme.AccentSoft : Theme.Track,
                BorderBrush = index == _selected ? Theme.Accent : null,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(8, 3, 8, 4),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = Cursors.Hand,
                Child = content,
            };
            chip.MouseLeftButtonUp += (_, _) => Select(index);
            remove.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                _selected = index;
                RemoveSelected();
            };
            _chips.Children.Add(chip);
        }
    }

    private void UpdateHeader()
    {
        _header.Text = T("tool.videoredact.areas", _areas.Count);
        // No areas means there is nothing to hide; the shared window disables Apply.
        _root?.SetCanApply(_ffmpeg is not null && _info is not null && _areas.Count > 0);
    }

    /// <summary>Re-applies the current Theme brushes after a light/dark switch.</summary>
    private void RepaintTheme()
    {
        _position.Foreground = Theme.Muted;
        _status.Foreground = Theme.Muted;
        _header.Foreground = Theme.Text;
        _emptyHint.Foreground = Theme.Muted;
        _placeholderText.Foreground = Theme.Muted;
        _placeholder.Background = Theme.Card;
        if (_previewCard is not null)
            _previewCard.Background = Theme.Card;

        foreach (var box in new[] { _startBox, _endBox })
        {
            box.Background = Theme.Card;
            box.Foreground = Theme.Text;
            box.BorderBrush = Theme.PanelBorder;
        }

        _scrubber.ApplyTheme();
        _styleChoice.ApplyTheme();
        _colorChoice.ApplyTheme();
        if (_previousButton is not null)
            VideoTheme.RepaintButton(_previousButton);
        if (_nextButton is not null)
            VideoTheme.RepaintButton(_nextButton);
        VideoTheme.RepaintButton(_remove);
        VideoTheme.RepaintButton(_cancel);
        _dragRectangle.Stroke = Theme.Accent;
        _dragRectangle.Fill = VideoPalette.Tint(Theme.AccentColor, 0x22);
        foreach (var handle in _handles)
        {
            handle.Background = Theme.Accent;
            handle.BorderBrush = Theme.TextOnAccent;
        }
        PaintAreas();
        PaintChips();
    }

    // ---------------------------------------------------------------- preview frames

    private void UpdatePosition()
    {
        if (_info is null)
        {
            _position.Text = string.Empty;
            return;
        }
        _position.Text = T(
            "tool.videoredact.position",
            FrameMath.FrameNumber(_frame),
            Bidi.Value(FrameMath.Timecode(_frame, _info.Fps)));
    }

    private void SeekTo(int frame)
    {
        if (_info is null)
            return;
        _frame = Math.Clamp(frame, 0, _total - 1);
        _scrubber.SetValue(_frame);
        UpdatePosition();
        _ = ShowFrameAsync(_frame, immediate: false);
    }

    private async Task ShowFrameAsync(int frame, bool immediate)
    {
        if (_ffmpeg is null || _info is null)
            return;

        _seek?.Cancel();
        _seek?.Dispose();
        var seek = _seek = new CancellationTokenSource();
        try
        {
            if (!immediate)
                await Task.Delay(PreviewDelayMilliseconds, seek.Token).ConfigureAwait(true);

            var bitmap = await VideoPreview.LoadAsync(
                _ffmpeg,
                _item.Path,
                FrameMath.SeekSeconds(frame, _info.Fps),
                T("tool.videoredact.unreadable"),
                seek.Token).ConfigureAwait(true);

            if (seek.IsCancellationRequested || !ReferenceEquals(seek, _seek))
                return;

            _image.Source = bitmap;
            _image.Visibility = Visibility.Visible;
            _placeholder.Visibility = Visibility.Collapsed;
            _spinner.Visibility = Visibility.Collapsed;
            _status.Text = string.Empty;
            LayoutOverlay();
        }
        catch (OperationCanceledException)
        {
            // A newer scrub request or the closing window superseded this frame.
        }
        catch (Exception ex)
        {
            if (!seek.IsCancellationRequested && ReferenceEquals(seek, _seek))
                ShowMessage(ex.Message);
        }
    }

    private void ShowMessage(string text)
    {
        _spinner.Visibility = Visibility.Collapsed;
        _placeholderText.Text = text;
        _placeholder.Visibility = Visibility.Visible;
        _status.Text = text;
    }

    private void Reset()
    {
        _seek?.Cancel();
        _dragKind = DragKind.None;
        _dragArea = null;
        _draftBox = null;
        _dragRectangle.Visibility = Visibility.Collapsed;
        if (_canvas.IsMouseCaptured)
            _canvas.ReleaseMouseCapture();
        _areas.Clear();
        _selected = -1;
        _style = VideoRedactionStyle.Solid;
        _color = "black";
        _frame = 0;
        _status.Text = string.Empty;
        if (_info is not null)
        {
            _scrubber.SetValue(0);
            UpdatePosition();
            _ = ShowFrameAsync(0, immediate: true);
        }
        PaintAreas();
        PaintChips();
        UpdateEditor();
        UpdateHeader();
    }

    private async Task<string?> ApplyAsync()
    {
        if (_ffmpeg is null || _info is null)
            return null;

        CommitTimes();
        var graph = RedactionGraph.Build(_areas, _info.Width, _info.Height);
        if (graph.Length == 0)
            return null;

        _seek?.Cancel();
        var directory = VideoOutput.Folder(_item, _targets);
        _apply = new CancellationTokenSource();
        _cancel.Visibility = Visibility.Visible;
        _status.Text = T("tool.videoredact.applying");
        var progress = new Progress<double>(value =>
            _status.Text = T("tool.videoredact.applying") + " "
                + Math.Round(Math.Clamp(value, 0, 1) * 100).ToString("0", CultureInfo.InvariantCulture) + "%");
        try
        {
            return await VideoRunner.RedactAsync(
                _ffmpeg,
                _item,
                graph,
                _info.DurationSeconds,
                new Settings().VideoCrf,
                directory,
                progress,
                _apply.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            _cancel.Visibility = Visibility.Collapsed;
            _apply.Dispose();
            _apply = null;
        }
    }

    /// <summary>What the pointer is doing over the preview until it is released.</summary>
    private enum DragKind
    {
        None,
        New,
        Move,
        Resize,
    }
}

/// <summary>
/// Adapts the shared <see cref="RedactionEditor"/> geometry to timed video areas. The editor works in
/// floating-point image pixels; video areas store whole pixels and add a time range and style, so every
/// move/resize keeps those untouched and only replaces the rectangle.
/// </summary>
internal static class VideoRedactionEditor
{
    public static RedactionBox Box(VideoRedactionArea area) =>
        new(area.X, area.Y, area.Width, area.Height);

    /// <summary>The index of the topmost timed area containing the point, or -1.</summary>
    public static int HitTest(IReadOnlyList<VideoRedactionArea> areas, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(areas);
        for (var i = areas.Count - 1; i >= 0; i--)
            if (areas[i] is { } area && Box(area).Contains(x, y))
                return i;
        return -1;
    }

    /// <summary>Moves an area by an image-pixel delta, keeping it inside the frame.</summary>
    public static VideoRedactionArea Move(
        VideoRedactionArea area,
        double dx,
        double dy,
        int imageWidth,
        int imageHeight)
    {
        ArgumentNullException.ThrowIfNull(area);
        return WithBox(
            area,
            RedactionEditor.Move(Box(area), dx, dy, imageWidth, imageHeight),
            imageWidth,
            imageHeight);
    }

    /// <summary>Resizes an area from one of its eight handles, anchoring the opposite edge or corner.</summary>
    public static VideoRedactionArea Resize(
        VideoRedactionArea area,
        RedactionHandle handle,
        double dx,
        double dy,
        int imageWidth,
        int imageHeight)
    {
        ArgumentNullException.ThrowIfNull(area);
        return WithBox(
            area,
            RedactionEditor.Resize(handle, Box(area), dx, dy, imageWidth, imageHeight),
            imageWidth,
            imageHeight);
    }

    /// <summary>Builds a new timed area from the rectangle dragged over the preview.</summary>
    public static VideoRedactionArea Create(
        RedactionBox box,
        double startSeconds,
        double endSeconds,
        VideoRedactionStyle style,
        string color,
        int imageWidth,
        int imageHeight) =>
        WithBox(
            new VideoRedactionArea(0, 0, 1, 1, startSeconds, endSeconds, style, color),
            box,
            imageWidth,
            imageHeight);

    /// <summary>Rounds the rectangle to whole pixels and clamps it to a frame of at least one pixel.</summary>
    private static VideoRedactionArea WithBox(
        VideoRedactionArea area,
        RedactionBox box,
        int imageWidth,
        int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || box.IsEmpty)
            return area;
        if (!double.IsFinite(box.X) || !double.IsFinite(box.Y) ||
            !double.IsFinite(box.Width) || !double.IsFinite(box.Height))
            return area;

        var left = Math.Clamp(
            (int)Math.Round(box.X, MidpointRounding.AwayFromZero), 0, imageWidth - 1);
        var top = Math.Clamp(
            (int)Math.Round(box.Y, MidpointRounding.AwayFromZero), 0, imageHeight - 1);
        var right = Math.Clamp(
            (int)Math.Round(box.Right, MidpointRounding.AwayFromZero), left + 1, imageWidth);
        var bottom = Math.Clamp(
            (int)Math.Round(box.Bottom, MidpointRounding.AwayFromZero), top + 1, imageHeight);
        return area with { X = left, Y = top, Width = right - left, Height = bottom - top };
    }
}
