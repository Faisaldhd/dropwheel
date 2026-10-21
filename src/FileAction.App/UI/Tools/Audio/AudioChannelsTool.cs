using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using FileAction.Core;
using FileAction.Core.Backends;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Audio;

/// <summary>
/// "Channels" on the Tools wheel: choose mono or stereo, set the two volume sliders and solo one side.
/// The window shows the real waveform of the file, so the sliders and the solo choice are visible before
/// anything is written. Apply writes "&lt;name&gt; Mono.ext" or "&lt;name&gt; Stereo.ext" next to the source.
/// </summary>
internal sealed class AudioChannelsTool : IToolWindowSpec
{
    public static readonly AudioChannelsTool Instance = new();

    public string Id => "tools.audiochannels";

    public string TitleKey => "tool.audiochannels.title";

    /// <summary>A waveform lane per side plus the two slider rows; the preview fills whatever is left.</summary>
    public ToolSpecLayout Layout { get; } = new(
        ChromeWidth: 36,
        ChromeHeight: 300,
        PreviewMinWidth: 300,
        PreviewMinHeight: 140,
        PreviewMaxWidth: 560,
        PreviewMaxHeight: 240,
        DefaultPreviewWidth: 480,
        DefaultPreviewHeight: 190);

    public bool Applies(IReadOnlyList<FileItem> items) =>
        items.Count == 1 && !items[0].IsDirectory && Formats.Audio.Contains(items[0].Format);

    public ToolContent Create(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new AudioChannelsView(items[0], targets).Build();
}

/// <summary>Registers the tool from its own file, so no shared list has to be edited.</summary>
internal static class AudioChannelsToolRegistration
{
    [ModuleInitializer]
    internal static void Register() => ToolRegistry.Register(AudioChannelsTool.Instance);
}

/// <summary>The channels window: waveform, mono/stereo choice, solo preview and the two volume sliders.</summary>
internal sealed class AudioChannelsView
{
    private readonly FileItem _item;
    private readonly DropTargets? _targets;
    private readonly ToolLocator _tools = new();
    private readonly TaskCompletionSource<MediaSize?> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _life = new();
    private readonly WaveformView _waveformView = new() { Stereo = true };
    private readonly StackPanel _controls = new();
    private readonly Border _placeholder;
    private readonly TextBlock _outputName = new();
    private readonly TextBlock _status = AudioToolChrome.Status();

    private AudioWaveform? _waveform;
    private AudioChannelMode _mode = AudioChannelMode.Stereo;
    private AudioChannelSolo _solo = AudioChannelSolo.Both;
    private int _leftVolume = 100;
    private int _rightVolume = 100;
    private bool _sourceIsMono;

    public AudioChannelsView(FileItem item, DropTargets? targets)
    {
        _item = item;
        _targets = targets;
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

    /// <summary>Loads the waveform once the window is up, then reports the preview size to the sizer.</summary>
    public async Task ActivateAsync()
    {
        try
        {
            var ffmpeg = _tools.Require(ExternalTool.Ffmpeg);
            // The filter has to know whether the second input channel exists: a mono source is duplicated
            // by the pan filter instead of reading a missing c1 (which would write silence on the right).
            var probe = AudioRunner.ProbeAsync(ffmpeg, _item.Path, _life.Token);
            var waveform = await AudioWaveform.LoadAsync(ffmpeg, _item.Path, _life.Token);
            _sourceIsMono = (await probe)?.IsMono ?? false;
            _waveform = waveform;
            _waveformView.Waveform = waveform;
            _waveformView.Stereo = true;
            _waveformView.Redraw();
            _placeholder.Visibility = Visibility.Collapsed;
            _ready.TrySetResult(new MediaSize(560, 200));
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

    /// <summary>Rebuilt on Reset, so the segmented pills and the sliders go back to their defaults too.</summary>
    private void FillControls()
    {
        _controls.Children.Clear();

        _controls.Children.Add(Choice(T("tool.audiochannels.mode"), Controls.Segmented(
        [
            (T("tool.audiochannels.mode.mono"), AudioChannelMode.Mono),
            (T("tool.audiochannels.mode.stereo"), AudioChannelMode.Stereo),
        ], _mode, mode =>
        {
            _mode = mode;
            Paint();
        })));

        _controls.Children.Add(Choice(T("tool.audiochannels.solo.label"), Controls.Segmented(
        [
            (T("tool.audiochannels.solo.both"), AudioChannelSolo.Both),
            (T("tool.audiochannels.solo.left"), AudioChannelSolo.Left),
            (T("tool.audiochannels.solo.right"), AudioChannelSolo.Right),
        ], _solo, solo =>
        {
            _solo = solo;
            Paint();
        })));

        _controls.Children.Add(SliderRow(T("tool.audiochannels.left"), _leftVolume, value =>
        {
            _leftVolume = value;
            Paint();
        }));

        _controls.Children.Add(SliderRow(T("tool.audiochannels.right"), _rightVolume, value =>
        {
            _rightVolume = value;
            Paint();
        }));

        _outputName.FontSize = 11.5;
        _outputName.Foreground = Theme.Muted;
        _outputName.TextTrimming = TextTrimming.CharacterEllipsis;
        _outputName.Margin = new Thickness(0, 4, 0, 0);
        _controls.Children.Add(_outputName);
        _controls.Children.Add(_status);

        Paint();
    }

    private static FrameworkElement Choice(string caption, FrameworkElement control)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(Caption(caption));
        row.Children.Add(control);
        return row;
    }

    private static FrameworkElement SliderRow(string caption, int value, Action<int> changed)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        row.Children.Add(Caption(caption));
        row.Children.Add(Controls.Slider(0, 200, value, changed, () => { }));
        return row;
    }

    private static TextBlock Caption(string text)
    {
        var caption = AudioToolChrome.Caption(text);
        caption.Width = 92;
        return caption;
    }

    /// <summary>Applies the current choices to the preview and to the "what will be written" name.</summary>
    private void Paint()
    {
        var leftGain = AudioEffects.Gain(_leftVolume) * (_solo == AudioChannelSolo.Right ? 0 : 1);
        var rightGain = AudioEffects.Gain(_rightVolume) * (_solo == AudioChannelSolo.Left ? 0 : 1);
        _waveformView.ShowChannels(leftGain, rightGain);

        var suffix = T(_mode == AudioChannelMode.Mono
            ? "tool.audiochannels.suffix.mono"
            : "tool.audiochannels.suffix.stereo");
        var format = FfmpegBackend.SafeContainer(_item.Format);
        _outputName.Text = Bidi.Value($"{_item.BaseName} {suffix}.{format}");
    }

    private void Reset()
    {
        _mode = AudioChannelMode.Stereo;
        _solo = AudioChannelSolo.Both;
        _leftVolume = 100;
        _rightVolume = 100;
        _status.Text = string.Empty;
        FillControls();
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
            throw new ConversionException(T("tool.audiochannels.error.decode"));

        var directory = AudioToolOutput.DirectoryFor(_item, _targets);
        var format = FfmpegBackend.SafeContainer(_item.Format);
        var suffix = T(_mode == AudioChannelMode.Mono
            ? "tool.audiochannels.suffix.mono"
            : "tool.audiochannels.suffix.stereo");
        var args = AudioEffects.ChannelArguments(_mode, _leftVolume, _rightVolume, _solo, _sourceIsMono);
        var codec = FfmpegBackend.CodecArgs(format, new Settings());
        var tools = _tools;
        var duration = _waveform.Duration;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
        _status.Text = T("tool.audiochannels.working");
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
            // The window was closed (or the drop cancelled): nothing was written, so nothing to report.
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
