using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using FileAction.Core;
using FileAction.Core.Backends;
using Microsoft.Win32;
using static FileAction.Core.Loc;
using Path = System.IO.Path;

namespace FileAction.App.UI.Tools.Video;

/// <summary>What the FFmpeg banner says about a clip: duration, frame rate and original pixel size.</summary>
internal sealed record VideoInfo(double DurationSeconds, double Fps, int Width, int Height)
{
    public int TotalFrames => FrameMath.TotalFrames(DurationSeconds, Fps);
}

/// <summary>
/// Reads the first video stream's facts from FFmpeg's own banner. Only <c>ffmpeg</c> is required (the app
/// already locates it through <see cref="ToolLocator"/>), so no separate ffprobe download is needed.
/// Returns null when no video stream or no pixel size could be read.
/// </summary>
internal static class VideoProbe
{
    private static readonly Regex DurationPattern =
        new(@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.Compiled);

    private static readonly Regex SizePattern = new(@"(?<w>\d{2,5})x(?<h>\d{2,5})", RegexOptions.Compiled);
    private static readonly Regex FpsPattern = new(@"(?<fps>\d+(?:\.\d+)?)\s+fps\b", RegexOptions.Compiled);
    private static readonly Regex TbrPattern = new(@"(?<tbr>\d+(?:\.\d+)?)\s+tbr\b", RegexOptions.Compiled);

    public static async Task<VideoInfo?> ReadAsync(string ffmpeg, string path, CancellationToken ct)
    {
        var lines = new List<string>();
        // "-i" with no output exits with code 1 on purpose; only the banner is wanted.
        await ProcessRunner.RunAsync(ffmpeg, ["-hide_banner", "-nostdin", "-i", path], ct,
            line =>
            {
                lock (lines)
                    lines.Add(line);
            },
            throwOnError: false).ConfigureAwait(false);

        List<string> copy;
        lock (lines)
            copy = [.. lines];

        var duration = 0.0;
        foreach (var line in copy)
        {
            if (DurationPattern.Match(line) is { Success: true } match)
            {
                duration = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 3600
                    + int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * 60
                    + double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                break;
            }
        }

        var videoLine = copy.FirstOrDefault(line => line.Contains("Video:", StringComparison.Ordinal));
        if (videoLine is null)
            return null;

        int width = 0, height = 0;
        if (SizePattern.Match(videoLine) is { Success: true } size)
        {
            width = int.Parse(size.Groups["w"].Value, CultureInfo.InvariantCulture);
            height = int.Parse(size.Groups["h"].Value, CultureInfo.InvariantCulture);
        }
        if (width <= 0 || height <= 0)
            return null;

        var fps = 0.0;
        if (FpsPattern.Match(videoLine) is { Success: true } rate)
            fps = double.Parse(rate.Groups["fps"].Value, CultureInfo.InvariantCulture);
        else if (TbrPattern.Match(videoLine) is { Success: true } tbr)
            fps = double.Parse(tbr.Groups["tbr"].Value, CultureInfo.InvariantCulture);
        if (!double.IsFinite(fps) || fps <= 0 || fps > 240)
            fps = FrameMath.DefaultFps;

        if (!double.IsFinite(duration) || duration < 0)
            duration = 0;
        return new VideoInfo(duration, fps, width, height);
    }
}

/// <summary>Decodes one downsampled preview frame to a frozen bitmap and removes its temporary PNG.</summary>
internal static class VideoPreview
{
    public const int PreviewMaxWidth = 1280;

    public static async Task<BitmapSource> LoadAsync(
        string ffmpeg,
        string path,
        double seconds,
        string failureMessage,
        CancellationToken ct)
    {
        var png = Path.Combine(Path.GetTempPath(), "dropwheel-frame-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            await ProcessRunner.RunAsync(ffmpeg,
            [
                "-hide_banner", "-nostdin", "-y",
                "-ss", seconds.ToString("0.000", CultureInfo.InvariantCulture),
                "-i", path,
                "-frames:v", "1",
                "-update", "1",
                "-vf", $"scale='min({PreviewMaxWidth},iw)':-2",
                "-q:v", "3",
                png,
            ], ct).ConfigureAwait(false);

            if (!File.Exists(png) || new FileInfo(png).Length == 0)
                throw new ConversionException(failureMessage);

            return await Task.Run(() => Load(png), ct).ConfigureAwait(false);
        }
        finally
        {
            OutputPaths.TryDelete(png);
        }
    }

    private static BitmapSource Load(string png)
    {
        using var stream = File.OpenRead(png);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}

