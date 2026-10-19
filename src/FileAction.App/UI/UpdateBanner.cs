using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FileAction.App.Diagnostics;
using FileAction.App.Updates;
using FileAction.Core.Updates;
using static FileAction.App.UI.Controls;
using static FileAction.Core.Loc;

namespace FileAction.App.UI;

/// <summary>
/// The violet update panel embedded in the Settings Updates tab. It shows the "Update available" strip,
/// the automatic-check switch, the manual check button and the download progress with a Cancel button.
/// The app supplies the service; a null service (the self-test builds the window without one) renders a
/// disabled "updates are not available in this build" state instead of throwing.
///
/// Everything is built from the shared <see cref="Controls"/> helpers and the current <see cref="Theme"/>
/// brushes, so a live language switch (via <see cref="RelocalizeAll"/>) and a light/dark switch both
/// re-render correctly. Network failures never surface: the service reports them through
/// <see cref="IUpdateService.LastError"/> and the UI stays put.
/// </summary>
internal sealed class UpdateBanner : Border
{
    /// <summary>Banners currently in a visual tree, so a live language switch can re-render them.</summary>
    private static readonly List<WeakReference<UpdateBanner>> LiveBanners = [];

    private enum Notice
    {
        None,
        Checking,
        UpToDate,
        CheckFailed,
        DownloadFailed,
    }

    private readonly IUpdateService? _service;
    private readonly AppSettings _settings;
    private readonly Action? _changed;
    private readonly StackPanel _content = new();

    private Grid? _progressBar;
    private Border? _progressFill;
    private TextBlock? _progressLabel;
    private bool _checking;
    private Notice _notice;
    private string? _error;
    private Version? _upToDate;
    private CancellationTokenSource? _download;
    private double _progress;
    private bool _subscribed;

    private UpdateBanner(IUpdateService? service, AppSettings settings, Action? changed)
    {
        _service = service;
        _settings = settings;
        _changed = changed;

        Background = Theme.AccentSoft;
        BorderBrush = Theme.Accent;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(10);
        Padding = new Thickness(16, 14, 16, 14);
        Margin = new Thickness(0, 0, 0, 8);
        Child = _content;

        if (_service is not null)
        {
            _service.StateChanged += OnServiceStateChanged;
            _subscribed = true;
        }

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Render();
    }

    /// <summary>Builds the banner for the Settings Updates tab. A null service means "updates unavailable".</summary>
    public static UpdateBanner Create(IUpdateService? service, AppSettings settings, Action? changed = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new UpdateBanner(service, settings, changed);
    }

    /// <summary>Re-renders the banner after a state change, a theme switch or a language switch.</summary>
    public void Refresh()
    {
        if (!Dispatcher.CheckAccess())
        {
            try
            {
                Dispatcher.BeginInvoke((Action)Refresh);
            }
            catch (InvalidOperationException)
            {
                // The dispatcher is shutting down; nothing left to render.
            }
            return;
        }

        FlowDirection = LanguageManager.FlowDirection;
        Render();
    }

    /// <summary>Relocalizes every banner that is currently in a window.</summary>
    public static void RelocalizeAll()
    {
        foreach (var reference in LiveBanners.ToArray())
        {
            if (reference.TryGetTarget(out var banner))
                banner.Refresh();
            else
                LiveBanners.Remove(reference);
        }
    }

    private void Render()
    {
        FlowDirection = LanguageManager.FlowDirection;
        _progressBar = null;
        _progressFill = null;
        _progressLabel = null;
        _content.Children.Clear();
        Opacity = 1;

        if (_service is null)
        {
            // Only the self-test and a build without the updater get here; keep it calm and explain.
            _content.Children.Add(Text(T("settings.updates.unavailable"), 14, FontWeights.SemiBold));
            Opacity = 0.65;
            return;
        }

        var info = _service.Available;
        if (info is not null)
            _content.Children.Add(AvailableStrip(info));

        var status = StatusLine();
        if (status is not null)
            _content.Children.Add(status);

        if (_download is not null)
            _content.Children.Add(ProgressRow());

        _content.Children.Add(OptionsRow());
    }

