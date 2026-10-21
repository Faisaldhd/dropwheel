using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FileAction.Core;
using FileAction.Core.Backends;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Audio;

/// <summary>
/// "Bleep" on the Tools wheel: a Bleep layer above the real waveform. Dragging across the layer adds a
/// section, dragging a clip moves it and dragging either edge resizes it (all clamped to the audio by
/// <see cref="AudioEffects"/>). "With Bleeps" previews the tone that will replace each section.
/// Apply writes "&lt;name&gt; Bleeped.ext" next to the source.
/// </summary>
internal sealed class AudioBleepTool : IToolWindowSpec
{
    public static readonly AudioBleepTool Instance = new();

    public string Id => "tools.audiobleep";

    public string TitleKey => "tool.audiobleep.title";

    /// <summary>Header, the two lanes, then the helper/preview/toolbar/tone rows below them.</summary>
    public ToolSpecLayout Layout { get; } = new(
        ChromeWidth: 36,
        ChromeHeight: 390,
        PreviewMinWidth: 320,
        PreviewMinHeight: 150,
        PreviewMaxWidth: 620,
        PreviewMaxHeight: 220,
        DefaultPreviewWidth: 560,
        DefaultPreviewHeight: 170);

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

/// <summary>The bleep window: counter header, Bleep layer, audio lane, preview row and tone controls.</summary>
internal sealed class AudioBleepView
{
    private readonly FileItem _item;
    private readonly DropTargets? _targets;
    private readonly ToolLocator _tools = new();
    private readonly TaskCompletionSource<MediaSize?> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _life = new();
    private readonly WaveformView _audioLane = new();
    private readonly BleepLaneView _bleepLane = new();
    private readonly StackPanel _controls = new();
    private readonly StackPanel _previewRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
    private readonly Border _placeholder;
    private readonly TextBlock _count = new();
    private readonly TextBlock _bleepStart = TimeLabel();
    private readonly TextBlock _bleepTotal = TimeLabel();
    private readonly TextBlock _audioStart = TimeLabel();
    private readonly TextBlock _audioTotal = TimeLabel();
    private readonly TextBlock _outputName = new();
    private readonly TextBlock _status = AudioToolChrome.Status();
    private readonly List<FrameworkElement> _selectionOnly = [];

    private PreviewButton? _originalPreview;
    private PreviewButton? _bleepedPreview;
    private AudioWaveform? _waveform;
    private Grid? _root;
    private double _duration;
    private bool _bleeped;
    private double _toneHz = AudioEffects.DefaultToneHz;
    private int _toneVolume = AudioEffects.DefaultToneVolume;
    private int _stepMs = 100;

    public AudioBleepView(FileItem item, DropTargets? targets)
    {
        _item = item;
        _targets = targets;
        _bleepLane.Changed += OnModelChanged;
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
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = BuildHeader();
        var lanes = BuildLanes();
        FillControls();
        Grid.SetRow(header, 0);
        Grid.SetRow(lanes, 1);
        Grid.SetRow(_controls, 2);
        root.Children.Add(header);
        root.Children.Add(lanes);
        root.Children.Add(_controls);

        root.Loaded += (_, _) => RetitlePrimaryButton();
        root.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Delete or Key.Back)
            {
                _bleepLane.RemoveSelected();
                e.Handled = true;
            }
        };
        root.Unloaded += (_, _) => Cancel();

