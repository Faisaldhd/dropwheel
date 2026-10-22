using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FileAction.Core;
using FileAction.Core.Backends;
using WpfImage = System.Windows.Controls.Image;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Video;

/// <summary>
/// The shared video preview: FFmpeg extracts a downsampled still (and a filmstrip for the timeline) and
/// every scrub request is debounced and cancelled when a newer one arrives. Playback is optional: only
/// containers WPF's Media Foundation pipeline can open get a play button, and when the file cannot be
/// opened the tool stays scrub-only and says so. Nothing here runs FFmpeg on the UI thread.
/// </summary>
internal sealed class VideoEditPreview : IDisposable
{
    /// <summary>Still frames are decoded to at most 960 px on the long side.</summary>
    private const int DecodeLongSide = 960;



    private readonly FileItem _item;
    private readonly string _keyPrefix;
    private readonly Grid _surface = new();
    private readonly WpfImage _image = new() { Stretch = Stretch.Uniform, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly Border _placeholder;
    private readonly Border _message;
    private readonly TextBlock _messageText;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly string? _ffmpeg;
    private readonly Border _playButton;
    private readonly Border _playNote;
    private readonly TextBlock _playNoteText;

    private CancellationTokenSource _seekCts = new();
    private MediaElement? _player;
    private DispatcherTimer? _playTimer;
    private TimeSpan _playStart;
    private TimeSpan _playEnd;
    private int _seekGeneration;
    private bool _externalPlayback;
    private bool _openingPlayback;
    private bool _playbackUnavailable;
    private bool _disposed;

    public VideoEditPreview(FileItem item, string keyPrefix)
    {
        _item = item;
        _keyPrefix = keyPrefix;
        _ffmpeg = new ToolLocator().Ffmpeg;

        _placeholder = new Border
        {
            Background = Theme.Card,
            CornerRadius = new CornerRadius(10),
            Child = new ProgressBar { IsIndeterminate = true, Width = 120, Height = 4 },
        };

        _messageText = new TextBlock
        {
            Foreground = Theme.Text,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(24),
            FontSize = 12.5,
        };
        _message = new Border
        {
            Background = Theme.Page,
            CornerRadius = new CornerRadius(10),
            Visibility = Visibility.Collapsed,
            Child = _messageText,
        };

        _playNoteText = VideoControls.Hint(T("tool.videotrim.scrubOnly"));
        _playNote = new Border
        {
            Background = Theme.Page,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 6, 10, 7),
            Margin = new Thickness(12, 12, 12, 52),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            MaxWidth = 520,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Child = _playNoteText,
        };
        Panel.SetZIndex(_playNote, 11);

        // One shared control for every view that uses this preview. Trim has its own external button
        // and hides this one as soon as it calls EnablePlaybackAsync.
        _playButton = Controls.Button(T("tool.videotrim.play"), () => _ = ToggleFromControlAsync(), glyph: Glyphs.Play);
        _playButton.HorizontalAlignment = HorizontalAlignment.Left;
        _playButton.VerticalAlignment = VerticalAlignment.Bottom;
        _playButton.Margin = new Thickness(14);
        _playButton.Visibility = Visibility.Collapsed;
        Panel.SetZIndex(_playButton, 12);
        PlayingChanged += playing =>
            Controls.SetText(_playButton, T(playing ? "tool.videotrim.pause" : "tool.videotrim.play"));

        _surface.Children.Add(_image);
        _surface.Children.Add(_overlay);
        _surface.Children.Add(_placeholder);
        _surface.Children.Add(_message);
        _surface.Children.Add(_playNote);
        _surface.Children.Add(_playButton);
        _surface.SizeChanged += (_, _) => SurfaceChanged?.Invoke();
        MediaSurface.Round(_surface);

        View = new Border
        {
            Background = Theme.Card,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(6),
            ClipToBounds = true,
            Child = _surface,
        };
    }

    public FrameworkElement View { get; }

    /// <summary>The area above the still and below the messages; the crop tool draws its rectangle here.</summary>
    public Canvas Overlay => _overlay;

    /// <summary>Layer for tool chrome (the trim play button); added above everything.</summary>
    public Grid Surface => _surface;

    public string? Ffmpeg => _ffmpeg;

    public FileAction.Core.VideoInfo? Info { get; private set; }

    public TimeSpan Position { get; private set; }

    public bool IsPlaying { get; private set; }

    public event Action? SurfaceChanged;

    public event Action<TimeSpan>? PlaybackPositionChanged;

    public event Action<bool>? PlayingChanged;

    internal Border PlaybackButton => _playButton;

    internal Border PlaybackNote => _playNote;

    internal TextBlock PlaybackNoteText => _playNoteText;

