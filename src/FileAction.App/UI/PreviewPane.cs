using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileAction.Core;
using FileAction.Core.Backends;
using static FileAction.Core.Loc;

namespace FileAction.App.UI;

/// <summary>
/// Lazy preview of the file a parameter dialog is about: a bounded image thumbnail, or a filmstrip of
/// five FFmpeg frames plus the duration for a video. Nothing is read or started until <see cref="StartAsync"/>
/// runs (from the dialog's Loaded event) and everything is cancelled and deleted by <see cref="Stop"/>.
/// </summary>
internal sealed class PreviewPane : UserControl
{
    private const int ImageWidth = 260;
    private const int ImageHeight = 180;
    private const int FrameCount = 5;
    private const int FrameWidth = 76;
    private const int FrameHeight = 43;
    private const int FramePixelWidth = FrameWidth * 2;

    private readonly PreviewKind _kind;
    private readonly string _path;
    private readonly ToolLocator _tools;
    private readonly CancellationTokenSource _cts = new();
    private readonly Image _image = new() { Stretch = Stretch.Uniform, MaxWidth = ImageWidth, MaxHeight = ImageHeight };
    private readonly Image[] _frames = new Image[FrameCount];
    private readonly StackPanel _strip = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _caption;
    private string? _ffmpeg;
    private bool _started;
    private bool _stopped;

    public PreviewPane(PreviewKind kind, string path, ToolLocator? tools)
    {
        _kind = kind;
        _path = path;
        _tools = tools ?? new ToolLocator();
        _caption = new TextBlock { FontSize = 11.5, Foreground = Theme.Muted, Margin = new Thickness(0, 5, 0, 0) };

        HorizontalAlignment = HorizontalAlignment.Left;
        Margin = new Thickness(0, 0, 0, 12);
        Visibility = Visibility.Collapsed; // only shown once there is something to see

        if (kind == PreviewKind.Image)
        {
            Content = new Border
            {
                Background = Theme.Card,
                BorderBrush = Theme.PanelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = _image,
            };
        }
        else if (kind == PreviewKind.Video)
        {
            for (var i = 0; i < FrameCount; i++)
            {
                var frame = new Image { Stretch = Stretch.UniformToFill };
                _frames[i] = frame;
                _strip.Children.Add(new Border
                {
                    Width = FrameWidth,
                    Height = FrameHeight,
                    Background = Theme.Card,
                    BorderBrush = Theme.PanelBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    ClipToBounds = true,
                    Margin = new Thickness(0, 0, 4, 0),
                    Child = frame,
                });
            }

            var column = new StackPanel();
            column.Children.Add(_strip);
            column.Children.Add(_caption);
            Content = column;
        }
        else
        {
            Content = new StackPanel();
        }
    }

