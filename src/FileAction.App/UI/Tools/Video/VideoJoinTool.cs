using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileAction.Core;
using static FileAction.Core.Loc;
using WpfImage = System.Windows.Controls.Image;

namespace FileAction.App.UI.Tools.Video;

/// <summary>
/// Join several videos: a filmstrip in the selection order, drag a clip to reorder it, remove a clip.
/// The first clip defines the output's size and frame rate; every other clip is scaled and padded to
/// it, and a clip without audio gets silence. Apply writes <c>Joined.mp4</c> next to the first clip.
/// </summary>
internal sealed class VideoJoinTool : IToolWindowSpec
{
    public static readonly VideoJoinTool Instance = new();

    /// <summary>Title and bottom bar plus the output line and the hint; the filmstrip takes the rest.</summary>
    private static readonly ToolSpecLayout LayoutSpec = new(
        ChromeWidth: 36,
        ChromeHeight: 200,
        PreviewMinWidth: 560,
        PreviewMinHeight: 300,
        PreviewMaxWidth: 820,
        PreviewMaxHeight: 560,
        DefaultPreviewWidth: 684,
        DefaultPreviewHeight: 440);

    public string Id => "tools.videojoin";

    public string TitleKey => "tool.videojoin.title";

    /// <summary>Bottom-right button: the join verb, not the generic "Apply".</summary>
    public string PrimaryLabelKey => "tool.videojoin.primary";

    public ToolSpecLayout Layout => LayoutSpec;

    public bool Applies(IReadOnlyList<FileItem> items) =>
        items.Count >= 2 && items.All(item => !item.IsDirectory && Formats.Video.Contains(item.Format));

    public ToolContent Create(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new VideoJoinView(items).Build();
}

/// <summary>Registers the tool from its own file, so no shared list has to be edited.</summary>
internal static class VideoJoinToolRegistration
{
    [ModuleInitializer]
    internal static void Register() => ToolRegistry.Register(VideoJoinTool.Instance);
}

/// <summary>The join window's content: the reorderable filmstrip, the output summary and the hint line.</summary>
internal sealed class VideoJoinView
{
    private const int ThumbWidth = 240;

    private readonly ToolLocator _tools = new();
    private readonly TaskCompletionSource<MediaSize?> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, BitmapSource?> _thumbs = new(StringComparer.OrdinalIgnoreCase);
    private readonly WrapPanel _strip = new() { Margin = new Thickness(2) };
    private readonly List<FrameworkElement> _cards = [];
    private readonly TextBlock _summary = new();
    private readonly TextBlock _status = new();
    private readonly ProgressBar _progress = new();

    private List<ClipInfo> _clips;
    private List<ClipInfo>? _initial;
    private Grid? _root;
    private ThemeWatcher? _theme;
    private string? _ffmpeg;
    private int _dragging = -1;
    private Point _dragStart;
    private bool _moved;
    private CancellationTokenSource? _applyCts;
    private bool _busy;
    private bool _loading;
    private bool _statusIsError;

    public VideoJoinView(IReadOnlyList<FileItem> items)
    {
        // The placeholder clips keep the strip visible while the probes are running.
        _clips = [.. items.Select(item => new ClipInfo(item.Path, 0, 0, 0, 0, false))];

        _summary.FontSize = 11.5;
        _summary.TextWrapping = TextWrapping.Wrap;
        _summary.Margin = new Thickness(2, 0, 2, 4);
        _status.FontSize = 11.5;
        _status.TextWrapping = TextWrapping.Wrap;
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.Margin = new Thickness(2, 0, 100, 0);
        _progress.Width = 90;
        _progress.Height = 4;
        _progress.Visibility = Visibility.Collapsed;
        _progress.HorizontalAlignment = HorizontalAlignment.Right;
        _progress.VerticalAlignment = VerticalAlignment.Center;

        _strip.MouseMove += StripMove;
        _strip.MouseLeftButtonUp += StripUp;
        _strip.LostMouseCapture += (_, _) => _dragging = -1;

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

    /// <summary>Rebuilds the view with the current palette; the clip order and thumbnails are untouched.</summary>
    private void Repaint() => Populate();

    private void Populate()
    {
        if (_root is null)
            return;

        // The state-bearing controls move into a freshly themed layout instead of being re-created.
        VideoToolCommon.Detach(_strip);
        VideoToolCommon.Detach(_summary);
        VideoToolCommon.Detach(_status);
        VideoToolCommon.Detach(_progress);

        _root.Children.Clear();
        _root.RowDefinitions.Clear();
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        PaintStaticSurfaces();

        var filmstrip = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _strip,
            Margin = new Thickness(0, 2, 0, 8),
        };
        var status = BuildStatus();

        Grid.SetRow(filmstrip, 0);
        Grid.SetRow(_summary, 1);
        Grid.SetRow(status, 2);
        _root.Children.Add(filmstrip);
        _root.Children.Add(_summary);
        _root.Children.Add(status);

        RebuildStrip();
        UpdateSummary();
    }