    /// <summary>Probes the file and shows its first frame. False when the preview cannot be built.</summary>
    public async Task<bool> LoadAsync()
    {
        if (_ffmpeg is null)
        {
            ShowMessage(T(_keyPrefix + ".noFfmpeg"));
            return false;
        }

        try
        {
            var info = await FileAction.Core.VideoProbe.ReadAsync(_ffmpeg, _item.Path, _lifetime.Token);
            if (info is null)
            {
                ShowMessage(T(_keyPrefix + ".probeFailed", Bidi.Value(_item.Name)));
                return false;
            }

            Info = info;
            var frame = await CaptureAsync(TimeSpan.Zero, _lifetime.Token);
            if (frame is null)
            {
                ShowMessage(T(_keyPrefix + ".probeFailed", Bidi.Value(_item.Name)));
                return false;
            }

            _image.Source = frame;
            _image.Visibility = Visibility.Visible;
            _placeholder.Visibility = Visibility.Collapsed;
            ShowPlaybackControl();
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            ShowMessage(T(_keyPrefix + ".probeFailed", ex.Message));
            return false;
        }
    }

    /// <summary>Loads the filmstrip in one FFmpeg pass; an empty list simply leaves the timeline dark.</summary>
    public async Task<IReadOnlyList<BitmapSource>> LoadStripAsync(int count, int width)
    {
        var ffmpeg = _ffmpeg;
        var info = Info;
        if (ffmpeg is null || info is null || info.Duration <= TimeSpan.Zero)
            return [];

        var directory = OutputPaths.TempDirectory();
        try
        {
            var pattern = Path.Combine(directory, "strip%02d.jpg");
            var args = VideoEdit.BuildStripArgs(_item.Path, pattern, info.Duration, count, width);
            await ProcessRunner.RunAsync(ffmpeg, args, _lifetime.Token, throwOnError: false);

            var frames = new List<BitmapSource>();
            foreach (var file in Directory.EnumerateFiles(directory, "strip*.jpg").Order(StringComparer.Ordinal))
            {
                if (Decode(file, width) is { } frame)
                    frames.Add(frame);
            }
            return frames;
        }
        catch (Exception)
        {
            return [];
        }
        finally
        {
            OutputPaths.TryDelete(directory);
        }
    }

    /// <summary>Shows the frame at <paramref name="position"/>; a newer call cancels the pending one.</summary>
    public async Task SeekAsync(TimeSpan position)
    {
        if (_disposed || Info is null)
            return;

        var clamped = VideoEdit.Clamp(position, Info.Duration);
        if (_player is not null)
        {
            try
            {
                _player.Position = clamped;
            }
            catch (InvalidOperationException)
            {
            }
        }

        var generation = ++_seekGeneration;
        var previous = _seekCts;
        _seekCts = new CancellationTokenSource();
        previous.Cancel();
        var token = _seekCts.Token;

        try
        {
            // Coalesce a drag: only the position that survives the debounce spawns FFmpeg.
            await Task.Delay(60, token);
            if (token.IsCancellationRequested || generation != _seekGeneration)
                return;
            var frame = await CaptureAsync(clamped, token);
            if (frame is null || generation != _seekGeneration)
                return;
            _image.Source = frame;
            _image.Visibility = Visibility.Visible;
            Position = clamped;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // A scrub frame is a convenience; the last good frame stays on screen.
        }
    }

    /// <summary>
    /// Tries WPF Media Foundation playback. Returns false for containers/codecs it cannot open, and the
    /// caller then hides the play button and shows the scrub-only note. A tool that already has its own
    /// play button calls this; the shared control is hidden so the two buttons never compete.
    /// </summary>
    public async Task<bool> EnablePlaybackAsync(TimeSpan start, TimeSpan end)
    {
        _externalPlayback = true;
        _playButton.Visibility = Visibility.Collapsed;
        _playNote.Visibility = Visibility.Collapsed;
        var available = await EnablePlaybackCoreAsync(start, end);
        if (!available)
            _playbackUnavailable = true;
        return available;
    }

    /// <summary>
    /// Every video format gets a play control. Windows Media Foundation may still refuse a codec; the
    /// button then hides and the existing scrub-only note explains why. We no longer pre-hide the button
    /// for MKV/WebM/FLV, because a format list cannot predict the codecs the machine has installed.
    /// </summary>
    internal static bool IsPlayableFormat(string? format) => !string.IsNullOrWhiteSpace(format);

    /// <summary>Shown only after the preview has a probe result and a first frame.</summary>
    private void ShowPlaybackControl()
    {
        if (_externalPlayback || _disposed || Info is null)
            return;
        if (IsPlayableFormat(_item.Format))
            _playButton.Visibility = Visibility.Visible;
        else
            _playNote.Visibility = Visibility.Visible;
    }

    /// <summary>The shared button's first click opens the player lazily; later clicks only toggle.</summary>
    private async Task ToggleFromControlAsync()
    {
        if (_disposed || _externalPlayback || _playbackUnavailable || _openingPlayback || Info is null)
            return;

        if (_player is null)
        {
            var opened = await EnablePlaybackCoreAsync(TimeSpan.Zero, Info.Duration);
            if (_disposed || _externalPlayback)
                return;
            if (!opened)
            {
                MarkPlaybackUnavailable();
                return;
            }
            SetPlayRange(TimeSpan.Zero, Info.Duration);
        }

        TogglePlayback();
    }

    private void MarkPlaybackUnavailable()
    {
        _playbackUnavailable = true;
        _playButton.Visibility = Visibility.Collapsed;
        if (!_externalPlayback)
            _playNote.Visibility = Visibility.Visible;
    }

