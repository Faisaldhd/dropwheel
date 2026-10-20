using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FileAction.Core;
using ImageMagick;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Compress;

/// <summary>
/// The compress window content: the Balanced / Strong preset, an optional image-size dropdown, the
/// "Compress to target file size" and "Reduce by percentage" options (each one reveals its field), and the
/// estimate of the result. The estimate and hint follow the fields as the user types, and the work runs
/// asynchronously so it can be cancelled from the same window.
/// </summary>
internal sealed class CompressView
{
    private readonly FileItem _item;
    private readonly DropTargets? _targets;
    private readonly ToolLocator _tools = new();
    private readonly MediaKind _kind;
    private readonly string _format;
    private readonly Task<SourceInfo> _probe;
    private readonly TaskCompletionSource<MediaSize?> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TextBox _targetBox = ToolControls.Box();
    private readonly TextBox _reductionBox = ToolControls.Box(CompressionDefaults.PercentageReduction.ToString(CultureInfo.InvariantCulture), 66);
    private readonly StackPanel _targetPanel = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly StackPanel _reductionPanel = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock _targetHint = ToolControls.Label(string.Empty);
    private readonly TextBlock _percentageLabel = ToolControls.Label(string.Empty);
    private readonly TextBlock _estimateValue = new();
    private readonly TextBlock _estimateDetail = ToolControls.Label(string.Empty);
    private readonly TextBlock _hint = ToolControls.Label(string.Empty);
    private readonly TextBlock _status = ToolControls.Label(string.Empty, 11.5);
    private readonly ProgressBar _progress = new() { Height = 4, Minimum = 0, Maximum = 1, Visibility = Visibility.Collapsed };
    private readonly Border _cancel;
    private readonly TextBlock _title = new();
    private readonly TextBlock _meta = ToolControls.Label(string.Empty, 11.5);
    private readonly ComboBox _sizeBox = new();
    private TextBlock? _sizeHint;

    private Action<CompressionPreset>? _selectPreset;
    private Action<bool>? _selectTarget;
    private Action<bool>? _selectPercentage;
    private CompressionPreset _preset = CompressionDefaults.Preset;
    private bool _targetEnabled = CompressionDefaults.TargetSizeEnabled;
    private bool _percentageEnabled = CompressionDefaults.PercentageEnabled;
    private CancellationTokenSource? _cts;
    private SourceInfo? _source;
    private bool _running;

    public CompressView(FileItem item, DropTargets? targets)
    {
        _item = item;
        _targets = targets;
        _format = Formats.Normalize(item.Format);
        _kind = Compression.KindOf(_format);
        _cancel = Controls.Button(T("tool.compress.cancel"), () => _cts?.Cancel());
        // Probe off the UI thread: the window opens immediately and shows the estimate when it lands.
        _probe = Task.Run(Probe);
    }

