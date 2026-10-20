using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileAction.App.Shell;
using FileAction.Core;
using WpfImage = System.Windows.Controls.Image;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Image;

/// <summary>
/// The redact window content: a downsampled preview with the areas drawn over it, a mode row (solid, blur
/// or pixelate, with the chosen colour or strength), a "Detect text" action, "Delete area" and the hint
/// line. Dragging on the image draws an area, dragging inside one moves it, dragging a handle resizes it and
/// clicking brings it to the front of the paint order.
/// </summary>
internal sealed class RedactView
{
    private const double HandleSize = 11;
    private const double HandleHitRadius = 8;
    private const double MinimumDraftDips = 8;
    private const int RenderDelayMs = 70;

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

    private static readonly uint[] SwatchColors =
    [
        RedactionColors.Black,
        0xFFFFFFFF,
        0xFF808080,
        0xFFD32F2F,
        0xFF1565C0,
        0xFF2E7D32,
    ];

    private readonly FileItem _item;
    private readonly Task<(BitmapSource Bitmap, byte[] Png, int Width, int Height)> _load;
    private readonly TaskCompletionSource<MediaSize?> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly Grid _preview = new() { Margin = new Thickness(0, 4, 0, 10), ClipToBounds = true };
    private readonly Canvas _overlay = new() { Background = Brushes.Transparent, ClipToBounds = true };
    private readonly WpfImage _imageView = new() { Stretch = Stretch.Uniform, IsHitTestVisible = false };
    private readonly Border _placeholder;
    private readonly Border _draft = new()
    {
        BorderThickness = new Thickness(1.5),
        CornerRadius = new CornerRadius(2),
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
    };
    private readonly TextBlock _hint = new()
    {
        FontSize = 11.5,
        Foreground = Theme.Muted,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 16,
    };
    private readonly TextBlock _targetLabel = new()
    {
        FontSize = 12,
        Foreground = Theme.Muted,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 10, 0),
    };
    private readonly Grid _params = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly StackPanel _strengthPanel = new() { Orientation = System.Windows.Controls.Orientation.Horizontal };
    private readonly StackPanel _colorPanel = new() { Orientation = System.Windows.Controls.Orientation.Horizontal };
    private readonly ContentControl _sliderHost = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly List<(Border Chip, TextBlock Text, RedactionMode Mode)> _modeChips = [];
    private readonly List<(uint Color, Border Swatch)> _swatches = [];
    private readonly List<RedactionArea> _areas = [];
    private readonly List<Border> _areaShapes = [];
    private readonly List<Border> _handles = [];

    private Border? _detectButton;
    private Border? _deleteButton;
    private RedactionStyle _style = RedactionStyle.Default;
    private int _selected = -1;
    private BitmapSource? _previewOriginal;
    private byte[]? _previewPng;
    private int _imageWidth;
    private int _imageHeight;
    private int _previewWidth;
    private int _previewHeight;
    private double _scale = 1;
    private double _originX;
    private double _originY;
    private int _renderGeneration;
    private DragKind _dragKind;
    private Point _dragStart;
    private RedactionBox _dragBox;
    private RedactionHandle _dragHandle;
    private RedactionBox? _draftBox;

    public RedactView(FileItem item)
    {
        _item = item;
        _draft.BorderBrush = Theme.Accent;
        _placeholder = new Border
        {
            Background = Theme.Card,
            CornerRadius = new CornerRadius(10),
            Child = new ProgressBar { IsIndeterminate = true, Width = 120, Height = 4 },
        };

        // Decode on a background thread so the window opens immediately with a placeholder.
        _load = Task.Run(() => RedactRunner.LoadPreview(item.Path));
    }

    public ToolContent Build()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var preview = BuildPreview();
        var modes = BuildModeRow();
        BuildParamsRow();
        var actions = BuildActionRow();
        _hint.Text = T("tool.redact.hint");

        Grid.SetRow(preview, 0);
        Grid.SetRow(modes, 1);
        Grid.SetRow(_params, 2);
        Grid.SetRow(actions, 3);
        Grid.SetRow(_hint, 4);
        root.Children.Add(preview);
        root.Children.Add(modes);
        root.Children.Add(_params);
        root.Children.Add(actions);
        root.Children.Add(_hint);

        RebuildAreaVisuals();
        UpdateControls();
        return new ToolContent(root, Reset, ApplyAsync, _ready.Task, ActivateAsync);
    }

    /// <summary>Swaps the placeholder for the decoded preview, on the UI thread.</summary>
    public async Task ActivateAsync()
    {
        try
        {
            var (bitmap, png, width, height) = await _load;
            _previewOriginal = bitmap;
            _previewPng = png;
            _imageWidth = width;
            _imageHeight = height;
            _previewWidth = bitmap.PixelWidth;
            _previewHeight = bitmap.PixelHeight;
            _imageView.Source = bitmap;
            _imageView.Visibility = Visibility.Visible;
            _placeholder.Visibility = Visibility.Collapsed;
            Layout();
            _ready.TrySetResult(new MediaSize(width, height));
        }
        catch (Exception ex)
        {
            _placeholder.Child = new TextBlock
            {
                Text = ex.Message,
                Foreground = Theme.Muted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(16),
            };
            _ready.TrySetResult(null);
        }
    }

    // ---------------------------------------------------------------- building

    private FrameworkElement BuildPreview()
    {
        _overlay.MouseLeftButtonDown += OnMouseDown;
        _overlay.MouseMove += OnMouseMove;
        _overlay.MouseLeftButtonUp += OnMouseUp;
        _overlay.LostMouseCapture += (_, _) => CancelDrag();

        _preview.Children.Add(_imageView);
        _preview.Children.Add(_overlay);
        _preview.Children.Add(new Grid { Children = { _placeholder } });
        _preview.SizeChanged += (_, _) => Layout();

        return new Border
        {
            Background = Theme.Card,
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(6),
            Child = _preview,
        };
    }

    private FrameworkElement BuildModeRow()
    {
        var header = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
        };
        header.Children.Add(_targetLabel);

        var chips = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        foreach (var mode in new[] { RedactionMode.Solid, RedactionMode.Blur, RedactionMode.Pixelate })
        {
            var text = new TextBlock
            {
                Text = T(ModeKey(mode)),
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var chip = new Border
            {
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(10, 4, 10, 5),
                Cursor = Cursors.Hand,
                Child = text,
            };
            var captured = mode;
            chip.MouseLeftButtonUp += (_, _) => SetMode(captured);
            _modeChips.Add((chip, text, mode));
            chips.Children.Add(chip);
        }

        header.Children.Add(new Border
        {
            Background = Theme.Track,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = chips,
        });
        return header;
    }

    private void BuildParamsRow()
    {
        _strengthPanel.Children.Add(new TextBlock
        {
            Text = T("tool.redact.strength"),
            FontSize = 12,
            Foreground = Theme.Muted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        });
        _strengthPanel.Children.Add(_sliderHost);

        _colorPanel.Children.Add(new TextBlock
        {
            Text = T("tool.redact.color"),
            FontSize = 12,
            Foreground = Theme.Muted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        });
        foreach (var color in SwatchColors)
        {
            var swatch = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(2),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush(ToColor(color)),
            };
            var captured = color;
            swatch.MouseLeftButtonUp += (_, _) => SetColor(captured);
            _swatches.Add((color, swatch));
            _colorPanel.Children.Add(swatch);
        }

        _params.Children.Add(_strengthPanel);
        _params.Children.Add(_colorPanel);
    }

    private FrameworkElement BuildActionRow()
    {
        _detectButton = Controls.Button(T("tool.redact.detectText"), () => _ = DetectTextAsync(), glyph: Glyphs.Text);
        _deleteButton = Controls.Button(T("tool.redact.delete"), DeleteSelected, glyph: Glyphs.Clear);

        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = false };
        DockPanel.SetDock(_detectButton, Dock.Left);
        DockPanel.SetDock(_deleteButton, Dock.Right);
        row.Children.Add(_detectButton);
        row.Children.Add(_deleteButton);
        return row;
    }

    // ---------------------------------------------------------------- state

    private void SetMode(RedactionMode mode)
    {
        var area = SelectedArea();
        if (area is null)
            _style = _style with { Mode = mode };
        else if (_selected >= 0)
            _areas[_selected] = area with { Mode = mode };

        UpdateControls();
        QueueRender();
    }

    private void SetStrength(int strength)
    {
        var area = SelectedArea();
        if (area is null)
            _style = _style with { Strength = strength };
        else if (_selected >= 0)
            _areas[_selected] = area with { Strength = strength };
    }

    private void SetColor(uint color)
    {
        _style = _style with { Color = color };
        PaintSwatches();
        QueueRender();
    }

    private void DeleteSelected()
    {
        if (SelectedArea() is null)
            return;

        _areas.RemoveAt(_selected);
        _selected = _areas.Count == 0 ? -1 : Math.Min(_selected, _areas.Count - 1);
        RebuildAreaVisuals();
        UpdateControls();
        QueueRender();
    }

    private void Reset()
    {
        _areas.Clear();
        _selected = -1;
        _style = RedactionStyle.Default;
        _draftBox = null;
        _renderGeneration++;
        _imageView.Source = _previewOriginal;
        RebuildAreaVisuals();
        UpdateControls();
        _hint.Text = T("tool.redact.hint");
    }

    private RedactionArea? SelectedArea() =>
        _selected >= 0 && _selected < _areas.Count ? _areas[_selected] : null;

    private void Select(int index, bool bringToFront)
    {
        var reordered = false;
        if (index >= 0 && index < _areas.Count && bringToFront && index != _areas.Count - 1)
        {
            index = RedactionEditor.BringToFront(_areas, index);
            reordered = true;
        }

        _selected = index >= 0 && index < _areas.Count ? index : -1;
        if (reordered)
            RebuildAreaVisuals();
        else
            LayoutAreas();
        UpdateControls();
    }

    private void UpdateControls()
    {
        var area = SelectedArea();
        var mode = area?.Mode ?? _style.Mode;
        var strength = Math.Clamp(
            area?.Strength ?? _style.Strength,
            RedactionPlan.MinStrength,
            RedactionPlan.MaxStrength);

        _targetLabel.Text = T(area is null ? "tool.redact.target.all" : "tool.redact.target.selected");
        PaintModeChips(mode);

        var solid = mode == RedactionMode.Solid;
        _strengthPanel.Visibility = solid ? Visibility.Collapsed : Visibility.Visible;
        _colorPanel.Visibility = solid ? Visibility.Visible : Visibility.Collapsed;
        if (!solid)
            RebuildSlider(strength);
        PaintSwatches();

        if (_deleteButton is not null)
        {
            _deleteButton.IsEnabled = area is not null;
            _deleteButton.Opacity = area is null ? 0.45 : 1;
        }
    }

    private void RebuildSlider(int strength) =>
        _sliderHost.Content = Controls.Slider(
            RedactionPlan.MinStrength,
            RedactionPlan.MaxStrength,
            strength,
            SetStrength,
            QueueRender);

    private void PaintModeChips(RedactionMode selected)
    {
        foreach (var (chip, text, mode) in _modeChips)
        {
            var on = mode == selected;
            chip.Background = on ? Theme.AccentGradient : Brushes.Transparent;
            text.Foreground = on ? Theme.TextOnAccent : Theme.Text;
            text.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private void PaintSwatches()
    {
        foreach (var (color, swatch) in _swatches)
        {
            swatch.BorderBrush = color == _style.Color ? Theme.Accent : Theme.PanelBorder;
        }
    }

    // ---------------------------------------------------------------- layout

    private void Layout()
    {
        if (_imageWidth <= 0)
            return;

        var availableWidth = Math.Max(1, _preview.ActualWidth);
        var availableHeight = Math.Max(1, _preview.ActualHeight);
        _scale = Math.Min(availableWidth / _imageWidth, availableHeight / _imageHeight);
        if (!(_scale > 0) || double.IsInfinity(_scale))
            _scale = 1;
        _originX = (_preview.ActualWidth - _imageWidth * _scale) / 2;
        _originY = (_preview.ActualHeight - _imageHeight * _scale) / 2;
        LayoutAreas();
    }

    private void RebuildAreaVisuals()
    {
        _overlay.Children.Clear();
        _areaShapes.Clear();
        _handles.Clear();

        foreach (var _ in _areas)
        {
            var shape = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = Theme.Accent,
                CornerRadius = new CornerRadius(2),
                IsHitTestVisible = false,
            };
            _areaShapes.Add(shape);
            _overlay.Children.Add(shape);
        }

        foreach (var _ in Handles)
        {
            var dot = new Border
            {
                Width = HandleSize,
                Height = HandleSize,
                CornerRadius = new CornerRadius(HandleSize / 2),
                Background = Theme.Accent,
                BorderBrush = Theme.TextOnAccent,
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
            };
            _handles.Add(dot);
            _overlay.Children.Add(dot);
        }

        _overlay.Children.Add(_draft);
        LayoutAreas();
    }

    private void LayoutAreas()
    {
        for (var i = 0; i < _areas.Count && i < _areaShapes.Count; i++)
        {
            var shape = _areaShapes[i];
            var box = _areas[i].Box;
            var selected = i == _selected;
            Canvas.SetLeft(shape, _originX + box.X * _scale);
            Canvas.SetTop(shape, _originY + box.Y * _scale);
            shape.Width = Math.Max(1, box.Width * _scale);
            shape.Height = Math.Max(1, box.Height * _scale);
            shape.BorderThickness = new Thickness(selected ? 2.5 : 1);
            shape.Opacity = selected ? 1 : 0.65;
        }

        var area = SelectedArea();
        for (var i = 0; i < _handles.Count && i < Handles.Length; i++)
        {
            if (area is null)
            {
                _handles[i].Visibility = Visibility.Collapsed;
                continue;
            }

            var (x, y) = RedactionEditor.HandlePoint(area.Box, Handles[i]);
            Canvas.SetLeft(_handles[i], _originX + x * _scale - HandleSize / 2);
            Canvas.SetTop(_handles[i], _originY + y * _scale - HandleSize / 2);
            _handles[i].Visibility = Visibility.Visible;
        }

        PositionDraft();
    }

    private void PositionDraft()
    {
        if (_draftBox is not { } box || box.IsEmpty || _imageWidth <= 0)
        {
            _draft.Visibility = Visibility.Collapsed;
            return;
        }

        _draft.Visibility = Visibility.Visible;
        Canvas.SetLeft(_draft, _originX + box.X * _scale);
        Canvas.SetTop(_draft, _originY + box.Y * _scale);
        _draft.Width = Math.Max(1, box.Width * _scale);
        _draft.Height = Math.Max(1, box.Height * _scale);
    }

    private Point ToImage(Point point) => new(
        (point.X - _originX) / _scale,
        (point.Y - _originY) / _scale);

    // ---------------------------------------------------------------- mouse

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_imageWidth <= 0 || e.ChangedButton != MouseButton.Left)
            return;

        var point = ToImage(e.GetPosition(_overlay));
        var tolerance = HandleHitRadius / Math.Max(_scale, 0.0001);
        var selected = SelectedArea();
        var handle = selected is null
            ? RedactionHandle.None
            : RedactionEditor.HandleAt(selected.Box, point.X, point.Y, tolerance);

        if (selected is not null && handle != RedactionHandle.None)
        {
            _dragKind = DragKind.Resize;
            _dragHandle = handle;
            _dragBox = selected.Box;
            _dragStart = point;
        }
        else
        {
            var hit = RedactionEditor.HitTest(_areas, point.X, point.Y);
            if (hit >= 0)
            {
                Select(hit, bringToFront: true);
                _dragKind = DragKind.Move;
                _dragBox = _areas[_selected].Box;
                _dragStart = point;
            }
            else
            {
                Select(-1, bringToFront: false);
                _dragKind = DragKind.New;
                _dragStart = point;
                _draftBox = null;
            }
        }

        _overlay.CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_imageWidth <= 0)
            return;

        var point = ToImage(e.GetPosition(_overlay));
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

        var dx = point.X - _dragStart.X;
        var dy = point.Y - _dragStart.Y;
        switch (_dragKind)
        {
            case DragKind.Move when SelectedArea() is not null:
                _areas[_selected] = _areas[_selected] with
                {
                    Box = RedactionEditor.Move(_dragBox, dx, dy, _imageWidth, _imageHeight),
                };
                LayoutAreas();
                break;

            case DragKind.Resize when SelectedArea() is not null:
                _areas[_selected] = _areas[_selected] with
                {
                    Box = RedactionEditor.Resize(_dragHandle, _dragBox, dx, dy, _imageWidth, _imageHeight),
                };
                LayoutAreas();
                break;

            case DragKind.New:
                _draftBox = RedactionEditor.Create(_dragStart.X, _dragStart.Y, point.X, point.Y, _imageWidth, _imageHeight);
                PositionDraft();
                break;
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
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
        _draftBox = null;
        if (_overlay.IsMouseCaptured)
            _overlay.ReleaseMouseCapture();
        _draft.Visibility = Visibility.Collapsed;

        if (kind == DragKind.New && draft is { } box &&
            box.Width * _scale >= MinimumDraftDips && box.Height * _scale >= MinimumDraftDips)
        {
            _areas.Add(new RedactionArea(box));
            _selected = _areas.Count - 1;
            RebuildAreaVisuals();
            UpdateControls();
            QueueRender();
            return;
        }

        if (kind is DragKind.Move or DragKind.Resize)
            QueueRender();
    }

    private void CancelDrag()
    {
        if (_dragKind == DragKind.None)
            return;
        _dragKind = DragKind.None;
        _draftBox = null;
        _draft.Visibility = Visibility.Collapsed;
    }

    private void UpdateCursor(Point point)
    {
        var selected = SelectedArea();
        if (selected is not null)
        {
            var tolerance = HandleHitRadius / Math.Max(_scale, 0.0001);
            var handle = RedactionEditor.HandleAt(selected.Box, point.X, point.Y, tolerance);
            if (handle != RedactionHandle.None)
            {
                _overlay.Cursor = CursorFor(handle);
                return;
            }
        }

        _overlay.Cursor = RedactionEditor.HitTest(_areas, point.X, point.Y) >= 0 ? Cursors.SizeAll : Cursors.Cross;
    }

    private static Cursor CursorFor(RedactionHandle handle) => handle switch
    {
        RedactionHandle.TopLeft or RedactionHandle.BottomRight => Cursors.SizeNWSE,
        RedactionHandle.TopRight or RedactionHandle.BottomLeft => Cursors.SizeNESW,
        RedactionHandle.Top or RedactionHandle.Bottom => Cursors.SizeNS,
        RedactionHandle.Left or RedactionHandle.Right => Cursors.SizeWE,
        _ => Cursors.Cross,
    };

    // ---------------------------------------------------------------- preview rendering

    /// <summary>
    /// Re-renders the downsampled preview after the areas or the style change. The delay and the generation
    /// counter keep rapid changes (a slider, a drag release) down to one render of the latest state.
    /// </summary>
    private void QueueRender()
    {
        if (_previewPng is null || _imageWidth <= 0)
            return;

        var generation = ++_renderGeneration;
        _ = RenderAsync(generation);
    }

    private async Task RenderAsync(int generation)
    {
        try
        {
            await Task.Delay(RenderDelayMs);
            if (generation != _renderGeneration || _previewPng is not { } png)
                return;

            var steps = PreviewSteps();
            if (steps.Count == 0)
            {
                _imageView.Source = _previewOriginal;
                return;
            }

            BitmapSource? bitmap = null;
            try
            {
                bitmap = await Task.Run(() => RedactRunner.RenderPreview(png, steps));
            }
            catch (Exception)
            {
                bitmap = null;
            }

            if (generation != _renderGeneration)
                return;
            _imageView.Source = bitmap ?? _previewOriginal;
        }
        catch (Exception)
        {
            // The effect preview is cosmetic; the areas and the saved file do not depend on it.
        }
    }

    private IReadOnlyList<RedactionStep> PreviewSteps()
    {
        if (_previewWidth <= 0 || _previewHeight <= 0)
            return [];

        var plan = RedactionPlan.Build(_imageWidth, _imageHeight, _areas, _style);
        var factor = _previewWidth / (double)_imageWidth;
        return RedactionPlan.ToPreview(plan, _previewWidth, _previewHeight, factor);
    }

    // ---------------------------------------------------------------- text detection

    private async Task DetectTextAsync()
    {
        if (_previewPng is not { } png || _previewWidth <= 0 || _imageWidth <= 0)
            return;

        if (_detectButton is not null)
            _detectButton.IsEnabled = false;
        _hint.Text = T("tool.redact.detecting");
        try
        {
            IReadOnlyList<RedactionBox>? lines;
            try
            {
                lines = await Task.Run(() => RedactTextDetector.Detect(png, CancellationToken.None));
            }
            catch (Exception)
            {
                lines = null;
            }

            if (lines is null)
            {
                Notifications.Show(T("tool.redact.title"), T("tool.redact.textUnavailable"), error: true);
                return;
            }
            if (lines.Count == 0)
            {
                Notifications.Show(T("tool.redact.title"), T("tool.redact.noText"));
                return;
            }

            // The detector works on the preview bitmap; the areas live in original-image pixels.
            var factor = _imageWidth / (double)_previewWidth;
            var added = 0;
            foreach (var line in lines)
            {
                var padded = RedactionText.Pad(line, 3 * factor);
                var box = RedactionEditor.Create(padded.X, padded.Y, padded.Right, padded.Bottom, _imageWidth, _imageHeight);
                if (box.IsEmpty)
                    continue;

                _areas.Add(new RedactionArea(box));
                added++;
            }

            if (added == 0)
            {
                Notifications.Show(T("tool.redact.title"), T("tool.redact.noText"));
                return;
            }

            _selected = _areas.Count - 1;
            RebuildAreaVisuals();
            UpdateControls();
            QueueRender();
        }
        finally
        {
            if (_detectButton is not null)
                _detectButton.IsEnabled = true;
            _hint.Text = T("tool.redact.hint");
        }
    }

    // ---------------------------------------------------------------- apply

    private async Task<string?> ApplyAsync()
    {
        if (_imageWidth <= 0)
            throw new ConversionException(T("tool.redact.notReady"));
        if (_areas.Count == 0)
            throw new ConversionException(T("tool.redact.noAreas"));

        var plan = RedactionPlan.Build(_imageWidth, _imageHeight, _areas, _style);
        if (plan.Count == 0)
            throw new ConversionException(T("tool.redact.noAreas"));

        // "Next to the source", as specified for this tool; the original file is only read.
        var directory = OutputTarget.SourceFolder(_item.Path) ?? Environment.CurrentDirectory;
        return await Task.Run(() => RedactRunner.Apply(_item, plan, directory));
    }

    private static string ModeKey(RedactionMode mode) => mode switch
    {
        RedactionMode.Blur => "tool.redact.mode.blur",
        RedactionMode.Pixelate => "tool.redact.mode.pixelate",
        _ => "tool.redact.mode.solid",
    };

    private static Color ToColor(uint argb)
    {
        var (a, r, g, b) = RedactionColors.Split(argb);
        return Color.FromArgb(a, r, g, b);
    }

    private enum DragKind
    {
        None,
        New,
        Move,
        Resize,
    }
}
