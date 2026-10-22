using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using FileAction.Core;
using static FileAction.Core.Loc;
using WpfImage = System.Windows.Controls.Image;

namespace FileAction.App.UI.Tools.Image;

/// <summary>
/// The editor's content: the preview card with a hold-to-compare original, one tab per slider group, and
/// the sliders of the selected tab (the Light tab scrolls when its eight rows do not fit). Slider moves
/// are debounced by 150 ms and rendered asynchronously on a downsampled copy; Apply re-runs the very same
/// parameters on the original.
/// </summary>
internal sealed class EditPhotosView
{
    private const double ThumbSize = 14;

    private readonly FileItem _item;
    private readonly DropTargets? _targets;
    private readonly PhotoParameters _parameters = new();
    private readonly Task<EditPhotoRenderer.LoadedPhoto> _load;
    private readonly TaskCompletionSource<MediaSize?> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ImageGateGrid _root = new();
    private readonly DispatcherTimer _debounce;
    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private readonly StackPanel _rows = new();
    private readonly Grid _previewHost = new() { ClipToBounds = true };
    private readonly WpfImage _imageView = new()
    {
        Stretch = Stretch.Fill,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private Border? _placeholder;
    private Border? _originalBadge;
    private Border? _comparePill;
    private ProgressBar? _busy;
    private EditPhotoRenderer.LoadedPhoto? _source;
    private BitmapSource? _edited;
    private PhotoEditGroup _group = PhotoEditGroup.Light;
    private int _renderVersion;
    private bool _holding;
    private bool _pinnedOriginal;

    public EditPhotosView(FileItem item, DropTargets? targets)
    {
        _item = item;
        _targets = targets;
        // Decode on a background thread, downsampled: the window opens immediately.
        _load = Task.Run(() => EditPhotoRenderer.Load(item.Path));
        _debounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await RenderAsync();
        };
        RenderOptions.SetBitmapScalingMode(_imageView, BitmapScalingMode.HighQuality);
    }

    public ToolContent Build()
    {
        var root = _root;
        root.Margin = new Thickness(18, 0, 18, 0);
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var preview = BuildPreview();
        var controls = BuildControls();
        Grid.SetRow(preview, 0);
        Grid.SetRow(controls, 1);
        root.Children.Add(preview);
        root.Children.Add(controls);

        // A closed window must not keep re-rendering through the debounce timer.
        root.Unloaded += (_, _) => _debounce.Stop();

        return new ToolContent(root, Reset, ApplyAsync, _ready.Task, ActivateAsync);
    }

    /// <summary>Replaces the placeholder with the decoded preview, on the UI thread.</summary>
    public async Task ActivateAsync()
    {
        try
        {
            var source = await _load;
            _source = source;
            _edited = source.Original;
            Display();
            _root.Ready();
            _ready.TrySetResult(new MediaSize(source.Width, source.Height));

            // A slider may have moved while the photo was still decoding.
            if (!PhotoEdit.IsNeutral(_parameters))
                await RenderAsync();
        }
        catch (Exception ex)
        {
            ShowFailure(ex.Message, ex);
            _ready.TrySetResult(null);
        }
    }

    // ---- preview -------------------------------------------------------------------------------

