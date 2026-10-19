using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using FileAction.App.Native;
using FileAction.App.UI;
using static FileAction.Core.Loc;

namespace FileAction.App.Shell;

/// <summary>
/// The notification-area icon and its menu, built on <c>Shell_NotifyIcon</c> and WPF instead of
/// Windows Forms: a hidden window receives the icon's callbacks and a WPF menu opens at the cursor.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    /// <summary>Callback message of the icon; the message's lParam is the mouse or balloon notification.</summary>
    private const int WM_TRAYICON = 0x8000 + 1; // WM_APP + 1

    private const int NIN_BALLOONHIDE = 0x0400 + 3;
    private const int NIN_BALLOONTIMEOUT = 0x0400 + 4;
    private const int NIN_BALLOONUSERCLICK = 0x0400 + 5;

    private const uint NIM_ADD = 0;
    private const uint NIM_MODIFY = 1;
    private const uint NIM_DELETE = 2;

    private const uint NIF_MESSAGE = 0x01;
    private const uint NIF_ICON = 0x02;
    private const uint NIF_TIP = 0x04;
    private const uint NIF_INFO = 0x10;

    private const uint NIIF_INFO = 0x01;
    private const uint NIIF_WARNING = 0x02;

    /// <summary>Version 3.0 icon resources; .ico images have been stored that way since Windows Vista.</summary>
    private const uint IconResourceVersion = 0x00030000;

    private static readonly int TaskbarCreated = Win32.RegisterWindowMessage("TaskbarCreated");

    private readonly HwndSource _window;
    private readonly ContextMenu _menu;
    private readonly MenuItem _pause;
    private readonly Action _openSettings;
    private readonly Win32.NOTIFYICONDATA _data;
    private readonly IntPtr _icon;
    private readonly List<(MenuItem Item, string Key)> _labels = [];
    private readonly MenuItem _check;
    private MenuItem? _update;
    private Separator? _updateSeparator;
    private Version? _updateVersion;
    private Action? _install;
    private Action? _checkForUpdates;
    private Action? _balloonClick;
    private bool _syncingPause;
    private bool _disposed;

    public TrayIcon(Action openSettings, Action openClipboard, Action openShelf, Action showHelp, Action<bool> setPaused, bool paused, Action exit)
    {
        _openSettings = openSettings;
        _pause = new MenuItem { Header = T("tray.pause"), IsCheckable = true, IsChecked = paused };
        _pause.Checked += (_, _) => OnPause(setPaused, true);
        _pause.Unchecked += (_, _) => OnPause(setPaused, false);

        _menu = CreateMenu();
        // The update group sits above the everyday commands. "Update to version x.y.z" is only added
        // while a newer release exists; the manual check is always available once the app wires it.
        _check = Item(T("update.tray.check"), () => _checkForUpdates?.Invoke());
        _check.IsEnabled = false;
        _labels.Add((_check, "update.tray.check"));
        _menu.Items.Add(_check);
        _menu.Items.Add(MenuSeparator());
        AddItem("tray.clipboardWheel", openClipboard);
        AddItem("tray.settings", openSettings);
        AddItem("tray.emptyShelf", openShelf);
        AddItem("tray.help", showHelp);
        _menu.Items.Add(_pause);
        _menu.Items.Add(MenuSeparator());
        AddItem("tray.exit", exit);

        _icon = CreateAppIcon();
        // A hidden top-level popup rather than a message-only window: Explorer's "TaskbarCreated"
        // broadcast skips message-only windows, and we need it to put the icon back after a restart.
        _window = new HwndSource(new HwndSourceParameters("DropWheelTrayIcon")
        {
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP
            ExtendedWindowStyle = 0x00000080,          // WS_EX_TOOLWINDOW keeps it out of Alt+Tab
        });
        _window.AddHook(WndProc);

        _data = new Win32.NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<Win32.NOTIFYICONDATA>(),
            hWnd = _window.Handle,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_TIP | (_icon != IntPtr.Zero ? NIF_ICON : 0),
            uCallbackMessage = WM_TRAYICON,
            hIcon = _icon,
            szTip = "DropWheel",
        };
        Notify(NIM_ADD, _data);
        Notifications.Sink = Show;
    }

    public bool Paused
    {
        set
        {
            if (_pause.IsChecked == value)
                return;
            // Changing the check state from code must not look like a click on the item.
            _syncingPause = true;
            _pause.IsChecked = value;
            _syncingPause = false;
        }
    }

    private void OnPause(Action<bool> setPaused, bool paused)
    {
        if (!_syncingPause)
            setPaused(paused);
    }

    /// <summary>Rebuilds the menu texts and direction after a live language switch, keeping the icon.</summary>
    public void Refresh()
    {
        if (_disposed)
            return;
        _menu.FlowDirection = LanguageManager.FlowDirection;
        _pause.Header = T("tray.pause");
        foreach (var (item, key) in _labels)
            item.Header = T(key);
        if (_update is not null)
        {
            var version = Bidi.Value(_updateVersion?.ToString() ?? "");
            _update.Header = T("update.menu", version);
        }
    }

    private void AddItem(string key, Action click)
    {
        var item = Item(T(key), click);
        _labels.Add((item, key));
        _menu.Items.Add(item);
    }

    /// <summary>Invoked by the "Check for updates" item; the app sets it right after creating the tray.</summary>
    public Action? CheckForUpdates
    {
        get => _checkForUpdates;
        set
        {
            _checkForUpdates = value;
            _check.IsEnabled = value is not null;
        }
    }

    /// <summary>Adds "Update to version x.y.z" at the top of the menu and announces it once.</summary>
    public void OfferUpdate(Version version, Action install)
    {
        _updateVersion = version;
        _install = install;
        var shown = Bidi.Value(version.ToString());
        var text = T("update.menu", shown);
        if (_update is null)
        {
            _update = new MenuItem { Header = text, FontWeight = FontWeights.Bold };
            _update.Click += (_, _) => _install?.Invoke();
            _menu.Items.Insert(0, _update);
            _updateSeparator = MenuSeparator();
            _menu.Items.Insert(1, _updateSeparator);
        }
        else
        {
            _update.Header = text;
        }
        Show(T("update.available.title"),
            T("update.available.body", shown), false);
        _balloonClick = install;
    }

    /// <summary>Removes the "Update to version" item and its separator when nothing newer exists.</summary>
    public void ClearUpdate()
    {
        if (_update is not null)
        {
            _menu.Items.Remove(_update);
            _update = null;
        }
        if (_updateSeparator is not null)
        {
            _menu.Items.Remove(_updateSeparator);
            _updateSeparator = null;
        }
        _updateVersion = null;
        _install = null;
        _balloonClick = null;
    }

    public void Show(string title, string text, bool error)
    {
        _balloonClick = null;
        var data = _data;
        data.uFlags = NIF_INFO;
        data.szInfoTitle = Clamp(title, 63);
        data.szInfo = Clamp(string.IsNullOrWhiteSpace(text) ? " " : text, 255);
        data.dwInfoFlags = error ? NIIF_WARNING : NIIF_INFO;
        data.uTimeout = 4000;
        Notify(NIM_MODIFY, data);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Notifications.Sink = null;
        _menu.IsOpen = false;
        Notify(NIM_DELETE, _data);
        _window.RemoveHook(WndProc);
        _window.Dispose();
        if (_icon != IntPtr.Zero)
            Win32.DestroyIcon(_icon);
    }

    private void Notify(uint message, Win32.NOTIFYICONDATA data) => Win32.Shell_NotifyIcon(message, ref data);

    private static string Clamp(string value, int length) => value.Length <= length ? value : value[..length];

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == TaskbarCreated && TaskbarCreated != 0)
        {
            // Explorer was restarted and dropped every tray icon; add ours back.
            Notify(NIM_ADD, _data);
            handled = true;
        }
        else if (message == WM_TRAYICON)
        {
            // A single left click does nothing (as before); double click opens the settings.
            switch ((int)lParam)
            {
                case Win32.WM_LBUTTONDBLCLK:
                    handled = true;
                    _openSettings();
                    break;
                case Win32.WM_RBUTTONUP:
                    handled = true;
                    ShowMenu();
                    break;
                case NIN_BALLOONUSERCLICK:
                    handled = true;
                    var action = _balloonClick;
                    _balloonClick = null;
                    action?.Invoke();
                    break;
                case NIN_BALLOONHIDE:
                case NIN_BALLOONTIMEOUT:
                    _balloonClick = null;
                    break;
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>Opens the menu at the cursor; a second right click closes it again.</summary>
    private void ShowMenu()
    {
        if (_menu.IsOpen)
        {
            _menu.IsOpen = false;
            return;
        }
        // Like NotifyIcon did: make our own window foreground first, otherwise the popup cannot take
        // the mouse capture and would stay open when the user clicks another application.
        Win32.SetForegroundWindow(_window.Handle);
        _menu.Placement = PlacementMode.MousePoint;
        _menu.IsOpen = true;
    }

    private MenuItem Item(string header, Action click)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) =>
        {
            // Let the menu close first, so windows and message boxes opened by the item are on top.
            _menu.IsOpen = false;
            _menu.Dispatcher.BeginInvoke(click);
        };
        return item;
    }

    private static ContextMenu CreateMenu()
    {
        var menu = new ContextMenu
        {
            Background = Theme.Panel,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            Foreground = Theme.Text,
            FontFamily = Theme.UiFont,
            FontSize = 13,
            MinWidth = 200,
            Padding = new Thickness(4),
            Placement = PlacementMode.MousePoint,
            FlowDirection = LanguageManager.FlowDirection,
            Template = SurfaceTemplate(),
        };
        menu.Resources.Add(typeof(MenuItem), MenuItemStyle());
        return menu;
    }

    /// <summary>The menu panel: a hairline-bordered surface with the app's colours instead of the system chrome.</summary>
    private static ControlTemplate SurfaceTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Theme.Panel);
        border.SetValue(Border.BorderBrushProperty, Theme.PanelBorder);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.PaddingProperty, new Thickness(4));
        border.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

        var items = new FrameworkElementFactory(typeof(StackPanel));
        items.SetValue(Panel.IsItemsHostProperty, true);
        border.AppendChild(items);

        return new ControlTemplate(typeof(ContextMenu)) { VisualTree = border };
    }

    /// <summary>Menu rows with a check column, a rounded hover highlight and the app's text colour.</summary>
    private static Style MenuItemStyle()
    {
        var check = new FrameworkElementFactory(typeof(TextBlock), "Check");
        check.SetValue(TextBlock.FontFamilyProperty, Theme.Icons);
        check.SetValue(TextBlock.FontSizeProperty, 12.0);
        check.SetValue(TextBlock.WidthProperty, 20.0);
        check.SetValue(TextBlock.ForegroundProperty, Theme.Accent);
        check.SetValue(TextBlock.TextProperty, string.Empty);
        check.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

        var header = new FrameworkElementFactory(typeof(ContentPresenter), "Header");
        header.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        header.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        header.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

        var row = new FrameworkElementFactory(typeof(StackPanel));
        row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        row.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        row.AppendChild(check);
        row.AppendChild(header);

        var chrome = new FrameworkElementFactory(typeof(Border), "Chrome");
        chrome.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        chrome.SetValue(Border.PaddingProperty, new Thickness(8, 6, 12, 6));
        chrome.AppendChild(row);

        var template = new ControlTemplate(typeof(MenuItem)) { VisualTree = chrome };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Theme.Hover, "Chrome"));
        template.Triggers.Add(hover);
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BackgroundProperty, Theme.Hover, "Chrome"));
        template.Triggers.Add(focus);
        var isChecked = new Trigger { Property = MenuItem.IsCheckedProperty, Value = true };
        isChecked.Setters.Add(new Setter(TextBlock.TextProperty, "\uE73E", "Check"));
        template.Triggers.Add(isChecked);

        var style = new Style(typeof(MenuItem));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Theme.Text));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
        return style;
    }

    private static Separator MenuSeparator()
    {
        var line = new FrameworkElementFactory(typeof(Border));
        line.SetValue(Border.BackgroundProperty, Theme.PanelBorder);
        line.SetValue(FrameworkElement.HeightProperty, 1.0);

        return new Separator
        {
            Margin = new Thickness(8, 4, 8, 4),
            Template = new ControlTemplate(typeof(Separator)) { VisualTree = line },
        };
    }

    /// <summary>
    /// Turns the .ico embedded in the assembly into an HICON of the size the notification area wants.
    /// </summary>
    private static IntPtr CreateAppIcon()
    {
        var bytes = ReadIconFile();
        if (bytes is null || bytes.Length < 6)
            return IntPtr.Zero;

        var count = BitConverter.ToUInt16(bytes, 4);
        var wanted = Math.Max(16, Win32.GetSystemMetrics(Win32.SM_CXSMICON));
        var best = -1;
        var bestWidth = int.MaxValue;
        var fallback = -1;
        var fallbackWidth = 0;
        for (var i = 0; i < count; i++)
        {
            var entry = 6 + 16 * i;
            if (entry + 16 > bytes.Length)
                break;
            var width = bytes[entry] == 0 ? 256 : bytes[entry];
            if (width >= wanted && width < bestWidth)
            {
                best = i;
                bestWidth = width;
            }
            if (width > fallbackWidth)
            {
                fallback = i;
                fallbackWidth = width;
            }
        }
        if (best < 0)
            best = fallback;
        if (best < 0)
            return IntPtr.Zero;

        var entryOffset = 6 + 16 * best;
        var length = (int)BitConverter.ToUInt32(bytes, entryOffset + 8);
        var offset = (int)BitConverter.ToUInt32(bytes, entryOffset + 12);
        if (length <= 0 || offset < 0 || offset + length > bytes.Length)
            return IntPtr.Zero;

        var image = new byte[length];
        Array.Copy(bytes, offset, image, 0, length);
        return Win32.CreateIconFromResourceEx(image, (uint)length, true, IconResourceVersion, wanted, wanted, 0);
    }

    private static byte[]? ReadIconFile()
    {
        var name = LightTaskbar() ? "DropWheel-tray-light.ico" : "DropWheel-tray-dark.ico";
        var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/" + name));
        if (info is null)
            return null;
        using var stream = info.Stream;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static bool LightTaskbar()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }
        catch
        {
            return false;
        }
    }
}
