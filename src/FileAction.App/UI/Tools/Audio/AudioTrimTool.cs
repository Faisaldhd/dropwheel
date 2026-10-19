using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FileAction.Core;
using FileAction.Core.Backends;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Audio;

/// <summary>
/// Trim one audio file: a background waveform, draggable start/end handles, exact time boxes and an
/// optional "silence at the edges only" pass. Apply writes a new file next to the source; the original is
/// only read.
/// </summary>
internal sealed class AudioTrimTool : IToolWindowSpec
{
    public static readonly AudioTrimTool Instance = new();

    public string Id => "tools.audiotrim";

    public string TitleKey => "tool.audiotrim.title";

    public ToolSpecLayout Layout => AudioToolLayout.Window;

    public bool Applies(IReadOnlyList<FileItem> items) =>
        items.Count == 1 && !items[0].IsDirectory && Formats.Audio.Contains(items[0].Format);

    public ToolContent Create(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new AudioTrimView(items[0], targets).Build();
}

/// <summary>Registers the tool from its own file; no shared list is edited.</summary>
internal static class AudioTrimToolRegistration
{
    [ModuleInitializer]
    internal static void Register() => ToolRegistry.Register(AudioTrimTool.Instance);
}

/// <summary>The trim window: waveform card, handles, time boxes, silence toggle and the preview button.</summary>
internal sealed class AudioTrimView
{
    private static readonly string MinimumTimeText =
        AudioTrim.MinimumDuration.ToString("0.00", CultureInfo.InvariantCulture);

    private readonly FileItem _item;
    private readonly DropTargets? _targets;
    private readonly ToolLocator _tools = new();
    private readonly TaskCompletionSource<MediaSize?> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly WaveformStrip _strip = new() { MinHeight = 90 };
    private readonly TextBox _startBox = Entry();
    private readonly TextBox _endBox = Entry();
    private readonly TextBlock _durationText = new()
    {
        FontSize = 12,
        Foreground = Theme.Muted,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.NoWrap,
        Margin = new Thickness(8, 0, 0, 0),
    };
    private readonly TextBlock _selectionText = new()
    {
        FontSize = 12,
        Foreground = Theme.Muted,
        VerticalAlignment = VerticalAlignment.Bottom,
        HorizontalAlignment = HorizontalAlignment.Right,
        TextWrapping = TextWrapping.NoWrap,
    };
    private readonly TextBlock _status = new()
    {
        Foreground = Theme.Muted,
        FontSize = 12.5,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(2, 6, 12, 0),
    };
    private readonly StackPanel _silenceRow = new()
    {
        Orientation = Orientation.Horizontal,
        Margin = new Thickness(2, 0, 0, 2),
    };

    private readonly MediaPlayer _player = new();
    private readonly DispatcherTimer _playTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    private Border? _playButton;
    private TrimRange _selection;
    private double _duration;
    private bool _silenceOn;
    private bool _playing;
    private bool _opening;
    private bool _previewBroken;
    private bool _syncing;
    private bool _applying;
    private int _analysisId;

    public AudioTrimView(FileItem item, DropTargets? targets)
    {
        _item = item;
        _targets = targets;
        foreach (var box in new[] { _startBox, _endBox })
        {
            box.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter)
                    return;
                e.Handled = true;
                CommitBoxes();
            };
            box.LostFocus += (_, _) =>
            {
                if (!_applying)
                    CommitBoxes();
            };
        }

        _strip.SelectionChanged += range => SetSelection(range, fromUser: true);
        _strip.SelectionCommitted += () => _status.Text = T("tool.audiotrim.ready");

