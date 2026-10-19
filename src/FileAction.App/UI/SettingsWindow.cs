using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using FileAction.App.Input;
using FileAction.App.Shell;
using FileAction.Core;
using static FileAction.App.UI.Controls;
using static FileAction.Core.Loc;

namespace FileAction.App.UI;

/// <summary>Settings in the style of the Windows 11 Settings app: sections of rounded cards.</summary>
internal sealed class SettingsWindow : Window
{
    private static SettingsWindow? _open;

    /// <summary>Set by the app: the update checker and the operations the wheel editor lists.</summary>

    public static Func<IReadOnlyList<Operation>>? Operations { get; set; }

    /// <summary>Downloads FFmpeg or Pandoc in the app (set by the app).</summary>
    public static Func<ExternalTool, Task<bool>>? DownloadTool { get; set; }
    private readonly AppSettings _settings;
    private readonly ToolLocator _tools;
    private readonly Action _changed;
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

    internal SettingsWindow(AppSettings settings, ToolLocator tools, Action changed)
    {
        _settings = settings;
        _tools = tools;
        _changed = changed;
        Theme.Refresh();
        Title = T("settings.title");
        Width = 680;
        Height = 820;
        MinWidth = 520;
        UiSetup.Configure(this);
        // At 200 % display scaling an 820-DIP window is taller than a 1080p work area.
        UiSetup.FitToWorkArea(this, 680, 820, 520, 360);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Theme.Page;
        FontFamily = Theme.UiFont;
        Content = _scroll;
        Build();
        Action refresh = () => Dispatcher.BeginInvoke(Rebuild);
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

    /// <summary>Rebuilds every card (used when one change affects others), keeping the scroll position.</summary>
    private void Rebuild()
    {
        var offset = _scroll.VerticalOffset;
        Build();
        _scroll.UpdateLayout();
        _scroll.ScrollToVerticalOffset(offset);
    }

    private void Build()
    {
        Title = T("settings.title");
        FlowDirection = LanguageManager.FlowDirection;
        var root = new StackPanel { Margin = new Thickness(32, 28, 32, 32), MaxWidth = 720 };
        root.Children.Add(Header());

        root.Children.Add(SectionHeader(T("settings.language.section")));
        root.Children.Add(Card(T("settings.language.app"),
            T("settings.language.detail"),
            Segmented<UiLanguage>(
                [(T("settings.system.section"), UiLanguage.System), ("العربية", UiLanguage.Arabic), ("English", UiLanguage.English)],
                _settings.Language,
                value =>
                {
                    _settings.Language = value;
                    // Save calls the app, which applies the direction to every open window and
                    // rebuilds this one through RelocalizeIfOpen.
                    Save();
                }),
            glyph: "\uE774"));

        root.Children.Add(SectionHeader(T("settings.gesture.section")));
        root.Children.Add(Card(
            T("settings.gesture.lead"),
            T("settings.gesture.detail"),
            null, glyph: Glyphs.Extract));
        foreach (var (wheel, description) in new[]
                 {
                     (WheelKind.Convert, T("settings.gesture.convert")),
                     (WheelKind.Tools, T("settings.gesture.tools")),
                     (WheelKind.Actions, T("settings.gesture.actions")),
                 })
        {
            root.Children.Add(Card(AppSettings.WheelName(wheel), description, KeyChooser(wheel)));
        }
        root.Children.Add(Card(T("settings.wheels.customize.title"),
            T("settings.wheels.customize.detail"),
            Button(T("settings.wheels.customize.button"), () =>
            {
                if (Operations is not null)
                    WheelEditorWindow.ShowSingle(_settings, Operations(), _changed);
            }, glyph: "\uE70F"),
            glyph: "\uE8EE"));
        root.Children.Add(Card(T("settings.gesture.pause.title"), T("settings.gesture.pause.detail"),
            Toggle(_settings.Paused, v =>
            {
                _settings.Paused = v;
                Save();
            })));

        root.Children.Add(SectionHeader(T("settings.keyboard.section")));
        root.Children.Add(Card(T("settings.keyboard.shortcut.title"),
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

        root.Children.Add(SectionHeader(T("settings.results.section")));
        var folderLine = Text(Bidi.Value(_settings.OutputDirectory ?? ""), 12, brush: Theme.Muted, wrap: false);
        var folderRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        folderRow.Children.Add(Button(T("settings.results.chooseFolder"), PickFolder, glyph: Glyphs.Folder));
        folderLine.Margin = new Thickness(12, 0, 0, 0);
        folderLine.VerticalAlignment = VerticalAlignment.Center;
        folderRow.Children.Add(folderLine);
        var toFolder = !string.IsNullOrWhiteSpace(_settings.OutputDirectory);
        var below = new StackPanel();
        below.Children.Add(Wrap(Segmented(
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
            below.Children.Add(folderRow);
        root.Children.Add(Card(T("settings.results.saveTitle"), T("settings.results.saveDetail"), null, below, Glyphs.Folder));
        root.Children.Add(Card(T("settings.results.reveal"), null,
            Toggle(_settings.RevealResult, v =>
            {
                _settings.RevealResult = v;
                Save();
            })));

        root.Children.Add(SectionHeader(T("settings.quality.section")));
        root.Children.Add(Card(T("settings.quality.images.title"), T("settings.quality.images.detail"),
            Slider(50, 100, _settings.Quality.JpegQuality, v => _settings.Quality.JpegQuality = v, Save), glyph: Glyphs.Photo));
        root.Children.Add(Card(T("settings.quality.compress.title"), T("settings.quality.compress.detail"),
            Slider(30, 95, _settings.Quality.CompressQuality, v => _settings.Quality.CompressQuality = v, Save), glyph: Glyphs.Shrink));
        root.Children.Add(Card(T("settings.quality.pdf.title"), T("settings.quality.pdf.detail"),
            Slider(72, 600, _settings.Quality.PdfDpi, v => _settings.Quality.PdfDpi = v, Save), glyph: Glyphs.Page));

        // R-20/R-31: company-safe switches in one section. Hooks and downloads default to OFF and IT can
        // lock them with HKLM policies; nothing is registered automatically, every integration is opt-in.
        root.Children.Add(SectionHeader(T("settings.company.section")));
        root.Children.Add(Card(Loc.T("settings.company.autostart.title"),
            Loc.T("settings.company.autostart.detail"),
            Toggle(Shell.ShellIntegration.IsAutostartEnabled, v =>
            {
                Shell.ShellIntegration.SetAutostart(v);
                Save();
            }),
            glyph: "\uE7E8"));
        root.Children.Add(Card(Loc.T("settings.company.contextMenu.title"),
            Loc.T("settings.company.contextMenu.detail"),
            Toggle(Shell.ShellIntegration.IsContextMenuEnabled, v =>
            {
                Shell.ShellIntegration.SetContextMenu(v);
                Save();
            }),
            glyph: "\uE8A7"));
        root.Children.Add(Card(Loc.T("settings.company.sendTo.title"),
            Loc.T("settings.company.sendTo.detail"),
            Toggle(Shell.ShellIntegration.IsSendToEnabled, v =>
            {
                Shell.ShellIntegration.SetSendTo(v);
                Save();
                Rebuild();
            }),
            glyph: "\uE8A7"));
        root.Children.Add(Card(Loc.T("settings.company.hooks.title"),
            Shell.Policy.HooksDisabled
                ? T("settings.company.policy.disabled")
                : Loc.T("settings.company.hooks.detail"),
            Toggle(_settings.WatchGestures && !Shell.Policy.HooksDisabled, v =>
            {
                _settings.WatchGestures = v;
                Save();
                Rebuild();
            }),
            glyph: "\uE7C9"));
        root.Children.Add(Card(Loc.T("settings.company.downloads.title"),
            Shell.Policy.DownloadsDisabled
                ? T("settings.company.policy.disabled")
                : Loc.T("settings.company.downloads.detail"),
            Toggle(_settings.AutoDownloadTools && !Shell.Policy.DownloadsDisabled, v =>
            {
                _settings.AutoDownloadTools = v;
                Save();
                Rebuild();
            }),
            glyph: "\uE896"));

        root.Children.Add(SectionHeader(T("settings.tools.section")));
        root.Children.Add(Card(T("settings.tools.builtIn.title"),
            T("settings.tools.builtIn.detail"),
            Button(T("settings.tools.checkAgain"), () =>
            {
                _tools.Refresh();
                Rebuild();
            }, glyph: "\uE72C"),
            glyph: "\uE90F"));
        foreach (var tool in Enum.GetValues<ExternalTool>())
            root.Children.Add(ToolCard(tool));

        var version = typeof(SettingsWindow).Assembly.GetName().Version;
        var footer = Text($"DropWheel {Bidi.Value(version?.ToString(3))} · " + T("settings.footer"), 12, brush: Theme.Muted);
        footer.Margin = new Thickness(2, 24, 0, 0);
        root.Children.Add(footer);

        _scroll.Content = root;
    }

    private static FrameworkElement Wrap(FrameworkElement element)
    {
        element.HorizontalAlignment = HorizontalAlignment.Left;
        element.Margin = new Thickness(0, 12, 0, 0);
        return element;
    }

    private UIElement Header()
    {
        var panel = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var image = new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/Assets/DropWheel.png")),
            Width = 56,
            Height = 56,
            Margin = new Thickness(0, 0, 18, 0),
        };
        panel.Children.Add(image);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = Text("DropWheel", 28, FontWeights.SemiBold);
        title.FontFamily = Theme.DisplayFont;
        text.Children.Add(title);
        text.Children.Add(Text(T("settings.header.tagline"), 13.5, brush: Theme.Muted));
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
