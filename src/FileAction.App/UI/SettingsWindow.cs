using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileAction.App.Diagnostics;
using FileAction.App.Input;
using FileAction.App.Shell;
using FileAction.App.Shell.Protection;
using FileAction.Core;
using FileAction.Core.Updates;
using static FileAction.App.UI.Controls;
using static FileAction.Core.Loc;
using AppThemeMode = FileAction.App.ThemeMode;

namespace FileAction.App.UI;

/// <summary>
/// Settings in six tabs — General, Wheels, Quality, Integration, Updates, About — in the style of the
/// Windows 11 Settings app: sections of rounded cards on the violet page. Every tab scrolls on its own,
/// so the window stays usable at its minimum size and at high display scaling.
/// </summary>
internal sealed class SettingsWindow : Window
{
    /// <summary>The public project page: shown in About and opened by the Source button.</summary>
    private const string RepositoryUrl = "https://github.com/Faisaldhd/dropwheel";

    private static SettingsWindow? _open;

    /// <summary>Set by the app: the operations the wheel editor lists.</summary>
    public static Func<IReadOnlyList<Operation>>? Operations { get; set; }

    /// <summary>Downloads FFmpeg or Pandoc in the app (set by the app).</summary>
    public static Func<ExternalTool, Task<bool>>? DownloadTool { get; set; }

    /// <summary>2.2.0: the update service shown in the Updates tab (set by the app).</summary>
    public static IUpdateService? Updates { get; set; }

    private readonly AppSettings _settings;
    private readonly ToolLocator _tools;
    private readonly Action _changed;
    private readonly List<ScrollViewer> _pages = [];

    /// <summary>The tab host lives inside the custom rounded frame; kept for SelectTab/rebuilds.</summary>
    private TabControl? _tabs;

    /// <summary>The scroll area of the selected tab; <see cref="Rebuild"/> restores its position.</summary>
    private ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private int _tabIndex;

    internal SettingsWindow(AppSettings settings, ToolLocator tools, Action changed)
    {
        _settings = settings;
        _tools = tools;
        _changed = changed;
        Theme.Refresh();
        Title = T("settings.title");
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ResizeMode = ResizeMode.NoResize;
        Background = Brushes.Transparent;
        Width = 700;
        Height = 860;
        MinWidth = 520;
        UiSetup.Configure(this);
        // At 200 % display scaling an 860-DIP window is taller than a 1080p work area.
        UiSetup.FitToWorkArea(this, 700, 860, 520, 360);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = Theme.UiFont;
        Build();
        Closed += (_, _) =>
        {
            _open = null;
        };
    }

    public static void ShowSingle(AppSettings settings, ToolLocator tools, Action changed)
    {
        if (_open is not null)
        {
            _open.Activate();
            return;
        }
        _open = new SettingsWindow(settings, tools, changed);
        _open.Show();
        _open.Activate();
    }

    /// <summary>Redraws the open settings window after the app changed a setting itself.</summary>
    public static void RefreshIfOpen() => _open?.Rebuild();

    /// <summary>Self-test hook: how many tabs the window shows.</summary>
    internal static int TabCount => 6;

    /// <summary>
    /// Self-test hook: selects one tab (0 = General, 1 = Wheels, 2 = Quality, 3 = Integration,
    /// 4 = Updates, 5 = About) so a preview run can capture every page.
    /// </summary>
    internal void SelectTab(int index)
    {
        if (_pages.Count == 0)
            return;
        _tabIndex = Math.Clamp(index, 0, _pages.Count - 1);
        if (_tabs is { } tabs)
            tabs.SelectedIndex = _tabIndex;
        _scroll = _pages[_tabIndex];
    }

    /// <summary>Applies a live language switch: new reading direction, new strings, same window.</summary>
    public static void RelocalizeIfOpen()
    {
        var open = _open;
        if (open is null)
            return;
        LanguageManager.Apply(open);
        open.Rebuild();
    }

    private void Save()
    {
        _settings.Save();
        _changed();
    }

    /// <summary>Rebuilds every tab (used when one change affects others), keeping tab and scroll position.</summary>
    private void Rebuild()
    {
        var offset = _scroll.VerticalOffset;
        Build();
        _scroll.UpdateLayout();
        _scroll.ScrollToVerticalOffset(offset);
    }