/// <summary>The folder a tool writes into: the drop folder when it is usable, otherwise next to the source.</summary>
internal static class VideoOutput
{
    public static string Folder(FileItem item, DropTargets? targets)
    {
        var detected = OutputTarget.Resolve(item.Path, targets?.UnderCursor, Directory.Exists, OutputTarget.CanWrite);
        if (!detected.UsesDetectedFolder && targets?.Remembered is { } remembered)
        {
            var fallback = OutputTarget.Resolve(item.Path, remembered, Directory.Exists, OutputTarget.CanWrite);
            if (fallback.UsesDetectedFolder)
                return fallback.Directory!;
        }
        return detected.Directory
            ?? OutputTarget.SourceFolder(item.Path)
            ?? Environment.CurrentDirectory;
    }
}

/// <summary>The FFmpeg work behind the two windows, always on the original file and never on the preview.</summary>
internal static class VideoRunner
{
    private static readonly Regex TimePattern =
        new(@"\btime=\s*(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.Compiled);

    /// <summary>
    /// Saves every captured frame at full resolution into a new folder next to the source. Each frame is
    /// first written into a temporary folder, so a cancelled or failed export leaves nothing behind and
    /// the unique folder name means an earlier export can never be overwritten.
    /// </summary>
    public static async Task<string> ExportFramesAsync(
        string ffmpeg,
        FileItem item,
        IReadOnlyList<int> frames,
        double fps,
        string outputDirectory,
        IProgress<(int Done, int Total)> progress,
        CancellationToken ct)
    {
        var temporary = OutputPaths.TempDirectory();
        try
        {
            var files = new List<string>(frames.Count);
            for (var i = 0; i < frames.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var name = FrameMath.CaptureFileName(item.BaseName, frames[i]);
                var framePath = Path.Combine(temporary, name);
                var seconds = FrameMath.SeekSeconds(frames[i], fps);
                await ProcessRunner.RunAsync(ffmpeg,
                [
                    "-hide_banner", "-nostdin", "-y",
                    "-ss", seconds.ToString("0.000", CultureInfo.InvariantCulture),
                    "-i", item.Path,
                    "-frames:v", "1",
                    "-update", "1",
                    "-f", "image2",
                    framePath,
                ], ct).ConfigureAwait(false);

                if (!File.Exists(framePath) || new FileInfo(framePath).Length == 0)
                    throw new ConversionException(T("error.conversion.noOutput"));

                files.Add(framePath);
                progress.Report((i + 1, frames.Count));
            }

            var folder = OutputPaths.UniqueDirectory(outputDirectory, FrameMath.FramesFolderName(item.BaseName));
            Directory.CreateDirectory(folder);
            foreach (var file in files)
                File.Move(file, Path.Combine(folder, Path.GetFileName(file)));
            return folder;
        }
        finally
        {
            OutputPaths.TryDelete(temporary);
        }
    }

    /// <summary>
    /// Re-encodes the clip with the redaction graph, keeps the original format when its container supports
    /// the encoder and otherwise falls back to MP4 (the same rule the rest of the app uses).
    /// </summary>
    public static Task<string> RedactAsync(
        string ffmpeg,
        FileItem item,
        string graph,
        double durationSeconds,
        int crf,
        string outputDirectory,
        IProgress<double> progress,
        CancellationToken ct)
    {
        var format = FfmpegBackend.SafeContainer(item.Format);
        return OutputPaths.ProduceAsync(outputDirectory, $"{item.BaseName} Redacted", format, async temporary =>
        {
            var args = new List<string>
            {
                "-hide_banner", "-nostdin", "-y",
                "-i", item.Path,
                "-filter_complex", graph,
                "-map", RedactionGraph.OutputLabel,
                "-map", "0:a?",
            };
            if (format == "webm")
            {
                args.AddRange(["-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0", "-row-mt", "1", "-deadline", "good", "-cpu-used", "4"]);
                args.AddRange(["-c:a", "libopus", "-b:a", "128k"]);
            }
            else
            {
                args.AddRange(["-c:v", "libx264", "-preset", "medium", "-crf", crf.ToString(CultureInfo.InvariantCulture), "-pix_fmt", "yuv420p"]);
                args.AddRange(["-c:a", "aac", "-b:a", "160k"]);
                if (format is "mp4" or "mov")
                    args.AddRange(["-movflags", "+faststart"]);
            }
            args.Add(temporary);

            void OnLine(string line)
            {
                if (durationSeconds <= 0)
                    return;
                if (TimePattern.Match(line) is { Success: true } match)
                {
                    var seconds = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 3600
                        + int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * 60
                        + double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                    progress.Report(Math.Clamp(seconds / durationSeconds, 0, 0.99));
                }
            }

            await ProcessRunner.RunAsync(ffmpeg, args, ct, OnLine).ConfigureAwait(false);
        });
    }
}

/// <summary>
/// A slim frame scrubber: the track fills from the left, the round thumb follows the pointer and the
/// value is an integer frame index. Unlike <see cref="Controls.Slider"/> it can be set from code when the
/// user steps a frame or clicks a captured chip.
/// </summary>
internal sealed class VideoScrubber : Grid
{
    private readonly Border _track;
    private readonly Border _fill;
    private readonly Ellipse _thumb;
    private int _maximum = 1;
    private int _value;

