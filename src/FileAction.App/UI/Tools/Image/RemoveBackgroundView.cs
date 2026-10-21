using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FileAction.App.UI;
using FileAction.App.UI.Tools;
using FileAction.App.UI.Tools.Image.Frame;
using FileAction.Core;
using FileAction.Core.Backends;
using ImageMagick;
using static FileAction.Core.Loc;
using WpfImage = System.Windows.Controls.Image;

namespace FileAction.App.UI.Tools.Image;

/// <summary>
/// The Remove BG window content: a checkerboard preview of the cut-out, Auto or explicit background-key
/// selection, tolerance and feather sliders, a Reset and a PNG save. The preview downsampling and the
/// saved file use the same sampled key, tolerance and feather, so the checkerboard is honest about the
/// result. The image is only read; a failed write never leaves a partial file.
/// </summary>
internal sealed class RemoveBackgroundView
{
    /// <summary>Longest preview side: big enough to judge the rim, small enough for slider-speed repaints.</summary>
    private const int PreviewMaxSide = 1600;

    private readonly FileItem _item;
    private readonly DropTargets? _targets;
    private readonly Task<PreviewSource> _load;
    private readonly TaskCompletionSource<MediaSize?> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ImageGateGrid _root = new();
    private readonly Grid _preview = new() { ClipToBounds = true };
    private readonly WpfImage _imageView = new() { Stretch = Stretch.Uniform, IsHitTestVisible = false };
    private readonly Border _placeholder;
    private readonly StackPanel _sliderHost = new();
    private readonly DispatcherTimer _repaintTimer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private readonly Border _keySwatch;

    private Action<bool>? _selectMode;
    private byte[]? _previewPng;
    private BackgroundColor _sampledKey;
    private BackgroundColor _customKey;
    private bool _customSet;
    private bool _auto = true;
    private int _fuzz = RemoveBackground.DefaultFuzz;
    private int _feather = RemoveBackground.DefaultFeather;
    private int _previewWidth;
    private int _previewHeight;
    private int _renderVersion;
    private bool _loaded;