    /// <summary>Applies the current palette to the controls that outlive a rebuild (they hold old brushes).</summary>
    private void PaintStaticSurfaces()
    {
        _summary.Foreground = Theme.Muted;
        _status.Foreground = _statusIsError ? Theme.Error : Theme.Muted;
        _progress.Foreground = Theme.Accent;
        _progress.Background = Theme.Track;
    }

    /// <summary>Probes every clip in order, showing each card as soon as its facts are known.</summary>
    public async Task ActivateAsync()
    {
        // Reordering or removing while a probe is writing its result would confuse the clip model.
        _loading = true;
        try
        {
            _ffmpeg = _tools.Require(ExternalTool.Ffmpeg);
            RebuildStrip();
            for (var index = 0; index < _clips.Count; index++)
            {
                _lifetime.Token.ThrowIfCancellationRequested();
                var info = await VideoToolCommon.ProbeAsync(_ffmpeg, _clips[index].Path, _lifetime.Token);
                _clips[index] = info;
                RebuildStrip();
                UpdateSummary();
                _ = LoadThumbnailAsync(index);
            }

            _initial = [.. _clips];
            var first = _clips.FirstOrDefault(clip => clip.IsUsable);
            _ready.TrySetResult(first is null ? null : new MediaSize(first.Width, first.Height));
        }
        catch (OperationCanceledException)
        {
            _ready.TrySetResult(null);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            RebuildStrip();
            _ready.TrySetResult(null);
        }
        finally
        {
            _loading = false;
        }
    }

    private FrameworkElement BuildStatus()
    {
        var row = new Grid();
        row.Children.Add(_status);
        row.Children.Add(_progress);
        return row;
    }

    // ---------------------------------------------------------------- filmstrip

    private void RebuildStrip()
    {
        _strip.Children.Clear();
        _cards.Clear();
        for (var index = 0; index < _clips.Count; index++)
        {
            var card = BuildCard(index);
            _cards.Add(card);
            _strip.Children.Add(card);
        }
    }

    private FrameworkElement BuildCard(int index)
    {
        var clip = _clips[index];
        var thumbnail = new WpfImage { Width = 120, Height = 68, Stretch = Stretch.UniformToFill };
        if (_thumbs.TryGetValue(clip.Path, out var cached) && cached is not null)
            thumbnail.Source = cached;

        var frame = new Border
        {
            Width = 120,
            Height = 68,
            CornerRadius = new CornerRadius(7),
            Background = Theme.Track,
            ClipToBounds = true,
            Child = thumbnail,
        };

        var number = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6, 1, 6, 2),
            Background = Theme.Accent,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(4),
            Child = new TextBlock
            {
                Text = Bidi.Number(index + 1),
                FontSize = 10.5,
                Foreground = Theme.TextOnAccent,
            },
        };