    private void Build()
    {
        try
        {
            BuildCore();
        }
        catch (Exception ex)
        {
            // A failed build must never leave the user with an untitled black window. Log the real
            // exception and show a readable fallback so the report can be acted on.
            AppLog.Error("settings window build failed", ex);
            ShowBuildFailure(ex);
        }
    }

    private void ShowBuildFailure(Exception ex)
    {
        Title = "DropWheel – settings";
        Background = Brushes.Transparent;
        FlowDirection = LanguageManager.FlowDirection;
        var panel = new StackPanel { Margin = new Thickness(32, 28, 32, 32), MaxWidth = 720 };
        panel.Children.Add(Text("DropWheel settings could not be drawn.", 20, FontWeights.SemiBold));
        panel.Children.Add(Text(
            "The window hit an unexpected error. Run \"DropWheel.exe --diagnose\" and send the report; " +
            "the details are also in log.txt.", 13, brush: Theme.Muted));
        panel.Children.Add(Text($"{ex.GetType().Name}: {ex.Message}", 12, brush: Theme.Error));
        Content = BuildFrame(new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });
    }

    /// <summary>
    /// The same rounded frame as the floating tools: translucent window, 20 DIP corners, a title bar with
    /// one close control, and a clipped body. Settings has no native title bar so it reads as one product
    /// with the tool windows instead of a stock Windows dialog.
    /// </summary>
    private FrameworkElement BuildFrame(UIElement body)
    {
        var close = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(16),
            Background = Theme.Track,
            Child = new TextBlock
            {
                Text = "\uE711",
                FontFamily = Theme.Icons,
                FontSize = Design.Icon.Inline,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Theme.Text,
            },
            Cursor = Cursors.Hand,
            ToolTip = T("tool.window.close"),
            Focusable = true,
        };
        KeyboardNavigation.SetIsTabStop(close, true);
        close.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            Close();
        };
        close.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space)
            {
                e.Handled = true;
                Close();
            }
        };
        Motion.Interactive(close);

        var title = Text(T("settings.title"), Design.Type.Title, FontWeights.SemiBold);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.VerticalAlignment = VerticalAlignment.Center;

        var bar = new DockPanel
        {
            // WPF only hit-tests a panel where it paints; transparent is required for the empty title
            // space to start DragMove, not just the title glyphs.
            Background = Brushes.Transparent,
            Margin = new Thickness(Design.Gutter, Design.Space.Xl, Design.Gutter, Design.Space.Md),
        };
        // LTR puts Close at the top-right; FlowDirection.RightToLeft mirrors it to the top-left.
        DockPanel.SetDock(close, Dock.Right);
        bar.Children.Add(close);
        bar.Children.Add(title);
        bar.MouseLeftButtonDown += (_, _) =>
        {
            if (close.IsMouseOver)
                return;
            if (Mouse.LeftButton == MouseButtonState.Pressed)
                DragMove();
        };

        var grid = new Grid { ClipToBounds = true };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(bar, 0);
        Grid.SetRow(body, 1);
        grid.Children.Add(bar);
        grid.Children.Add(body);
        grid.SizeChanged += (_, _) =>
        {
            if (grid.ActualWidth <= 0 || grid.ActualHeight <= 0)
                return;
            grid.Clip = new RectangleGeometry(
                new Rect(0, 0, grid.ActualWidth, grid.ActualHeight),
                Design.Radius.Window - 1, Design.Radius.Window - 1);
        };

        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        return new Border
        {
            CornerRadius = new CornerRadius(Design.Radius.Window),
            Background = Theme.Panel,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            // 24 DIP leaves room for the 38 DIP dark halo inside the transparent window bounds.
            Margin = new Thickness(24),
            Effect = WindowShadow(),
            Child = grid,
        };
    }

    /// <summary>
    /// A black drop shadow disappears on the dark page, so dark mode gets a soft violet halo; light mode
    /// keeps the neutral shadow.
    /// </summary>
    private static System.Windows.Media.Effects.DropShadowEffect WindowShadow() => Theme.IsDark
        ? new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 38,
            ShadowDepth = 0,
            Opacity = 0.55,
            Color = Theme.AccentColor,
        }
        : new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 30,
            ShadowDepth = 6,
            Opacity = 0.28,
            Color = Colors.Black,
        };

    private void BuildCore()
    {
        Title = T("settings.title");
        FlowDirection = LanguageManager.FlowDirection;
        Background = Brushes.Transparent;

        var itemStyle = TabItemStyle();
        var tabs = new TabControl
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Style = TabControlStyle(),
        };
        tabs.SelectionChanged += (_, _) =>
        {
            if (tabs.SelectedIndex >= 0 && tabs.SelectedIndex < _pages.Count)
            {
                _tabIndex = tabs.SelectedIndex;
                _scroll = _pages[_tabIndex];
            }
        };

        // Remember the tab before adding items: a TabControl selects its first item while items arrive,
        // which would otherwise jump back to General on every rebuild.
        var selected = _tabIndex;
        _pages.Clear();
        AddTab(tabs, itemStyle, T("settings.tab.general"), GeneralPage());
        AddTab(tabs, itemStyle, T("settings.tab.wheels"), WheelsPage());
        AddTab(tabs, itemStyle, T("settings.tab.quality"), QualityPage());
        AddTab(tabs, itemStyle, T("settings.tab.integration"), IntegrationPage());
        AddTab(tabs, itemStyle, T("settings.tab.updates"), UpdatesPage());
        AddTab(tabs, itemStyle, T("settings.tab.about"), AboutPage());

        _tabIndex = Math.Clamp(selected, 0, _pages.Count - 1);
        tabs.SelectedIndex = _tabIndex;
        _scroll = _pages[_tabIndex];
        _tabs = tabs;
        Content = BuildFrame(tabs);
    }

    private void AddTab(TabControl tabs, Style itemStyle, string header, ScrollViewer page)
    {
        tabs.Items.Add(new TabItem { Header = header, Content = page, Style = itemStyle });
        _pages.Add(page);
    }

    /// <summary>A tab body: the card column with page margins, scrolling inside the tab.</summary>
    private static StackPanel PageContent() => new() { Margin = new Thickness(32, 16, 32, 32) };

    private static ScrollViewer TabPage(UIElement content) => new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Background = Brushes.Transparent,
        Content = content,
    };

    private ScrollViewer GeneralPage()
    {
        var page = PageContent();

        // Company-safe default (R-20): first card, because it decides whether drag gestures work at all.
        var hooksLocked = Shell.Policy.HooksDisabled;
        page.Children.Add(Card(
            T("settings.general.watch.title"),
            hooksLocked ? T("settings.general.watch.policy") : T("settings.general.watch.detail"),
            hooksLocked
                ? LockedToggle(false)
                : Toggle(_settings.WatchGestures, value =>
                {
                    _settings.WatchGestures = value;
                    Save();
                    Rebuild();
                }),
            glyph: "\uE7C9"));

        page.Children.Add(SectionHeader(T("settings.language.section")));
        page.Children.Add(Card(T("settings.language.app"),
            T("settings.language.detail"),
            Segmented<UiLanguage>(
                [
                    (T("settings.language.system"), UiLanguage.System),
                    (T("language.arabic"), UiLanguage.Arabic),
                    (T("language.english"), UiLanguage.English),
                ],
                _settings.Language,
                value =>
                {
                    _settings.Language = value;
                    // Save calls the app, which applies the direction to every open window and
                    // rebuilds this one through RelocalizeIfOpen.
                    Save();
                }),
            glyph: "\uE774"));

        page.Children.Add(SectionHeader(T("settings.general.theme.title")));
        page.Children.Add(SectionDetail(T("settings.general.theme.detail")));
        // Window.ThemeMode (WPF) shadows the app's enum name inside a Window, so the alias above is used.
        var theme = Segmented<AppThemeMode>(
            [
                (T("settings.theme.system"), AppThemeMode.System),
                (T("settings.theme.dark"), AppThemeMode.Dark),
                (T("settings.theme.light"), AppThemeMode.Light),
            ],
            _settings.Theme,
            value =>
            {
                _settings.Theme = value;
                Theme.Apply(_settings.Theme);
                Save();
                Rebuild();
            });
        theme.HorizontalAlignment = HorizontalAlignment.Left;
        theme.Margin = new Thickness(2, 0, 0, 6);
        page.Children.Add(theme);

        // 2.4.0 motion: one switch for every transition in the app. Windows' own animation setting is
        // honoured on top of it; the description says so instead of silently ignoring the toggle.
        page.Children.Add(Card(
            T("settings.motion.title"),
            Motion.SystemAnimationsAvailable ? T("settings.motion.detail") : T("settings.motion.windowsOff"),
            Toggle(_settings.Animations, value =>
            {
                _settings.Animations = value;
                Motion.SetSettingsEnabled(value);
                Save();
            }),
            glyph: "\uE945"));

        page.Children.Add(SectionHeader(T("settings.general.gestures.title")));
        page.Children.Add(SectionDetail(T("settings.general.gestures.detail")));
        foreach (var (wheel, description) in new[]
                 {
                     (WheelKind.Convert, T("settings.gesture.convert")),
                     (WheelKind.Tools, T("settings.gesture.tools")),
                     (WheelKind.Actions, T("settings.gesture.actions")),
                 })
        {
            page.Children.Add(Card(AppSettings.WheelName(wheel), description, KeyChooser(wheel)));
        }

        page.Children.Add(SectionHeader(T("settings.results.section")));
        var folder = Text(Bidi.Value(_settings.OutputDirectory ?? ""), 12, brush: Theme.Muted, wrap: false);
        var folderRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        folderRow.Children.Add(Button(T("settings.results.chooseFolder"), PickFolder, glyph: Glyphs.Folder));
        folder.Margin = new Thickness(12, 0, 0, 0);
        folder.VerticalAlignment = VerticalAlignment.Center;
        folderRow.Children.Add(folder);
        var toFolder = !string.IsNullOrWhiteSpace(_settings.OutputDirectory);
        var saveOptions = new StackPanel();
        saveOptions.Children.Add(Wrap(Segmented(
            [(T("settings.results.nextToOriginal"), false), (T("settings.results.toFolder"), true)],
            toFolder,
            value =>
            {
                if (value)
                {
                    PickFolder();
                }
                else
                {
                    _settings.OutputDirectory = null;
                    Save();
                    Rebuild();
                }
            })));
        if (toFolder)
            saveOptions.Children.Add(folderRow);
        page.Children.Add(Card(T("settings.results.saveTitle"), T("settings.results.saveDetail"), null, saveOptions, Glyphs.Folder));
        page.Children.Add(Card(T("settings.results.reveal"), null,
            Toggle(_settings.RevealResult, value =>
            {
                _settings.RevealResult = value;
                Save();
            })));

        page.Children.Add(SectionHeader(T("settings.protection.section")));
        page.Children.Add(Card(T("settings.protection.title"), T("settings.protection.detail"),
            Button(T("settings.protection.open"), LockWiring.Settings)));
        return TabPage(page);
    }

    private ScrollViewer WheelsPage()
    {
        var page = PageContent();

        // A short pointer to General: the three key choosers live there, so they are not duplicated here.
        page.Children.Add(SectionDetail(T("settings.general.gestures.detail")));

        page.Children.Add(Card(T("settings.wheels.customize.title"),
            T("settings.wheels.customize.detail"),
            Button(T("settings.wheels.customize.button"), () =>
            {
                if (Operations is not null)
                    WheelEditorWindow.ShowSingle(_settings, Operations(), _changed);
            }, glyph: "\uE70F"),
            glyph: "\uE8EE"));
        page.Children.Add(Card(T("settings.gesture.pause.title"), T("settings.gesture.pause.detail"),
            Toggle(_settings.Paused, value =>
            {
                _settings.Paused = value;
                Save();
            })));

        page.Children.Add(SectionHeader(T("settings.keyboard.section")));
        page.Children.Add(Card(T("settings.keyboard.shortcut.title"),
            T("settings.keyboard.shortcut.detail"),
            null,
            below: Wrap(Segmented(
                Enum.GetValues<HotkeyPreset>().Where(p => p != HotkeyPreset.None).Append(HotkeyPreset.None)
                    .Select(p => (AppSettings.HotkeyName(p), p)).ToList(),
                _settings.Hotkey,
                value =>
                {
                    _settings.Hotkey = value;
                    Save();
                })),
            glyph: "\uE765"));

        return TabPage(page);
    }

    private ScrollViewer QualityPage()
    {
        var page = PageContent();
        page.Children.Add(Card(T("settings.quality.images.title"), T("settings.quality.images.detail"),
            Slider(50, 100, _settings.Quality.JpegQuality, v => _settings.Quality.JpegQuality = v, Save), glyph: Glyphs.Photo));
        page.Children.Add(Card(T("settings.quality.compress.title"), T("settings.quality.compress.detail"),
            Slider(30, 95, _settings.Quality.CompressQuality, v => _settings.Quality.CompressQuality = v, Save), glyph: Glyphs.Shrink));
        page.Children.Add(Card(T("settings.quality.pdf.title"), T("settings.quality.pdf.detail"),
            Slider(72, 600, _settings.Quality.PdfDpi, v => _settings.Quality.PdfDpi = v, Save), glyph: Glyphs.Page));
        return TabPage(page);
    }

    private ScrollViewer IntegrationPage()
    {
        var page = PageContent();

        // R-20/R-31: company-safe switches. Hooks and downloads default to OFF and IT can lock them;
        // nothing is registered automatically, every integration is opt-in.
        page.Children.Add(SectionHeader(T("settings.company.section")));
        page.Children.Add(Card(Loc.T("settings.company.autostart.title"),
            Loc.T("settings.company.autostart.detail"),
            Toggle(Shell.ShellIntegration.IsAutostartEnabled, value =>
            {
                Shell.ShellIntegration.SetAutostart(value);
                Save();
            }),
            glyph: "\uE7E8"));
        page.Children.Add(Card(Loc.T("settings.company.contextMenu.title"),
            Loc.T("settings.company.contextMenu.detail"),
            Toggle(Shell.ShellIntegration.IsContextMenuEnabled, value =>
            {
                Shell.ShellIntegration.SetContextMenu(value);
                Save();
            }),
            glyph: "\uE8A7"));
        page.Children.Add(Card(Loc.T("settings.company.sendTo.title"),
            Loc.T("settings.company.sendTo.detail"),
            Toggle(Shell.ShellIntegration.IsSendToEnabled, value =>
            {
                Shell.ShellIntegration.SetSendTo(value);
                Save();
                Rebuild();
            }),
            glyph: "\uE8A7"));

        var downloadsLocked = Shell.Policy.DownloadsDisabled;
        page.Children.Add(Card(Loc.T("settings.company.downloads.title"),
            downloadsLocked
                ? T("settings.company.policy.disabled")
                : Loc.T("settings.company.downloads.detail"),
            downloadsLocked
                ? LockedToggle(false)
                : Toggle(_settings.AutoDownloadTools, value =>
                {
                    _settings.AutoDownloadTools = value;
                    Save();
                    Rebuild();
                }),
            glyph: "\uE896"));

        page.Children.Add(SectionHeader(T("settings.tools.section")));
        page.Children.Add(Card(T("settings.tools.builtIn.title"),
            T("settings.tools.builtIn.detail"),
            Button(T("settings.tools.checkAgain"), () =>
            {
                _tools.Refresh();
                Rebuild();
            }, glyph: "\uE72C"),
            glyph: "\uE90F"));
        foreach (var tool in Enum.GetValues<ExternalTool>())
            page.Children.Add(ToolCard(tool));

        return TabPage(page);
    }

    private ScrollViewer UpdatesPage()
    {
        var page = PageContent();
        page.Children.Add(SectionHeader(T("settings.updates.title")));

        // The banner owns the automatic-check switch and every check/download state; never add a second one.
        if (Updates is { } updates)
        {
            page.Children.Add(SectionDetail(T("settings.updates.detail")));
            var version = typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "2.4.1";
            var current = Text(T("settings.updates.current", version), Design.Type.Caption, brush: Theme.Muted);
            current.Margin = new Thickness(Design.Space.Xs, Design.Space.Md, 0, Design.Space.Lg);
            page.Children.Add(current);
            page.Children.Add(UpdateBanner.Create(updates, _settings, Save));
        }
        else
        {
            page.Children.Add(SectionDetail(T("settings.updates.unavailable")));
        }

        return TabPage(page);
    }

    private ScrollViewer AboutPage()
    {
        var page = PageContent();
        page.Children.Add(Header(T("settings.about.tagline")));

        var version = typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "2.2.0";
        var aboutVersion = T("settings.about.version", version);
        if (!aboutVersion.Contains(version, StringComparison.Ordinal))
            aboutVersion = aboutVersion + " " + Bidi.Value(version);
        var versionLine = Text(aboutVersion, 13.5, brush: Theme.Muted);
        versionLine.Margin = new Thickness(2, 12, 0, 0);
        page.Children.Add(versionLine);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 20, 0, 0) };
        actions.Children.Add(Button(T("settings.about.licenses"), OpenLicenses, primary: true, glyph: Glyphs.Document));
        var source = Button(T("settings.about.source"), OpenRepository, glyph: Glyphs.Link);
        source.Margin = new Thickness(10, 0, 0, 0);
        source.ToolTip = UiSetup.Tip(RepositoryUrl);
        actions.Children.Add(source);
        page.Children.Add(actions);

        var website = new TextBlock
        {
            FontSize = 12.5,
            TextWrapping = TextWrapping.NoWrap,
            Margin = new Thickness(2, 14, 0, 0),
        };
        var websiteText = T("settings.about.website");
        if (!websiteText.Contains("http", StringComparison.OrdinalIgnoreCase))
            websiteText = websiteText + " · " + Bidi.Value(RepositoryUrl);
        // Fully qualified: System.Windows.Documents.Glyphs would clash with FileAction.Core.Glyphs.
        var websiteLink = new System.Windows.Documents.Hyperlink { Foreground = Theme.Accent, TextDecorations = null };
        websiteLink.Inlines.Add(new System.Windows.Documents.Run(websiteText));
        websiteLink.Click += (_, _) => OpenRepository();
        website.Inlines.Add(websiteLink);
        page.Children.Add(website);

        var credits = Text(T("settings.about.credits"), 12.5, brush: Theme.Muted);
        credits.Margin = new Thickness(2, 18, 0, 0);
        page.Children.Add(credits);

        var footer = Text(T("settings.footer"), 12, brush: Theme.Muted);
        footer.Margin = new Thickness(2, 26, 0, 0);
        page.Children.Add(footer);

        return TabPage(page);
    }

    /// <summary>Tab header pills: violet fill for the selected tab, hover tint otherwise.</summary>
    private static Style TabItemStyle()
    {
        var style = new Style(typeof(TabItem));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Theme.Muted));
        style.Setters.Add(new Setter(Control.FontFamilyProperty, Theme.UiFont));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 13.5));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Normal));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 6, 14, 7)));
        style.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0, 0, 4, 0)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        style.Setters.Add(new Setter(Control.TemplateProperty, TabItemTemplate()));

        // The selected trigger comes last so it wins over the hover tint above.
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, Theme.Hover));
        style.Triggers.Add(hover);
        var selected = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, Theme.Accent));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, Theme.TextOnAccent));
        selected.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        style.Triggers.Add(selected);
        return style;
    }

    private static ControlTemplate TabItemTemplate()
    {
        var chrome = new FrameworkElementFactory(typeof(Border));
        chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        chrome.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        chrome.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var header = new FrameworkElementFactory(typeof(ContentPresenter));
        header.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        chrome.AppendChild(header);
        return new ControlTemplate(typeof(TabItem)) { VisualTree = chrome };
    }

    /// <summary>The tab strip on a transparent page background, with the selected page underneath.</summary>
    private static Style TabControlStyle()
    {
        var style = new Style(typeof(TabControl));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.TemplateProperty, TabControlTemplate()));
        return style;
    }

    private static ControlTemplate TabControlTemplate()
    {
        var dock = new FrameworkElementFactory(typeof(DockPanel));
        dock.SetValue(DockPanel.LastChildFillProperty, true);

        var strip = new FrameworkElementFactory(typeof(Border));
        strip.SetValue(DockPanel.DockProperty, Dock.Top);
        strip.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        strip.SetValue(Border.BorderBrushProperty, Theme.PanelBorder);
        strip.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
        strip.SetValue(Border.PaddingProperty, new Thickness(32, 10, 32, 0));

        var headers = new FrameworkElementFactory(typeof(TabPanel));
        headers.SetValue(Panel.IsItemsHostProperty, true);
        strip.AppendChild(headers);
        dock.AppendChild(strip);

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.Name = "PART_SelectedContentHost";
        content.SetValue(ContentPresenter.ContentSourceProperty, "SelectedContent");
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        dock.AppendChild(content);

        return new ControlTemplate(typeof(TabControl)) { VisualTree = dock };
    }

    private static TextBlock SectionDetail(string text)
    {
        var detail = Text(text, 12.5, brush: Theme.Muted);
        detail.Margin = new Thickness(2, 0, 0, 10);
        return detail;
    }

    /// <summary>A toggle shown greyed out and non-interactive where a machine policy locks the setting.</summary>
    private static FrameworkElement LockedToggle(bool value)
    {
        var toggle = Toggle(value, _ => { });
        toggle.IsHitTestVisible = false;
        toggle.Opacity = 0.45;
        return toggle;
    }

    private static FrameworkElement Wrap(FrameworkElement element)
    {
        element.HorizontalAlignment = HorizontalAlignment.Left;
        element.Margin = new Thickness(0, 12, 0, 0);
        return element;
    }

    private UIElement Header(string tagline)
    {
        var panel = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var image = new Image
        {
            Width = 56,
            Height = 56,
            Margin = new Thickness(0, 0, 18, 0),
        };
        try
        {
            image.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/DropWheel.png"));
        }
        catch (Exception ex)
        {
            // A missing image must not take the whole settings window down (and must not leave it black).
            AppLog.Error("settings header image could not be loaded", ex);
        }
        panel.Children.Add(image);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = Text("DropWheel", 28, FontWeights.SemiBold);
        title.FontFamily = Theme.DisplayFont;
        text.Children.Add(title);
        text.Children.Add(Text(tagline, 13.5, brush: Theme.Muted));
        panel.Children.Add(text);
        return panel;
    }

    private Border KeyChooser(WheelKind wheel) => Segmented(
        [("Shift", ModifierKey.Shift), ("Ctrl", ModifierKey.Ctrl), ("Alt", ModifierKey.Alt), (T("settings.gesture.off"), ModifierKey.None)],
        _settings.KeyFor(wheel),
        key =>
        {
            var old = _settings.KeyFor(wheel);
            // Keep keys unique: the wheel that had this key gets the old one.
            foreach (var other in new[] { WheelKind.Convert, WheelKind.Tools, WheelKind.Actions })
            {
                if (other != wheel && key != ModifierKey.None && _settings.KeyFor(other) == key)
                    SetKey(other, old);
            }
            SetKey(wheel, key);
            Save();
            Rebuild();
        });

    private void SetKey(WheelKind wheel, ModifierKey key)
    {
        switch (wheel)
        {
            case WheelKind.Convert:
                _settings.ConvertKey = key;
                break;
            case WheelKind.Tools:
                _settings.ToolsKey = key;
                break;
            default:
                _settings.ActionsKey = key;
                break;
        }
    }

    private void PickFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = T("settings.outputFolder.dialogTitle") };
        if (dialog.ShowDialog(this) == true)
        {
            _settings.OutputDirectory = dialog.FolderName;
            Save();
        }
        Rebuild();
    }

    private static void OpenLicenses()
    {
        try
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "licenses");
            if (!Directory.Exists(folder))
                folder = AppContext.BaseDirectory;
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
        }
    }

    private static void OpenRepository()
    {
        try
        {
            Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
        }
    }

    private Border ToolCard(ExternalTool tool)
    {
        var path = _tools.Find(tool);
        var description = path is not null
            ? Bidi.Value(path)
            : (tool switch
            {
                ExternalTool.Ffmpeg => T("settings.tools.audioVideo"),
                ExternalTool.Pandoc => T("settings.tools.documents"),
                _ => T("settings.tools.office"),
            });
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(Badge(path is null ? T("settings.tools.missing") : T("settings.tools.found"), path is not null));
        if (path is null)
        {
            var id = tool switch
            {
                ExternalTool.Ffmpeg => "Gyan.FFmpeg",
                ExternalTool.Pandoc => "JohnMacFarlane.Pandoc",
                _ => "TheDocumentFoundation.LibreOffice",
            };
            var downloadSize = Bidi.Value(Shell.ToolInstaller.DownloadSize(tool));
            var install = Shell.ToolInstaller.CanInstall(tool) && DownloadTool is not null
                ? Button(T("settings.tools.download", downloadSize), async () =>
                {
                    if (await DownloadTool(tool))
                        Rebuild();
                }, primary: true, glyph: "\uE896")
                : Button(T("settings.tools.install"), () =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo("cmd.exe",
                        $"/c winget install -e --id {id} --accept-source-agreements --accept-package-agreements & echo. & echo {T("settings.tools.installDone")} & pause")
                    { UseShellExecute = true });
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    Dialogs.Show(this, ex.Message, "DropWheel", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }, primary: true, glyph: "\uE896");
            install.Margin = new Thickness(10, 0, 0, 0);
            right.Children.Add(install);
        }
        return Card(ToolLocator.DisplayName(tool), description, right);
    }
}