    private FrameworkElement BuildPreview()
    {
        _placeholder = new Border
        {
            Background = Theme.Card,
            CornerRadius = new CornerRadius(10),
            Child = new ProgressBar
            {
                IsIndeterminate = true,
                Width = 120,
                Height = 4,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        _originalBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xB8, 0, 0, 0)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(9, 3, 9, 4),
            Margin = new Thickness(10),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            Child = new TextBlock
            {
                Text = T("tool.editphotos.original"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
            },
        };

        _busy = new ProgressBar
        {
            IsIndeterminate = true,
            Height = 2,
            Margin = new Thickness(8, 0, 8, 3),
            Background = Brushes.Transparent,
            Foreground = Theme.Accent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Bottom,
            Visibility = Visibility.Collapsed,
        };

        var compare = BuildComparePill();
        _previewHost.Children.Add(_imageView);
        _previewHost.Children.Add(_placeholder);
        _previewHost.Children.Add(_originalBadge);
        _previewHost.Children.Add(_busy);
        _previewHost.Children.Add(compare);
        _previewHost.SizeChanged += (_, _) => LayoutPreview();
        MediaSurface.Round(_previewHost);

        // Hold anywhere on the preview to see the original while pressed.
        _previewHost.MouseLeftButtonDown += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed)
                return;
            _holding = true;
            Display();
        };
        _previewHost.MouseLeftButtonUp += (_, _) => ReleaseHold();
        _previewHost.MouseLeave += (_, _) => ReleaseHold();