        var remove = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Background = Theme.Card,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(4),
            Cursor = Cursors.Hand,
            ToolTip = T("tool.videojoin.remove"),
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
        remove.MouseLeftButtonDown += (_, e) => e.Handled = true;
        remove.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            RemoveClip(index);
        };

        var frameHost = new Grid();
        frameHost.Children.Add(frame);
        frameHost.Children.Add(number);
        frameHost.Children.Add(remove);

        var name = new TextBlock
        {
            Text = Bidi.Value(Path.GetFileName(clip.Path)),
            FontSize = 11.5,
            Foreground = Theme.Text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            Width = 120,
            Margin = new Thickness(0, 5, 0, 0),
        };

        var meta = new TextBlock
        {
            Text = clip.IsUsable
                ? Bidi.Value(VideoToolCommon.Time(clip.Duration)) + (clip.HasAudio ? "" : " · " + T("tool.videojoin.silent"))
                : T("tool.videojoin.error.probe"),
            FontSize = 10.5,
            Foreground = clip.IsUsable ? Theme.Muted : Theme.Error,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Width = 120,
            Margin = new Thickness(0, 1, 0, 0),
        };

        var content = new StackPanel();
        content.Children.Add(frameHost);
        content.Children.Add(name);
        content.Children.Add(meta);

        var card = new Border
        {
            Width = 136,
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(7),
            CornerRadius = new CornerRadius(11),
            Background = Theme.Card,
            BorderBrush = clip.IsUsable ? Theme.PanelBorder : Theme.Error,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Tag = index,
            ToolTip = T("tool.videojoin.clip", Bidi.Number(index + 1)) + Environment.NewLine + Bidi.Value(clip.Path),
            Child = content,
        };
        card.MouseLeftButtonDown += CardDown;
        return card;
    }

    private void CardDown(object sender, MouseButtonEventArgs e)
    {
        if (_busy || _loading || sender is not Border card || card.Tag is not int index)
            return;
        _dragging = index;
        _dragStart = e.GetPosition(_strip);
        _moved = false;
        _strip.CaptureMouse();
        e.Handled = true;
    }

    private void StripMove(object sender, MouseEventArgs e)
    {
        if (_dragging < 0 || e.LeftButton != MouseButtonState.Pressed)
            return;

        var point = e.GetPosition(_strip);
        if (!_moved && Math.Abs(point.X - _dragStart.X) < 8 && Math.Abs(point.Y - _dragStart.Y) < 8)
            return;
        _moved = true;

        var target = IndexAt(point);
        if (target < 0 || target == _dragging)
            return;

        _clips = [.. VideoSplitJoin.Move(_clips, _dragging, target)];
        _dragging = target;
        RebuildStrip();
        UpdateSummary();
    }

    private void StripUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = -1;
        if (_strip.IsMouseCaptured)
            _strip.ReleaseMouseCapture();
        if (_moved)
            UpdateSummary();
        _moved = false;
    }

    /// <summary>The card under the pointer; works in both layout directions because it uses the cards' own bounds.</summary>
    private int IndexAt(Point point)
    {
        for (var index = 0; index < _cards.Count; index++)
        {
            var card = _cards[index];
            var origin = card.TranslatePoint(new Point(0, 0), _strip);
            if (point.X >= origin.X && point.X <= origin.X + card.ActualWidth
                && point.Y >= origin.Y && point.Y <= origin.Y + card.ActualHeight)
            {
                return index;
            }
        }
        return -1;
    }

    private void RemoveClip(int index)
    {
        if (_busy || _loading)
            return;
        if (_clips.Count <= 2)
        {
            ShowError(T("tool.videojoin.noClips"));
            return;
        }
        _clips = [.. VideoSplitJoin.RemoveAt(_clips, index)];
        RebuildStrip();
        UpdateSummary();
        ShowHint();
    }

    private async Task LoadThumbnailAsync(int index)
    {
        if (_ffmpeg is null || index < 0 || index >= _clips.Count)
            return;
        var clip = _clips[index];
        var ct = _lifetime.Token;
        try
        {
            var seconds = Math.Min(1, clip.Duration * 0.25);
            var bitmap = await VideoToolCommon.FrameAsync(_ffmpeg, clip.Path, seconds, ThumbWidth, ct);
            if (bitmap is null || ct.IsCancellationRequested)
                return;
            _thumbs[clip.Path] = bitmap;
            if (_dragging < 0)
                RebuildStrip();
        }
        catch (OperationCanceledException)
        {
            // The window closed while the frame was being decoded.
        }
        catch (Exception)
        {
            // A missing thumbnail is cosmetic.
        }
    }

    private void UpdateSummary()
    {
        if (_clips.Count == 0)
        {
            _summary.Text = string.Empty;
            return;
        }

        var output = T("tool.videojoin.output", Bidi.Value(VideoSplitJoin.JoinedName + "." + VideoSplitJoin.JoinedExtension));
        var first = _clips[0];
        if (!first.IsUsable)
        {
            _summary.Text = output;
            return;
        }

        var frame = T(
            "tool.videojoin.frame",
            Bidi.Value(first.Width.ToString(CultureInfo.InvariantCulture)),
            Bidi.Value(first.Height.ToString(CultureInfo.InvariantCulture)),
            Bidi.Value((first.Fps > 0 ? first.Fps : 30).ToString("0.##", CultureInfo.InvariantCulture)));
        _summary.Text = $"{output}  ·  {frame}";
    }

    // ---------------------------------------------------------------- status and apply

    private void ShowHint()
    {
        _statusIsError = false;
        _status.Foreground = Theme.Muted;
        _status.Text = T("tool.videojoin.hint");
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
        _status.Text = T("tool.videojoin.working") + " "
            + Bidi.Value(Math.Round(value * 100).ToString(CultureInfo.InvariantCulture) + "%");
    }

    private void Reset()
    {
        if (_busy || _initial is not { } initial)
            return;
        _clips = [.. initial];
        ShowHint();
        _progress.Visibility = Visibility.Collapsed;
        RebuildStrip();
        UpdateSummary();
    }

    private async Task<string?> ApplyAsync()
    {
        // The window is already interactive while the clips are probed, so Apply waits for that probe.
        await _ready.Task;
        if (_busy)
            return null;
        if (_clips.Count < 2)
            throw new ConversionException(T("tool.videojoin.noClips"));
        if (_clips.Any(clip => !clip.IsUsable) || _ffmpeg is null)
            throw new ConversionException(T("tool.videojoin.error.probe"));

        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _applyCts = cts;
        _busy = true;
        ShowProgress(0);

        try
        {
            var progress = new Progress<double>(ShowProgress);
            // Returning the path lets the shared window show its toast and close, like every other tool.
            return await VideoJoinRunner.RunAsync(_ffmpeg, _clips, progress, cts.Token);
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
            if (!_statusIsError)
                ShowHint();
        }
    }

    private void Cancel()
    {
        _lifetime.Cancel();
        _applyCts?.Cancel();
    }
}