        _root = root;
        return new ToolContent(root, Reset, ApplyAsync, _ready.Task, ActivateAsync);
    }

    /// <summary>Loads the waveform, sizes the lanes and reports the preview size to the adaptive sizer.</summary>
    public async Task ActivateAsync()
    {
        try
        {
            var ffmpeg = _tools.Require(ExternalTool.Ffmpeg);
            var waveform = await AudioWaveform.LoadAsync(ffmpeg, _item.Path, _life.Token);
            _waveform = waveform;
            _duration = waveform.Duration;
            _audioLane.Waveform = waveform;
            _audioLane.Stereo = false;
            _bleepLane.Duration = waveform.Duration;
            _bleepLane.ClipLabel = T("tool.audiobleep.lane.bleep");
            _placeholder.Visibility = Visibility.Collapsed;
            UpdateTimes();
            RefreshFromModel();
            _ready.TrySetResult(new MediaSize(560, 170));
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

    private FrameworkElement BuildHeader()
    {
        _count.FontSize = 13.5;
        _count.FontWeight = FontWeights.SemiBold;
        _count.Foreground = Theme.Text;
        _count.Margin = new Thickness(2, 0, 0, 6);
        _count.Text = T("tool.audiobleep.count", 0);
        return _count;
    }

    /// <summary>Bleep layer above the audio waveform, both with their own start/end time labels.</summary>
    private FrameworkElement BuildLanes()
    {
        var grid = new Grid
        {
            // A timeline always runs left to right, in Arabic too; the rest of the window still mirrors.
            FlowDirection = FlowDirection.LeftToRight,
        };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _bleepLane.MinHeight = 42;
        _audioLane.MinHeight = 60;

        var bleepTitle = LaneTitle(T("tool.audiobleep.lane.bleep"));
        var audioTitle = LaneTitle(T("tool.audiobleep.lane.audio"));
        // The lane grid stays LTR because it is a time axis; the Arabic lane captions must not.
        bleepTitle.FlowDirection = LanguageManager.FlowDirection;
        audioTitle.FlowDirection = LanguageManager.FlowDirection;
        var bleepBorder = LaneSurface(_bleepLane);
        var audioSurface = new Grid();
        audioSurface.Children.Add(_audioLane);
        audioSurface.Children.Add(new Grid { Children = { _placeholder } });
        var audioBorder = LaneSurface(audioSurface);
        var bleepTimes = TimeRow(_bleepStart, _bleepTotal);
        var audioTimes = TimeRow(_audioStart, _audioTotal);

        Place(bleepTitle, 0, 0);
        Place(bleepBorder, 0, 1);
        Place(bleepTimes, 1, 1);
        Place(audioTitle, 2, 0);
        Place(audioBorder, 2, 1);
        Place(audioTimes, 3, 1);

        grid.Children.Add(bleepTitle);
        grid.Children.Add(bleepBorder);
        grid.Children.Add(bleepTimes);
        grid.Children.Add(audioTitle);
        grid.Children.Add(audioBorder);
        grid.Children.Add(audioTimes);
        return grid;

        static void Place(FrameworkElement element, int row, int column)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
        }
    }

    private static TextBlock LaneTitle(string text) => new()
    {
        Text = text,
        Width = 62,
        FontSize = 11,
        Foreground = Theme.Muted,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.NoWrap,
    };

    private static Border LaneSurface(FrameworkElement content) => new()
    {
        Background = Theme.Card,
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(4),
        Margin = new Thickness(0, 0, 0, 2),
        ClipToBounds = true,
        Child = content,
    };

    private static FrameworkElement TimeRow(TextBlock start, TextBlock total)
    {
        var row = new DockPanel { LastChildFill = false, Margin = new Thickness(4, 0, 4, 2) };
        DockPanel.SetDock(start, Dock.Left);
        DockPanel.SetDock(total, Dock.Right);
        row.Children.Add(start);
        row.Children.Add(total);
        start.Text = AudioEffects.FormatTimecode(0);
        total.Text = AudioEffects.FormatTimecode(0);
        return row;
    }

    private static TextBlock TimeLabel() => new()
    {
        FontSize = 10.5,
        Foreground = Theme.Muted,
    };

    private void UpdateTimes()
    {
        _bleepStart.Text = Bidi.Value(AudioEffects.FormatTimecode(0));
        _bleepTotal.Text = Bidi.Value(AudioEffects.FormatTimecode(_duration));
        _audioStart.Text = Bidi.Value(AudioEffects.FormatTimecode(0));
        _audioTotal.Text = Bidi.Value(AudioEffects.FormatTimecode(_duration));
    }

    private void FillControls()
    {
        _controls.Children.Clear();
        _selectionOnly.Clear();

        _controls.Children.Add(new TextBlock
        {
            Text = T("tool.audiobleep.help"),
            FontSize = 12,
            Foreground = Theme.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 2, 2, 10),
        });

        BuildPreviewRow();
        _controls.Children.Add(_previewRow);
        _controls.Children.Add(new TextBlock
        {
            Text = T("tool.audiobleep.preview.note"),
            FontSize = 11,
            Foreground = Theme.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 0, 2, 8),
        });
        _controls.Children.Add(BuildToolbar());

        _controls.Children.Add(SliderRow(
            T("tool.audiobleep.tone"),
            (int)Math.Round(_toneHz),
            100,
            5000,
            value =>
            {
                _toneHz = value;
                RefreshFromModel();
            }));

        _controls.Children.Add(SliderRow(
            T("tool.audiobleep.volume"),
            _toneVolume,
            0,
            100,
            value =>
            {
                _toneVolume = value;
                RefreshFromModel();
            }));

        _outputName.FontSize = 11.5;
        _outputName.Foreground = Theme.Muted;
        _outputName.TextTrimming = TextTrimming.CharacterEllipsis;
        _outputName.Margin = new Thickness(2, 8, 0, 0);
        _controls.Children.Add(_outputName);
        _controls.Children.Add(_status);

        RefreshFromModel();
    }

    /// <summary>
    /// Two view toggles: the untouched waveform, or the waveform with the sections drawn as the tone that
    /// will replace them. This window has no audio playback; the note below says so and points at Save.
    /// </summary>
    private void BuildPreviewRow()
    {
        _previewRow.Children.Clear();

        _originalPreview = new PreviewButton(T("tool.audiobleep.preview.original"), () => SetPreview(false));
        _bleepedPreview = new PreviewButton(T("tool.audiobleep.preview.bleeped"), () => SetPreview(true));
        _originalPreview.SetSelected(!_bleeped);
        _bleepedPreview.SetSelected(_bleeped);

        _previewRow.Children.Add(_originalPreview.Root);
        _previewRow.Children.Add(_bleepedPreview.Root);
    }

    private FrameworkElement BuildToolbar()
    {
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };

        var step = Controls.Segmented(
        [
            ("10 ms", 10),
            ("100 ms", 100),
        ], _stepMs, value => _stepMs = value);
        step.Margin = new Thickness(0, 0, 8, 6);
        step.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(step);
        _selectionOnly.Add(step);

        bar.Children.Add(Nudge(T("tool.audiobleep.start") + " \u2212", start: true, direction: -1));
        bar.Children.Add(Nudge(T("tool.audiobleep.start") + " +", start: true, direction: 1));
        bar.Children.Add(Nudge(T("tool.audiobleep.end") + " \u2212", start: false, direction: -1));
        bar.Children.Add(Nudge(T("tool.audiobleep.end") + " +", start: false, direction: 1));

        var remove = Controls.Button(T("tool.audiobleep.remove"), () => _bleepLane.RemoveSelected());
        remove.Margin = new Thickness(0, 0, 8, 6);
        bar.Children.Add(remove);
        _selectionOnly.Add(remove);
        return bar;
    }

    private FrameworkElement Nudge(string text, bool start, int direction)
    {
        var button = Controls.Button(text, () => NudgeSelected(start, direction));
        button.Margin = new Thickness(0, 0, 6, 6);
        _selectionOnly.Add(button);
        return button;
    }

    private static FrameworkElement SliderRow(string caption, int value, int min, int max, Action<int> changed)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var label = AudioToolChrome.Caption(caption);
        label.Width = 104;
        row.Children.Add(label);
        row.Children.Add(Controls.Slider(min, max, value, changed, () => { }));
        return row;
    }

    private void SetPreview(bool bleeped)
    {
        _bleeped = bleeped;
        _originalPreview?.SetSelected(!bleeped);
        _bleepedPreview?.SetSelected(bleeped);
        RefreshFromModel();
    }

    private void NudgeSelected(bool start, int direction)
    {
        var index = _bleepLane.Selected;
        if (index < 0 || index >= _bleepLane.Ranges.Count)
            return;

        var delta = direction * _stepMs / 1000.0;
        var range = _bleepLane.Ranges[index];
        _bleepLane.UpdateSelected(start
            ? AudioEffects.NudgeStart(range, delta, _duration)
            : AudioEffects.NudgeEnd(range, delta, _duration));
    }

    /// <summary>Keeps the counter, the audio lane, the output name and the selection-only controls in step.</summary>
    private void RefreshFromModel()
    {
        _count.Text = T("tool.audiobleep.count", _bleepLane.Ranges.Count);
        _audioLane.SetRanges(_bleepLane.Ranges);
        _audioLane.ShowBleep(_bleeped, Math.Clamp(_toneVolume, 0, 100) / 100.0);

        var format = FfmpegBackend.SafeContainer(_item.Format);
        _outputName.Text = Bidi.Value($"{_item.BaseName} {T("tool.audiobleep.suffix")}.{format}");

        var hasSelection = _bleepLane.Selected >= 0;
        foreach (var element in _selectionOnly)
            element.Opacity = hasSelection ? 1 : 0.45;
    }

    private void OnModelChanged() => RefreshFromModel();

    private void Reset()
    {
        _bleeped = false;
        _toneHz = AudioEffects.DefaultToneHz;
        _toneVolume = AudioEffects.DefaultToneVolume;
        _stepMs = 100;
        _status.Text = string.Empty;
        _bleepLane.SetModel([], -1);
        FillControls();
        UpdateTimes();
    }

    private void ShowProblem(string message)
    {
        _placeholder.Child = new TextBlock
        {
            Text = message,
            Foreground = Theme.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(18),
            // The lane grid is LTR for its time axis; this message is Arabic prose.
            FlowDirection = LanguageManager.FlowDirection,
        };
        _placeholder.Visibility = Visibility.Visible;
        _status.Text = message;
    }

    private void Report(double value) =>
        _status.Text = ((int)Math.Round(Math.Clamp(value, 0, 1) * 100)).ToString(System.Globalization.CultureInfo.InvariantCulture) + " %";

    /// <summary>
    /// The Wave A chrome labels its primary button "Apply"; this tool's primary action is "Save Bleeped
    /// Copy". The framework has no per-tool label hook yet, so the button is found once in the loaded
    /// window and retitled. If the chrome changes shape it simply keeps its default label.
    /// </summary>
    private void RetitlePrimaryButton()
    {
        if (_root is null)
            return;
        var window = Window.GetWindow(_root);
        if (window is null)
            return;

        var apply = T("tool.window.apply");
        foreach (var border in Descendants(window).OfType<Border>())
        {
            if (border.Background is not LinearGradientBrush)
                continue;
            if (border.Child is not StackPanel panel || panel.Children.Count == 0)
                continue;
            if (panel.Children[^1] is TextBlock label && label.Text == apply)
                Controls.SetText(border, T("tool.audiobleep.save"));
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child))
                yield return nested;
        }
    }

    private async Task<string?> ApplyAsync()
    {
        if (_waveform is null)
            throw new ConversionException(T("tool.audiobleep.error.decode"));

        var ranges = AudioEffects.NormalizeRanges(_bleepLane.Ranges, _duration);
        if (ranges.Count == 0)
            throw new ConversionException(T("tool.audiobleep.error.noRanges"));

        var directory = AudioToolOutput.DirectoryFor(_item, _targets);
        var format = FfmpegBackend.SafeContainer(_item.Format);
        var suffix = T("tool.audiobleep.suffix");
        var args = AudioEffects.BleepArguments(ranges, _toneHz, _toneVolume);
        var codec = FfmpegBackend.CodecArgs(format, new Settings());
        var tools = _tools;
        var duration = _duration;

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

    /// <summary>A pill toggle that shows which waveform view is currently on screen.</summary>
    private sealed class PreviewButton
    {
        private readonly TextBlock _label;

        public PreviewButton(string text, Action click)
        {
            _label = new TextBlock
            {
                Text = text,
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(_label);

            Root = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 5, 12, 6),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand,
                Child = content,
            };
            Root.MouseLeftButtonUp += (_, _) => click();
        }

        public Border Root { get; }

        public void SetSelected(bool selected)
        {
            Root.Background = selected ? Theme.AccentGradient : Theme.Card;
            Root.BorderBrush = selected ? Brushes.Transparent : Theme.PanelBorder;
            _label.Foreground = selected ? Theme.TextOnAccent : Theme.Text;
            _label.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }
}
