using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileAction.Core;
using ImageMagick;
using static FileAction.Core.Loc;
using Ellipse = System.Windows.Shapes.Ellipse;
using Orientation = System.Windows.Controls.Orientation;
using TextAlignment = System.Windows.TextAlignment;
using WpfImage = System.Windows.Controls.Image;

namespace FileAction.App.UI.Tools.Collage;

/// <summary>
/// The collage window content: a live preview of the output canvas, the layout/size/spacing/corner/
/// background/fit controls, and a filmstrip whose thumbnails are dragged to reorder the images. The
/// preview is downsampled; Apply renders from the originals through <see cref="CollageRenderer"/>.
/// </summary>
internal sealed class CollageView
{
    private const int PreviewMaxSide = 640;
    private const int ThumbMaxSide = 96;
    private const double ThumbSize = 64;
    private const double ThumbGap = 8;

    private static readonly (string Key, CollageLayout Value)[] Layouts =
    [
        ("tool.collage.layout.grid", CollageLayout.Grid),
        ("tool.collage.layout.row", CollageLayout.Row),
        ("tool.collage.layout.column", CollageLayout.Column),
        ("tool.collage.layout.featured", CollageLayout.Featured),
    ];

    private static readonly (string Key, CollageFit Value)[] Fits =
    [
        ("tool.collage.fit.fill", CollageFit.Fill),
        ("tool.collage.fit.fit", CollageFit.Fit),
    ];

    private static readonly (string Key, CollageBackgroundKind Value)[] Backgrounds =
    [
        ("tool.collage.background.solid", CollageBackgroundKind.Solid),
        ("tool.collage.background.gradient", CollageBackgroundKind.Gradient),
    ];

    private readonly IReadOnlyList<FileItem> _items;
    private readonly Task<LoadedImage[]> _loadTask;
    private readonly TaskCompletionSource<MediaSize?> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CollageOrder _order;

    private readonly Grid _host = new() { ClipToBounds = true };
    private readonly Canvas _canvas = new() { ClipToBounds = true, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _placeholder;
    private readonly TextBox _widthBox = new() { Width = 74 };
    private readonly TextBox _heightBox = new() { Width = 74 };
    private readonly StackPanel _strip = new() { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight };
    private readonly List<(FrameworkElement Tile, Border Outline)> _tiles = [];
    private readonly List<Border> _swatches = [];

    private LoadedImage[]? _loaded;
    private ChipSet<CollageLayout> _layoutChips = null!;
    private ChipSet<CollageSize> _sizeChips = null!;
    private ChipSet<CollageFit> _fitChips = null!;
    private ChipSet<CollageBackgroundKind> _backgroundChips = null!;
    private SliderRow _spacingSlider = null!;
    private SliderRow _radiusSlider = null!;

    private CollageLayout _layout = CollageLayout.Grid;
    private CollageFit _fit = CollageFit.Fill;
    private CollageSize _size = CollagePresets.Square;
    private int _spacing = 12;
    private int _radius = 16;
    private CollageBackgroundKind _backgroundKind = CollageBackgroundKind.Solid;
    private int _paletteIndex = Theme.IsDark ? 0 : 1;
    private bool _syncing;

    private int _dragFrom = -1;
    private int _dragTarget = -1;
    private double _dragStartX;
    private bool _dragActive;
    private FrameworkElement? _dragTile;

    public CollageView(IReadOnlyList<FileItem> items)
    {
        _items = items;
        _order = new CollageOrder(items.Count);
        _placeholder = new Border
        {
            Background = Theme.Card,
            CornerRadius = new CornerRadius(10),
            Child = new ProgressBar { IsIndeterminate = true, Width = 120, Height = 4 },
        };

        // Decode on a background thread, downsampled: the window opens immediately and stays responsive.
        _loadTask = Task.Run(() => items.Select(Load).ToArray());
    }

    public ToolContent Build()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var preview = BuildPreview();
        var controls = BuildControls();
        var strip = BuildFilmstrip();
        Grid.SetRow(preview, 0);
        Grid.SetRow(controls, 1);
        Grid.SetRow(strip, 2);
        root.Children.Add(preview);
        root.Children.Add(controls);
        root.Children.Add(strip);

        SyncSize();
        PaintSwatches();
        return new ToolContent(root, Reset, ApplyAsync, _ready.Task, ActivateAsync);
    }

