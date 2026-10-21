using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using FileAction.App.Shell;
using FileAction.App.UI;
using FileAction.Core;
using ImageMagick;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Image.Frame;

/// <summary>The decoded preview plus the photo's real size (after EXIF orientation).</summary>
internal readonly record struct AddBackgroundSource(BitmapSource Bitmap, int Width, int Height);

/// <summary>
/// The Add Background window content: a live composition of the photo over the chosen backdrop, with
/// gradient/colour/image backgrounds, padding/corner/shadow sliders in preview pixels, ratio chips, and a
/// save that repeats the same composition on the original file. Everything numeric comes from
/// <see cref="AddBackground"/>, so the window has no geometry of its own.
/// </summary>
internal sealed class AddBackgroundView
{
    private static readonly BackgroundColor CustomDefault = new(0x82, 0x24, 0xD4);

    private readonly FileItem _item;
    private readonly DropTargets? _targets;
    private readonly Task<AddBackgroundSource> _load;
    private readonly TaskCompletionSource<MediaSize?> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly Grid _stageHost = new();
    private readonly Canvas _layer = new();
    private readonly Border _stage;
    private readonly Border _background;
    private readonly Border _photo;
    private readonly Border _placeholder;
    private readonly StackPanel _sliderHost = new();
    private readonly StackPanel _ratioHost = new();
    private readonly List<(Border Swatch, GradientPreset Preset)> _gradientSwatches = [];
    private readonly List<(Border Swatch, BackgroundColor Color)> _solidSwatches = [];
    private Border? _customSwatch;
    private TextBlock? _imageName;
    private Border? _clearImage;
    private FrameworkElement? _blurToggle;
    private TextBlock? _blurLabel;

    private BackgroundKind _kind = BackgroundKind.Gradient;
    private GradientPreset _gradient = GradientPreset.Violet;
    private BackgroundColor _solid = AddBackground.SolidColors[0];
    private bool _solidIsCustom;
    private BackgroundColor _custom = CustomDefault;
    private string? _imagePath;
    private BitmapSource? _imageBitmap;
    private bool _blur = true;
    private int _padding = AddBackground.DefaultPadding;
    private int _corners = AddBackground.DefaultCorners;
    private int _shadow = AddBackground.DefaultShadow;
    private BackgroundRatio _ratio = BackgroundRatio.Auto;

    private int _originalWidth;
    private int _originalHeight;
    private int _previewWidth;
    private int _previewHeight;
    private BackgroundComposition _composition;
    private bool _loaded;

    public AddBackgroundView(FileItem item, DropTargets? targets)
    {
        _item = item;
        _targets = targets;

        _stage = new Border
        {
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FlowDirection = System.Windows.FlowDirection.LeftToRight,
            Visibility = Visibility.Visible,
        };
        _background = new Border();
        _photo = new Border { Background = Brushes.Transparent };
        _layer.Children.Add(_background);
        _layer.Children.Add(_photo);
        _stage.Child = _layer;

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
                        Text = T("tool.addbackground.loading"),
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

        // Decode on a background thread: the window opens at once and the preview appears when ready.
        _load = Task.Run(() => Load(item.Path));
    }