    /// <summary>The violet "Update available: x.y.z" strip with the primary button and the release link.</summary>
    private UIElement AvailableStrip(UpdateInfo info)
    {
        var title = Text(T("settings.updates.available", Bidi.Value(info.Version.ToString(3))), 15, FontWeights.SemiBold);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (_download is null && !_service!.IsDownloading)
        {
            var update = Button(T("settings.updates.updateNow"), DownloadNow, primary: true, glyph: "\uE896");
            update.Margin = new Thickness(0, 0, 10, 0);
            actions.Children.Add(update);
        }
        actions.Children.Add(WhatsNew(info.ReleasePage));

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(title);
        Grid.SetColumn(actions, 1);
        row.Children.Add(actions);
        return row;
    }

    private static UIElement WhatsNew(Uri releasePage)
    {
        var link = new TextBlock
        {
            Text = T("settings.updates.whatsNew"),
            FontSize = 12.5,
            Foreground = Theme.Accent,
            Cursor = Cursors.Hand,
            TextDecorations = TextDecorations.Underline,
            VerticalAlignment = VerticalAlignment.Center,
        };
        link.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Launch(releasePage);
        };
        return link;
    }

    private static void Launch(Uri uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            AppLog.Error($"could not open {uri.AbsoluteUri}", ex);
        }
    }

    /// <summary>The one-line state under the strip: checking, up to date, or the last user-visible error.</summary>
    private UIElement? StatusLine()
    {
        TextBlock message;
        switch (_notice)
        {
            case Notice.Checking:
                message = Text(T("settings.updates.checking"), 12.5, brush: Theme.Muted);
                break;
            case Notice.UpToDate:
                message = Text(
                    T("settings.updates.upToDate", Bidi.Value(_upToDate?.ToString(3) ?? "")), 12.5, brush: Theme.Muted);
                break;
            case Notice.CheckFailed:
                message = Text(_error is null
                    ? T("update.check.failed")
                    : T("settings.updates.failed", Bidi.Value(_error)), 12.5, brush: Theme.Error);
                break;
            case Notice.DownloadFailed:
                message = Text(_error is null
                    ? T("update.download.failed")
                    : T("update.download.failed") + Environment.NewLine + Bidi.Value(_error), 12.5, brush: Theme.Error);
                break;
            default:
                return null;
        }
        message.Margin = new Thickness(0, 10, 0, 0);
        return message;
    }

    /// <summary>"Downloading… 42 %" with a violet bar and the Cancel button.</summary>
    private UIElement ProgressRow()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };

        _progressFill = new Border
        {
            CornerRadius = new CornerRadius(2.5),
            Background = Theme.AccentGradient,
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 0,
        };
        _progressBar = new Grid { Height = 5, Margin = new Thickness(0, 8, 0, 0) };
        _progressBar.Children.Add(new Border { CornerRadius = new CornerRadius(2.5), Background = Theme.Track });
        _progressBar.Children.Add(_progressFill);
        _progressBar.SizeChanged += (_, _) => PaintProgress();
        PaintProgress();

        _progressLabel = Text(
            T("settings.updates.downloading", Bidi.Number((int)Math.Round(_progress * 100))), 12.5);
        stack.Children.Add(_progressLabel);
        stack.Children.Add(_progressBar);

        var cancel = Button(T("update.download.cancel"), () => _download?.Cancel(), glyph: "\uE711");
        cancel.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(cancel);
        return stack;
    }

    /// <summary>The automatic-check switch on the left and the manual check button on the right.</summary>
    private UIElement OptionsRow()
    {
        var auto = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        auto.Children.Add(Toggle(_settings.CheckForUpdates, OnAutomaticChanged));

        var labels = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(Text(T("settings.updates.automatic.title"), 13));
        labels.Children.Add(Text(T("settings.updates.automatic.detail"), 11.5, brush: Theme.Muted));
        auto.Children.Add(labels);

        var busy = _checking || _download is not null;
        var check = Button(
            _checking ? T("settings.updates.checking") : T("settings.updates.check"),
            CheckNow,
            glyph: "\uE72C");
        check.Margin = new Thickness(12, 0, 0, 0);
        if (busy)
        {
            // A second check would race the first one; download and check stay mutually exclusive.
            check.IsEnabled = false;
            check.Opacity = 0.55;
        }

        var row = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(auto);
        Grid.SetColumn(check, 1);
        row.Children.Add(check);
        return row;
    }

    private void OnAutomaticChanged(bool value)
    {
        if (_settings.CheckForUpdates == value)
            return;
        _settings.CheckForUpdates = value;
        AppLog.Info($"automatic update checks: {(value ? "on" : "off")}");
        // The app persists the setting and starts/stops the background timer.
        _changed?.Invoke();
        Refresh();
    }

    /// <summary>User-initiated check. Never throws: offline is just <see cref="UpdateCheckStatus.Failed"/>.</summary>
    private async void CheckNow()
    {
        if (_service is null || _checking || _download is not null)
            return;

        _checking = true;
        _notice = Notice.Checking;
        _error = null;
        _upToDate = null;
        Refresh();

        UpdateCheckStatus status;
        try
        {
            status = await _service.CheckAsync(true);
        }
        catch (Exception ex)
        {
            // The contract says this never throws; stay on the safe side so a bug cannot crash the app.
            AppLog.Error("update check failed", ex);
            _error = ex.Message;
            status = UpdateCheckStatus.Failed;
        }

        _checking = false;
        switch (status)
        {
            case UpdateCheckStatus.UpToDate:
                _upToDate = _service.CurrentVersion;
                _notice = Notice.UpToDate;
                break;
            case UpdateCheckStatus.Available:
                _notice = Notice.None;
                break;
            default:
                _error ??= _service.LastError;
                _notice = Notice.CheckFailed;
                break;
        }

        AppLog.Info($"update check finished: {status}");
        Refresh();
    }

    /// <summary>Downloads, verifies and applies the update; the app exits on success.</summary>
    private async void DownloadNow()
    {
        var info = _service?.Available;
        if (_service is null || info is null || _download is not null || _service.IsDownloading)
            return;

        var download = new CancellationTokenSource();
        _download = download;
        _progress = 0;
        _error = null;
        _notice = Notice.None;
        AppLog.Info($"downloading update {info.Version}");
        Refresh();

        try
        {
            var progress = new Progress<double>(ShowProgress);
            var result = await UpdateApplier.DownloadAndApplyAsync(_service, info, progress, download.Token);
            if (result.Success)
            {
                // The applier starts the installer/helper; this process has to go away for it to replace files.
                AppLog.Info("update verified; closing DropWheel for the updater");
                _download = null;
                download.Dispose();
                Application.Current?.Shutdown();
                return;
            }
            if (!download.IsCancellationRequested)
            {
                _error = result.Error;
                _notice = Notice.DownloadFailed;
                AppLog.Warn($"update download/apply failed: {result.Error ?? "no details"}");
            }
        }
        catch (OperationCanceledException)
        {
            AppLog.Info("update download cancelled by the user");
        }
        catch (Exception ex)
        {
            AppLog.Error("update download failed", ex);
            _error = ex.Message;
            _notice = Notice.DownloadFailed;
        }
        finally
        {
            if (ReferenceEquals(_download, download))
            {
                _download = null;
                download.Dispose();
            }
        }

        Refresh();
    }

    private void ShowProgress(double value)
    {
        if (!Dispatcher.CheckAccess())
        {
            try
            {
                Dispatcher.BeginInvoke((Action)(() => ShowProgress(value)));
            }
            catch (InvalidOperationException)
            {
            }
            return;
        }

        _progress = Math.Clamp(value, 0, 1);
        if (_progressLabel is not null)
            _progressLabel.Text = T("settings.updates.downloading", Bidi.Number((int)Math.Round(_progress * 100)));
        PaintProgress();
    }

    private void PaintProgress()
    {
        if (_progressBar is not null && _progressFill is not null)
            _progressFill.Width = Math.Max(0, _progressBar.ActualWidth) * _progress;
    }

    private void OnServiceStateChanged()
    {
        // Background checks raise this from a timer thread; the banner only touches WPF on the UI thread.
        if (!Dispatcher.CheckAccess())
        {
            try
            {
                Dispatcher.BeginInvoke((Action)Refresh);
            }
            catch (InvalidOperationException)
            {
            }
            return;
        }
        Refresh();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LiveBanners.RemoveAll(reference => !reference.TryGetTarget(out _));
        if (!LiveBanners.Any(reference => reference.TryGetTarget(out var banner) && ReferenceEquals(banner, this)))
            LiveBanners.Add(new WeakReference<UpdateBanner>(this));
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // The settings window rebuilds its cards on a language/theme switch; stop listening, but let a
        // running download finish (the applier owns it and the app exits on success).
        if (_subscribed && _service is not null)
        {
            _service.StateChanged -= OnServiceStateChanged;
            _subscribed = false;
        }
        LiveBanners.RemoveAll(reference =>
            reference.TryGetTarget(out var banner) && ReferenceEquals(banner, this));
    }
}
