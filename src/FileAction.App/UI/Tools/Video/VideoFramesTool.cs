using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FileAction.Core;
using static FileAction.Core.Loc;
using WpfImage = System.Windows.Controls.Image;

namespace FileAction.App.UI.Tools.Video;

/// <summary>
/// Save frames: scrub a video, step one frame at a time, capture the interesting ones and write every
/// captured frame as a full-resolution PNG into a new folder next to the source. The preview is a
/// downsampled FFmpeg frame; the export always reads the original file.
/// </summary>
internal sealed class VideoFramesTool : IToolWindowSpec
{
    public static readonly VideoFramesTool Instance = new();

    public string Id => "tools.videoframes";

    public string TitleKey => "tool.videoframes.title";

    /// <summary>The count is filled in at runtime by <see cref="VideoFramesView.UpdatePrimaryLabel"/>.</summary>
    public string PrimaryLabelKey => "tool.videoframes.primary.initial";

    public ToolSpecLayout Layout => LayoutSpec;

    public bool Applies(IReadOnlyList<FileItem> items) =>
        items.Count == 1 && !items[0].IsDirectory && Formats.Video.Contains(items[0].Format);

    public ToolContent Create(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new VideoFramesView(items[0], targets).Build();

    /// <summary>
    /// A 16:9 clip opens around 720 × 705 DIP: a wide preview (560 DIP minimum) plus the capture controls.
    /// The sizer shrinks it on small screens and follows portrait media inside the same bounds.
    /// </summary>
    internal static readonly ToolSpecLayout LayoutSpec = new(
        ChromeWidth: 36,
        ChromeHeight: 320,
        PreviewMinWidth: 560,
        PreviewMinHeight: 315,
        PreviewMaxWidth: 684,
        PreviewMaxHeight: 385,
        DefaultPreviewWidth: 684,
        DefaultPreviewHeight: 385);
}

/// <summary>Registers the tool from its own file; no shared list is edited.</summary>
internal static class VideoFramesRegistration
{
    [ModuleInitializer]
    internal static void Register() => ToolRegistry.Register(VideoFramesTool.Instance);
}

/// <summary>The wheel operation. It only opens the window; the writing happens after Apply.</summary>
internal sealed class VideoFramesOperation : Operation, IInteractiveTool
{
    public VideoFramesOperation()
        : base("tools.videoframes", WheelKind.Tools, T("tool.videoframes.label"), Glyphs.Camera, T("tool.videoframes.subtitle"))
    {
        Requires = [ExternalTool.Ffmpeg];
    }

    public override bool AppliesTo(IReadOnlyList<FileItem> items) => VideoFramesTool.Instance.Applies(items);

    public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
        Task.FromResult(OperationResult.Of([]));

    public void Open(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new ToolWindow(VideoFramesTool.Instance, items, targets).Show();
}

/// <summary>The window content: preview, position, scrubber, step/capture buttons and the capture list.</summary>
internal sealed class VideoFramesView
{
    private const int PreviewDelayMilliseconds = 70;

    private readonly FileItem _item;
    private readonly DropTargets? _targets;
    private readonly ToolLocator _tools = new();
    private readonly TaskCompletionSource<MediaSize?> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CaptureList _captures = new();

    private readonly Grid _preview = new() { ClipToBounds = true };
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

    private Border? _previewCard;
    private Border? _previousButton;
    private Border? _nextButton;
    private Border? _captureButton;
    private Grid? _root;
    private ToolWindow? _window;
    private VideoThemeHook? _theme;

    private string? _ffmpeg;
    private VideoInfo? _info;
    private int _total = 1;
    private int _frame;
    private CancellationTokenSource? _seek;
    private CancellationTokenSource? _apply;

    public VideoFramesView(FileItem item, DropTargets? targets)
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
        _placeholderText.Text = T("tool.videoframes.loading");
        _placeholderText.TextAlignment = TextAlignment.Center;

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
        var root = new Grid();
        _root = root;
        for (var i = 0; i < 6; i++)
            root.RowDefinitions.Add(new RowDefinition { Height = i == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });

