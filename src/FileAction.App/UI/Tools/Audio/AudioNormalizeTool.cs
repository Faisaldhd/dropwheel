using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileAction.App.Shell;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Audio;

/// <summary>
/// Normalize one audio file to a loudness target (EBU R128): a waveform preview, three sliders, an analysis
/// pass that measures the source and predicts the result, and a two-pass apply. The original is only read.
/// </summary>
internal sealed class AudioNormalizeTool : IToolWindowSpec
{
    public static readonly AudioNormalizeTool Instance = new();

    public string Id => "tools.audionormalize";

    public string TitleKey => "tool.audionormalize.title";

    public ToolSpecLayout Layout => AudioToolLayout.Window;

    public bool Applies(IReadOnlyList<FileItem> items) =>
        items.Count == 1 && !items[0].IsDirectory && Formats.Audio.Contains(items[0].Format);

    public ToolContent Create(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new AudioNormalizeView(items[0], targets).Build();
}

/// <summary>Registers the tool from its own file; no shared list is edited.</summary>
internal static class AudioNormalizeToolRegistration
{
    [ModuleInitializer]
    internal static void Register() => ToolRegistry.Register(AudioNormalizeTool.Instance);
}

/// <summary>The normalize window: waveform transport, target sliders, input-vs-output table and primary action.</summary>
internal sealed class AudioNormalizeView
{
    private const double SliderWidth = 132;

    private readonly FileItem _item;
    private readonly DropTargets? _targets;
    private readonly ToolLocator _tools = new();
    private readonly TaskCompletionSource<MediaSize?> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly AudioPreview _preview;
    private readonly WaveformView _waveform = new() { Height = 104 };
    private readonly AudioSlider _loudness = new(-30, -5, LoudnessTarget.Default.IntegratedLufs, 0.5, "0.#") { Width = SliderWidth };
    private readonly AudioSlider _range = new(1, 50, LoudnessTarget.Default.LoudnessRangeLu, 1, "0") { Width = SliderWidth };
    private readonly AudioSlider _peak = new(-9, 0, LoudnessTarget.Default.TruePeakDb, 0.5, "0.#") { Width = SliderWidth };
    private readonly TextBlock _timeText = new()
    {
        FontSize = 12,
        Foreground = Theme.Muted,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Right,
        TextWrapping = TextWrapping.NoWrap,
        FlowDirection = FlowDirection.LeftToRight,
    };
    private readonly TextBlock _status = new()
    {
        Foreground = Theme.Muted,
        FontSize = 12.5,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(2, 6, 12, 0),
    };

    private readonly TextBlock _beforeLoudness = ValueCell();
    private readonly TextBlock _beforePeak = ValueCell();
    private readonly TextBlock _beforeRange = ValueCell();
    private readonly TextBlock _afterLoudness = ValueCell();
    private readonly TextBlock _afterPeak = ValueCell();
    private readonly TextBlock _afterRange = ValueCell();

    private FrameworkElement? _root;
    private Border? _playButton;
    private Border? _primary;
    private LoudnessTarget _measuredTarget;
    private LoudnessMeasurement? _measurement;
    private CancellationTokenSource? _analysis;
    private int _analysisId;
    private double _duration;
    private bool _applying;

    public AudioNormalizeView(FileItem item, DropTargets? targets)
    {
        _item = item;
        _targets = targets;
        _preview = new AudioPreview(item.Path);
        _preview.PositionChanged += OnPreviewPosition;
        _preview.Stopped += OnPreviewStopped;
        _preview.StateChanged += UpdatePlayButton;
        _preview.Failed += OnPreviewFailed;
        foreach (var slider in new[] { _loudness, _range, _peak })
            slider.Committed += _ => StartAnalysis();

        UpdateTime(0);
    }