    public VideoScrubber()
    {
        Height = 24;
        Background = VideoPalette.Transparent;
        Cursor = Cursors.Hand;
        // The time axis always runs left to right, also in the Arabic UI, so the pointer mapping matches
        // the painted track.
        FlowDirection = FlowDirection.LeftToRight;
        _track = new Border
        {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = Theme.Track,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Children.Add(_track);
        _fill = new Border
        {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = Theme.AccentGradient,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 0,
        };
        _thumb = new Ellipse
        {
            Width = 18,
            Height = 18,
            Fill = Theme.TextOnAccent,
            Stroke = Theme.PanelBorder,
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = Theme.Shadow(6),
        };
        Children.Add(_fill);
        Children.Add(_thumb);

        SizeChanged += (_, _) => Paint();
        MouseLeftButtonDown += (_, e) =>
        {
            CaptureMouse();
            SetFrom(e.GetPosition(this));
        };
        MouseMove += (_, e) =>
        {
            if (IsMouseCaptured)
                SetFrom(e.GetPosition(this));
        };
        MouseLeftButtonUp += (_, _) =>
        {
            ReleaseMouseCapture();
            Committed?.Invoke();
        };
    }

    /// <summary>Raised while dragging, once per frame value.</summary>
    public event Action<int>? Scrub;

    /// <summary>Raised when the pointer is released.</summary>
    public event Action? Committed;

    public int Maximum
    {
        get => _maximum;
        set
        {
            _maximum = Math.Max(0, value);
            _value = Math.Clamp(_value, 0, _maximum);
            Paint();
        }
    }

    public int Value => _value;

    /// <summary>Moves the thumb; <paramref name="notify"/> replays the scrub event for keyboard/chip moves.</summary>
    public void SetValue(int value, bool notify = false)
    {
        value = Math.Clamp(value, 0, _maximum);
        if (value != _value)
            _value = value;
        Paint();
        if (notify)
            Scrub?.Invoke(_value);
    }

    /// <summary>The window's theme changed; repaint with the current Theme brushes.</summary>
    public void ApplyTheme()
    {
        _track.Background = Theme.Track;
        _fill.Background = Theme.AccentGradient;
        _thumb.Fill = Theme.TextOnAccent;
        _thumb.Stroke = Theme.PanelBorder;
        _thumb.Effect = Theme.Shadow(6);
        Paint();
    }

    private void SetFrom(Point point)
    {
        var track = Math.Max(1, ActualWidth - 18);
        var fraction = Math.Clamp((point.X - 9) / track, 0, 1);
        SetValue((int)Math.Round(fraction * _maximum), notify: true);
    }

    private void Paint()
    {
        var width = Math.Max(1, ActualWidth);
        var track = Math.Max(1, width - 18);
        var fraction = _maximum <= 0 ? 0 : _value / (double)_maximum;
        _fill.Width = Math.Max(0, fraction * width);
        _thumb.Margin = new Thickness(fraction * track, 0, 0, 0);
    }
}

/// <summary>A two-or-more pill switch whose selected value can also follow the selection in a list.</summary>
internal sealed class PillChoice<T> : Border
    where T : notnull
{
    private readonly List<(T Value, Border Pill, TextBlock Text)> _items = [];
    private T _value;

    public PillChoice(IReadOnlyList<(string Label, T Value)> options, T selected)
    {
        Background = Theme.Track;
        CornerRadius = new CornerRadius(9);
        Padding = new Thickness(2);
        VerticalAlignment = VerticalAlignment.Center;
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, value) in options)
        {
            var text = new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            var pill = new Border
            {
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(10, 4, 10, 5),
                Cursor = Cursors.Hand,
                Child = text,
            };
            pill.MouseLeftButtonUp += (_, _) => Set(value, notify: true);
            _items.Add((value, pill, text));
            panel.Children.Add(pill);
        }
        Child = panel;
        _value = selected;
        Paint();
    }

