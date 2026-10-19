using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using FileAction.App.Shell;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools;

/// <summary>
/// The shared chrome for floating tools: a borderless, topmost, rounded card with a title bar, a close
/// button, the tool's own view and a Reset / primary-action bar. The window is centred on the monitor that
/// holds the cursor and clamps the tool's declared size to the work area.
/// </summary>
internal sealed class ToolWindow : Window
{
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmsbtMainWindow = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public int Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private readonly IToolWindowSpec _spec;
    private readonly ToolContent _content;
    private Border? _applyButton;

    public ToolWindow(IToolWindowSpec spec, IReadOnlyList<FileItem> items, DropTargets? targets)
    {
        _spec = spec;
        Title = T(spec.TitleKey);
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.Manual;
        WindowStartupLocation = WindowStartupLocation.Manual;

        Theme.Refresh();
        FontFamily = Theme.UiFont;
        Foreground = Theme.Text;
        FlowDirection = LanguageManager.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        var (width, height) = ToolLayout.Clamp(
            spec.Sizes.Width, spec.Sizes.Height, SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height);
        Width = width;
        Height = height;

        _content = spec.Create(items, targets);
        Content = Build();
        UiSetup.Configure(this);
        SourceInitialized += (_, _) => CenterOnCursorMonitor();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
    }

    private FrameworkElement Build()
    {
        var panel = (Theme.Panel as SolidColorBrush)?.Color ?? Colors.Black;
        var glass = new SolidColorBrush(Color.FromArgb(0xE6, panel.R, panel.G, panel.B));

        var titleBar = BuildTitleBar();
        var bottom = BuildBottomBar();

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(titleBar, 0);
        Grid.SetRow(_content.View, 1);
        Grid.SetRow(bottom, 2);
        grid.Children.Add(titleBar);
        grid.Children.Add(_content.View);
        grid.Children.Add(bottom);

        return new Border
        {
            CornerRadius = new CornerRadius(20),
            Background = glass,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            Child = grid,
            Margin = new Thickness(12),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 28,
                ShadowDepth = 6,
                Opacity = 0.35,
            },
        };
    }

    private FrameworkElement BuildTitleBar()
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
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Theme.Text,
            },
            Cursor = Cursors.Hand,
            ToolTip = T("tool.window.close"),
        };
        close.MouseLeftButtonUp += (_, _) => Close();

        var title = new TextBlock
        {
            Text = T(_spec.TitleKey),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var bar = new DockPanel { Margin = new Thickness(18, 16, 18, 10) };
        DockPanel.SetDock(close, Dock.Left);
        bar.Children.Add(close);
        bar.Children.Add(title);
        bar.MouseLeftButtonDown += (_, _) =>
        {
            if (Mouse.LeftButton == MouseButtonState.Pressed)
                DragMove();
        };
        return bar;
    }

    private FrameworkElement BuildBottomBar()
    {
        var reset = Controls.Button(T("tool.window.reset"), () => _content.Reset());
        var apply = Controls.Button(T("tool.window.apply"), () => _ = ApplyAsync(), primary: true);
        _applyButton = apply;

        var bar = new DockPanel { Margin = new Thickness(18, 8, 18, 18), LastChildFill = false };
        DockPanel.SetDock(reset, Dock.Left);
        DockPanel.SetDock(apply, Dock.Right);
        bar.Children.Add(reset);
        bar.Children.Add(apply);
        return bar;
    }

    private async Task ApplyAsync()
    {
        if (_applyButton is not null)
            _applyButton.IsEnabled = false;
        try
        {
            var created = await _content.Apply();
            if (!string.IsNullOrEmpty(created))
                Notifications.Show(T("tool.window.done.title"), T("tool.window.done", Bidi.Value(Path.GetFileName(created))));
            Close();
        }
        catch (Exception ex)
        {
            Notifications.Show(T("app.error.title"), ex.Message, error: true);
        }
        finally
        {
            if (_applyButton is not null)
                _applyButton.IsEnabled = true;
        }
    }

    private void CenterOnCursorMonitor()
    {
        try
        {
            if (!GetCursorPos(out var cursor))
                return;
            var monitor = MonitorFromPoint(cursor, 2 /* MONITOR_DEFAULTTONEAREST */);
            if (monitor == IntPtr.Zero)
                return;
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info))
                return;

            var source = PresentationSource.FromVisual(this);
            var toDips = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var topLeft = toDips.Transform(new Point(info.Work.Left, info.Work.Top));
            var bottomRight = toDips.Transform(new Point(info.Work.Right, info.Work.Bottom));
            Left = topLeft.X + ((bottomRight.X - topLeft.X) - ActualWidth) / 2;
            Top = topLeft.Y + ((bottomRight.Y - topLeft.Y) - ActualHeight) / 2;
            ApplyBackdrop();
        }
        catch
        {
            // Centring is cosmetic; the default position is still usable.
        }
    }

    private void ApplyBackdrop()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
                return;
            var backdrop = DwmsbtMainWindow;
            DwmSetWindowAttribute(handle, DwmwaSystemBackdropType, ref backdrop, sizeof(int));
        }
        catch
        {
            // Windows 10 has no system backdrop; the semi-transparent panel is the fallback.
        }
    }
}
