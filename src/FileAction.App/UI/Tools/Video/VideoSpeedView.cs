using System.Windows;
using System.Windows.Controls;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Video;

/// <summary>
/// Change a video's speed from 0.25× to 4×: a logarithmic slider, presets, a scrub strip and the new
/// length. Video gets setpts and audio an atempo chain, so the pitch is kept; a silent video is fine.
/// </summary>
internal sealed class VideoSpeedView
{
    private const int StripFrames = 9;

    private static readonly double[] Presets = [0.25, 0.5, 1, 1.5, 2, 4];

    private readonly FileItem _item;
    private readonly DropTargets? _targets;
    private readonly VideoPreview _preview;
    private readonly VideoTimeline _timeline = new(compact: true);
    private readonly VideoRateSlider _slider = new();
    private readonly StackPanel _presetRow = new() { Orientation = Orientation.Horizontal };
    private readonly List<(double Value, Border Chip)> _presetChips = [];
    private readonly TextBlock _lengthText;
    private readonly TextBlock _status;
    private readonly TaskCompletionSource<MediaSize?> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _lifetime = new();

    private VideoInfo? _info;
    private double _rate = 1;
    private bool _busy;

    public VideoSpeedView(FileItem item, DropTargets? targets)
    {
        _item = item;
        _targets = targets;
        _preview = new VideoPreview(item, "tool.videospeed");
        _lengthText = VideoControls.Hint(string.Empty);
        _lengthText.Foreground = Theme.Text;
        _status = VideoControls.Hint(string.Empty);

        _slider.ValueChanged += value => SetRate(value, fromSlider: true);
        _timeline.SeekRequested += position => _ = _preview.SeekAsync(position);
    }

    public ToolContent Build()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var sliderRow = BuildSliderRow();
        var presets = BuildPresets();
        var footer = BuildFooter();
        root.Children.Add(_preview.View);
        root.Children.Add(_timeline);
        root.Children.Add(sliderRow);
        root.Children.Add(presets);
        root.Children.Add(footer);
        Grid.SetRow(_preview.View, 0);
        Grid.SetRow(_timeline, 1);
        Grid.SetRow(sliderRow, 2);
        Grid.SetRow(presets, 3);
        Grid.SetRow(footer, 4);

        root.Unloaded += (_, _) =>
        {
            _lifetime.Cancel();
            _preview.Dispose();
        };

        return new ToolContent(root, Reset, ApplyAsync, _ready.Task, ActivateAsync);
    }

    private FrameworkElement BuildSliderRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 4) };
        row.Children.Add(VideoControls.FieldLabel(T("tool.videospeed.rate")));
        row.Children.Add(_slider);
        return row;
    }

    private FrameworkElement BuildPresets()
    {
        _presetRow.Margin = new Thickness(0, 2, 0, 4);
        foreach (var preset in Presets)
        {
            var chip = VideoControls.Chip(VideoEdit.SpeedText(preset), () => SetRate(preset, fromSlider: false));
            _presetChips.Add((preset, chip));
            _presetRow.Children.Add(chip);
        }
        PaintPresets();
        return _presetRow;
    }

    private FrameworkElement BuildFooter()
    {
        var footer = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        var info = new StackPanel { Orientation = Orientation.Horizontal };
        _lengthText.Margin = new Thickness(0, 0, 18, 0);
        info.Children.Add(_lengthText);
        info.Children.Add(VideoControls.Hint(T("tool.videospeed.pitch") + "  ·  " + T("tool.videospeed.silent")));
        footer.Children.Add(info);
        footer.Children.Add(_status);
        return footer;
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
            _timeline.SetFrameRate(_info.Fps);
            _timeline.SetDuration(_info.Duration);
            _timeline.SetPosition(TimeSpan.Zero);
            SetRate(1, fromSlider: false);
            _ready.TrySetResult(new MediaSize(_info.Width, _info.Height));

            _ = LoadStripAsync();
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
        var frames = await _preview.LoadStripAsync(StripFrames, 150);
        if (frames.Count > 0 && !_lifetime.IsCancellationRequested)
            _timeline.SetStrip(frames);
    }

    private void SetRate(double value, bool fromSlider)
    {
        _rate = VideoEdit.ClampSpeed(value);
        if (!fromSlider)
            _slider.SetValue(_rate);
        UpdateLength();
        PaintPresets();
    }

    private void PaintPresets()
    {
        foreach (var (value, chip) in _presetChips)
            VideoControls.PaintChip(chip, Math.Abs(value - _rate) < 0.001);
    }

    private void UpdateLength()
    {
        if (_info is null)
            return;
        var seconds = _info.Duration.TotalSeconds / _rate;
        _lengthText.Text = T("tool.videospeed.newLength") + "  "
            + Bidi.Value(VideoEdit.FormatClock(TimeSpan.FromSeconds(seconds)));
    }

    private void Reset()
    {
        SetRate(1, fromSlider: false);
        _timeline.SetPosition(TimeSpan.Zero);
        _ = _preview.SeekAsync(TimeSpan.Zero);
    }

    private async Task<string?> ApplyAsync()
    {
        var info = _info;
        if (info is null)
            throw new ConversionException(T("tool.videospeed.probeFailed", Bidi.Value(_item.Name)));
        var ffmpeg = _preview.Ffmpeg
            ?? throw new ConversionException(T("tool.videospeed.noFfmpeg"));
        if (_busy)
            return null;

        var rate = VideoEdit.ClampSpeed(_rate);
        var container = VideoEdit.OutputContainerFor(_item.Format);
        var directory = VideoOutput.DirectoryFor(_item, _targets);
        var name = VideoOutput.NameFor(_item, VideoEdit.SpeedSuffix(rate));
        var request = new VideoSpeedRequest(_item.Path, string.Empty, rate, container, info.HasAudio);

        SetBusy(true);
        try
        {
            return await OutputPaths.ProduceAsync(directory, name, container, temporary =>
                ProcessRunner.RunAsync(
                    ffmpeg,
                    VideoEdit.BuildSpeedArgs(request with { OutputPath = temporary }),
                    _lifetime.Token));
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
        _status.Text = busy ? T("tool.videospeed.applyBusy") : string.Empty;
    }
}