    private async Task<bool> EnablePlaybackCoreAsync(TimeSpan start, TimeSpan end)
    {
        if (Info is null || !IsPlayableFormat(_item.Format) || _playbackUnavailable)
            return false;
        if (_player is not null)
        {
            _playStart = start;
            _playEnd = end;
            return true;
        }
        if (_openingPlayback)
            return false;

        _openingPlayback = true;
        MediaElement? media = null;
        try
        {
            // MediaPlayer has no visual output; a MediaElement is the surface that actually shows the
            // moving frames. It is inserted over the first-frame Image and hidden until it opens.
            media = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Manual,
                ScrubbingEnabled = true,
                Stretch = Stretch.Uniform,
                Volume = 0.7,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
            };
            _surface.Children.Insert(1, media);
            var opened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            media.MediaOpened += (_, _) => opened.TrySetResult(true);
            media.MediaFailed += (_, _) => opened.TrySetResult(false);
            media.Source = new Uri(_item.Path, UriKind.Absolute);

            var winner = await Task.WhenAny(opened.Task, Task.Delay(4000));
            if (winner != opened.Task || !await opened.Task || _disposed)
            {
                media.Close();
                _surface.Children.Remove(media);
                return false;
            }

            _player = media;
            _image.Visibility = Visibility.Collapsed;
            media.Visibility = Visibility.Visible;
            media.Position = VideoEdit.Clamp(start, Info.Duration);
            _playStart = start;
            _playEnd = end;
            _playTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            _playTimer.Tick += (_, _) => OnPlayTick();
            return true;
        }
        catch (Exception)
        {
            media?.Close();
            if (media is not null)
                _surface.Children.Remove(media);
            return false;
        }
        finally
        {
            _openingPlayback = false;
        }
    }

    public void SetPlayRange(TimeSpan start, TimeSpan end)
    {
        _playStart = start;
        _playEnd = end;
    }

    public void TogglePlayback()
    {
        if (_player is null || Info is null)
            return;
        if (IsPlaying)
        {
            StopPlayback();
            return;
        }

        try
        {
            _player.Position = VideoEdit.Clamp(_playStart, Info.Duration);
            _player.Play();
            IsPlaying = true;
            _playTimer?.Start();
            PlayingChanged?.Invoke(true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void StopPlayback()
    {
        _playTimer?.Stop();
        try
        {
            _player?.Pause();
        }
        catch (InvalidOperationException)
        {
        }
        if (!IsPlaying)
            return;
        IsPlaying = false;
        PlayingChanged?.Invoke(false);
    }

    private void OnPlayTick()
    {
        if (_player is null)
            return;
        var position = _player.Position;
        PlaybackPositionChanged?.Invoke(position);
        if (position < _playEnd - TimeSpan.FromMilliseconds(50))
            return;

        // Reached the trim end (or the file end): pause and park the playhead back at the start.
        _playTimer?.Stop();
        try
        {
            _player.Pause();
            _player.Position = _playStart;
        }
        catch (InvalidOperationException)
        {
        }
        IsPlaying = false;
        PlayingChanged?.Invoke(false);
        PlaybackPositionChanged?.Invoke(_playStart);
    }

    private async Task<BitmapSource?> CaptureAsync(TimeSpan position, CancellationToken token)
    {
        var ffmpeg = _ffmpeg;
        var info = Info;
        if (ffmpeg is null || info is null)
            return null;

        var width = DecodeWidth(info);
        var directory = OutputPaths.TempDirectory();
        try
        {
            var file = Path.Combine(directory, "frame.jpg");
            var seconds = Math.Clamp(
                position.TotalSeconds,
                0,
                Math.Max(0, info.Duration.TotalSeconds - 0.05));
            var ok = await FfmpegBackend.ExtractFrameAsync(ffmpeg, _item.Path, seconds, width, file, token);
            return ok ? Decode(file, width) : null;
        }
        finally
        {
            OutputPaths.TryDelete(directory);
        }
    }

    private static int DecodeWidth(FileAction.Core.VideoInfo info) => info.Width >= info.Height
        ? DecodeLongSide
        : Math.Max(320, (int)Math.Round(DecodeLongSide * (double)info.Width / info.Height));

    private static BitmapSource? Decode(string file, int width)
    {
        try
        {
            using var stream = File.OpenRead(file);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.StreamSource = stream;
            bitmap.DecodePixelWidth = Math.Max(1, width);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void ShowMessage(string text)
    {
        _messageText.Text = text;
        _message.Visibility = Visibility.Visible;
        _placeholder.Visibility = Visibility.Collapsed;
        _image.Visibility = Visibility.Collapsed;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            _lifetime.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        // Close must never leave a player running; pause first so PlayingChanged settles.
        StopPlayback();
        _playTimer?.Stop();
        _playTimer = null;
        _playButton.Visibility = Visibility.Collapsed;
        try
        {
            _player?.Close();
        }
        catch (Exception)
        {
        }
        _player = null;

        try
        {
            _seekCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        _seekCts.Dispose();
        _lifetime.Dispose();
    }
}