        return new Border
        {
            Background = Theme.Card,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 4, 0, 12),
            Child = _previewHost,
        };
    }

    /// <summary>A pill that toggles the original on, and also shows it while it is being pressed.</summary>
    private FrameworkElement BuildComparePill()
    {
        _comparePill = new Border
        {
            Background = Theme.Track,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(11, 4, 11, 5),
            Margin = new Thickness(10),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Cursor = Cursors.Hand,
            ToolTip = UiSetup.Tip(T("tool.editphotos.hint")),
            Child = new TextBlock
            {
                Text = T("tool.editphotos.compare"),
                FontSize = 11.5,
                Foreground = Theme.Text,
            },
        };
        _comparePill.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _holding = false;
            _pinnedOriginal = !_pinnedOriginal;
            Display();
        };
        return _comparePill;
    }

    private void ReleaseHold()
    {
        if (!_holding)
            return;
        _holding = false;
        Display();
    }

    /// <summary>Shows the edited preview or the original, and paints the compare state.</summary>
    private void Display()
    {
        var original = _pinnedOriginal || _holding;
        var bitmap = original ? _source?.Original : _edited ?? _source?.Original;
        if (bitmap is not null)
        {
            _imageView.Source = bitmap;
            _imageView.Visibility = Visibility.Visible;
            LayoutPreview();
            if (_placeholder is not null)
                _placeholder.Visibility = Visibility.Collapsed;
        }

        if (_originalBadge is not null)
            _originalBadge.Visibility = original ? Visibility.Visible : Visibility.Collapsed;

        if (_comparePill is not null)
        {
            _comparePill.Background = original ? Theme.Accent : Theme.Track;
            if (_comparePill.Child is TextBlock label)
                label.Foreground = original ? Theme.TextOnAccent : Theme.Text;
        }
    }

    /// <summary>
    /// Sizes the bitmap at its fitted 1x–2x scale and lets the Grid centre it, so a tiny photo keeps its
    /// pixels and the rest of the card is letterboxing instead of a stretched bitmap.
    /// </summary>
    private void LayoutPreview()
    {
        if (_imageView.Source is not BitmapSource bitmap)
            return;
        var fit = PreviewFit.Fit(
            _previewHost.ActualWidth, _previewHost.ActualHeight, bitmap.PixelWidth, bitmap.PixelHeight);
        _imageView.Width = fit.Width;
        _imageView.Height = fit.Height;
    }

    private void ShowFailure(string message, Exception? error = null)
    {
        SetBusy(false);
        Diagnostics.AppLog.Error($"editphotos: {message}", error);
        // Keep the last good preview: only an empty card is replaced by the error.
        if (_placeholder is null || _imageView.Visibility == Visibility.Visible)
            return;
        _placeholder.Visibility = Visibility.Visible;
        _placeholder.Child = new TextBlock
        {
            Text = message,
            Foreground = Theme.Muted,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(18),
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    // ---- controls ------------------------------------------------------------------------------

    private FrameworkElement BuildControls()
    {
        var panel = new StackPanel();
        var options = Enum.GetValues<PhotoEditGroup>()
            .Select(group => (T(PhotoEdit.GroupKey(group)), group))
            .ToList();

        var tabs = Controls.Segmented(options, _group, group =>
        {
            _group = group;
            ShowGroup();
        });
        tabs.Margin = new Thickness(0, 0, 0, 12);
        panel.Children.Add(tabs);

        // Light now has eight sliders (brightness and gamma joined the six tone controls), which is
        // taller than the panel the window was sized for. Scrolling the rows keeps the 500x745 default
        // window instead of growing past a 720p work area; the other tabs still fit without a scrollbar.
        var rowsScroll = new ScrollViewer
        {
            Content = _rows,
            MaxHeight = 320,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
        };
        panel.Children.Add(rowsScroll);

        var hint = Controls.Text(T("tool.editphotos.hint"), 11.5, brush: Theme.Muted);
        hint.Margin = new Thickness(2, 10, 2, 0);
        panel.Children.Add(hint);

        // HEIC is readable but cannot be encoded here; the saved copy is a JPEG, and the user is told
        // before pressing Apply instead of getting a failed write.
        if (ImageOutput.IsHeic(Formats.Normalize(_item.Format)))
        {
            var note = Controls.Text(T("tool.editphotos.heicNote"), 11.5, brush: Theme.Muted);
            note.Margin = new Thickness(2, 6, 2, 0);
            note.TextWrapping = TextWrapping.Wrap;
            panel.Children.Add(note);
        }

        ShowGroup();
        return panel;
    }

    private void ShowGroup()
    {
        _rows.Children.Clear();
        foreach (var spec in PhotoEdit.InGroup(_group))
            _rows.Children.Add(BuildSliderRow(spec));
    }

    /// <summary>
    /// One editable slider: name on the leading edge, value on the trailing edge, a centre-based track for
    /// the bipolar ranges, and double-click to return that slider to its default.
    /// </summary>
    private FrameworkElement BuildSliderRow(PhotoParameterSpec spec)
    {
        var value = new TextBlock
        {
            Text = Format(_parameters[spec.Parameter]),
            FontSize = 12,
            Foreground = Theme.Muted,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 34,
            TextAlignment = TextAlignment.Right,
        };
        var name = new TextBlock
        {
            Text = T(spec.Key),
            FontSize = 12.5,
            Foreground = Theme.Text,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var header = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(value, Dock.Right);
        header.Children.Add(value);
        header.Children.Add(name);

        // Sliders keep their left-to-right direction in Arabic: the value grows to the right everywhere.
        var surface = new Grid
        {
            Height = 22,
            Margin = new Thickness(0, 4, 0, 0),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            FlowDirection = FlowDirection.LeftToRight,
        };
        var thumb = new Ellipse
        {
            Width = ThumbSize,
            Height = ThumbSize,
            Fill = Brushes.White,
            Stroke = Theme.PanelBorder,
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = Theme.Shadow(6),
        };
        var fill = new Border
        {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = Theme.AccentGradient,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        var rail = new Border
        {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = Theme.Track,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var notch = new Border
        {
            Width = 1,
            Height = 8,
            Background = Theme.Muted,
            Opacity = 0.45,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = spec.IsBipolar ? Visibility.Visible : Visibility.Collapsed,
        };
        surface.Children.Add(rail);
        surface.Children.Add(notch);
        surface.Children.Add(fill);
        surface.Children.Add(thumb);

        var current = _parameters[spec.Parameter];
        void Paint()
        {
            var width = Math.Max(ThumbSize + 1, surface.ActualWidth);
            var usable = width - ThumbSize;
            var position = (current - spec.Min) / (double)(spec.Max - spec.Min);
            var start = spec.IsBipolar ? 0.5 : 0.0;

            thumb.Margin = new Thickness(position * usable, 0, 0, 0);
            var left = (Math.Min(start, position) * usable) + ThumbSize / 2;
            var right = (Math.Max(start, position) * usable) + ThumbSize / 2;
            fill.Margin = new Thickness(left, 0, 0, 0);
            fill.Width = Math.Max(0, right - left);
            fill.Visibility = fill.Width > 0.5 ? Visibility.Visible : Visibility.Collapsed;
            value.Text = Format(current);
        }
        void Set(int next)
        {
            next = spec.Clamp(next);
            if (next == current)
                return;
            current = next;
            _parameters[spec.Parameter] = next;
            Paint();
            ScheduleRender();
        }
        void SetFrom(Point point)
        {
            var usable = Math.Max(1, surface.ActualWidth - ThumbSize);
            var position = Math.Clamp((point.X - ThumbSize / 2) / usable, 0, 1);
            Set((int)Math.Round(spec.Min + position * (spec.Max - spec.Min)));
        }

        surface.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                Set(spec.Default);
                e.Handled = true;
                return;
            }
            surface.CaptureMouse();
            SetFrom(e.GetPosition(surface));
        };
        surface.MouseMove += (_, e) =>
        {
            if (surface.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
                SetFrom(e.GetPosition(surface));
        };
        surface.MouseLeftButtonUp += (_, _) => surface.ReleaseMouseCapture();
        surface.SizeChanged += (_, _) => Paint();
        surface.Loaded += (_, _) => Paint();

        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(header);
        row.Children.Add(surface);
        return row;
    }

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);

    // ---- live preview --------------------------------------------------------------------------

    private void ScheduleRender()
    {
        if (_source is null)
            return;
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>Renders the current parameters on the downsampled copy; stale renders are dropped.</summary>
    private async Task RenderAsync()
    {
        if (_source is not { } source)
            return;

        var version = ++_renderVersion;
        var parameters = _parameters.Clone();
        if (PhotoEdit.IsNeutral(parameters))
        {
            _edited = source.Original;
            SetBusy(false);
            Display();
            return;
        }

        SetBusy(true);
        await _renderGate.WaitAsync();
        try
        {
            if (version != _renderVersion)
                return;

            var bitmap = await Task.Run(() => EditPhotoRenderer.RenderPreview(source.PreviewBytes, parameters));
            if (version != _renderVersion)
                return;

            _edited = bitmap;
            Display();
            SetBusy(false);
        }
        catch (Exception ex)
        {
            ShowFailure(ex.Message, ex);
        }
        finally
        {
            _renderGate.Release();
        }
    }

    private void SetBusy(bool busy)
    {
        if (_busy is not null)
            _busy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- actions -------------------------------------------------------------------------------

    private void Reset()
    {
        _parameters.Reset();
        _pinnedOriginal = false;
        _holding = false;
        _renderVersion++;
        _edited = _source?.Original;
        SetBusy(false);
        ShowGroup();
        Display();
    }

    private Task<string?> ApplyAsync()
    {
        if (_source is null || !EditPhotosTool.Instance.Applies([_item]))
            return Task.FromResult<string?>(null);

        // The final render must not share a parameter object with a slider still being dragged.
        var parameters = _parameters.Clone();
        var suffix = T("tool.editphotos.suffix");
        var directory = OutputDirectory();
        return Task.Run(() => (string?)EditPhotoRenderer.RenderFinal(_item, parameters, suffix, directory));
    }

    /// <summary>Next to the source, or the folder this drop resolved (2.2.3 rules).</summary>
    private string OutputDirectory()
    {
        var detected = OutputTarget.Resolve(_item.Path, _targets?.UnderCursor, Directory.Exists, OutputTarget.CanWrite);
        if (!detected.UsesDetectedFolder && _targets?.Remembered is { } remembered)
        {
            var fallback = OutputTarget.Resolve(_item.Path, remembered, Directory.Exists, OutputTarget.CanWrite);
            if (fallback.UsesDetectedFolder)
                return fallback.Directory!;
        }
        return detected.Directory
            ?? OutputTarget.SourceFolder(_item.Path)
            ?? Environment.CurrentDirectory;
    }
}
