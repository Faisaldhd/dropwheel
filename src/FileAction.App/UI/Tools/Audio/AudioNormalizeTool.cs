using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Audio;

/// <summary>
/// Normalize one audio file to a loudness target (EBU R128): an analysis pass measures the source and
/// predicts the result, Apply runs the second pass with those measurements. The original file is only read.
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

/// <summary>The normalize window: targets, measured-vs-predicted table, status line.</summary>
internal sealed class AudioNormalizeView
{
    private readonly FileItem _item;
    private readonly DropTargets? _targets;
    private readonly ToolLocator _tools = new();
    private readonly TaskCompletionSource<MediaSize?> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TextBox _lufsBox = Entry();
    private readonly TextBox _peakBox = Entry();
    private readonly TextBox _rangeBox = Entry();
    private readonly TextBlock _status = new()
    {
        Foreground = Theme.Muted,
        FontSize = 12.5,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(2, 4, 2, 0),
    };

    private readonly TextBlock _beforeLoudness = ValueCell();
    private readonly TextBlock _beforePeak = ValueCell();
    private readonly TextBlock _beforeRange = ValueCell();
    private readonly TextBlock _afterLoudness = ValueCell();
    private readonly TextBlock _afterPeak = ValueCell();
    private readonly TextBlock _afterRange = ValueCell();

    private LoudnessTarget _measuredTarget;
    private LoudnessMeasurement? _measurement;
    private CancellationTokenSource? _analysis;
    private int _analysisId;
    private bool _applying;

    public AudioNormalizeView(FileItem item, DropTargets? targets)
    {
        _item = item;
        _targets = targets;
        _lufsBox.Text = Number(LoudnessTarget.Default.IntegratedLufs);
        _peakBox.Text = Number(LoudnessTarget.Default.TruePeakDb);
        _rangeBox.Text = Number(LoudnessTarget.Default.LoudnessRangeLu);

        foreach (var box in new[] { _lufsBox, _peakBox, _rangeBox })
        {
            box.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter)
                    return;
                e.Handled = true;
                StartAnalysis();
            };
            box.LostFocus += (_, _) => StartAnalysis();
        }
    }

    public ToolContent Build()
    {
        var root = new Grid { Margin = new Thickness(18, 2, 18, 0) };
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

        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        body.Children.Add(Card(BuildTargets()));
        body.Children.Add(Card(BuildTable()));
        var scroll = new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        Grid.SetRow(header, 0);
        Grid.SetRow(scroll, 1);
        Grid.SetRow(_status, 2);
        root.Children.Add(header);
        root.Children.Add(scroll);
        root.Children.Add(_status);

        root.Unloaded += (_, _) => Cancel();
        _ready.TrySetResult(null);
        StartAnalysis();
        return new ToolContent(root, Reset, ApplyAsync, _ready.Task, () => Task.CompletedTask);
    }

    private FrameworkElement BuildTargets()
    {
        var panel = new StackPanel();
        var caption = new TextBlock
        {
            Text = T("tool.audionormalize.target"),
            FontSize = 12,
            Foreground = Theme.Muted,
            Margin = new Thickness(0, 0, 0, 8),
        };
        panel.Children.Add(caption);

        var row = new Grid();
        for (var i = 0; i < 3; i++)
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        AddTarget(row, 0, "tool.audionormalize.loudness", _lufsBox);
        AddTarget(row, 1, "tool.audionormalize.truepeak", _peakBox);
        AddTarget(row, 2, "tool.audionormalize.range", _rangeBox);
        panel.Children.Add(row);
        return panel;
    }

    private static void AddTarget(Grid row, int column, string labelKey, TextBox box)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = T(labelKey),
            FontSize = 11.5,
            Foreground = Theme.Muted,
            Margin = new Thickness(2, 0, 0, 4),
            TextWrapping = TextWrapping.NoWrap,
        });
        box.HorizontalAlignment = HorizontalAlignment.Left;
        stack.Children.Add(box);
        Grid.SetColumn(stack, column);
        row.Children.Add(stack);
    }

    private FrameworkElement BuildTable()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
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
            FontSize = 13,
            Foreground = Theme.Text,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
        });
        AddCell(grid, row, 1, before);
        AddCell(grid, row, 2, after);
    }

    private static void AddCell(Grid grid, int row, int column, TextBlock text)
    {
        text.Margin = new Thickness(0, 5, 0, 5);
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
        FontSize = 14,
        Foreground = Theme.Text,
    };

    private static Border Card(UIElement content) => new()
    {
        Background = Theme.Card,
        BorderBrush = Theme.PanelBorder,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(16, 12, 16, 12),
        Margin = new Thickness(0, 0, 0, 10),
        Child = content,
    };

    private static TextBox Entry() => new()
    {
        Width = 92,
        Padding = new Thickness(8, 5, 8, 6),
        Background = Theme.Page,
        Foreground = Theme.Text,
        BorderBrush = Theme.PanelBorder,
        BorderThickness = new Thickness(1),
        FontSize = 13,
        TextAlignment = TextAlignment.Center,
        FlowDirection = FlowDirection.LeftToRight,
    };

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
            // Closing the window or typing a new target cancels the pass.
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
        if (!TryRead(_lufsBox, out var lufs) || !TryRead(_peakBox, out var peak) || !TryRead(_rangeBox, out var range)
            || !LoudnessTarget.TryCreate(lufs, peak, range, out var target))
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

    private static bool TryRead(TextBox box, out double value) =>
        double.TryParse(
            box.Text.Trim().Replace(',', '.'),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value)
        && double.IsFinite(value);

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

    private void Reset()
    {
        _lufsBox.Text = Number(LoudnessTarget.Default.IntegratedLufs);
        _peakBox.Text = Number(LoudnessTarget.Default.TruePeakDb);
        _rangeBox.Text = Number(LoudnessTarget.Default.LoudnessRangeLu);
        _measurement = null;
        _measuredTarget = default;
        UpdateTable();
        StartAnalysis();
    }

    private async Task<string?> ApplyAsync()
    {
        _applying = true;
        try
        {
            var target = RequireTarget();
            var ffmpeg = AudioRunner.RequireFfmpeg(_tools);
            _status.Text = T("tool.audionormalize.applying");

            // A click on Apply can leave a target box first, which starts an analysis; that one is stale now.
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
        _lifetime.Cancel();
        _analysis?.Cancel();
    }
}