    public ToolContent Build()
    {
        var root = new Grid { Margin = new Thickness(18, 6, 18, 6) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = BuildHeader();
        var body = BuildBody();
        var status = BuildStatus();
        Grid.SetRow(header, 0);
        Grid.SetRow(body, 1);
        Grid.SetRow(status, 2);
        root.Children.Add(header);
        root.Children.Add(body);
        root.Children.Add(status);

        ShowOptions();
        UpdateEstimate();
        return new ToolContent(root, Reset, ApplyAsync, _ready.Task, ActivateAsync);
    }

    /// <summary>Shows the original size and fills the target box with half of it.</summary>
    public async Task ActivateAsync()
    {
        try
        {
            var source = await _probe;
            _source = source;
            if (source.Bytes > 0)
            {
                _targetBox.Text = Compression.FormatMegabytes(Compression.MegabytesFromBytes(source.Bytes) / 2);
                _meta.Text = T("tool.compress.originalSize", Bidi.Value(Compression.FormatSize(source.Bytes)));
            }
            else
            {
                _meta.Text = T("tool.compress.error.unsupported");
            }

            UpdateEstimate();
        }
        catch (Exception ex)
        {
            _meta.Text = ex.Message;
        }
        finally
        {
            // The window keeps its declared panel size: this tool has no media aspect to adapt to.
            _ready.TrySetResult(null);
        }
    }

    // ------------------------------------------------------------------ layout

    private FrameworkElement BuildHeader()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 6) };
        _title.Text = Bidi.Value(_item.Name);
        _title.FontSize = 15;
        _title.FontWeight = FontWeights.SemiBold;
        _title.Foreground = Theme.Text;
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        _title.TextWrapping = TextWrapping.NoWrap;
        _meta.Text = T("tool.compress.probe");
        _meta.Margin = new Thickness(0, 2, 0, 0);
        stack.Children.Add(_title);
        stack.Children.Add(_meta);
        return stack;
    }

    private FrameworkElement BuildBody()
    {
        var panel = new StackPanel();

        // Preset: Balanced / Strong with the fixed helper line.
        var (presetChips, selectPreset) = ToolControls.Chips<CompressionPreset>(
        [
            (T("tool.compress.preset.balanced"), CompressionPreset.Medium),
            (T("tool.compress.preset.strong"), CompressionPreset.Low),
        ], _preset, preset =>
        {
            _preset = preset;
            UpdateEstimate();
        });
        _selectPreset = selectPreset;

        var presetContent = new StackPanel();
        presetContent.Children.Add(presetChips);
        var helper = ToolControls.Label(T("tool.compress.preset.helper"), 11.5);
        helper.Margin = new Thickness(2, 8, 2, 0);
        helper.Opacity = 0.85;
        presetContent.Children.Add(helper);
        panel.Children.Add(ToolControls.Section(T("tool.compress.mode.preset"), presetContent));

        // Image size: keep the original dimensions or cap the long side.
        if (_kind == MediaKind.Image)
        {
            var sizeContent = new StackPanel();
            var sizeRow = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            BuildSizeBox();
            sizeRow.Children.Add(_sizeBox);
            var sourceSize = ToolControls.Label(string.Empty, 11.5);
            sourceSize.VerticalAlignment = VerticalAlignment.Center;
            sourceSize.Margin = new Thickness(10, 0, 0, 0);
            sizeRow.Children.Add(sourceSize);
            sizeContent.Children.Add(sizeRow);
            panel.Children.Add(ToolControls.Section(T("tool.compress.imageSize"), sizeContent));
            _sizeHint = sourceSize;
        }

        // Options: target size and percentage reduction each reveal their field.
        var options = new StackPanel();
        var (targetRow, selectTarget) = ToolControls.CheckBox(T("tool.compress.target.toggle"), _targetEnabled, OnTargetToggled);
        _selectTarget = selectTarget;
        BuildTargetPanel();
        options.Children.Add(targetRow);
        options.Children.Add(_targetPanel);

        var (percentageRow, selectPercentage) = ToolControls.CheckBox(T("tool.compress.percentage.toggle"), _percentageEnabled, OnPercentageToggled);
        _selectPercentage = selectPercentage;
        var percentageHolder = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        percentageHolder.Children.Add(percentageRow);
        BuildReductionPanel();
        percentageHolder.Children.Add(_reductionPanel);
        options.Children.Add(percentageHolder);
        panel.Children.Add(ToolControls.Section(T("tool.compress.options"), options));

        // Estimate.
        var estimate = new StackPanel();
        estimate.Children.Add(ToolControls.Label(T("tool.compress.estimate"), 11.5));
        _estimateValue.FontSize = 20;
        _estimateValue.FontWeight = FontWeights.SemiBold;
        _estimateValue.Foreground = Theme.Text;
        _estimateValue.TextWrapping = TextWrapping.Wrap;
        _estimateValue.Margin = new Thickness(0, 2, 0, 0);
        _estimateDetail.Margin = new Thickness(0, 2, 0, 0);
        estimate.Children.Add(_estimateValue);
        estimate.Children.Add(_estimateDetail);
        panel.Children.Add(ToolControls.Card(estimate));

        _hint.FontSize = 11.5;
        _hint.Margin = new Thickness(2, 10, 2, 4);
        panel.Children.Add(_hint);

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = panel,
        };
    }

    private void BuildSizeBox()
    {
        _sizeBox.FontSize = 12.5;
        _sizeBox.MinWidth = 210;
        _sizeBox.Padding = new Thickness(8, 4, 8, 4);
        _sizeBox.Background = Theme.Card;
        _sizeBox.Foreground = Theme.Text;
        _sizeBox.BorderBrush = Theme.PanelBorder;
        _sizeBox.VerticalContentAlignment = VerticalAlignment.Center;

        _sizeBox.Items.Add(new ComboBoxItem
        {
            Content = T("tool.compress.imageSize.keep"),
            Tag = CompressionDefaults.KeepOriginalDimensions,
        });
        foreach (var longSide in CompressionDefaults.ImageSizePresets)
        {
            _sizeBox.Items.Add(new ComboBoxItem
            {
                Content = T("tool.compress.imageSize.entry", longSide.ToString(CultureInfo.InvariantCulture)),
                Tag = longSide,
            });
        }

        _sizeBox.SelectedIndex = 0;
        _sizeBox.SelectionChanged += (_, _) => UpdateEstimate();
    }

    private void BuildTargetPanel()
    {
        var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        _targetBox.TextChanged += (_, _) => UpdateEstimate();
        row.Children.Add(_targetBox);
        var megabytes = ToolControls.Label("MB", 12.5);
        megabytes.VerticalAlignment = VerticalAlignment.Center;
        megabytes.Margin = new Thickness(8, 0, 0, 0);
        row.Children.Add(megabytes);
        _targetHint.VerticalAlignment = VerticalAlignment.Center;
        _targetHint.Margin = new Thickness(12, 0, 0, 0);
        row.Children.Add(_targetHint);
        _targetPanel.Children.Add(row);
    }

    private void BuildReductionPanel()
    {
        var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        _reductionBox.TextChanged += (_, _) => UpdateEstimate();
        row.Children.Add(_reductionBox);
        var percent = ToolControls.Label("%", 12.5);
        percent.VerticalAlignment = VerticalAlignment.Center;
        percent.Margin = new Thickness(8, 0, 0, 0);
        row.Children.Add(percent);
        _percentageLabel.VerticalAlignment = VerticalAlignment.Center;
        _percentageLabel.Margin = new Thickness(12, 0, 0, 0);
        row.Children.Add(_percentageLabel);
        _reductionPanel.Children.Add(row);
    }

    private FrameworkElement BuildStatus()
    {
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 2), LastChildFill = true };

        _cancel.Visibility = Visibility.Collapsed;
        _cancel.Margin = new Thickness(12, 0, 0, 0);
        _cancel.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(_cancel, Dock.Right);
        row.Children.Add(_cancel);

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _status.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(_progress);
        stack.Children.Add(_status);
        row.Children.Add(stack);
        return row;
    }

    private void OnTargetToggled(bool on)
    {
        _targetEnabled = on;
        if (on && _percentageEnabled)
        {
            _percentageEnabled = false;
            _selectPercentage?.Invoke(false);
        }

        ShowOptions();
        UpdateEstimate();
    }

    private void OnPercentageToggled(bool on)
    {
        _percentageEnabled = on;
        if (on && _targetEnabled)
        {
            _targetEnabled = false;
            _selectTarget?.Invoke(false);
        }

        ShowOptions();
        UpdateEstimate();
    }

    private void ShowOptions()
    {
        _targetPanel.Visibility = _targetEnabled ? Visibility.Visible : Visibility.Collapsed;
        _reductionPanel.Visibility = _percentageEnabled ? Visibility.Visible : Visibility.Collapsed;
    }

    // ------------------------------------------------------------------ estimate

    private void UpdateEstimate()
    {
        if (_source is not { } source || source.Bytes <= 0)
        {
            _estimateValue.Text = T("tool.compress.probe");
            _estimateDetail.Text = string.Empty;
            _hint.Text = string.Empty;
            return;
        }

        if (_targetEnabled && !Compression.TryParseMegabytes(_targetBox.Text, out _))
        {
            ShowProblem(T("tool.compress.error.target"));
            return;
        }

        if (_percentageEnabled && !Compression.TryParseReduction(_reductionBox.Text, out _))
        {
            ShowProblem(T("tool.compress.error.percentage"));
            return;
        }

        var plan = PlanFor(source);
        _estimateValue.Text = Bidi.Value("~" + Compression.FormatSize(plan.EstimatedBytes));

        var smaller = Compression.PercentSmaller(source.Bytes, plan.EstimatedBytes);
        var detail = smaller > 0
            ? T("tool.compress.estimate.smaller", Bidi.Value($"{smaller}%"))
            : T("tool.compress.estimate.larger");

        if (plan.Kind == MediaKind.Image && plan.Resized)
            detail += " · " + Bidi.Value(Compression.FormatPixels(plan.Width, plan.Height));
        else if (plan.Kind == MediaKind.Video && plan.VideoKbps > 0)
            detail += " · " + Bidi.Value($"{plan.VideoKbps} kbps");
        else if (plan.Kind == MediaKind.Audio && plan.AudioKbps > 0)
            detail += " · " + Bidi.Value($"{plan.AudioKbps} kbps");
        _estimateDetail.Text = detail;

        UpdateCaption(source, plan);
    }

    private void ShowProblem(string message)
    {
        _estimateValue.Text = message;
        _estimateDetail.Text = string.Empty;
        _hint.Text = string.Empty;
    }

    private void UpdateCaption(SourceInfo source, CompressionPlan plan)
    {
        _targetHint.Text = T("tool.compress.target.hint", Bidi.Value(Compression.FormatSize(source.Bytes)));

        var reduction = Compression.TryParseReduction(_reductionBox.Text, out var percent)
            ? percent
            : CompressionDefaults.PercentageReduction;
        var keep = Compression.KeepPercentageFromReduction(reduction);
        _percentageLabel.Text = T("tool.compress.percentage", Bidi.Value($"{keep}%"));

        if (_sizeHint is not null)
        {
            _sizeHint.Text = source.Width > 0 && source.Height > 0
                ? Bidi.Value(Compression.FormatPixels(source.Width, source.Height))
                : string.Empty;
        }

        _hint.Text = Compression.NeedsDuration(_kind, Mode) && source.Duration <= 0
            ? T("tool.compress.hint.duration")
            : _kind switch
            {
                MediaKind.Image => T("tool.compress.hint.image"),
                MediaKind.Audio => plan.LosslessAudio ? T("tool.compress.hint.lossless") : T("tool.compress.hint.audio"),
                MediaKind.Video => T("tool.compress.hint.video"),
                MediaKind.Pdf => T("tool.compress.hint.pdf"),
                _ => string.Empty,
            };
    }

    private CompressionMode Mode =>
        _targetEnabled ? CompressionMode.TargetSize
        : _percentageEnabled ? CompressionMode.Percentage
        : CompressionMode.Preset;

    private int SelectedLongSide() =>
        _sizeBox.SelectedItem is ComboBoxItem { Tag: int value } ? value : CompressionDefaults.KeepOriginalDimensions;

    private CompressionPlan PlanFor(SourceInfo source) =>
        Compression.Plan(BuildRequest(), source.Bytes, source.Width, source.Height, source.Duration);

    private CompressionRequest BuildRequest()
    {
        var target = Compression.TryParseMegabytes(_targetBox.Text, out var megabytes) ? megabytes : 1.0;
        var reduction = Compression.TryParseReduction(_reductionBox.Text, out var percent)
            ? percent
            : CompressionDefaults.PercentageReduction;
        return new CompressionRequest(_kind, _format, Mode, target,
            Compression.KeepPercentageFromReduction(reduction), _preset, SelectedLongSide());
    }

    // ------------------------------------------------------------------ actions

    private void Reset()
    {
        _cts?.Cancel();
        _preset = CompressionDefaults.Preset;
        _targetEnabled = CompressionDefaults.TargetSizeEnabled;
        _percentageEnabled = CompressionDefaults.PercentageEnabled;
        _selectPreset?.Invoke(CompressionDefaults.Preset);
        _selectTarget?.Invoke(false);
        _selectPercentage?.Invoke(false);
        _sizeBox.SelectedIndex = 0;
        _reductionBox.Text = CompressionDefaults.PercentageReduction.ToString(CultureInfo.InvariantCulture);
        _targetBox.Text = _source is { Bytes: > 0 } source
            ? Compression.FormatMegabytes(Compression.MegabytesFromBytes(source.Bytes) / 2)
            : string.Empty;
        ShowOptions();
        UpdateEstimate();
    }

    private async Task<string?> ApplyAsync()
    {
        if (_running)
            return null;

        var source = await _probe;
        if (_kind == MediaKind.None || source.Bytes <= 0)
            throw new ConversionException(T("tool.compress.error.unsupported"));

        if (_targetEnabled && !Compression.TryParseMegabytes(_targetBox.Text, out _))
            throw new ConversionException(T("tool.compress.error.target"));
        if (_percentageEnabled && !Compression.TryParseReduction(_reductionBox.Text, out _))
            throw new ConversionException(T("tool.compress.error.percentage"));
        if (Compression.NeedsDuration(_kind, Mode) && source.Duration <= 0)
            throw new ConversionException(T("tool.compress.error.duration"));

        var plan = PlanFor(source);
        var directory = ToolOutputFolder.For(_item, _targets);

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var progress = new Progress<double>(value =>
        {
            _progress.Value = Math.Clamp(value, 0, 1);
            _status.Text = T("tool.compress.progress");
        });

        BeginRun();
        try
        {
            return _kind switch
            {
                MediaKind.Image => await CompressRunner.ImageAsync(_item, plan, directory, token, progress),
                MediaKind.Pdf => await CompressRunner.PdfAsync(_item, directory, token),
                _ => await CompressRunner.MediaAsync(_item, plan, source.Duration, directory, _tools, token, progress),
            };
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            EndRun();
        }
    }

    private void BeginRun()
    {
        _running = true;
        _progress.Visibility = Visibility.Visible;
        _progress.Value = 0;
        _status.Text = T("tool.compress.progress");
        _cancel.Visibility = Visibility.Visible;
    }

    private void EndRun()
    {
        _running = false;
        _progress.Visibility = Visibility.Collapsed;
        _progress.Value = 0;
        _status.Text = string.Empty;
        _cancel.Visibility = Visibility.Collapsed;
    }

    // ------------------------------------------------------------------ probing

    /// <summary>
    /// Reads the source size and, per kind, its pixels or its duration. Runs on a worker thread; a missing
    /// FFmpeg or an unreadable file leaves the numbers at zero and the view explains what is missing.
    /// </summary>
    private SourceInfo Probe()
    {
        long bytes = 0;
        try
        {
            bytes = new FileInfo(_item.Path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            bytes = 0;
        }

        try
        {
            switch (_kind)
            {
                case MediaKind.Image:
                {
                    var info = new MagickImageInfo(_item.Path);
                    return new SourceInfo(bytes, (int)info.Width, (int)info.Height, 0);
                }

                case MediaKind.Pdf:
                    return new SourceInfo(bytes, 0, 0, 0);

                default:
                {
                    var ffmpeg = _tools.Ffmpeg;
                    if (ffmpeg is null)
                        return new SourceInfo(bytes, 0, 0, 0);

                    var banner = new StringBuilder();
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    // "-i file" with no output always exits non-zero; only its banner is wanted here.
                    ProcessRunner.RunAsync(ffmpeg, ["-hide_banner", "-nostdin", "-i", _item.Path], timeout.Token,
                        line =>
                        {
                            lock (banner)
                                banner.AppendLine(line);
                        },
                        throwOnError: false).GetAwaiter().GetResult();

                    var probe = MetadataFfmpeg.ParseProbe(banner.ToString());
                    return new SourceInfo(bytes, probe.Width, probe.Height, probe.DurationSeconds);
                }
            }
        }
        catch
        {
            return new SourceInfo(bytes, 0, 0, 0);
        }
    }

    private readonly record struct SourceInfo(long Bytes, int Width, int Height, double Duration);
}