    public ToolContent Build()
    {
        var root = new Grid { Margin = new Thickness(18, 2, 18, 6) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new TextBlock
        {
            Text = Bidi.Value(_item.Name),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            Margin = new Thickness(2, 0, 2, 8),
            ToolTip = _item.Path,
        };

        var body = BuildBody();
        var footer = BuildFooter();
        Grid.SetRow(header, 0);
        Grid.SetRow(body, 1);
        Grid.SetRow(footer, 2);
        root.Children.Add(header);
        root.Children.Add(body);
        root.Children.Add(footer);
        _root = root;

        _status.Text = T("tool.audionormalize.analyzing");
        root.Loaded += (_, _) => AudioTheme.Watch(root);
        root.Unloaded += (_, _) =>
        {
            AudioTheme.Unwatch(root);
            Cancel();
        };
        _ready.TrySetResult(null);
        _ = LoadWaveformAsync();
        StartAnalysis();
        return new ToolContent(root, Reset, ApplyAsync, _ready.Task, () => Task.CompletedTask);
    }

    private FrameworkElement BuildBody()
    {
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });

        var left = new StackPanel();
        left.Children.Add(WaveformCard());

        var right = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        right.Children.Add(Card(BuildSliders()));
        right.Children.Add(Card(BuildTable()));

        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        body.Children.Add(left);
        body.Children.Add(right);
        return body;
    }

    private FrameworkElement WaveformCard()
    {
        _playButton = Controls.Button(T("tool.audionormalize.play"), TogglePreview, glyph: Glyphs.Play);
        _playButton.VerticalAlignment = VerticalAlignment.Center;
        var transport = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(_timeText, Dock.Right);
        transport.Children.Add(_timeText);
        transport.Children.Add(_playButton);

        var content = new StackPanel();
        content.Children.Add(_waveform);
        content.Children.Add(transport);
        return Card(content);
    }

    private FrameworkElement BuildSliders()
    {
        var panel = new StackPanel();
        panel.Children.Add(SliderRow("tool.audionormalize.slider.loudness", _loudness));
        panel.Children.Add(SliderRow("tool.audionormalize.slider.range", _range));
        panel.Children.Add(SliderRow("tool.audionormalize.slider.peak", _peak));
        return panel;
    }