    public RemoveBackgroundView(FileItem item, DropTargets? targets)
    {
        _item = item;
        _targets = targets;

        _placeholder = new Border
        {
            Background = Theme.Card,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = T("tool.removebackground.loading"),
                        FontSize = 12,
                        Foreground = Theme.Muted,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                    new ProgressBar
                    {
                        IsIndeterminate = true,
                        Width = 120,
                        Height = 4,
                        Margin = new Thickness(0, 10, 0, 0),
                    },
                },
            },
        };

        _keySwatch = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(7),
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(6, 0, 6, 0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _keySwatch.MouseLeftButtonUp += (_, _) => PickColor();

        // The key is sampled and the first cut-out is rendered off the UI thread, so the window opens at
        // once and Apply stays disabled until the preview is really there.
        _load = Task.Run(() => Load(item.Path));
    }

    public ToolContent Build()
    {
        var root = _root;
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var preview = BuildPreview();
        var controls = BuildControls();
        Grid.SetRow(preview, 0);
        Grid.SetRow(controls, 1);
        root.Children.Add(preview);
        root.Children.Add(controls);

        _repaintTimer.Tick += (_, _) =>
        {
            _repaintTimer.Stop();
            _ = RepaintAsync();
        };

        PaintKey();
        return new ToolContent(root, Reset, ApplyAsync, _ready.Task, ActivateAsync);
    }

    /// <summary>Shows the decoded cut-out and lets the window size itself to the photo.</summary>
    public async Task ActivateAsync()
    {
        try
        {
            var source = await _load;
            _previewPng = source.Png;
            _previewWidth = source.Width;
            _previewHeight = source.Height;
            _sampledKey = source.Sampled;
            _customKey = source.Sampled;
            _customSet = true;
            _loaded = true;
            _placeholder.Visibility = Visibility.Collapsed;
            PaintKey();

            // Render once before opening the gate: Apply must never run against an unrendered preview.
            var bitmap = await Task.Run(() => Render(source.Png, EffectiveKey(), _fuzz, _feather));
            _imageView.Source = bitmap;
            _root.Ready();
            _ready.TrySetResult(new MediaSize(source.Width, source.Height));
        }
        catch (Exception ex)
        {
            _placeholder.Child = new TextBlock
            {
                Text = ex.Message,
                Foreground = Theme.Muted,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 320,
                Margin = new Thickness(16),
            };
            _ready.TrySetResult(null);
        }
    }

    // ------------------------------------------------------------------ view

    private FrameworkElement BuildPreview()
    {
        // The checkerboard is the transparent background: every pixel the removal clears shows through.
        var surface = new Grid { Background = Checkerboard() };
        surface.Children.Add(_imageView);
        surface.Children.Add(new Grid { Children = { _placeholder } });
        _preview.Background = Theme.Track;
        _preview.Children.Add(surface);
        _preview.SizeChanged += (_, _) => ClipPreview();
        _preview.ToolTip = UiSetup.Tip(T("tool.removebackground.checkerboard"));

        return new Border
        {
            Background = Theme.Card,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 4, 0, 10),
            Child = _preview,
        };
    }

    /// <summary>Rounds the media surface to the card's curve (the card pads it by 6 DIP).</summary>
    private void ClipPreview()
    {
        var width = _preview.ActualWidth;
        var height = _preview.ActualHeight;
        if (width <= 0 || height <= 0)
            return;
        _preview.Clip = new RectangleGeometry(new Rect(0, 0, width, height), Design.Radius.Card, Design.Radius.Card);
    }

    private FrameworkElement BuildControls()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        stack.Children.Add(StackedRow("tool.removebackground.background", BuildBackgroundRow()));
        BuildSliders();
        stack.Children.Add(_sliderHost);

        var hint = ToolControls.Label(T("tool.removebackground.checkerboard"), 11.5);
        hint.Margin = new Thickness(0, 2, 0, 0);
        hint.Opacity = 0.85;
        stack.Children.Add(hint);

        // A narrow portrait window can leave less height than the rows need; scroll instead of clipping.
        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            Content = stack,
        };
    }

    private FrameworkElement BuildBackgroundRow()
    {
        // A wrap panel: "Auto", the key swatch and "Choose color…" reflow on a narrow window instead of
        // running past the content card's right edge.
        var row = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        var chips = ToolControls.Chips(
            [(T("tool.removebackground.auto"), true), (T("tool.removebackground.color"), false)],
            _auto,
            SetAutoMode);
        _selectMode = chips.Select;
        row.Children.Add(chips.Element);
        row.Children.Add(_keySwatch);
        row.Children.Add(Controls.Button(T("tool.removebackground.pick"), PickColor));
        return row;
    }

    private void BuildSliders()
    {
        _sliderHost.Children.Clear();
        _sliderHost.Children.Add(SliderRow("tool.removebackground.tolerance",
            RemoveBackground.MinFuzz, RemoveBackground.MaxFuzz, _fuzz, value =>
            {
                _fuzz = value;
                ScheduleRepaint();
            }));
        _sliderHost.Children.Add(SliderRow("tool.removebackground.feather",
            RemoveBackground.MinFeather, RemoveBackground.MaxFeather, _feather, value =>
            {
                _feather = value;
                ScheduleRepaint();
            }));
    }

    private static FrameworkElement SliderRow(string key, int min, int max, int value, Action<int> changed)
    {
        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 2) };
        var label = RowLabel(key);
        label.Margin = new Thickness(0, 0, 0, 1);
        row.Children.Add(label);
        row.Children.Add(Controls.Slider(min, max, value, changed, () => { }));
        return row;
    }

    private static FrameworkElement StackedRow(string key, FrameworkElement content, double bottom = 6)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, bottom) };
        var label = RowLabel(key);
        label.Margin = new Thickness(0, 0, 0, 3);
        stack.Children.Add(label);
        stack.Children.Add(content);
        return stack;
    }

    private static TextBlock RowLabel(string key) => new()
    {
        Text = T(key),
        FontSize = 11.5,
        Foreground = Theme.Muted,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>A checkerboard that stays visible in both themes; the color values are deliberately neutral.</summary>
    private static DrawingBrush Checkerboard()
    {
        var light = new SolidColorBrush(Theme.IsDark ? Color.FromRgb(0x39, 0x39, 0x3F) : Color.FromRgb(0xE9, 0xE9, 0xEE));
        var dark = new SolidColorBrush(Theme.IsDark ? Color.FromRgb(0x29, 0x29, 0x2E) : Color.FromRgb(0xD4, 0xD4, 0xDA));
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(light, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        group.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
        group.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(8, 8, 8, 8))));

        var brush = new DrawingBrush
        {
            Drawing = group,
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 16, 16),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
        brush.Freeze();
        return brush;
    }

    // ------------------------------------------------------------------ state

    private BackgroundColor EffectiveKey() => _auto || !_customSet ? _sampledKey : _customKey;

    private void SetAutoMode(bool auto)
    {
        _auto = auto;
        if (!auto && !_customSet)
        {
            _customKey = _sampledKey;
            _customSet = true;
        }
        PaintKey();
        ScheduleRepaint();
    }

    private void PaintKey()
    {
        var key = EffectiveKey();
        _keySwatch.Background = new SolidColorBrush(Color.FromRgb(key.R, key.G, key.B));
        _keySwatch.ToolTip = UiSetup.Tip(
            $"{T("tool.removebackground.background")} #{key.R:X2}{key.G:X2}{key.B:X2}");
    }

    private void PickColor()
    {
        var picked = AddBackgroundColorDialog.Pick(Window.GetWindow(_root), EffectiveKey());
        if (picked is not { } color)
            return;
        _customKey = color;
        _customSet = true;
        _auto = false;
        _selectMode?.Invoke(false);
        PaintKey();
        ScheduleRepaint();
    }

    private void Reset()
    {
        _auto = true;
        _fuzz = RemoveBackground.DefaultFuzz;
        _feather = RemoveBackground.DefaultFeather;
        if (_loaded)
        {
            _customKey = _sampledKey;
            _customSet = true;
        }
        _selectMode?.Invoke(true);
        BuildSliders();
        PaintKey();
        ScheduleRepaint();
    }

    private void ScheduleRepaint()
    {
        if (!_loaded)
            return;
        _repaintTimer.Stop();
        _repaintTimer.Start();
    }

    private async Task RepaintAsync()
    {
        if (!_loaded || _previewPng is null)
            return;

        var version = ++_renderVersion;
        try
        {
            var key = EffectiveKey();
            var fuzz = _fuzz;
            var feather = _feather;
            var png = _previewPng;
            var bitmap = await Task.Run(() => Render(png, key, fuzz, feather));
            if (version != _renderVersion || !_loaded)
                return;
            _imageView.Source = bitmap;
        }
        catch
        {
            // Keep the last good frame; a transient render failure must not clear the preview or the gate.
        }
    }

    // ------------------------------------------------------------------ actions

    private async Task<string?> ApplyAsync()
    {
        if (!_loaded || _previewPng is null || _previewWidth <= 0 || _previewHeight <= 0
            || !RemoveBackgroundTool.Instance.Applies([_item]))
            return null;

        var directory = OutputDirectory();
        var baseName = $"{_item.BaseName} ({T("tool.removebackground.suffix")})";
        // The same key the checkerboard used: the preview is the contract, not a second sample that could
        // move a pixel or two away from what the user approved. Full-resolution pixels are rebuilt from the
        // source file; only the colour key comes from the preview.
        var key = EffectiveKey();
        var fuzz = _fuzz;
        var feather = _feather;

        return await OutputPaths.ProduceAsync(directory, baseName, RemoveBackground.DefaultFormat, temporary => Task.Run(() =>
        {
            using var image = ImageBackend.Load(_item.Path);
            RemoveBackground.Apply(image, fuzz, key, feather);
            ImageBackend.Save(image, temporary, RemoveBackground.DefaultFormat);
        }));
    }

    /// <summary>Next to the source, or the folder this drop resolved (2.2.3 rules).</summary>
    private string OutputDirectory()
    {
        var detected = OutputTarget.Resolve(_item.Path, _targets?.UnderCursor, Directory.Exists, OutputTarget.CanWrite);
        if (!detected.UsesDetectedFolder && _targets?.Remembered is { } remembered)
        {
            var fallback = OutputTarget.Resolve(_item.Path, remembered, Directory.Exists, OutputTarget.CanWrite);
            if (fallback.UsesDetectedFolder)
                return fallback.Directory!;
        }
        return detected.Directory
            ?? OutputTarget.SourceFolder(_item.Path)
            ?? Environment.CurrentDirectory;
    }

    // ------------------------------------------------------------------ media

    /// <summary>
    /// Reads the first frame with EXIF orientation applied, samples the key before downsampling (the
    /// median border patches are colour data, not geometry), and keeps a PNG copy for the slider repaints.
    /// </summary>
    private static PreviewSource Load(string path)
    {
        using var image = ImageBackend.Load(path);
        var width = (int)image.Width;
        var height = (int)image.Height;
        var sampled = RemoveBackground.Sample(image);

        var longSide = Math.Max(width, height);
        if (longSide > PreviewMaxSide)
        {
            var scale = (double)PreviewMaxSide / longSide;
            image.Resize(new MagickGeometry(
                (uint)Math.Max(1, (int)Math.Round(width * scale)),
                (uint)Math.Max(1, (int)Math.Round(height * scale))));
        }

        return new PreviewSource(image.ToByteArray(MagickFormat.Png), sampled, width, height);
    }

    /// <summary>Applies the same cut-out at preview resolution and returns a frozen bitmap for WPF.</summary>
    private static BitmapSource Render(byte[] png, BackgroundColor key, int fuzz, int feather)
    {
        using var image = new MagickImage(png);
        RemoveBackground.Apply(image, fuzz, key, feather);
        using var stream = new MemoryStream(image.ToByteArray(MagickFormat.Png));
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private readonly record struct PreviewSource(byte[] Png, BackgroundColor Sampled, int Width, int Height);
}
