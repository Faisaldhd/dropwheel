using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Video;

/// <summary>
/// Trim one video: a scrub timeline with start/end handles, "hh:mm:ss.mmm" boxes, one-frame nudges and a
/// fast stream-copy mode that automatically falls back to a re-encode when the codecs do not allow copy.
/// The preview only plays when WPF can open the file; otherwise the tool says it is scrub-only.
/// </summary>
internal sealed class VideoTrimView
{
    private const int StripFrames = 9;

    private readonly FileItem _item;
    private readonly DropTargets? _targets;
    private readonly VideoPreview _preview;
    private readonly VideoTimeline _timeline = new(compact: false, showRange: true);
    private readonly VideoTimeBox _startBox = new();
    private readonly VideoTimeBox _endBox = new();
    private readonly TextBlock _durationText;
    private readonly TextBlock _lengthText;
    private readonly TextBlock _status;
    private readonly TextBlock _scrubOnly;
    private readonly Border _playButton;
    private readonly TaskCompletionSource<MediaSize?> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _lifetime = new();

    private VideoInfo? _info;
    private TimeSpan _start;
    private TimeSpan _end;
    private bool _fastCopy = true;
    private bool _busy;

    public VideoTrimView(FileItem item, DropTargets? targets)
    {
        _item = item;
        _targets = targets;
        _preview = new VideoPreview(item, "tool.videotrim");

        _durationText = VideoControls.Hint(string.Empty);
        _durationText.Foreground = Theme.Text;
        _lengthText = VideoControls.Hint(string.Empty);
        _lengthText.Foreground = Theme.Text;
        _status = VideoControls.Hint(string.Empty);
        _scrubOnly = VideoControls.Hint(T("tool.videotrim.scrubOnly"));
        _scrubOnly.Visibility = Visibility.Collapsed;
        _scrubOnly.Margin = new Thickness(0, 2, 0, 0);

        _playButton = Controls.Button(T("tool.videotrim.play"), TogglePlayback, glyph: Glyphs.Play);
        _playButton.HorizontalAlignment = HorizontalAlignment.Left;
        _playButton.VerticalAlignment = VerticalAlignment.Bottom;
        _playButton.Margin = new Thickness(14);
        _playButton.Visibility = Visibility.Collapsed;
        Panel.SetZIndex(_playButton, 6);

        _timeline.SeekRequested += OnSeekRequested;
        _timeline.RangeChanged += OnRangeChanged;
        _preview.PlaybackPositionChanged += position => _timeline.SetPosition(position);
        _preview.PlayingChanged += playing =>
            Controls.SetText(_playButton, T(playing ? "tool.videotrim.pause" : "tool.videotrim.play"));
    }

    public ToolContent Build()
    {
        var host = new Grid();
        host.Children.Add(_preview.View);
        host.Children.Add(_playButton);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var timeRow = BuildTimeRow();
        var footer = BuildFooter();
        root.Children.Add(host);
        root.Children.Add(_timeline);
        root.Children.Add(timeRow);
        root.Children.Add(footer);
        Grid.SetRow(host, 0);
        Grid.SetRow(_timeline, 1);
        Grid.SetRow(timeRow, 2);
        Grid.SetRow(footer, 3);

        root.Unloaded += (_, _) =>
        {
            _lifetime.Cancel();
            _preview.Dispose();
        };

        return new ToolContent(root, Reset, ApplyAsync, _ready.Task, ActivateAsync);
    }

