using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileAction.Core;
using FileAction.Core.Backends;
using Microsoft.Win32;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Audio;

/// <summary>
/// "Visualizer" on the Tools wheel: turn an audio file into an MP4 with an animated waveform (or a vector
/// scope), on black or over a still image, in landscape, portrait or square. The preview draws the chosen
/// frame and a sample of the animation. Apply writes "&lt;name&gt; Visualizer.mp4" next to the source.
/// </summary>
internal sealed class AudioVisualizerTool : IToolWindowSpec
{
    public static readonly AudioVisualizerTool Instance = new();

    public string Id => "tools.audiovisualizer";

    public string TitleKey => "tool.audiovisualizer.title";

    /// <summary>Three choice rows and a status line above the adaptive preview.</summary>
    public ToolSpecLayout Layout { get; } = new(
        ChromeWidth: 36,
        ChromeHeight: 280,
        PreviewMinWidth: 280,
        PreviewMinHeight: 200,
        PreviewMaxWidth: 484,
        PreviewMaxHeight: 420,
        DefaultPreviewWidth: 420,
        DefaultPreviewHeight: 320);

    public bool Applies(IReadOnlyList<FileItem> items) =>
        items.Count == 1 && !items[0].IsDirectory && Formats.Audio.Contains(items[0].Format);

    public ToolContent Create(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new AudioVisualizerView(items[0], targets).Build();
}

/// <summary>Registers the tool from its own file, so no shared list has to be edited.</summary>
internal static class AudioVisualizerToolRegistration
{
    [ModuleInitializer]
    internal static void Register() => ToolRegistry.Register(AudioVisualizerTool.Instance);
}

/// <summary>The visualizer window: orientation, style and background choices over an aspect-true preview.</summary>
internal sealed class AudioVisualizerView
{
    private readonly FileItem _item;
    private readonly DropTargets? _targets;
    private readonly ToolLocator _tools = new();
    private readonly TaskCompletionSource<MediaSize?> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _life = new();
    private readonly VisualizerPreview _preview = new();
    private readonly StackPanel _controls = new();
    private readonly TextBlock _sizeText = new();
    private readonly TextBlock _imageText = new();
    private readonly TextBlock _outputName = new();
    private readonly TextBlock _status = AudioToolChrome.Status();

    private AudioVisualizerOrientation _orientation = AudioVisualizerOrientation.Landscape;
    private AudioVisualizerStyle _style = AudioVisualizerStyle.Waveform;
    private bool _useImage;
    private string? _imagePath;
    private BitmapSource? _image;

    public AudioVisualizerView(FileItem item, DropTargets? targets)
    {
        _item = item;
        _targets = targets;
    }

    public ToolContent Build()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var preview = AudioToolChrome.PreviewSurface(_preview);
        FillControls();
        Grid.SetRow(preview, 0);
        Grid.SetRow(_controls, 1);
        root.Children.Add(preview);
        root.Children.Add(_controls);

        root.Unloaded += (_, _) => Cancel();
        return new ToolContent(root, Reset, ApplyAsync, _ready.Task, ActivateAsync);
    }

    /// <summary>Nothing to decode: the frame size is all the adaptive sizer needs.</summary>
    public Task ActivateAsync()
    {
        _ready.TrySetResult(new MediaSize(484, 360));
        return Task.CompletedTask;
    }

