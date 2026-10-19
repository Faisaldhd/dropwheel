using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileAction.Core;
using FileAction.Core.Backends;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Audio;

/// <summary>
/// "Bleep" on the Tools wheel: mark one or more ranges on the waveform and replace exactly those parts
/// with a sine tone (1 kHz by default) at a chosen volume. Edges can be dragged on the waveform or nudged
/// by 10/100 ms, and A/B switches the preview between the original and the replaced audio.
/// Apply writes "&lt;name&gt; Bleeped.ext" next to the source.
/// </summary>
internal sealed class AudioBleepTool : IToolWindowSpec
{
    public static readonly AudioBleepTool Instance = new();

    public string Id => "tools.audiobleep";

    public string TitleKey => "tool.audiobleep.title";

    /// <summary>One waveform lane, two tone sliders, the range list and its toolbar.</summary>
    public ToolSpecLayout Layout { get; } = new(
        ChromeWidth: 36,
        ChromeHeight: 440,
        PreviewMinWidth: 320,
        PreviewMinHeight: 180,
        PreviewMaxWidth: 680,
        PreviewMaxHeight: 300,
        DefaultPreviewWidth: 560,
        DefaultPreviewHeight: 240);

    public bool Applies(IReadOnlyList<FileItem> items) =>
        items.Count == 1 && !items[0].IsDirectory && Formats.Audio.Contains(items[0].Format);

    public ToolContent Create(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new AudioBleepView(items[0], targets).Build();
}

/// <summary>Registers the tool from its own file, so no shared list has to be edited.</summary>
internal static class AudioBleepToolRegistration
{
    [ModuleInitializer]
    internal static void Register() => ToolRegistry.Register(AudioBleepTool.Instance);
}

/// <summary>The bleep window: waveform with draggable range edges, A/B preview, tone controls, range list.</summary>
internal sealed class AudioBleepView
{
    private readonly FileItem _item;
    private readonly DropTargets? _targets;
    private readonly ToolLocator _tools = new();
    private readonly TaskCompletionSource<MediaSize?> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _life = new();
    private readonly WaveformView _waveformView = new();
    private readonly List<AudioRange> _ranges = [];
    private readonly StackPanel _controls = new();
    private readonly Border _placeholder;
    private readonly TextBlock _outputName = new();
    private readonly TextBlock _status = AudioToolChrome.Status();
    private StackPanel _rangeList = new();

    private AudioWaveform? _waveform;
    private bool _bleeped;
    private double _toneHz = AudioEffects.DefaultToneHz;
    private int _toneVolume = AudioEffects.DefaultToneVolume;
    private int _stepMs = 100;
    private int _selected = -1;

    public AudioBleepView(FileItem item, DropTargets? targets)
    {
        _item = item;
        _targets = targets;
        _waveformView.EdgeDragged += OnEdgeDragged;
        _waveformView.EdgeDragCompleted += RefreshRanges;
        _placeholder = new Border
        {
            Background = Theme.Card,
            CornerRadius = new CornerRadius(10),
            Child = new ProgressBar { IsIndeterminate = true, Width = 120, Height = 4 },
        };
    }

    public ToolContent Build()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var preview = BuildPreview();
        FillControls();
        Grid.SetRow(preview, 0);
        Grid.SetRow(_controls, 1);
        root.Children.Add(preview);
        root.Children.Add(_controls);

        root.Unloaded += (_, _) => Cancel();
        return new ToolContent(root, Reset, ApplyAsync, _ready.Task, ActivateAsync);
    }

    /// <summary>Loads the waveform and marks a first range, so the tool is usable straight away.</summary>
    public async Task ActivateAsync()
    {
        try
        {
            var ffmpeg = _tools.Require(ExternalTool.Ffmpeg);
            var waveform = await AudioWaveform.LoadAsync(ffmpeg, _item.Path, _life.Token);
            _waveform = waveform;
            _waveformView.Waveform = waveform;
            _waveformView.Stereo = false;
            if (_ranges.Count == 0)
                AddRange();
            _placeholder.Visibility = Visibility.Collapsed;
            Paint();
            _ready.TrySetResult(new MediaSize(680, 260));
        }
        catch (OperationCanceledException)
        {
            _ready.TrySetResult(null);
        }
        catch (Exception ex)
        {
            ShowProblem(ex.Message);
            _ready.TrySetResult(null);
        }
    }

    private FrameworkElement BuildPreview()
    {
        var surface = new Grid { ClipToBounds = true };
        surface.Children.Add(_waveformView);
        surface.Children.Add(new Grid { Children = { _placeholder } });
        return AudioToolChrome.PreviewSurface(surface);
    }