    public ToolContent Build()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var preview = BuildPreview();
        var controls = BuildControls();
        Grid.SetRow(preview, 0);
        Grid.SetRow(controls, 1);
        root.Children.Add(preview);
        root.Children.Add(controls);
        Paint();
        return new ToolContent(root, Reset, ApplyAsync, _ready.Task, ActivateAsync);
    }

    /// <summary>Shows the decoded photo and lets the window size itself to the composition.</summary>
    public async Task ActivateAsync()
    {
        try
        {
            var source = await _load;
            _originalWidth = source.Width;
            _originalHeight = source.Height;
            _previewWidth = source.Bitmap.PixelWidth;
            _previewHeight = source.Bitmap.PixelHeight;
            _photo.Background = new ImageBrush(source.Bitmap)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center,
            };
            _loaded = true;
            _placeholder.Visibility = Visibility.Collapsed;
            Paint();
            Relayout();
            var composition = AddBackground.Compose(_previewWidth, _previewHeight, _padding, _corners, _shadow, _ratio);
            _ready.TrySetResult(new MediaSize(composition.CanvasWidth, composition.CanvasHeight));
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
        var stageGrid = new Grid();
        stageGrid.Children.Add(_stage);
        stageGrid.Children.Add(new Grid { Children = { _placeholder } });
        _stageHost.Children.Add(new Grid { Children = { stageGrid } });
        _stageHost.SizeChanged += (_, _) => Relayout();

        return new Border
        {
            Background = Theme.Card,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 4, 0, 10),
            Child = _stageHost,
        };
    }

    private FrameworkElement BuildControls()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

        // Gradients: nine swatches; the selected one is outlined.
        var gradients = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var preset in Enum.GetValues<GradientPreset>())
        {
            var swatch = CreateSwatch(GradientBrush(preset), () =>
            {
                _kind = BackgroundKind.Gradient;
                _gradient = preset;
                Paint();
                Relayout();
            });
            _gradientSwatches.Add((swatch, preset));
            gradients.Children.Add(swatch);
        }
        stack.Children.Add(LabeledRow("tool.addbackground.gradients", gradients));

        // Solids: nine presets plus the custom colour swatch.
        var solids = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var color in AddBackground.SolidColors)
        {
            var value = color;
            var swatch = CreateSwatch(new SolidColorBrush(ToColor(value)), () =>
            {
                _kind = BackgroundKind.Solid;
                _solid = value;
                _solidIsCustom = false;
                Paint();
                Relayout();
            });
            _solidSwatches.Add((swatch, value));
            solids.Children.Add(swatch);
        }

        _customSwatch = CreateSwatch(new SolidColorBrush(ToColor(_custom)), PickCustomColor);
        _customSwatch.ToolTip = UiSetup.Tip(T("tool.addbackground.color.custom"));
        _customSwatch.Child = new TextBlock
        {
            Text = "+",
            FontSize = 12,
            Foreground = Theme.TextOnAccent,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        solids.Children.Add(_customSwatch);
        stack.Children.Add(LabeledRow("tool.addbackground.colors", solids));

        stack.Children.Add(BuildImageRow());
        stack.Children.Add(_sliderHost);
        BuildSliders();
        stack.Children.Add(LabeledRow("tool.addbackground.ratio", _ratioHost));
        BuildRatios();
        return stack;
    }

    private FrameworkElement BuildImageRow()
    {
        var inner = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        inner.Children.Add(Controls.Button(T("tool.addbackground.image.choose"), PickImage));

        _imageName = new TextBlock
        {
            FontSize = 11.5,
            Foreground = Theme.Muted,
            MaxWidth = 104,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        inner.Children.Add(_imageName);

        _blurLabel = new TextBlock
        {
            Text = T("tool.addbackground.image.blur"),
            FontSize = 11.5,
            Foreground = Theme.Muted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 6, 0),
            Opacity = 0.45,
        };
        inner.Children.Add(_blurLabel);

        _blurToggle = Controls.Toggle(_blur, value =>
        {
            _blur = value;
            if (_imagePath is not null)
                _kind = BackgroundKind.Image;
            Paint();
            Relayout();
        });
        _blurToggle.IsEnabled = false;
        inner.Children.Add(_blurToggle);

        _clearImage = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = Theme.Track,
            Margin = new Thickness(10, 0, 0, 0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            ToolTip = UiSetup.Tip(T("tool.addbackground.image.clear")),
            Child = new TextBlock
            {
                Text = "\uE711",
                FontFamily = Theme.Icons,
                FontSize = 9,
                Foreground = Theme.Text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        _clearImage.MouseLeftButtonUp += (_, _) => ClearImage();
        inner.Children.Add(_clearImage);

        return LabeledRow("tool.addbackground.image", inner);
    }

    private static Grid LabeledRow(string key, FrameworkElement content)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var label = RowLabel(key);
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);
        return grid;
    }

    private static TextBlock RowLabel(string key) => new()
    {
        Text = T(key),
        Width = 86,
        FontSize = 11.5,
        Foreground = Theme.Muted,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        Margin = new Thickness(0, 0, 6, 0),
    };

    private Border CreateSwatch(Brush brush, Action click)
    {
        var swatch = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(7),
            Background = brush,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 6, 5),
            Cursor = Cursors.Hand,
        };
        swatch.MouseLeftButtonUp += (_, _) => click();
        return swatch;
    }

    private void BuildSliders()
    {
        _sliderHost.Children.Clear();
        _sliderHost.Children.Add(SliderRow("tool.addbackground.padding", AddBackground.MaxPadding, _padding, value =>
        {
            _padding = value;
            Relayout();
        }));
        _sliderHost.Children.Add(SliderRow("tool.addbackground.corners", AddBackground.MaxCorners, _corners, value =>
        {
            _corners = value;
            Relayout();
        }));
        _sliderHost.Children.Add(SliderRow("tool.addbackground.shadow", AddBackground.MaxShadow, _shadow, value =>
        {
            _shadow = value;
            Relayout();
        }));
    }

    private static FrameworkElement SliderRow(string key, int max, int value, Action<int> changed)
    {
        var row = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 1, 0, 1) };
        var label = RowLabel(key);
        DockPanel.SetDock(label, Dock.Left);
        var slider = Controls.Slider(0, max, value, changed, committed: () => { });
        DockPanel.SetDock(slider, Dock.Right);
        row.Children.Add(label);
        row.Children.Add(slider);
        return row;
    }

    private void BuildRatios()
    {
        _ratioHost.Children.Clear();
        var options = new List<(string Label, BackgroundRatio Value)>
        {
            (T("tool.addbackground.ratio.auto"), BackgroundRatio.Auto),
            (T("tool.addbackground.ratio.1x1"), BackgroundRatio.Square),
            (T("tool.addbackground.ratio.16x9"), BackgroundRatio.Wide),
            (T("tool.addbackground.ratio.4x5"), BackgroundRatio.Portrait),
        };
        _ratioHost.Children.Add(Controls.Segmented(options, _ratio, value =>
        {
            _ratio = value;
            Relayout();
        }));
    }

    // ------------------------------------------------------------------ painting

    private void Paint()
    {
        PaintBackground();
        PaintSwatches();
        PaintImageRow();
    }

    private void PaintBackground()
    {
        _background.Effect = null;
        switch (EffectiveKind())
        {
            case BackgroundKind.Image when _imageBitmap is not null:
                _background.Background = new ImageBrush(_imageBitmap)
                {
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center,
                };
                if (_blur)
                {
                    _background.Effect = new BlurEffect
                    {
                        Radius = 18,
                        KernelType = KernelType.Gaussian,
                        RenderingBias = RenderingBias.Performance,
                    };
                }
                break;
            case BackgroundKind.Solid:
                _background.Background = new SolidColorBrush(ToColor(_solid));
                break;
            default:
                _background.Background = GradientBrush(_gradient);
                break;
        }
    }

    private void PaintSwatches()
    {
        foreach (var (swatch, preset) in _gradientSwatches)
        {
            var stops = AddBackground.Gradient(preset);
            Outline(swatch, _kind == BackgroundKind.Gradient && _gradient == preset, Average(stops));
        }

        foreach (var (swatch, color) in _solidSwatches)
            Outline(swatch, _kind == BackgroundKind.Solid && !_solidIsCustom && _solid == color, color);

        if (_customSwatch is not null)
        {
            _customSwatch.Background = new SolidColorBrush(ToColor(_custom));
            Outline(_customSwatch, _kind == BackgroundKind.Solid && _solidIsCustom, _custom);
            if (_customSwatch.Child is TextBlock plus)
                plus.Foreground = new SolidColorBrush(ContrastOn(_custom));
        }
    }

    /// <summary>
    /// The selection ring is drawn against the swatch's own colour, not the theme's text colour: a white
    /// custom colour in the light theme (or a black one in the dark theme) would otherwise lose it.
    /// </summary>
    private static void Outline(Border swatch, bool selected, BackgroundColor color)
    {
        swatch.BorderBrush = selected ? new SolidColorBrush(ContrastOn(color)) : Theme.PanelBorder;
        swatch.BorderThickness = new Thickness(selected ? 2 : 1);
    }

    /// <summary>Black or white, whichever stays readable on this backdrop colour in either theme.</summary>
    private static Color ContrastOn(BackgroundColor color) =>
        (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) > 150
            ? Color.FromRgb(0x12, 0x12, 0x12)
            : Colors.White;

    private static BackgroundColor Average(GradientStops stops) => new(
        (byte)((stops.Start.R + stops.End.R) / 2),
        (byte)((stops.Start.G + stops.End.G) / 2),
        (byte)((stops.Start.B + stops.End.B) / 2));

    private void PaintImageRow()
    {
        var hasImage = _imagePath is not null && _imageBitmap is not null;
        if (_imageName is not null)
        {
            _imageName.Text = hasImage ? Bidi.Value(Path.GetFileName(_imagePath)) : string.Empty;
            _imageName.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
        }
        if (_clearImage is not null)
            _clearImage.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
        if (_blurToggle is not null)
        {
            _blurToggle.IsEnabled = hasImage;
            // A disabled Border would otherwise keep its full-strength accent and look usable.
            _blurToggle.Opacity = hasImage ? 1 : 0.45;
        }
        if (_blurLabel is not null)
            _blurLabel.Opacity = hasImage ? 1 : 0.45;
    }

    /// <summary>Layout: the composition is fitted into the preview card and every layer is placed from it.</summary>
    private void Relayout()
    {
        if (!_loaded || _previewWidth <= 0)
            return;

        _composition = AddBackground.Compose(_previewWidth, _previewHeight, _padding, _corners, _shadow, _ratio);
        var availableWidth = Math.Max(1, _stageHost.ActualWidth - 12);
        var availableHeight = Math.Max(1, _stageHost.ActualHeight - 12);
        var scale = Math.Min(availableWidth / _composition.CanvasWidth, availableHeight / _composition.CanvasHeight);
        if (!(scale > 0) || double.IsInfinity(scale))
            scale = 1;

        _stage.Width = Math.Max(1, _composition.CanvasWidth * scale);
        _stage.Height = Math.Max(1, _composition.CanvasHeight * scale);

        // A blurred background has to reach past the canvas, otherwise its edges fade transparent.
        var blurRadius = EffectiveKind() == BackgroundKind.Image && _blur
            ? Math.Max(8, _composition.CanvasWidth * scale * 0.03)
            : 0;
        var overscan = (int)Math.Ceiling(blurRadius);
        _background.Width = Math.Max(1, _composition.BackgroundWidth * scale + 2 * overscan);
        _background.Height = Math.Max(1, _composition.BackgroundHeight * scale + 2 * overscan);
        Canvas.SetLeft(_background, _composition.BackgroundX * scale - overscan);
        Canvas.SetTop(_background, _composition.BackgroundY * scale - overscan);
        if (_background.Effect is BlurEffect blur)
            blur.Radius = Math.Max(6, blurRadius);

        _photo.Width = Math.Max(1, _composition.ImageWidth * scale);
        _photo.Height = Math.Max(1, _composition.ImageHeight * scale);
        Canvas.SetLeft(_photo, _composition.ImageX * scale);
        Canvas.SetTop(_photo, _composition.ImageY * scale);
        _photo.CornerRadius = new CornerRadius(Math.Max(0, _composition.CornerRadius * scale));
        _photo.Effect = _composition.ShadowBlur > 0 && _composition.ShadowOpacity > 0
            ? new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = Math.Max(1, _composition.ShadowBlur * scale),
                ShadowDepth = Math.Max(0, _composition.ShadowOffsetY * scale),
                Direction = 270,
                Opacity = _composition.ShadowOpacity,
            }
            : null;
    }

    // ------------------------------------------------------------------ actions

    private void Reset()
    {
        _kind = BackgroundKind.Gradient;
        _gradient = GradientPreset.Violet;
        _solid = AddBackground.SolidColors[0];
        _solidIsCustom = false;
        _custom = CustomDefault;
        _imagePath = null;
        _imageBitmap = null;
        _blur = true;
        _padding = AddBackground.DefaultPadding;
        _corners = AddBackground.DefaultCorners;
        _shadow = AddBackground.DefaultShadow;
        _ratio = BackgroundRatio.Auto;
        BuildSliders();
        BuildRatios();
        Paint();
        Relayout();
    }

    private async Task<string?> ApplyAsync()
    {
        if (!_loaded || _previewWidth <= 0 || _originalWidth <= 0 || _originalHeight <= 0
            || !AddBackgroundTool.Instance.Applies([_item]))
            return null;

        // The sliders are preview pixels; the export multiplies them so the frame keeps its proportions.
        var scale = AddBackground.ScaleFor(_originalWidth, _previewWidth);
        var padding = AddBackground.Scale(_padding, scale);
        var corners = AddBackground.Scale(_corners, scale);
        var shadow = AddBackground.Scale(_shadow, scale);
        var fill = CurrentFill();
        var directory = OutputDirectory();
        var baseName = $"{_item.BaseName} {T("tool.addbackground.suffix")}";

        return await OutputPaths.ProduceAsync(directory, baseName, "png",
            temp => Task.Run(() => AddBackgroundRenderer.Render(_item.Path, fill, padding, corners, shadow, _ratio, temp)));
    }

    private async void PickImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = T("tool.addbackground.image.dialogTitle"),
            Filter = $"{T("tool.addbackground.image.filter")}|*.png;*.jpg;*.jpeg;*.webp;*.avif;*.tif;*.tiff;*.bmp;*.gif;*.ico",
            CheckFileExists = true,
        };
        var owner = Window.GetWindow(_stageHost);
        var picked = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        if (picked != true)
            return;

        try
        {
            var bitmap = await Task.Run(() => DecodeBackground(dialog.FileName));
            _imageBitmap = bitmap;
            _imagePath = dialog.FileName;
            _kind = BackgroundKind.Image;
            Paint();
            Relayout();
        }
        catch (Exception ex)
        {
            Notifications.Show(T("app.error.title"), T("tool.addbackground.image.failed", ex.Message), error: true);
        }
    }

    private void ClearImage()
    {
        _imagePath = null;
        _imageBitmap = null;
        if (_kind == BackgroundKind.Image)
            _kind = BackgroundKind.Gradient;
        Paint();
        Relayout();
    }

    private void PickCustomColor()
    {
        var picked = AddBackgroundColorDialog.Pick(Window.GetWindow(_stageHost), _custom);
        if (picked is not { } color)
            return;
        _custom = color;
        _solid = color;
        _solidIsCustom = true;
        _kind = BackgroundKind.Solid;
        Paint();
        Relayout();
    }

    private BackgroundKind EffectiveKind() =>
        _kind == BackgroundKind.Image && (_imagePath is null || _imageBitmap is null)
            ? BackgroundKind.Gradient
            : _kind;

    private BackgroundFill CurrentFill() => EffectiveKind() switch
    {
        BackgroundKind.Image when _imagePath is not null => BackgroundFill.OfImage(_imagePath, _blur),
        BackgroundKind.Solid => BackgroundFill.OfColor(_solid),
        _ => BackgroundFill.OfGradient(_gradient),
    };

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
    /// Reads the image, applies EXIF orientation, keeps the original pixel size for the export and
    /// decodes a preview of at most <see cref="AddBackground.PreviewMaxSide"/> on the long side.
    /// </summary>
    private static AddBackgroundSource Load(string path)
    {
        using var image = Read(path);
        var width = (int)image.Width;
        var height = (int)image.Height;
        return new AddBackgroundSource(Decode(image), width, height);
    }

    /// <summary>The background image only needs the bitmap; its real size never reaches the export.</summary>
    private static BitmapSource DecodeBackground(string path)
    {
        using var image = Read(path);
        return Decode(image);
    }

    /// <summary>First frame only, EXIF orientation applied; nothing is resampled before <see cref="Decode"/>.</summary>
    private static MagickImage Read(string path)
    {
        var image = new MagickImage(path, new MagickReadSettings { FrameIndex = 0, FrameCount = 1 });
        image.AutoOrient();
        return image;
    }

    private static BitmapSource Decode(MagickImage image)
    {
        var width = (int)image.Width;
        var height = (int)image.Height;
        var longSide = Math.Max(width, height);
        if (longSide > AddBackground.PreviewMaxSide)
        {
            var scale = AddBackground.PreviewMaxSide / (double)longSide;
            image.Resize(new MagickGeometry(
                (uint)Math.Max(1, (int)Math.Round(width * scale)),
                (uint)Math.Max(1, (int)Math.Round(height * scale))));
        }

        using var stream = new MemoryStream(image.ToByteArray(MagickFormat.Png));
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    // ------------------------------------------------------------------ helpers

    private static Color ToColor(BackgroundColor color) => Color.FromRgb(color.R, color.G, color.B);

    private static LinearGradientBrush GradientBrush(GradientPreset preset)
    {
        var stops = AddBackground.Gradient(preset);
        return new LinearGradientBrush(ToColor(stops.Start), ToColor(stops.End), new Point(0.5, 0), new Point(0.5, 1));
    }
}