    private FrameworkElement BuildTimeRow()
    {
        var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        for (var index = 0; index < 9; index++)
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = index == 4 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto,
            });

        var startLabel = VideoControls.FieldLabel(T("tool.videotrim.start"));
        var endLabel = VideoControls.FieldLabel(T("tool.videotrim.end"));
        endLabel.Margin = new Thickness(18, 0, 8, 0);

        var backStart = VideoControls.IconButton("\uE76B", T("tool.videotrim.frameBack"), () => NudgeStart(-1));
        var forwardStart = VideoControls.IconButton("\uE76C", T("tool.videotrim.frameForward"), () => NudgeStart(+1));
        var backEnd = VideoControls.IconButton("\uE76B", T("tool.videotrim.frameBack"), () => NudgeEnd(-1));
        var forwardEnd = VideoControls.IconButton("\uE76C", T("tool.videotrim.frameForward"), () => NudgeEnd(+1));

        Add(row, startLabel, 0);
        Add(row, _startBox, 1);
        Add(row, backStart, 2);
        Add(row, forwardStart, 3);
        Add(row, endLabel, 5);
        Add(row, _endBox, 6);
        Add(row, backEnd, 7);
        Add(row, forwardEnd, 8);

        _startBox.Committed += value => CommitStart(value);
        _endBox.Committed += value => CommitEnd(value);
        return row;
    }

    private FrameworkElement BuildFooter()
    {
        var footer = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var info = new StackPanel { Orientation = Orientation.Horizontal };
        _durationText.Margin = new Thickness(0, 0, 18, 0);
        info.Children.Add(_durationText);
        info.Children.Add(_lengthText);

        var copyRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var copyLabel = VideoControls.FieldLabel(T("tool.videotrim.fastCopy"));
        copyLabel.Margin = new Thickness(0, 0, 8, 0);
        copyRow.Children.Add(copyLabel);
        copyRow.Children.Add(Controls.Toggle(true, on => _fastCopy = on));

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(info);
        top.Children.Add(copyRow);
        Grid.SetColumn(copyRow, 1);
        copyRow.ToolTip = UiSetup.Tip(T("tool.videotrim.fastCopyHint"));

        var bottom = new StackPanel();
        bottom.Children.Add(_status);
        bottom.Children.Add(_scrubOnly);

        footer.Children.Add(top);
        footer.Children.Add(bottom);
        Grid.SetRow(top, 0);
        Grid.SetRow(bottom, 1);
        return footer;
    }

    private static void Add(Grid grid, UIElement element, int column)
    {
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    public async Task ActivateAsync()
    {
        try
        {
            if (!await _preview.LoadAsync())
            {
                _ready.TrySetResult(null);
                return;
            }

            _info = _preview.Info!;
            _start = TimeSpan.Zero;
            _end = _info.Duration;
            _timeline.SetFrameRate(_info.Fps);
            _timeline.SetDuration(_info.Duration);
            _timeline.SetRange(_start, _end);
            _timeline.SetPosition(TimeSpan.Zero);
            _startBox.Value = _start;
            _endBox.Value = _end;
            _preview.SetPlayRange(_start, _end);
            UpdateLengths();
            _ready.TrySetResult(new MediaSize(_info.Width, _info.Height));

            _ = LoadStripAsync();
            _ = EnablePlaybackAsync();
        }
        catch (OperationCanceledException)
        {
            _ready.TrySetResult(null);
        }
        catch (Exception)
        {
            _ready.TrySetResult(null);
        }
    }

    private async Task LoadStripAsync()
    {
        var frames = await _preview.LoadStripAsync(StripFrames, 170);
        if (frames.Count > 0 && !_lifetime.IsCancellationRequested)
            _timeline.SetStrip(frames);
    }

    private async Task EnablePlaybackAsync()
    {
        var available = await _preview.EnablePlaybackAsync(_start, _end);
        if (available)
        {
            _playButton.Visibility = Visibility.Visible;
            return;
        }
        if (!_lifetime.IsCancellationRequested)
            _scrubOnly.Visibility = Visibility.Visible;
    }

    private void TogglePlayback()
    {
        _preview.SetPlayRange(_start, _end);
        _preview.TogglePlayback();
    }

    private void OnSeekRequested(TimeSpan position)
    {
        _preview.StopPlayback();
        _ = _preview.SeekAsync(position);
    }

    private void OnRangeChanged(TimeSpan start, TimeSpan end)
    {
        _start = start;
        _end = end;
        _startBox.Value = start;
        _endBox.Value = end;
        _preview.SetPlayRange(start, end);
        UpdateLengths();
        _ = _preview.SeekAsync(start);
    }

    private void NudgeStart(int frames)
    {
        if (_info is null)
            return;
        var minimum = VideoEdit.FrameStep(_info.Fps);
        _start = VideoEdit.StepFrames(_start, _info.Fps, frames, _end - minimum);
        ApplyRange();
    }

    private void NudgeEnd(int frames)
    {
        if (_info is null)
            return;
        var minimum = VideoEdit.FrameStep(_info.Fps);
        var candidate = VideoEdit.StepFrames(_end, _info.Fps, frames, _info.Duration);
        _end = candidate < _start + minimum ? VideoEdit.Clamp(_start + minimum, _info.Duration) : candidate;
        ApplyRange();
    }

    private void CommitStart(TimeSpan value)
    {
        if (_info is null)
            return;
        _start = VideoEdit.Clamp(value, _end - VideoEdit.FrameStep(_info.Fps));
        ApplyRange();
        // A typed value outside the range is clamped: show what will actually be used.
        _startBox.ShowValue();
    }

    private void CommitEnd(TimeSpan value)
    {
        if (_info is null)
            return;
        var minimum = VideoEdit.FrameStep(_info.Fps);
        var candidate = VideoEdit.Clamp(value, _info.Duration);
        _end = candidate < _start + minimum ? VideoEdit.Clamp(_start + minimum, _info.Duration) : candidate;
        ApplyRange();
        _endBox.ShowValue();
    }

    private void ApplyRange()
    {
        _timeline.SetRange(_start, _end);
        _startBox.Value = _start;
        _endBox.Value = _end;
        _preview.SetPlayRange(_start, _end);
        UpdateLengths();
        _ = _preview.SeekAsync(_start);
    }

    private void UpdateLengths()
    {
        if (_info is null)
            return;
        _durationText.Text = T("tool.videotrim.duration") + "  " + Bidi.Value(VideoEdit.FormatClock(_info.Duration));
        var length = _end > _start ? _end - _start : TimeSpan.Zero;
        _lengthText.Text = T("tool.videotrim.newLength") + "  " + Bidi.Value(VideoEdit.FormatClock(length));
    }

    private void Reset()
    {
        _preview.StopPlayback();
        if (_info is null)
            return;
        _start = TimeSpan.Zero;
        _end = _info.Duration;
        ApplyRange();
        _timeline.SetPosition(TimeSpan.Zero);
        _ = _preview.SeekAsync(TimeSpan.Zero);
    }

    private async Task<string?> ApplyAsync()
    {
        var info = _info;
        if (info is null)
            throw new ConversionException(T("tool.videotrim.probeFailed", Bidi.Value(_item.Name)));
        var ffmpeg = _preview.Ffmpeg
            ?? throw new ConversionException(T("tool.videotrim.noFfmpeg"));
        if (_busy)
            return null;
        if (_end <= _start)
            throw new ConversionException(T("tool.videotrim.invalidRange"));

        var container = VideoEdit.OutputContainerFor(_item.Format);
        var mode = _fastCopy && VideoEdit.SupportsStreamCopy(_item.Format)
            ? VideoTrimMode.StreamCopy
            : VideoTrimMode.ReEncode;
        var directory = VideoOutput.DirectoryFor(_item, _targets);
        var name = VideoOutput.NameFor(_item, T("tool.videotrim.suffix"));

        SetBusy(true);
        try
        {
            return await OutputPaths.ProduceAsync(directory, name, container, async temporary =>
            {
                var request = new VideoTrimRequest(
                    _item.Path, temporary, _start, _end, container, mode, info.HasAudio);
                try
                {
                    await ProcessRunner.RunAsync(ffmpeg, VideoEdit.BuildTrimArgs(request), _lifetime.Token);
                }
                catch (ConversionException) when (mode == VideoTrimMode.StreamCopy && !_lifetime.IsCancellationRequested)
                {
                    // Some codec/container pairs refuse a copy (for example MPEG-2 into MP4): encode instead.
                    await ProcessRunner.RunAsync(
                        ffmpeg, VideoEdit.BuildTrimArgs(request with { Mode = VideoTrimMode.ReEncode }), _lifetime.Token);
                }
            });
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _status.Text = busy ? T("tool.videotrim.applyBusy") : string.Empty;
    }
}