    private void FillControls()
    {
        _controls.Children.Clear();

        var orientation = Controls.Segmented(
        [
            (T("tool.audiovisualizer.orientation.landscape"), AudioVisualizerOrientation.Landscape),
            (T("tool.audiovisualizer.orientation.portrait"), AudioVisualizerOrientation.Portrait),
            (T("tool.audiovisualizer.orientation.square"), AudioVisualizerOrientation.Square),
        ], _orientation, value =>
        {
            _orientation = value;
            Paint();
        });

        _sizeText.FontSize = 11.5;
        _sizeText.Foreground = Theme.Muted;
        _sizeText.VerticalAlignment = VerticalAlignment.Center;
        _sizeText.Margin = new Thickness(10, 0, 0, 0);

        var orientationRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        orientationRow.Children.Add(Caption(T("tool.audiovisualizer.orientation")));
        orientationRow.Children.Add(orientation);
        orientationRow.Children.Add(_sizeText);
        _controls.Children.Add(orientationRow);

        _controls.Children.Add(Choice(T("tool.audiovisualizer.style"), Controls.Segmented(
        [
            (T("tool.audiovisualizer.style.waveform"), AudioVisualizerStyle.Waveform),
            (T("tool.audiovisualizer.style.scope"), AudioVisualizerStyle.Scope),
        ], _style, value =>
        {
            _style = value;
            Paint();
        })));

        var background = Controls.Segmented(
        [
            (T("tool.audiovisualizer.background.none"), false),
            (T("tool.audiovisualizer.background.image"), true),
        ], _useImage, value =>
        {
            _useImage = value;
            if (value && _imagePath is null)
            {
                // Deferred: a file dialog must not open inside the pill's own click handler.
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(new Action(ChooseImage));
                return;
            }
            Paint();
        });

        var choose = Controls.Button(T("tool.audiovisualizer.background.choose"), ChooseImage, glyph: "\uE8B7");
        choose.Margin = new Thickness(8, 0, 0, 0);

        _imageText.FontSize = 11.5;
        _imageText.Foreground = Theme.Muted;
        _imageText.VerticalAlignment = VerticalAlignment.Center;
        _imageText.TextTrimming = TextTrimming.CharacterEllipsis;
        _imageText.Margin = new Thickness(10, 0, 0, 0);

        var backgroundRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        backgroundRow.Children.Add(Caption(T("tool.audiovisualizer.background")));
        backgroundRow.Children.Add(background);
        backgroundRow.Children.Add(choose);
        backgroundRow.Children.Add(_imageText);
        _controls.Children.Add(backgroundRow);

        _outputName.FontSize = 11.5;
        _outputName.Foreground = Theme.Muted;
        _outputName.TextTrimming = TextTrimming.CharacterEllipsis;
        _controls.Children.Add(_outputName);
        _controls.Children.Add(_status);

        Paint();
    }