    /// <summary>Replaces the spinner with the decoded preview, on the UI thread.</summary>
    public async Task ActivateAsync()
    {
        try
        {
            _loaded = await _loadTask;
            _placeholder.Visibility = Visibility.Collapsed;
            RebuildStrip();
            Paint();
            _ready.TrySetResult(CollageTool.PreviewShape);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            _ready.TrySetResult(null);
        }
    }

    private FrameworkElement BuildPreview()
    {
        _host.Children.Add(_canvas);
        _host.Children.Add(new Grid { Children = { _placeholder } });
        _host.SizeChanged += (_, _) => Paint();

        return new Border
        {
            Background = Theme.Card,
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 0, 0, 10),
            Child = _host,
        };
    }

    private FrameworkElement BuildControls()
    {
        var panel = new StackPanel();

        _layoutChips = new ChipSet<CollageLayout>(
            Layouts.Select(option => (T(option.Key), option.Value)).ToList(),
            _layout,
            value => { _layout = value; Paint(); });
        panel.Children.Add(Row(T("tool.collage.layout"), _layoutChips.View));

        _sizeChips = new ChipSet<CollageSize>(
            CollagePresets.All.Select(size => (Label(size), size)).ToList(),
            _size,
            value =>
            {
                _size = value;
                SyncSize();
                Paint();
            });
        panel.Children.Add(Row(T("tool.collage.size"), _sizeChips.View));

        var boxes = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        PrepareBox(_widthBox, T("tool.collage.width"));
        PrepareBox(_heightBox, T("tool.collage.height"));
        boxes.Children.Add(_widthBox);
        boxes.Children.Add(new TextBlock
        {
            Text = "×",
            Foreground = Theme.Muted,
            FontSize = 13,
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        boxes.Children.Add(_heightBox);
        panel.Children.Add(Row(T("tool.collage.custom"), boxes));

        _spacingSlider = new SliderRow(T("tool.collage.spacing"), 0, 48, _spacing, value =>
        {
            _spacing = value;
            Paint();
        });
        panel.Children.Add(_spacingSlider.View);

        _radiusSlider = new SliderRow(T("tool.collage.radius"), 0, 64, _radius, value =>
        {
            _radius = value;
            Paint();
        });
        panel.Children.Add(_radiusSlider.View);

        _backgroundChips = new ChipSet<CollageBackgroundKind>(
            Backgrounds.Select(option => (T(option.Key), option.Value)).ToList(),
            _backgroundKind,
            value => { _backgroundKind = value; Paint(); });
        panel.Children.Add(Row(T("tool.collage.background"), BackgroundControl()));

        _fitChips = new ChipSet<CollageFit>(
            Fits.Select(option => (T(option.Key), option.Value)).ToList(),
            _fit,
            value => { _fit = value; Paint(); });
        panel.Children.Add(Row(T("tool.collage.fit"), _fitChips.View));

        panel.Margin = new Thickness(0, 0, 0, 6);
        return panel;
    }

    private FrameworkElement BackgroundControl()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(_backgroundChips.View);

        var swatches = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < CollagePalette.Colors.Count; i++)
        {
            var index = i;
            var colour = CollagePalette.Colors[i];
            var swatch = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromRgb(colour.R, colour.G, colour.B)),
                BorderBrush = Theme.PanelBorder,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand,
                ToolTip = colour.ToHex(),
            };
            swatch.MouseLeftButtonUp += (_, _) =>
            {
                _paletteIndex = index;
                PaintSwatches();
                Paint();
            };
            _swatches.Add(swatch);
            swatches.Children.Add(swatch);
        }
        panel.Children.Add(swatches);
        return panel;
    }

    private FrameworkElement BuildFilmstrip()
    {
        var header = new DockPanel { LastChildFill = true, Margin = new Thickness(2, 0, 2, 6) };
        var title = new TextBlock
        {
            Text = T("tool.collage.order"),
            FontSize = 12,
            Foreground = Theme.Muted,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var hint = new TextBlock
        {
            Text = T("tool.collage.order.hint"),
            FontSize = 11.5,
            Foreground = Theme.Muted,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(title, Dock.Left);
        header.Children.Add(title);
        header.Children.Add(hint);

        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            FlowDirection = FlowDirection.LeftToRight,
            Content = _strip,
            Padding = new Thickness(0),
        };

        var card = new Border
        {
            Background = Theme.Card,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8),
            Child = scroll,
        };

        var root = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
        root.Children.Add(header);
        root.Children.Add(card);
        return root;
    }

    private void RebuildStrip()
    {
        ResetDrag();
        _strip.Children.Clear();
        _tiles.Clear();
        if (_loaded is null)
            return;

        for (var position = 0; position < _order.Count; position++)
        {
            var source = _order[position];
            var image = _loaded[source];

            var thumbnail = new Border
            {
                Width = ThumbSize,
                Height = ThumbSize,
                CornerRadius = new CornerRadius(8),
                Background = Theme.Track,
                Clip = new RectangleGeometry(new Rect(0, 0, ThumbSize, ThumbSize), 8, 8),
            };
            thumbnail.Child = new WpfImage
            {
                Source = image.Thumb,
                Stretch = Stretch.UniformToFill,
            };

            var outline = new Border
            {
                Width = ThumbSize,
                Height = ThumbSize,
                CornerRadius = new CornerRadius(8),
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(2),
            };

            var badge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xC8, 0, 0, 0)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(5, 0, 5, 1),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(3),
                Child = new TextBlock
                {
                    Text = (position + 1).ToString(CultureInfo.InvariantCulture),
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                },
            };

            var tile = new Grid
            {
                Width = ThumbSize,
                Height = ThumbSize,
                Margin = new Thickness(0, 0, ThumbGap, 0),
                Cursor = Cursors.Hand,
                ToolTip = Bidi.Value(Path.GetFileName(image.Item.Path)),
            };
            tile.Children.Add(thumbnail);
            tile.Children.Add(outline);
            tile.Children.Add(badge);

            var dragPosition = position;
            tile.MouseLeftButtonDown += (_, e) => BeginDrag(dragPosition, tile, e);
            tile.MouseMove += (_, e) => ContinueDrag(e);
            tile.MouseLeftButtonUp += (_, e) => EndDrag(e);
            tile.LostMouseCapture += (_, _) => ResetDrag();

            _tiles.Add((tile, outline));
            _strip.Children.Add(tile);
        }
    }

    private void Paint()
    {
        if (_loaded is null)
            return;

        var availableWidth = Math.Max(1, _host.ActualWidth);
        var availableHeight = Math.Max(1, _host.ActualHeight);
        var scale = Math.Min(availableWidth / _size.Width, availableHeight / _size.Height);
        if (scale <= 0 || double.IsInfinity(scale))
            scale = 1;

        _canvas.Width = Math.Max(1, _size.Width * scale);
        _canvas.Height = Math.Max(1, _size.Height * scale);
        _canvas.Background = BackgroundBrush();
        _canvas.Children.Clear();

        var cells = CollageMath.Cells(_loaded.Length, _layout, _size.Width, _size.Height, _spacing);
        for (var position = 0; position < cells.Count && position < _order.Count; position++)
        {
            var source = _order[position];
            var cell = cells[position];
            var image = _loaded[source];
            var radius = CollageMath.CornerRadius(cell.Bounds, _radius) * scale;
            var width = Math.Max(1, cell.Bounds.Width * scale);
            var height = Math.Max(1, cell.Bounds.Height * scale);

            var frame = new Border
            {
                Width = width,
                Height = height,
                Background = Brushes.Transparent,
                Clip = new RectangleGeometry(new Rect(0, 0, width, height), Math.Min(radius, Math.Min(width, height) / 2), Math.Min(radius, Math.Min(width, height) / 2)),
            };
            var picture = new WpfImage
            {
                Source = image.Preview,
                Stretch = _fit == CollageFit.Fill ? Stretch.UniformToFill : Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
            frame.Child = picture;

            Canvas.SetLeft(frame, cell.Bounds.X * scale);
            Canvas.SetTop(frame, cell.Bounds.Y * scale);
            _canvas.Children.Add(frame);
        }
    }

    private Brush BackgroundBrush()
    {
        var background = CollageBackground.For(CollagePalette.Colors[_paletteIndex], _backgroundKind);
        var start = ToWpf(background.Start);
        if (background.Kind == CollageBackgroundKind.Solid)
            return new SolidColorBrush(start);
        return new LinearGradientBrush(start, ToWpf(background.End), new Point(0, 0), new Point(0, 1));
    }

    private void PaintSwatches()
    {
        for (var i = 0; i < _swatches.Count; i++)
        {
            var selected = i == _paletteIndex;
            _swatches[i].BorderBrush = selected ? Theme.Text : Theme.PanelBorder;
            _swatches[i].BorderThickness = new Thickness(selected ? 2 : 1);
        }
    }

    private void Reset()
    {
        _layout = CollageLayout.Grid;
        _fit = CollageFit.Fill;
        _size = CollagePresets.Square;
        _spacing = 12;
        _radius = 16;
        _backgroundKind = CollageBackgroundKind.Solid;
        _paletteIndex = Theme.IsDark ? 0 : 1;
        _order.Reset();

        _layoutChips.Select(_layout);
        _fitChips.Select(_fit);
        _backgroundChips.Select(_backgroundKind);
        _spacingSlider.Set(_spacing);
        _radiusSlider.Set(_radius);
        SyncSize();
        PaintSwatches();
        RebuildStrip();
        Paint();
    }

    private async Task<string?> ApplyAsync()
    {
        if (_loaded is null)
            throw new InvalidOperationException(T("tool.collage.error.notReady"));

        var ordered = _order.Apply(_items);
        var paths = ordered.Select(item => item.Path).ToList();
        var directory = Path.GetDirectoryName(_items[0].Path);
        if (string.IsNullOrEmpty(directory))
            directory = Environment.CurrentDirectory;

        var options = new CollageOptions
        {
            Layout = _layout,
            Fit = _fit,
            Size = _size,
            Spacing = _spacing,
            Radius = _radius,
            Background = CollageBackground.For(CollagePalette.Colors[_paletteIndex], _backgroundKind),
        };
        return await CollageRenderer.RenderAsync(paths, options, directory);
    }

    private void PrepareBox(TextBox box, string tooltip)
    {
        box.Padding = new Thickness(8, 6, 8, 6);
        box.Background = Theme.Card;
        box.Foreground = Theme.Text;
        box.BorderBrush = Theme.PanelBorder;
        box.BorderThickness = new Thickness(1);
        box.HorizontalContentAlignment = HorizontalAlignment.Center;
        box.ToolTip = tooltip;
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                CommitSize();
        };
        box.LostFocus += (_, _) => CommitSize();
    }

    private void CommitSize()
    {
        if (_syncing)
            return;
        if (!int.TryParse(_widthBox.Text, out var width) || !int.TryParse(_heightBox.Text, out var height))
        {
            SyncSize();
            return;
        }

        var clamped = CollagePresets.Clamp(width, height);
        if (clamped != _size)
        {
            _size = clamped;
            Paint();
        }
        SyncSize();
    }

    private void SyncSize()
    {
        _syncing = true;
        _widthBox.Text = _size.Width.ToString(CultureInfo.InvariantCulture);
        _heightBox.Text = _size.Height.ToString(CultureInfo.InvariantCulture);
        _syncing = false;
        _sizeChips?.Select(_size);
    }

    private void BeginDrag(int position, FrameworkElement tile, MouseButtonEventArgs e)
    {
        if (_loaded is null)
            return;
        _dragFrom = position;
        _dragTarget = position;
        _dragStartX = e.GetPosition(_strip).X;
        _dragActive = false;
        _dragTile = tile;
        tile.CaptureMouse();
        e.Handled = true;
    }

    private void ContinueDrag(MouseEventArgs e)
    {
        if (_dragFrom < 0 || _dragTile is null)
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ResetDrag();
            return;
        }

        var x = e.GetPosition(_strip).X;
        if (!_dragActive && Math.Abs(x - _dragStartX) < 6)
            return;
        _dragActive = true;

        var target = IndexAt(x);
        if (target == _dragTarget)
            return;
        _dragTarget = target;
        PaintDragTarget();
    }

    private void EndDrag(MouseButtonEventArgs e)
    {
        if (_dragFrom < 0)
            return;

        var from = _dragFrom;
        var to = _dragTarget;
        var moved = _dragActive && to >= 0 && to != from;
        ResetDrag();
        if (moved)
        {
            _order.Move(from, to);
            RebuildStrip();
            Paint();
        }
        e.Handled = true;
    }

    private int IndexAt(double x)
    {
        var nearest = 0;
        var nearestDistance = double.MaxValue;
        for (var i = 0; i < _tiles.Count; i++)
        {
            var left = _tiles[i].Tile.TransformToAncestor(_strip).Transform(new Point(0, 0)).X;
            var centre = left + _tiles[i].Tile.ActualWidth / 2;
            var distance = Math.Abs(x - centre);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = i;
            }
        }
        return nearest;
    }

    private void PaintDragTarget()
    {
        for (var i = 0; i < _tiles.Count; i++)
        {
            var selected = _dragActive && i == _dragTarget;
            _tiles[i].Outline.BorderBrush = selected ? Theme.Accent : Brushes.Transparent;
            _tiles[i].Tile.Opacity = _dragActive && i == _dragFrom ? 0.55 : 1;
        }
    }

    private void ResetDrag()
    {
        if (_dragTile is { IsMouseCaptured: true })
            _dragTile.ReleaseMouseCapture();
        _dragTile = null;
        _dragFrom = -1;
        _dragTarget = -1;
        _dragActive = false;
        foreach (var (tile, outline) in _tiles)
        {
            outline.BorderBrush = Brushes.Transparent;
            tile.Opacity = 1;
        }
    }

    private void ShowError(string message)
    {
        _placeholder.Visibility = Visibility.Visible;
        _placeholder.Child = new TextBlock
        {
            Text = message,
            Foreground = Theme.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(16),
        };
    }

    private static string Label(CollageSize size) =>
        $"{size.Width.ToString(CultureInfo.InvariantCulture)} × {size.Height.ToString(CultureInfo.InvariantCulture)}";

    private static Color ToWpf(CollageColor colour) => Color.FromRgb(colour.R, colour.G, colour.B);

    private static FrameworkElement Row(string label, FrameworkElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var text = new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = Theme.Muted,
            VerticalAlignment = VerticalAlignment.Center,
        };
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(text, 0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);
        return grid;
    }

    private static LoadedImage Load(FileItem item)
    {
        using var image = new MagickImage(item.Path);
        image.AutoOrient();
        var width = (int)image.Width;
        var height = (int)image.Height;
        return new LoadedImage(item, width, height, Decode(image, PreviewMaxSide), Decode(image, ThumbMaxSide));
    }

    private static BitmapSource Decode(MagickImage image, int maxSide)
    {
        using var copy = image.Clone();
        var longSide = Math.Max((int)copy.Width, (int)copy.Height);
        if (longSide > maxSide)
        {
            var scale = (double)maxSide / longSide;
            copy.Resize(new MagickGeometry(
                (uint)Math.Max(1, (int)Math.Round(copy.Width * scale)),
                (uint)Math.Max(1, (int)Math.Round(copy.Height * scale))));
        }

        using var stream = new MemoryStream(copy.ToByteArray(MagickFormat.Png));
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private sealed record LoadedImage(FileItem Item, int Width, int Height, BitmapSource Preview, BitmapSource Thumb);

    /// <summary>A row of mutually exclusive pills with a programmatic selection, so Reset can repaint it.</summary>
    private sealed class ChipSet<T>
    {
        private readonly List<(Border Pill, TextBlock Text, T Value)> _pills = [];
        private readonly Action<T> _changed;

        public ChipSet(IReadOnlyList<(string Label, T Value)> options, T selected, Action<T> changed)
        {
            _changed = changed;
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (label, value) in options)
            {
                var text = new TextBlock { Text = label, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
                var pill = new Border
                {
                    CornerRadius = new CornerRadius(7),
                    Padding = new Thickness(11, 5, 11, 6),
                    Background = Brushes.Transparent,
                    Cursor = Cursors.Hand,
                    Child = text,
                };
                var captured = value;
                pill.MouseLeftButtonUp += (_, _) =>
                {
                    Select(captured);
                    _changed(captured);
                };
                _pills.Add((pill, text, value));
                panel.Children.Add(pill);
            }

            View = new Border
            {
                Background = Theme.Track,
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = panel,
            };
            Select(selected);
        }

        public FrameworkElement View { get; }

        /// <summary>Highlights the matching option; a value that matches none leaves every pill unselected.</summary>
        public void Select(T value)
        {
            foreach (var (pill, text, option) in _pills)
            {
                var on = EqualityComparer<T>.Default.Equals(option, value);
                pill.Background = on ? Theme.AccentGradient : Brushes.Transparent;
                text.Foreground = on ? Theme.TextOnAccent : Theme.Text;
                text.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }
    }

    /// <summary>A labelled slider whose value Reset can set, unlike the settings-only <see cref="Controls.Slider"/>.</summary>
    private sealed class SliderRow
    {
        private readonly Grid _surface = new() { Height = 22, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        private readonly Border _fill;
        private readonly Ellipse _thumb;
        private readonly TextBlock _valueText;
        private readonly int _min;
        private readonly int _max;
        private readonly Action<int> _changed;
        private int _value;

        public SliderRow(string label, int min, int max, int value, Action<int> changed)
        {
            _min = min;
            _max = Math.Max(min + 1, max);
            _changed = changed;
            _value = Math.Clamp(value, min, _max);
            _fill = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Theme.AccentGradient, HorizontalAlignment = HorizontalAlignment.Left };
            _thumb = new Ellipse
            {
                Width = 16,
                Height = 16,
                Fill = Brushes.White,
                Stroke = Theme.PanelBorder,
                StrokeThickness = 1,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            _surface.Children.Add(new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Theme.Track });
            _surface.Children.Add(_fill);
            _surface.Children.Add(_thumb);
            _surface.SizeChanged += (_, _) => Show();
            _surface.MouseLeftButtonDown += (_, e) =>
            {
                _surface.CaptureMouse();
                SetFrom(e.GetPosition(_surface).X);
            };
            _surface.MouseMove += (_, e) =>
            {
                if (_surface.IsMouseCaptured)
                    SetFrom(e.GetPosition(_surface).X);
            };
            _surface.MouseLeftButtonUp += (_, _) => _surface.ReleaseMouseCapture();

            _valueText = new TextBlock
            {
                Width = 30,
                FontSize = 12,
                Foreground = Theme.Text,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var labelText = new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = Theme.Muted,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var track = new Grid { Margin = new Thickness(0, 0, 10, 0) };
            track.Children.Add(_surface);
            Grid.SetColumn(labelText, 0);
            Grid.SetColumn(track, 1);
            Grid.SetColumn(_valueText, 2);
            grid.Children.Add(labelText);
            grid.Children.Add(track);
            grid.Children.Add(_valueText);
            View = grid;
            Show();
        }

        public FrameworkElement View { get; }

        public int Value => _value;

        public void Set(int value)
        {
            _value = Math.Clamp(value, _min, _max);
            Show();
        }

        private void SetFrom(double x)
        {
            var width = Math.Max(1, _surface.ActualWidth);
            var t = Math.Clamp((x - 8) / Math.Max(1, width - 16), 0, 1);
            var value = (int)Math.Round(_min + t * (_max - _min));
            if (value == _value)
                return;
            _value = value;
            Show();
            _changed(value);
        }

        private void Show()
        {
            var width = Math.Max(1, _surface.ActualWidth);
            var t = (_value - _min) / (double)(_max - _min);
            _fill.Width = Math.Max(0, t * width);
            _thumb.Margin = new Thickness(Math.Max(0, t * (width - 16)), 0, 0, 0);
            _valueText.Text = _value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