    public event Action<T>? Changed;

    public T Value => _value;

    /// <summary>Repaints the pill row with the current Theme brushes.</summary>
    public void ApplyTheme()
    {
        Background = Theme.Track;
        Paint();
    }

    public void Set(T value, bool notify = false)
    {
        _value = value;
        Paint();
        if (notify)
            Changed?.Invoke(value);
    }

    private void Paint()
    {
        foreach (var (value, pill, text) in _items)
        {
            var selected = EqualityComparer<T>.Default.Equals(value, _value);
            pill.Background = selected ? Theme.AccentGradient : VideoPalette.Transparent;
            text.Foreground = selected ? Theme.TextOnAccent : Theme.Text;
            text.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }
}

/// <summary>Four swatch chips for the solid redaction colour; the chosen one gets an accent ring.</summary>
internal sealed class ColorChoice : StackPanel
{
    private static readonly string[] Labels =
    [
        "tool.videoredact.color.black",
        "tool.videoredact.color.white",
        "tool.videoredact.color.red",
        "tool.videoredact.color.violet",
    ];

    private readonly List<Border> _rings = [];
    private string _value;

    public ColorChoice(string selected)
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        _value = selected;
        for (var i = 0; i < VideoPalette.Options.Count; i++)
        {
            var option = VideoPalette.Options[i];
            var ring = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(2),
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(2),
                Cursor = Cursors.Hand,
                ToolTip = T(Labels[i]),
                Child = new Ellipse { Fill = new SolidColorBrush(option.Fill) },
            };
            var token = option.Token;
            ring.MouseLeftButtonUp += (_, _) => Set(token, notify: true);
            _rings.Add(ring);
            Children.Add(ring);
        }
        Paint();
    }

    public event Action<string>? Changed;

    public string Value => _value;

    /// <summary>Repaints the selection rings with the current Theme brushes.</summary>
    public void ApplyTheme() => Paint();

    public void Set(string value, bool notify = false)
    {
        _value = value;
        Paint();
        if (notify)
            Changed?.Invoke(value);
    }

    private void Paint()
    {
        for (var i = 0; i < _rings.Count; i++)
        {
            var selected = string.Equals(VideoPalette.Options[i].Token, _value, StringComparison.OrdinalIgnoreCase);
            _rings[i].BorderBrush = selected ? Theme.Accent : Theme.PanelBorder;
        }
    }
}

/// <summary>Repaint helpers for the tool-local buttons, which bake Theme brushes when they are built.</summary>
internal static class VideoTheme
{
    public static void RepaintButton(Border button, bool primary = false)
    {
        button.Background = primary ? Theme.AccentGradient : Theme.Card;
        button.BorderBrush = primary ? null : Theme.PanelBorder;
        if (button.Child is not StackPanel panel)
            return;
        foreach (var child in panel.Children)
        {
            if (child is TextBlock text)
                text.Foreground = primary ? Theme.TextOnAccent : Theme.Text;
        }
    }
}

/// <summary>
/// Watches for light/dark changes while a video tool window is open. Windows raises
/// <see cref="SystemEvents.UserPreferenceChanged"/> for an OS theme switch; the app's own theme setting
/// applies on the next activation. Either way the view's repaint callback runs once per change, on the
/// UI thread, and never rebuilds the window.
/// </summary>
internal sealed class VideoThemeHook : IDisposable
{
    private readonly FrameworkElement _root;
    private readonly Action _repaint;
    private Window? _window;
    private bool _dark;
    private bool _hooked;

    public VideoThemeHook(FrameworkElement root, Action repaint)
    {
        _root = root;
        _repaint = repaint;
        root.Loaded += OnLoaded;
        root.Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_hooked)
            return;
        _hooked = true;
        _dark = Theme.IsDark;
        _window = Window.GetWindow(_root);
        if (_window is not null)
            _window.Activated += OnActivated;
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e) => Detach();

    private void OnActivated(object? sender, EventArgs e) => Refresh();

    private void OnPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (_root.Dispatcher.CheckAccess())
            Refresh();
        else
            _root.Dispatcher.BeginInvoke(Refresh);
    }

    private void Refresh()
    {
        Theme.Refresh();
        if (Theme.IsDark == _dark)
            return;
        _dark = Theme.IsDark;
        _repaint();
    }

    private void Detach()
    {
        if (!_hooked)
            return;
        _hooked = false;
        if (_window is not null)
            _window.Activated -= OnActivated;
        SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
        _window = null;
    }

    public void Dispose() => Detach();
}