    private FrameworkElement Choice(string caption, FrameworkElement control)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(Caption(caption));
        row.Children.Add(control);
        return row;
    }

    private static TextBlock Caption(string text)
    {
        var caption = AudioToolChrome.Caption(text);
        caption.Width = 96;
        return caption;
    }

    private void ChooseImage()
    {
        var dialog = new OpenFileDialog
        {
            Title = T("tool.audiovisualizer.background.dialog"),
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.gif",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true)
        {
            if (_imagePath is null)
                _useImage = false;
        }
        else
        {
            try
            {
                _image = LoadImage(dialog.FileName);
                _imagePath = dialog.FileName;
                _useImage = true;
                _status.Text = string.Empty;
            }
            catch (Exception ex)
            {
                _status.Text = ex.Message;
                _image = null;
                _imagePath = null;
                _useImage = false;
            }
        }

        // Rebuild on the next dispatcher turn: this may run inside the control it replaces.
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(new Action(FillControls));
    }

    private static BitmapSource LoadImage(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path);
        bitmap.DecodePixelWidth = 1200;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private void Paint()
    {
        var (width, height) = AudioEffects.VisualizerDimensions(_orientation);
        _sizeText.Text = T("tool.audiovisualizer.size", width.ToString(), height.ToString());
        _imageText.Text = _useImage && _imagePath is not null
            ? Bidi.Value(Path.GetFileName(_imagePath))
            : string.Empty;

        _preview.Show(_orientation, _style, _useImage ? _image : null);
        _outputName.Text = Bidi.Value($"{_item.BaseName} {T("tool.audiovisualizer.suffix")}.mp4");
    }

    private void Reset()
    {
        _orientation = AudioVisualizerOrientation.Landscape;
        _style = AudioVisualizerStyle.Waveform;
        _useImage = false;
        _imagePath = null;
        _image = null;
        _status.Text = string.Empty;
        FillControls();
    }

    private void Report(double value) =>
        _status.Text = ((int)Math.Round(Math.Clamp(value, 0, 1) * 100)).ToString(System.Globalization.CultureInfo.InvariantCulture) + " %";

    private async Task<string?> ApplyAsync()
    {
        if (_useImage && _imagePath is null)
            throw new ConversionException(T("tool.audiovisualizer.error.noImage"));

        var directory = AudioToolOutput.DirectoryFor(_item, _targets);
        var suffix = T("tool.audiovisualizer.suffix");
        var args = AudioEffects.VisualizerArguments(_orientation, _style, AudioEffects.DefaultCrf, _imagePath is not null);
        List<string> inputs = _imagePath is null ? [_item.Path] : [_imagePath, _item.Path];
        var inputArgs = _imagePath is null ? null : new[] { "-loop", "1", "-framerate", "25" };
        var tools = _tools;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
        _status.Text = T("tool.audiovisualizer.working");
        try
        {
            return await OutputPaths.ProduceAsync(directory, $"{_item.BaseName} {suffix}", "mp4", temp =>
            {
                var context = new OperationContext
                {
                    Tools = tools,
                    CancellationToken = cts.Token,
                    OutputDirectory = directory,
                };
                return FfmpegBackend.RunAsync(context, inputs, args, temp, new Progress<double>(Report), inputArgs);
            });
        }
        catch (OperationCanceledException)
        {
            // The window was closed: nothing was written, so nothing to report.
            return null;
        }
        finally
        {
            _status.Text = string.Empty;
        }
    }

    private void Cancel()
    {
        try
        {
            if (!_life.IsCancellationRequested)
                _life.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}

/// <summary>Draws the chosen video frame with a sample of the animation; it never runs ffmpeg.</summary>
internal sealed class VisualizerPreview : FrameworkElement
{
    private static readonly Brush Backdrop = AudioTheme.VisualizerBackdrop;
    private static readonly Brush WaveFill = AudioTheme.VisualizerWaveFill;
    private static readonly Pen WavePen = AudioTheme.VisualizerWavePen;
    private static readonly Pen FramePen = new(Theme.PanelBorder, 1);

    private AudioVisualizerOrientation _orientation = AudioVisualizerOrientation.Landscape;
    private AudioVisualizerStyle _style = AudioVisualizerStyle.Waveform;
    private ImageSource? _image;

    public void Show(AudioVisualizerOrientation orientation, AudioVisualizerStyle style, ImageSource? image)
    {
        _orientation = orientation;
        _style = style;
        _image = image;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 4 || height < 4)
            return;

        var (mediaWidth, mediaHeight) = AudioEffects.VisualizerDimensions(_orientation);
        var scale = Math.Min((width - 8) / mediaWidth, (height - 8) / mediaHeight);
        var frameWidth = mediaWidth * scale;
        var frameHeight = mediaHeight * scale;
        var frame = new Rect((width - frameWidth) / 2, (height - frameHeight) / 2, frameWidth, frameHeight);

        drawingContext.PushClip(new RectangleGeometry(frame));
        drawingContext.DrawRectangle(Backdrop, null, frame);
        if (_image is { Width: > 0, Height: > 0 })
        {
            var fill = Math.Max(frameWidth / _image.Width, frameHeight / _image.Height);
            var drawWidth = _image.Width * fill;
            var drawHeight = _image.Height * fill;
            drawingContext.DrawImage(_image, new Rect(
                frame.Left + (frameWidth - drawWidth) / 2,
                frame.Top + (frameHeight - drawHeight) / 2,
                drawWidth,
                drawHeight));
        }

        if (_style == AudioVisualizerStyle.Scope)
            DrawScope(drawingContext, frame);
        else
            DrawWave(drawingContext, frame);
        drawingContext.Pop();

        drawingContext.DrawRectangle(null, FramePen, frame);
    }

    private static void DrawScope(DrawingContext dc, Rect frame)
    {
        var center = new Point(frame.Left + frame.Width / 2, frame.Top + frame.Height / 2);
        var radius = Math.Min(frame.Width, frame.Height) * 0.32;
        for (var i = 0; i < 3; i++)
        {
            var shrink = 1 - i * 0.22;
            dc.DrawEllipse(null, WavePen, center, radius * shrink, radius * 0.82 * shrink);
        }
    }

    private static void DrawWave(DrawingContext dc, Rect frame)
    {
        const int points = 96;
        var middle = frame.Top + frame.Height / 2;
        var amplitude = frame.Height * 0.3;

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var envelope = new double[points];
            for (var i = 0; i < points; i++)
            {
                var t = i / (double)(points - 1);
                envelope[i] = 0.12 + 0.88 * Math.Abs(Math.Sin(t * 21.0) * (0.35 + 0.65 * Math.Abs(Math.Sin(t * 3.1))));
            }

            var first = new Point(frame.Left, middle - envelope[0] * amplitude);
            context.BeginFigure(first, isFilled: true, isClosed: true);
            for (var i = 1; i < points; i++)
                context.LineTo(new Point(frame.Left + frame.Width * i / (points - 1), middle - envelope[i] * amplitude), true, false);
            for (var i = points - 1; i >= 0; i--)
                context.LineTo(new Point(frame.Left + frame.Width * i / (points - 1), middle + envelope[i] * amplitude), true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(WaveFill, WavePen, geometry);
    }
}