    private void FillControls()
    {
        _controls.Children.Clear();

        _controls.Children.Add(Choice(T("tool.audiobleep.ab"), Controls.Segmented(
        [
            (T("tool.audiobleep.ab.original"), false),
            (T("tool.audiobleep.ab.bleeped"), true),
        ], _bleeped, bleeped =>
        {
            _bleeped = bleeped;
            Paint();
        })));

        _controls.Children.Add(SliderRow(
            T("tool.audiobleep.tone"),
            (int)Math.Round(_toneHz),
            100,
            5000,
            value =>
            {
                _toneHz = value;
                Paint();
            }));

        _controls.Children.Add(SliderRow(
            T("tool.audiobleep.volume"),
            _toneVolume,
            0,
            100,
            value =>
            {
                _toneVolume = value;
                Paint();
            }));

        _rangeList = new StackPanel();
        _controls.Children.Add(new ScrollViewer
        {
            Content = _rangeList,
            MaxHeight = 96,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 2, 0, 6),
        });

        _controls.Children.Add(Toolbar());

        _outputName.FontSize = 11.5;
        _outputName.Foreground = Theme.Muted;
        _outputName.TextTrimming = TextTrimming.CharacterEllipsis;
        _outputName.Margin = new Thickness(0, 8, 0, 0);
        _controls.Children.Add(_outputName);
        _controls.Children.Add(_status);