        var preview = BuildPreview();
        var buttons = BuildButtons();
        var footer = BuildFooter();
        var chips = BuildChips();

        Grid.SetRow(preview, 0);
        Grid.SetRow(_position, 1);
        Grid.SetRow(_scrubber, 2);
        Grid.SetRow(buttons, 3);
        Grid.SetRow(footer, 4);
        Grid.SetRow(chips, 5);
        root.Children.Add(preview);
        root.Children.Add(_position);
        root.Children.Add(_scrubber);
        root.Children.Add(buttons);
        root.Children.Add(footer);
        root.Children.Add(chips);

        _position.Margin = new Thickness(2, 0, 2, 2);
        _position.TextTrimming = TextTrimming.CharacterEllipsis;
        _scrubber.Margin = new Thickness(2, 0, 2, 6);

        _theme = new VideoThemeHook(root, RepaintTheme);
        root.Loaded += (_, _) =>
        {
            _window = Window.GetWindow(root) as ToolWindow;
            UpdatePrimaryLabel();
        };
        root.Unloaded += (_, _) =>
        {
            _window = null;
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
                ShowMessage(T("tool.videoframes.needsFfmpeg"));
                _ready.TrySetResult(null);
                return;
            }

            var info = await VideoProbe.ReadAsync(_ffmpeg, _item.Path, CancellationToken.None);
            if (info is null)
            {
                ShowMessage(T("tool.videoframes.unreadable"));
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
            PaintCaptures();
            _ready.TrySetResult(new MediaSize(info.Width, info.Height));

            _spinner.Visibility = Visibility.Visible;
            _placeholder.Visibility = Visibility.Visible;
            _placeholderText.Text = T("tool.videoframes.loading");
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
        _preview.Children.Add(_image);
        _preview.Children.Add(_placeholder);

        _previewCard = new Border
        {
            Background = Theme.Card,
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 4, 0, 8),
            Child = _preview,
        };
        return _previewCard;
    }

    private FrameworkElement BuildButtons()
    {
        var previous = Controls.Button(T("tool.videoframes.previous"), () => SeekTo(_frame - 1), glyph: "\uE76B");
        previous.Margin = new Thickness(0, 0, 6, 0);
        var next = Controls.Button(T("tool.videoframes.next"), () => SeekTo(_frame + 1), glyph: "\uE76C");
        _previousButton = previous;
        _nextButton = next;
        var nav = new StackPanel { Orientation = Orientation.Horizontal };
        nav.Children.Add(previous);
        nav.Children.Add(next);

        var capture = Controls.Button(T("tool.videoframes.capture"), Capture, glyph: Glyphs.Camera);
        _captureButton = capture;

        Controls.SetText(_cancel, T("tool.videoframes.cancel"));
        _cancel.Padding = new Thickness(10, 4, 10, 5);
        _cancel.Margin = new Thickness(0, 0, 6, 0);
        _cancel.Visibility = Visibility.Collapsed;
        _cancel.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _apply?.Cancel();
        };

        var row = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(nav, Dock.Left);
        DockPanel.SetDock(capture, Dock.Right);
        DockPanel.SetDock(_cancel, Dock.Right);
        row.Children.Add(nav);
        row.Children.Add(capture);
        row.Children.Add(_cancel);
        return row;
    }

    /// <summary>Header on the left ("Captured frames (3)"), progress/status on the right.</summary>
    private FrameworkElement BuildFooter()
    {
        _header.Text = T("tool.videoframes.captured", 0);
        _status.TextTrimming = TextTrimming.CharacterEllipsis;
        _status.HorizontalAlignment = HorizontalAlignment.Right;
        _status.MaxWidth = 360;

        var row = new DockPanel { LastChildFill = true, Margin = new Thickness(2, 0, 2, 4) };
        DockPanel.SetDock(_header, Dock.Left);
        row.Children.Add(_header);
        row.Children.Add(_status);
        return row;
    }

    private FrameworkElement BuildChips()
    {
        _chips.Margin = new Thickness(0, 0, 0, 2);
        _emptyHint.Text = T("tool.videoframes.empty");
        PaintCaptures();
        return new ScrollViewer
        {
            MaxHeight = 74,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _chips,
        };
    }