    private static FrameworkElement SliderRow(string labelKey, AudioSlider slider)
    {
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4), LastChildFill = true };
        DockPanel.SetDock(slider, Dock.Right);
        slider.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(slider);
        row.Children.Add(new TextBlock
        {
            Text = T(labelKey),
            FontSize = 12,
            Foreground = Theme.Text,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 6, 0),
        });
        return row;
    }

    private FrameworkElement BuildTable()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        for (var i = 0; i < 4; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddCell(grid, 0, 1, Head("tool.audionormalize.before"));
        AddCell(grid, 0, 2, Head("tool.audionormalize.after"));
        AddRow(grid, 1, "tool.audionormalize.loudness", _beforeLoudness, _afterLoudness);
        AddRow(grid, 2, "tool.audionormalize.truepeak", _beforePeak, _afterPeak);
        AddRow(grid, 3, "tool.audionormalize.range", _beforeRange, _afterRange);
        return grid;
    }

    private static void AddRow(Grid grid, int row, string labelKey, TextBlock before, TextBlock after)
    {
        AddCell(grid, row, 0, new TextBlock
        {
            Text = T(labelKey),
            FontSize = 11.5,
            Foreground = Theme.Text,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        AddCell(grid, row, 1, before);
        AddCell(grid, row, 2, after);
    }

    private static void AddCell(Grid grid, int row, int column, TextBlock text)
    {
        text.Margin = new Thickness(0, 3, 0, 3);
        text.VerticalAlignment = VerticalAlignment.Center;
        text.TextAlignment = TextAlignment.Right;
        text.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetRow(text, row);
        Grid.SetColumn(text, column);
        grid.Children.Add(text);
    }

    private static TextBlock Head(string key) => new()
    {
        Text = T(key),
        FontSize = 11.5,
        Foreground = Theme.Muted,
        TextAlignment = TextAlignment.Right,
        Margin = new Thickness(0, 0, 0, 2),
    };

    private static TextBlock ValueCell() => new()
    {
        Text = "—",
        FontSize = 13.5,
        Foreground = Theme.Text,
    };

    private static Border Card(UIElement content) => new()
    {
        Background = Theme.Card,
        BorderBrush = Theme.PanelBorder,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(14, 10, 14, 10),
        Margin = new Thickness(0, 0, 0, 10),
        Child = content,
    };

    private FrameworkElement BuildFooter()
    {
        var footer = new StackPanel { Margin = new Thickness(2, 6, 2, 0) };
        footer.Children.Add(new TextBlock
        {
            Text = T("tool.audionormalize.hint"),
            FontSize = 11.5,
            Foreground = Theme.Muted,
            TextWrapping = TextWrapping.Wrap,
        });

        _primary = Controls.Button(T("tool.audionormalize.primary"), () => _ = RunPrimaryAsync(), primary: true);
        _primary.VerticalAlignment = VerticalAlignment.Center;
        var actions = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(_primary, Dock.Right);
        actions.Children.Add(_primary);
        actions.Children.Add(_status);
        footer.Children.Add(actions);
        return footer;
    }

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private void StartAnalysis()
    {
        if (_applying)
            return;

        _analysis?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _analysis = cts;
        var id = ++_analysisId;
        _ = AnalyzeAsync(id, cts.Token);
    }

    private async Task AnalyzeAsync(int id, CancellationToken ct)
    {
        try
        {
            LoudnessTarget target;
            try
            {
                target = RequireTarget();
            }
            catch (ConversionException ex)
            {
                if (id == _analysisId)
                {
                    _measurement = null;
                    _measuredTarget = default;
                    UpdateTable();
                    _status.Text = ex.Message;
                }
                return;
            }

            if (id == _analysisId)
                _status.Text = T("tool.audionormalize.analyzing");

            var ffmpeg = AudioRunner.RequireFfmpeg(_tools);
            var measurement = await AudioRunner.MeasureAsync(ffmpeg, _item.Path, target, ct).ConfigureAwait(true);
            if (id != _analysisId)
                return;

            _measurement = measurement;
            _measuredTarget = target;
            UpdateTable();
            _status.Text = measurement is null
                ? T("tool.audionormalize.error.measure")
                : measurement.IsSilent
                    ? T("tool.audionormalize.silent")
                    : T("tool.audionormalize.ready");
        }
        catch (OperationCanceledException)
        {
            // Closing the window or moving a slider cancels the pass.
        }
        catch (Exception ex)
        {
            if (id != _analysisId)
                return;
            _measurement = null;
            _measuredTarget = default;
            UpdateTable();
            _status.Text = AudioRunner.Message(ex, "tool.audionormalize.error.stream");
        }
    }

    private LoudnessTarget RequireTarget()
    {
        if (!LoudnessTarget.TryCreate(_loudness.Value, _peak.Value, _range.Value, out var target))
        {
            throw new ConversionException(T(
                "tool.audionormalize.error.target",
                Bidi.Value(Number(LoudnessTarget.MinIntegratedLufs)),
                Bidi.Value(Number(LoudnessTarget.MaxIntegratedLufs)),
                Bidi.Value(Number(LoudnessTarget.MinTruePeakDb)),
                Bidi.Value(Number(LoudnessTarget.MaxTruePeakDb)),
                Bidi.Value(Number(LoudnessTarget.MinLoudnessRangeLu)),
                Bidi.Value(Number(LoudnessTarget.MaxLoudnessRangeLu))));
        }
        return target;
    }

    private void UpdateTable()
    {
        _beforeLoudness.Text = Format(_measurement?.Integrated, 1);
        _beforePeak.Text = Format(_measurement?.TruePeak, 1);
        _beforeRange.Text = Format(_measurement?.LoudnessRange, 1);
        _afterLoudness.Text = Format(_measurement?.PredictedIntegrated, 1);
        _afterPeak.Text = Format(_measurement?.PredictedTruePeak, 1);
        _afterRange.Text = Format(_measurement?.PredictedRange, 1);
    }

    /// <summary>-inf (silence) and unknown values show an em dash instead of a meaningless number.</summary>
    private static string Format(double? value, int decimals) =>
        value is { } number && double.IsFinite(number)
            ? Bidi.Value(number.ToString($"0.{new string('0', decimals)}", CultureInfo.InvariantCulture))
            : "—";

    private async Task LoadWaveformAsync()
    {
        try
        {
            var ffmpeg = AudioRunner.RequireFfmpeg(_tools);
            var waveform = await AudioRunner.WaveformAsync(ffmpeg, _item.Path, _lifetime.Token).ConfigureAwait(true);
            _duration = waveform.Duration;
            _waveform.SetWaveform(waveform.Peaks, _duration);
            UpdateTime(0);
        }
        catch (OperationCanceledException)
        {
            // Closing the window cancels the decode.
        }
        catch (Exception ex)
        {
            _status.Text = AudioRunner.Message(ex, "tool.audionormalize.error.stream");
        }
    }

    private void TogglePreview()
    {
        if (_duration <= 0)
            return;

        _preview.Play(0, _duration);
    }

    private void UpdatePlayButton()
    {
        if (_playButton is not null)
            Controls.SetText(_playButton, T(_preview.IsPlaying ? "tool.audionormalize.pause" : "tool.audionormalize.play"));
    }

    private void OnPreviewPosition(double seconds)
    {
        _waveform.Playhead = seconds;
        UpdateTime(seconds);
    }

    private void OnPreviewStopped()
    {
        _waveform.Playhead = null;
        UpdateTime(0);
    }

    private void OnPreviewFailed()
    {
        if (_playButton is not null)
        {
            _playButton.IsEnabled = false;
            _playButton.Opacity = 0.5;
        }
        _status.Text = T("tool.audionormalize.preview.unavailable");
    }

    private void UpdateTime(double position) =>
        _timeText.Text = T(
            "tool.audionormalize.time",
            Bidi.Value(AudioTrim.FormatTime(position)),
            Bidi.Value(AudioTrim.FormatTime(_duration)));

    private void Reset()
    {
        _loudness.SetValue(LoudnessTarget.Default.IntegratedLufs);
        _range.SetValue(LoudnessTarget.Default.LoudnessRangeLu);
        _peak.SetValue(LoudnessTarget.Default.TruePeakDb);
        _measurement = null;
        _measuredTarget = default;
        UpdateTable();
        _preview.Stop();
        StartAnalysis();
    }

    private async Task RunPrimaryAsync()
    {
        if (_applying || _primary is null)
            return;
        _primary.IsEnabled = false;
        try
        {
            var created = await ApplyAsync().ConfigureAwait(true);
            if (string.IsNullOrEmpty(created))
                return;
            Notifications.Show(T("tool.window.done.title"), T("tool.window.done", Bidi.Value(Path.GetFileName(created))));
            if (_root is not null)
                Window.GetWindow(_root)?.Close();
        }
        catch (OperationCanceledException)
        {
            // The window is closing.
        }
        catch (Exception ex)
        {
            Notifications.Show(T("app.error.title"), ex.Message, error: true);
        }
        finally
        {
            _primary.IsEnabled = true;
        }
    }

    private async Task<string?> ApplyAsync()
    {
        if (_applying)
            return null;
        _applying = true;
        try
        {
            var target = RequireTarget();
            var ffmpeg = AudioRunner.RequireFfmpeg(_tools);
            _status.Text = T("tool.audionormalize.applying");

            // A click on Apply can leave a slider first, which starts an analysis; that one is stale now.
            _analysis?.Cancel();
            _analysisId++;

            // A measurement made for another target would carry the wrong offset, so re-measure.
            var measurement = _measurement;
            if (measurement is null || _measuredTarget != target)
                measurement = await AudioRunner.MeasureAsync(ffmpeg, _item.Path, target, _lifetime.Token).ConfigureAwait(true);
            if (measurement is null)
                throw new ConversionException(T("tool.audionormalize.error.measure"));
            if (measurement.IsSilent)
                throw new ConversionException(T("tool.audionormalize.silent"));

            _preview.Stop();
            _preview.Close();
            return await AudioRunner.NormalizeAsync(
                ffmpeg,
                _item,
                target,
                measurement,
                AudioRunner.OutputDirectory(_item, _targets),
                _lifetime.Token).ConfigureAwait(true);
        }
        finally
        {
            _applying = false;
        }
    }

    private void Cancel()
    {
        _analysis?.Cancel();
        _preview.Close();
        _lifetime.Cancel();
    }
}