        RefreshRanges();
        Paint();
    }

    private FrameworkElement Choice(string caption, FrameworkElement control)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(Caption(caption));
        row.Children.Add(control);
        return row;
    }

    private FrameworkElement SliderRow(string caption, int value, int min, int max, Action<int> changed)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        row.Children.Add(Caption(caption));
        row.Children.Add(Controls.Slider(min, max, value, changed, () => { }));
        return row;
    }

    private static TextBlock Caption(string text)
    {
        var caption = AudioToolChrome.Caption(text);
        caption.Width = 104;
        return caption;
    }

    private FrameworkElement Toolbar()
    {
        var bar = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };

        var add = Controls.Button(T("tool.audiobleep.add"), AddRange, glyph: "\uE710");
        add.Margin = new Thickness(0, 0, 8, 6);
        bar.Children.Add(add);

        var step = Controls.Segmented(
        [
            ("10 ms", 10),
            ("100 ms", 100),
        ], _stepMs, value => _stepMs = value);
        step.Margin = new Thickness(0, 0, 8, 6);
        step.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(step);

        bar.Children.Add(Nudge(T("tool.audiobleep.start") + " \u2212", start: true, direction: -1));
        bar.Children.Add(Nudge(T("tool.audiobleep.start") + " +", start: true, direction: 1));
        bar.Children.Add(Nudge(T("tool.audiobleep.end") + " \u2212", start: false, direction: -1));
        bar.Children.Add(Nudge(T("tool.audiobleep.end") + " +", start: false, direction: 1));

        var remove = Controls.Button(T("tool.audiobleep.remove"), RemoveSelected);
        remove.Margin = new Thickness(0, 0, 8, 6);
        bar.Children.Add(remove);
        return bar;
    }

    private FrameworkElement Nudge(string text, bool start, int direction)
    {
        var button = Controls.Button(text, () => NudgeSelected(start, direction));
        button.Margin = new Thickness(0, 0, 6, 6);
        return button;
    }

    private void RefreshRanges()
    {
        _rangeList.Children.Clear();
        if (_ranges.Count == 0)
        {
            _rangeList.Children.Add(new TextBlock
            {
                Text = T("tool.audiobleep.empty"),
                FontSize = 12,
                Foreground = Theme.Muted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 2, 2, 2),
            });
            return;
        }

        for (var i = 0; i < _ranges.Count; i++)
            _rangeList.Children.Add(RangeRow(i, _ranges[i]));
    }

    private FrameworkElement RangeRow(int index, AudioRange range)
    {
        var selected = index == _selected;
        var times = Bidi.Value($"{FfmpegBackend.FormatTime(range.Start)} \u2013 {FfmpegBackend.FormatTime(range.End)}");
        var label = new TextBlock
        {
            Text = $"{T("tool.audiobleep.rangeLabel", index + 1)}   {times}",
            FontSize = 12,
            Foreground = selected ? Theme.TextOnAccent : Theme.Text,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var close = new TextBlock
        {
            Text = "\uE711",
            FontFamily = Theme.Icons,
            FontSize = 10,
            Foreground = selected ? Theme.TextOnAccent : Theme.Muted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            Cursor = Cursors.Hand,
        };
        close.MouseLeftButtonUp += (_, e) =>
        {
            Remove(index);
            e.Handled = true;
        };

        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(label);
        content.Children.Add(close);

        var row = new Border
        {
            Background = selected ? Theme.Accent : Theme.Track,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 0, 4),
            Cursor = Cursors.Hand,
            Child = content,
        };
        row.MouseLeftButtonUp += (_, _) => Select(index);
        return row;
    }

    /// <summary>Adds a range after the last one, or at the start when there is no room left.</summary>
    private void AddRange()
    {
        var duration = _waveform?.Duration ?? 0;
        if (duration <= AudioEffects.MinRangeSeconds)
            return;

        var start = _ranges.Count == 0 ? 0 : Math.Min(duration, _ranges[^1].End + 0.1);
        var end = Math.Min(duration, start + Math.Max(0.5, duration / 10));
        if (end - start < AudioEffects.MinRangeSeconds)
        {
            start = 0;
            end = Math.Min(duration, Math.Max(0.5, duration / 10));
        }
        _ranges.Add(new AudioRange(Math.Round(start, 3), Math.Round(end, 3)));
        _selected = _ranges.Count - 1;
        RefreshRanges();
        Paint();
    }

    private void Remove(int index)
    {
        if (index < 0 || index >= _ranges.Count)
            return;
        _ranges.RemoveAt(index);
        _selected = _ranges.Count == 0 ? -1 : Math.Min(_selected, _ranges.Count - 1);
        RefreshRanges();
        Paint();
    }

    private void RemoveSelected() => Remove(_selected);

    private void Select(int index)
    {
        _selected = index;
        RefreshRanges();
        Paint();
    }

    private void NudgeSelected(bool start, int direction)
    {
        if (_selected < 0 || _selected >= _ranges.Count)
            return;
        var duration = _waveform?.Duration ?? 0;
        var delta = direction * _stepMs / 1000.0;
        _ranges[_selected] = start
            ? AudioEffects.NudgeStart(_ranges[_selected], delta, duration)
            : AudioEffects.NudgeEnd(_ranges[_selected], delta, duration);
        RefreshRanges();
        Paint();
    }

    /// <summary>Dragging an edge on the waveform updates the range directly, then the list catches up.</summary>
    private void OnEdgeDragged(int index, bool start, double seconds)
    {
        if (index < 0 || index >= _ranges.Count)
            return;
        var duration = _waveform?.Duration ?? 0;
        var range = _ranges[index];
        _ranges[index] = start
            ? new AudioRange(
                Math.Clamp(seconds, 0, Math.Max(0, range.End - AudioEffects.MinRangeSeconds)),
                range.End)
            : new AudioRange(
                range.Start,
                Math.Clamp(seconds, Math.Min(duration, range.Start + AudioEffects.MinRangeSeconds), Math.Max(0, duration)));
        Paint();
    }

    private void Paint()
    {
        _waveformView.SetRanges(_ranges);
        _waveformView.ShowBleep(_bleeped, Math.Clamp(_toneVolume, 0, 100) / 100.0);

        var format = FfmpegBackend.SafeContainer(_item.Format);
        _outputName.Text = Bidi.Value($"{_item.BaseName} {T("tool.audiobleep.suffix")}.{format}");
    }

    private void Reset()
    {
        _ranges.Clear();
        _selected = -1;
        _bleeped = false;
        _toneHz = AudioEffects.DefaultToneHz;
        _toneVolume = AudioEffects.DefaultToneVolume;
        _stepMs = 100;
        _status.Text = string.Empty;
        FillControls();
        if (_waveform is not null)
            AddRange();
    }

    private void ShowProblem(string message)
    {
        _placeholder.Child = new TextBlock
        {
            Text = message,
            Foreground = Theme.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(18),
        };
        _placeholder.Visibility = Visibility.Visible;
        _status.Text = message;
    }

    private void Report(double value) =>
        _status.Text = ((int)Math.Round(Math.Clamp(value, 0, 1) * 100)).ToString(CultureInfo.InvariantCulture) + " %";

    private async Task<string?> ApplyAsync()
    {
        if (_waveform is null)
            throw new ConversionException(T("tool.audiobleep.error.decode"));

        var ranges = AudioEffects.NormalizeRanges(_ranges, _waveform.Duration);
        if (ranges.Count == 0)
            throw new ConversionException(T("tool.audiobleep.error.noRanges"));

        var directory = AudioToolOutput.DirectoryFor(_item, _targets);
        var format = FfmpegBackend.SafeContainer(_item.Format);
        var suffix = T("tool.audiobleep.suffix");
        var args = AudioEffects.BleepArguments(ranges, _toneHz, _toneVolume);
        var codec = FfmpegBackend.CodecArgs(format, new Settings());
        var tools = _tools;
        var duration = _waveform.Duration;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
        _status.Text = T("tool.audiobleep.working");
        try
        {
            return await OutputPaths.ProduceAsync(directory, $"{_item.BaseName} {suffix}", format, temp =>
            {
                var context = new OperationContext
                {
                    Tools = tools,
                    CancellationToken = cts.Token,
                    OutputDirectory = directory,
                };
                return FfmpegBackend.RunAsync(
                    context,
                    [_item.Path],
                    [.. args, .. codec],
                    temp,
                    new Progress<double>(Report),
                    knownDuration: duration);
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