        _player.MediaOpened += (_, _) => _dispatcher.BeginInvoke(new Action(StartPlayback));
        _player.MediaFailed += (_, _) => _dispatcher.BeginInvoke(new Action(FailPreview));
        _player.MediaEnded += (_, _) => _dispatcher.BeginInvoke(new Action(StopPreview));
        _playTimer.Tick += (_, _) => OnPlayTick();
        BuildSilenceToggle();
    }

    public ToolContent Build()
    {
        var root = new Grid { Margin = new Thickness(18, 2, 18, 0) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = BuildHeader();
        var card = new Border
        {
            Background = Theme.Card,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10, 16, 10, 16),
            Margin = new Thickness(0, 0, 0, 10),
            Child = _strip,
        };
        var times = BuildTimes();
        var bottom = BuildBottom();

        Grid.SetRow(header, 0);
        Grid.SetRow(card, 1);
        Grid.SetRow(times, 2);
        Grid.SetRow(_silenceRow, 3);
        Grid.SetRow(bottom, 4);
        root.Children.Add(header);
        root.Children.Add(card);
        root.Children.Add(times);
        root.Children.Add(_silenceRow);
        root.Children.Add(bottom);

        _status.Text = T("tool.audiotrim.analyzing");
        root.Unloaded += (_, _) => Cancel();
        _ready.TrySetResult(null);
        _ = AnalyzeAsync();
        return new ToolContent(root, Reset, ApplyAsync, _ready.Task, () => Task.CompletedTask);
    }

    private FrameworkElement BuildHeader()
    {
        var name = new TextBlock
        {
            Text = Bidi.Value(_item.Name),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = _item.Path,
        };
        var panel = new DockPanel { Margin = new Thickness(2, 0, 2, 8) };
        DockPanel.SetDock(_durationText, Dock.Right);
        panel.Children.Add(_durationText);
        panel.Children.Add(name);
        return panel;
    }

    private FrameworkElement BuildTimes()
    {
        var panel = new DockPanel { Margin = new Thickness(2, 0, 2, 6), LastChildFill = false };
        var start = Field("tool.audiotrim.start", _startBox);
        start.Margin = new Thickness(0, 0, 16, 0);
        var end = Field("tool.audiotrim.end", _endBox);
        DockPanel.SetDock(start, Dock.Left);
        DockPanel.SetDock(end, Dock.Left);
        DockPanel.SetDock(_selectionText, Dock.Right);
        panel.Children.Add(start);
        panel.Children.Add(end);
        panel.Children.Add(_selectionText);
        return panel;
    }

    private FrameworkElement BuildBottom()
    {
        var panel = new DockPanel();
        _playButton = Controls.Button(T("tool.audiotrim.play"), TogglePreview, glyph: Glyphs.Play);
        _playButton.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(_playButton, Dock.Right);
        panel.Children.Add(_playButton);
        panel.Children.Add(_status);
        return panel;
    }

    private static FrameworkElement Field(string labelKey, TextBox box)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = T(labelKey),
            FontSize = 11.5,
            Foreground = Theme.Muted,
            Margin = new Thickness(2, 0, 0, 4),
        });
        stack.Children.Add(box);
        return stack;
    }

    private static TextBox Entry() => new()
    {
        Width = 104,
        Padding = new Thickness(8, 5, 8, 6),
        Background = Theme.Page,
        Foreground = Theme.Text,
        BorderBrush = Theme.PanelBorder,
        BorderThickness = new Thickness(1),
        FontSize = 13,
        TextAlignment = TextAlignment.Center,
        FlowDirection = FlowDirection.LeftToRight,
    };

    private void BuildSilenceToggle()
    {
        _silenceRow.Children.Clear();
        var toggle = Controls.Toggle(_silenceOn, OnSilenceToggle);
        toggle.Margin = new Thickness(0, 0, 10, 0);
        toggle.VerticalAlignment = VerticalAlignment.Center;
        _silenceRow.Children.Add(toggle);
        _silenceRow.Children.Add(new TextBlock
        {
            Text = T("tool.audiotrim.silence"),
            FontSize = 12.5,
            Foreground = Theme.Text,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
        });
    }

    private void OnSilenceToggle(bool on)
    {
        _silenceOn = on;
        if (on)
        {
            _ = DetectSilenceAsync();
            return;
        }

        SetSelection(AudioTrim.Full(_duration), fromUser: false);
        _status.Text = _duration > 0 ? T("tool.audiotrim.ready") : T("tool.audiotrim.analyzing");
    }

    private async Task AnalyzeAsync()
    {
        var id = ++_analysisId;
        try
        {
            _status.Text = T("tool.audiotrim.analyzing");
            var ffmpeg = AudioRunner.RequireFfmpeg(_tools);

            // One header probe for the exact container duration and one decode for the envelope.
            var probe = FfmpegBackend.ProbeDurationAsync(ffmpeg, _item.Path, _lifetime.Token);
            var waveformTask = AudioRunner.WaveformAsync(ffmpeg, _item.Path, _lifetime.Token);
            await Task.WhenAll((Task)probe, (Task)waveformTask).ConfigureAwait(true);
            if (id != _analysisId)
                return;

            var waveform = await waveformTask.ConfigureAwait(true);
            var duration = await probe.ConfigureAwait(true) ?? 0;
            _duration = duration > 0 ? duration : waveform.Duration;
            _strip.SetWaveform(waveform.Peaks, _duration);
            _durationText.Text = $"{T("tool.audiotrim.duration")} {Bidi.Value(AudioTrim.FormatTime(_duration))}";
            SetSelection(AudioTrim.Full(_duration), fromUser: false);
            _status.Text = T("tool.audiotrim.ready");
        }
        catch (OperationCanceledException)
        {
            // Closing the window cancels the decode.
        }
        catch (Exception ex)
        {
            if (id != _analysisId)
                return;
            _duration = 0;
            _status.Text = AudioRunner.Message(ex, "tool.audiotrim.error.stream");
        }
    }

    private async Task DetectSilenceAsync()
    {
        if (_duration <= 0)
        {
            _status.Text = T("tool.audiotrim.error.notReady");
            _silenceOn = false;
            BuildSilenceToggle();
            return;
        }

        try
        {
            _status.Text = T("tool.audiotrim.silence.detecting");
            var ffmpeg = AudioRunner.RequireFfmpeg(_tools);
            var spans = await AudioRunner.DetectSilenceAsync(ffmpeg, _item.Path, _lifetime.Token).ConfigureAwait(true);

            if (AudioTrim.IsEntirelySilent(spans, _duration))
            {
                _status.Text = T("tool.audiotrim.silence.all");
                return;
            }

            var range = AudioTrim.TrimSilence(spans, _duration);
            if (range.Start <= 0.001 && range.Duration >= _duration - 0.001)
            {
                _status.Text = T("tool.audiotrim.silence.none");
                return;
            }

            SetSelection(range, fromUser: false);
            _status.Text = T(
                "tool.audiotrim.silence.found",
                Bidi.Value(AudioTrim.FormatTime(range.Start)),
                Bidi.Value(AudioTrim.FormatTime(_duration - range.End)));
        }
        catch (OperationCanceledException)
        {
            // Closing the window cancels the pass.
        }
        catch (Exception ex)
        {
            _status.Text = AudioRunner.Message(ex, "tool.audiotrim.error.stream");
            _silenceOn = false;
            BuildSilenceToggle();
        }
    }

    private void SetSelection(TrimRange range, bool fromUser)
    {
        _selection = AudioTrim.Clamp(range, _duration);
        _strip.Selection = _selection;
        SyncBoxes();
        if (!fromUser)
            return;

        if (_playing)
            StopPreview();
        if (_silenceOn)
        {
            _silenceOn = false;
            BuildSilenceToggle();
        }
    }

    private void SyncBoxes()
    {
        _syncing = true;
        _startBox.Text = AudioTrim.FormatTime(_selection.Start);
        _endBox.Text = AudioTrim.FormatTime(_selection.End);
        _syncing = false;
        _selectionText.Text = T("tool.audiotrim.selection", Bidi.Value(AudioTrim.FormatTime(_selection.Duration)));
    }

    private void CommitBoxes()
    {
        if (_syncing)
            return;

        if (_duration <= 0)
        {
            _status.Text = T("tool.audiotrim.error.notReady");
            return;
        }

        if (!AudioTrim.TryParseTime(_startBox.Text, out var start) || !AudioTrim.TryParseTime(_endBox.Text, out var end))
        {
            _status.Text = T("tool.audiotrim.error.time");
            SyncBoxes();
            return;
        }

        var range = AudioTrim.FromTimes(start, end, _duration);
        if (range.Duration < AudioTrim.MinimumDuration)
        {
            _status.Text = T("tool.audiotrim.error.range", Bidi.Value(MinimumTimeText));
            SyncBoxes();
            return;
        }

        SetSelection(range, fromUser: true);
        _status.Text = T("tool.audiotrim.ready");
    }

    private void Reset()
    {
        StopPreview();
        _silenceOn = false;
        BuildSilenceToggle();
        SetSelection(AudioTrim.Full(_duration), fromUser: false);
        _status.Text = _duration > 0 ? T("tool.audiotrim.ready") : T("tool.audiotrim.analyzing");
        if (_duration <= 0)
            _ = AnalyzeAsync();
    }

    private void TogglePreview()
    {
        if (_previewBroken || _duration <= 0 || _opening)
            return;

        if (_playing)
        {
            StopPreview();
            return;
        }

        if (_player.Source is null)
        {
            // Opening is asynchronous: playback starts from MediaOpened, once the position can be set.
            _opening = true;
            _player.Open(new Uri(_item.Path));
            return;
        }

        StartPlayback();
    }

    private void StartPlayback()
    {
        _opening = false;
        if (_previewBroken || _duration <= 0 || _lifetime.IsCancellationRequested)
            return;

        try
        {
            var start = AudioTrim.Clamp(_selection, _duration).Start;
            _player.Position = TimeSpan.FromSeconds(start);
            _player.Play();
            _playing = true;
            _playTimer.Start();
            _strip.Playhead = start;
            if (_playButton is not null)
                Controls.SetText(_playButton, T("tool.audiotrim.pause"));
        }
        catch (Exception)
        {
            FailPreview();
        }
    }

    private void OnPlayTick()
    {
        var position = _player.Position.TotalSeconds;
        _strip.Playhead = position;
        if (position >= Math.Min(_selection.End, _duration) - 0.02)
            StopPreview();
    }

    private void StopPreview()
    {
        _playTimer.Stop();
        _playing = false;
        try
        {
            if (_player.Source is not null)
                _player.Pause();
        }
        catch (InvalidOperationException)
        {
            // The media failed to open; there is nothing to pause.
        }
        _strip.Playhead = null;
        if (_playButton is not null)
            Controls.SetText(_playButton, T("tool.audiotrim.play"));
    }

    private void FailPreview()
    {
        _previewBroken = true;
        _opening = false;
        StopPreview();
        if (_playButton is not null)
        {
            _playButton.IsEnabled = false;
            _playButton.Opacity = 0.5;
        }
        _status.Text = T("tool.audiotrim.preview.unavailable");
    }

    private async Task<string?> ApplyAsync()
    {
        _applying = true;
        try
        {
            if (_duration <= 0)
                throw new ConversionException(T("tool.audiotrim.error.notReady"));

            var range = AudioTrim.Clamp(_selection, _duration);
            if (range.Duration < AudioTrim.MinimumDuration)
                throw new ConversionException(T("tool.audiotrim.error.range", Bidi.Value(MinimumTimeText)));

            StopPreview();
            _status.Text = T("tool.audiotrim.applying");
            var ffmpeg = AudioRunner.RequireFfmpeg(_tools);
            return await AudioRunner.TrimAsync(
                ffmpeg,
                _item,
                range,
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
        StopPreview();
        _playTimer.Stop();
        if (_player.Source is not null)
            _player.Close();
        _lifetime.Cancel();
    }
}