    private void UpdatePosition()
    {
        if (_info is null)
        {
            _position.Text = string.Empty;
            return;
        }
        _position.Text = T(
            "tool.videoframes.position",
            FrameMath.FrameNumber(_frame),
            Bidi.Value(FrameMath.Timecode(_frame, _info.Fps)));
    }

    private void UpdateHeader() => _header.Text = T("tool.videoframes.captured", _captures.Count);

    /// <summary>"Save N Frame(s)" on the shared primary button, refreshed as the capture list changes.</summary>
    private void UpdatePrimaryLabel()
    {
        _window ??= _root is null ? null : Window.GetWindow(_root) as ToolWindow;
        if (_window is null)
            return;
        _window.SetPrimaryLabel(_captures.Count == 1
            ? T("tool.videoframes.primary.single", 1)
            : T("tool.videoframes.primary.plural", _captures.Count));
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
        _scrubber.ApplyTheme();
        if (_previousButton is not null)
            VideoTheme.RepaintButton(_previousButton);
        if (_nextButton is not null)
            VideoTheme.RepaintButton(_nextButton);
        if (_captureButton is not null)
            VideoTheme.RepaintButton(_captureButton);
        VideoTheme.RepaintButton(_cancel);
        PaintCaptures();
    }

    private void PaintCaptures()
    {
        UpdatePrimaryLabel();
        _chips.Children.Clear();
        if (_captures.IsEmpty)
        {
            _chips.Children.Add(_emptyHint);
            return;
        }

        foreach (var frame in _captures.Frames)
        {
            var number = new TextBlock
            {
                Text = FrameMath.FrameNumber(frame),
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
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = T("tool.videoframes.remove"),
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(number);
            content.Children.Add(remove);

            var chip = new Border
            {
                Background = Theme.Track,
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(8, 3, 8, 4),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = System.Windows.Input.Cursors.Hand,
                Child = content,
            };
            var capturedFrame = frame;
            chip.MouseLeftButtonUp += (_, _) => SeekTo(capturedFrame);
            remove.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                _captures.Remove(capturedFrame);
                PaintCaptures();
                UpdateHeader();
            };
            _chips.Children.Add(chip);
        }
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

    private void Capture()
    {
        if (_info is null || !_captures.Add(_frame))
            return;
        PaintCaptures();
        UpdateHeader();
    }

    /// <summary>Loads one downsampled preview frame, cancelling the previous scrub request.</summary>
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
                FrameMath.TimeOfFrame(frame, _info.Fps),
                T("tool.videoframes.unreadable"),
                seek.Token).ConfigureAwait(true);

            if (seek.IsCancellationRequested || !ReferenceEquals(seek, _seek))
                return;

            _image.Source = bitmap;
            _image.Visibility = Visibility.Visible;
            _placeholder.Visibility = Visibility.Collapsed;
            _spinner.Visibility = Visibility.Collapsed;
            _status.Text = string.Empty;
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
        _captures.Clear();
        _frame = 0;
        _status.Text = string.Empty;
        if (_info is not null)
        {
            _scrubber.SetValue(0);
            UpdatePosition();
            _ = ShowFrameAsync(0, immediate: true);
        }
        PaintCaptures();
        UpdateHeader();
    }

    private async Task<string?> ApplyAsync()
    {
        if (_ffmpeg is null || _info is null || _captures.IsEmpty)
            return null;

        _seek?.Cancel();
        var frames = _captures.Frames.ToArray();
        var directory = VideoOutput.Folder(_item, _targets);
        _apply = new CancellationTokenSource();
        _cancel.Visibility = Visibility.Visible;
        _status.Text = T("tool.videoframes.exporting", 0, frames.Length);
        var progress = new Progress<(int Done, int Total)>(value =>
            _status.Text = T("tool.videoframes.exporting", value.Done, value.Total));
        try
        {
            return await VideoRunner.ExportFramesAsync(
                _ffmpeg, _item, frames, _info.Fps, directory, progress, _apply.Token).ConfigureAwait(true);
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
}