    /// <summary>Starts loading in the background; the dialog calls this once it is shown.</summary>
    public async Task StartAsync()
    {
        if (_started || _stopped || _kind == PreviewKind.None)
            return;
        _started = true;
        try
        {
            if (!File.Exists(_path))
                return;
            if (_kind == PreviewKind.Image)
                await LoadImageAsync();
            else
                await LoadFilmstripAsync();
        }
        catch (Exception)
        {
            // A preview is a convenience; it must never break the dialog or the operation.
        }
        finally
        {
            try
            {
                _cts.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    /// <summary>Cancels the work and lets it delete its temporary frames; called when the dialog closes.</summary>
    public void Stop()
    {
        if (_stopped)
            return;
        _stopped = true;
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        if (!_started)
            _cts.Dispose();
    }

    // ---------------------------------------------------------------- image

    private async Task LoadImageAsync()
    {
        var bitmap = await Task.Run(() => DecodeImage(_path, ImageWidth * 2, ImageHeight * 2), _cts.Token);
        if (bitmap is null)
            return;
        await Ui(() =>
        {
            if (_stopped)
                return;
            Fit(bitmap);
            Visibility = Visibility.Visible;
        });
    }

    /// <summary>Scales the thumbnail to fit 260×180 while keeping its aspect ratio; never enlarges it.</summary>
    private void Fit(BitmapSource bitmap)
    {
        var naturalWidth = bitmap.PixelWidth * 96.0 / bitmap.DpiX;
        var naturalHeight = bitmap.PixelHeight * 96.0 / bitmap.DpiY;
        var scale = Math.Min(1.0, Math.Min(ImageWidth / naturalWidth, ImageHeight / naturalHeight));
        _image.Width = Math.Max(1, Math.Round(naturalWidth * scale));
        _image.Height = Math.Max(1, Math.Round(naturalHeight * scale));
        _image.Source = bitmap;
    }

    /// <summary>Decodes a memory-bounded thumbnail on a background thread; null when WPF cannot read the file.</summary>
    private static BitmapSource? DecodeImage(string path, int maxWidth, int maxHeight)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            // Header only, to learn the orientation: then just the long side has to be decoded.
            var probe = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var width = probe.PixelWidth;
            var height = probe.PixelHeight;
            if (width <= 0 || height <= 0)
                return null;
            stream.Position = 0;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.StreamSource = stream;
            if (width >= height)
                bitmap.DecodePixelWidth = Math.Min(width, maxWidth);
            else
                bitmap.DecodePixelHeight = Math.Min(height, maxHeight);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            // Not an image WPF has a codec for (webp, heic, avif…) or a broken file: no preview.
            return null;
        }
    }

    // ---------------------------------------------------------------- video

    private async Task LoadFilmstripAsync()
    {
        _ffmpeg = _tools.Find(ExternalTool.Ffmpeg);
        if (_ffmpeg is null)
        {
            _strip.Visibility = Visibility.Collapsed;
            _caption.Text = T("Náhled potřebuje FFmpeg", "Preview needs FFmpeg");
            Visibility = Visibility.Visible;
            return;
        }

        // Empty slots and the caption appear before the first frame is decoded.
        Visibility = Visibility.Visible;
        _caption.Text = "…";
        string? temp = null;
        try
        {
            temp = OutputPaths.TempDirectory();
            var token = _cts.Token;

            // The duration probe runs while the first frame is extracted, so something shows up at once.
            var durationTask = FfmpegBackend.ProbeDurationAsync(_ffmpeg, _path, token);
            if (await ExtractFrameAsync(temp, 0, 0, token) is { } first)
                await ShowFrameAsync(0, first);

            var duration = await durationTask;
            if (_stopped)
                return;

            double[] times;
            string caption;
            if (duration is > 0)
            {
                var seconds = duration.Value;
                caption = T($"Délka {FormatDuration(seconds)}", $"Duration {FormatDuration(seconds)}");
                times = [seconds * 0.25, seconds * 0.5, seconds * 0.75, seconds * 0.95];
            }
            else
            {
                caption = T("Délka neznámá", "Duration unknown");
                times = [2, 4, 6, 8]; // a guess; frames past the end simply do not appear
            }
            await Ui(() => _caption.Text = caption);

            for (var i = 0; i < times.Length; i++)
            {
                if (token.IsCancellationRequested)
                    break;
                if (await ExtractFrameAsync(temp, times[i], i + 1, token) is { } bitmap)
                    await ShowFrameAsync(i + 1, bitmap);
            }
        }
        finally
        {
            if (temp is not null)
                OutputPaths.TryDelete(temp);
        }
    }

    /// <summary>Extracts one frame into <paramref name="temp"/> and decodes it, both off the UI thread.</summary>
    private async Task<BitmapSource?> ExtractFrameAsync(string temp, double seconds, int index, CancellationToken token)
    {
        var file = Path.Combine(temp, $"frame{index}.jpg");
        var ffmpeg = _ffmpeg!;
        return await Task.Run(async () =>
        {
            if (!await FfmpegBackend.ExtractFrameAsync(ffmpeg, _path, seconds, FramePixelWidth, file, token))
                return null;
            return DecodeFrame(file);
        }, token);
    }

    private Task ShowFrameAsync(int index, BitmapSource bitmap) => Ui(() =>
    {
        if (!_stopped)
            _frames[index].Source = bitmap;
    });

    /// <summary>Runs a UI update on the control's thread, whichever thread the background work resumed on.</summary>
    private Task Ui(Action action)
    {
        if (Dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }
        return Dispatcher.InvokeAsync(action).Task;
    }

    private static BitmapSource? DecodeFrame(string file)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.UriSource = new Uri(file, UriKind.Absolute);
            bitmap.DecodePixelWidth = FramePixelWidth;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string FormatDuration(double seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : time.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }
}
