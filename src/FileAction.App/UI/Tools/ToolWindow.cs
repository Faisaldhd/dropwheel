using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FileAction.App.Shell;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools;

/// <summary>
/// The shared chrome for floating tools: a borderless, topmost, rounded card with a title bar, a close
/// button, the tool's own view and a Reset / primary-action bar. The size is adaptive: it opens at the
/// tool's default, then animates to the size computed from the media aspect by
/// <see cref="ToolWindowSizer"/>, clamped to the work area of the monitor under the cursor.
/// </summary>
internal sealed class ToolWindow : Window
{
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmsbtMainWindow = 2;

    /// <summary>Room inside the transparent window for the 38 DIP halo; the same inset Settings uses.</summary>
    private const double FrameMargin = 24;

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

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private readonly IToolWindowSpec _spec;
    private readonly IReadOnlyList<FileItem> _items;
    private readonly DropTargets? _targets;
    private readonly ToolApplyState _applyState = new();
    private ToolContent _content;
    private Border? _applyButton;
    private Border? _chrome;
    private Border? _contentHost;
    private Motion.Handle? _motion;
    private NativePoint? _cursorAnchor;
    private bool _realized;
    private bool _closeMotionRunning;
    private bool _prepared;

    public ToolWindow(IToolWindowSpec spec, IReadOnlyList<FileItem> items, DropTargets? targets)
    {
        _spec = spec;
        _items = items;
        _targets = targets;
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

        // Open immediately at the tool's default size; the media probe refines it later.
        var initial = ToolWindowSizer.Compute(
            spec.Layout, null, null, SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height, 1.0);
        Width = initial.Width;
        Height = initial.Height;

        // Show the frame immediately; the tool's real view is built after the window is on screen. This
        // removes the click-to-window delay on tools whose Create() touches COM, icons or a decoder.
        _content = Placeholder();
        Content = Build();
        // The one place tool-window motion is wired: the chrome Border contains the title bar, the tool's
        // view (the content root) and the button bar, so one transform set animates all three together.
        _motion = Motion.Prepare(_chrome!);
        UiSetup.Configure(this, animate: false);
        SourceInitialized += (_, _) => PlaceNearCursor();
        Loaded += (_, _) =>
        {
            StartOpenMotion();
            _ = PrepareAsync(animate: true);
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
            else if (e.Key == Key.Enter && _content.View is IToolActionGate gate && gate.CanApply)
            {
                e.Handled = true;
                _ = ApplyAsync();
            }
        };
    }

    /// <summary>
    /// The short open pop: fade in, rise 8 px and settle from 0.97 → 1 (180 ms), scaled from the
    /// wheel/click anchor instead of the chrome's centre. Skipped in reduced-motion mode.
    /// </summary>
    private void StartOpenMotion()
    {
        if (_chrome is null || _motion is null)
            return;
        AnchorMotionOrigin();
        if (!Motion.Enabled)
        {
            Motion.Reset(_motion);
            _chrome.Opacity = 1;
            return;
        }
        Motion.Play(Motion.Open(_chrome, _motion), _chrome);
    }

    /// <summary>
    /// Puts the chrome's scale origin on the pointer that opened the wheel, so the pop grows out of the
    /// clicked slice rather than fading from the window centre. The point can sit just outside the chrome:
    /// the 18 DIP placement gap overlaps the 24 DIP halo margin.
    /// </summary>
    private void AnchorMotionOrigin()
    {
        if (_chrome is null || !IsVisible || PresentationSource.FromVisual(_chrome) is null ||
            _chrome.ActualWidth <= 0 || _chrome.ActualHeight <= 0)
            return;

        if (_cursorAnchor is null && GetCursorPos(out var cursor))
            _cursorAnchor = cursor;
        if (_cursorAnchor is not { } anchor)
            return;

        try
        {
            // PointFromScreen takes the physical cursor pixels and returns chrome-local DIPs.
            var local = _chrome.PointFromScreen(new Point(anchor.X, anchor.Y));
            _chrome.RenderTransformOrigin = new Point(
                local.X / _chrome.ActualWidth, local.Y / _chrome.ActualHeight);
        }
        catch (InvalidOperationException)
        {
            // The window can be disconnected during shutdown; the centred origin remains usable.
        }
    }

    /// <summary>
    /// The light view shown until the tool's real content is built. It keeps the frame, title and close
    /// button usable immediately instead of blocking Show() on a decoder or COM initialisation.
    /// </summary>
    private static ToolContent Placeholder()
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(new TextBlock
        {
            Text = T("tool.window.loading"),
            Foreground = Theme.Muted,
            FontSize = Design.Type.Caption,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, Design.Space.Md),
        });
        panel.Children.Add(new ProgressBar
        {
            IsIndeterminate = true,
            Width = 120,
            Height = 4,
            Foreground = Theme.Accent,
            Background = Theme.Track,
        });
        return new ToolContent(
            panel,
            () => { },
            () => Task.FromResult<string?>(null),
            Task.FromResult<MediaSize?>(null),
            () => Task.CompletedTask);
    }

    /// <summary>Builds the real tool view once, after the frame is visible.</summary>
    private async Task RealizeAsync()
    {
        if (_realized)
            return;
        _realized = true;
        try
        {
            var content = _spec.Create(_items, _targets);
            _content = content;
            if (_contentHost is not null)
                _contentHost.Child = content.View;
            RefreshActionBar();
        }
        catch (Exception ex)
        {
            _contentHost?.Child = new TextBlock
            {
                Text = ex.Message,
                Foreground = Theme.Error,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(Design.Gutter),
            };
        }
        await Task.CompletedTask;
    }

    /// <summary>
    /// Loads the preview and then sizes the window to the media. Also used by --self-test, where the
    /// window is never shown.
    /// </summary>
    public async Task PrepareAsync(bool animate)
    {
        if (_prepared)
            return;
        _prepared = true;
        await RealizeAsync();

        try
        {
            await _content.Activate();
        }
        catch
        {
            // The tool shows its own failure inside the preview.
        }

        var media = await _content.Ready;
        if (media is not { Width: > 0, Height: > 0 })
            return;
        if (!TryGetWorkArea(out var work))
        {
            // A hidden/headless window can fail the monitor probe; fall back to the WPF work area so the
            // media still gets its correct aspect instead of staying at the tall placeholder size.
            var area = SystemParameters.WorkArea;
            work = new WorkArea(area.Left, area.Top, area.Width, area.Height, 1.0);
        }

        var size = ToolWindowSizer.Compute(_spec.Layout, media.Value.Width, media.Value.Height,
            work.WidthInPixels, work.HeightInPixels, work.Scale);

        var (left, top) = Placement(size.Width, size.Height, work);

        if (animate)
        {
            // The one allowed width/height animation in the app: the window follows the media
            // orientation over 180 ms instead of jumping between portrait and landscape.
            Motion.Resize(this, left, top, size.Width, size.Height);
        }
        else
        {
            Width = size.Width;
            Height = size.Height;
            Left = left;
            Top = top;
        }
    }

    /// <summary>
    /// Detaches the chrome for the motion self-test and the website clips. The element can be rendered
    /// repeatedly at different poses; the window itself is never shown.
    /// </summary>
    internal FrameworkElement DetachForCapture()
    {
        if (_chrome is null)
            throw new InvalidOperationException("tool window chrome was not built");
        Content = null;
        var root = new Grid
        {
            Background = SelfTest.Desktop(),
            Width = Width,
            Height = Height,
            FlowDirection = FlowDirection,
        };
        root.Children.Add(_chrome);
        return root;
    }

    /// <summary>
    /// Applies the exact anchored tool-window pose with no animation clocks: the 180 ms fade + 8 px rise +
    /// 0.97 → 1 settle and its reverse, optionally cross-faded by the 120 ms close.
    /// </summary>
    internal void Pose(double openMs, double closeMs = 0)
    {
        if (_chrome is null || _motion is null)
            return;
        AnchorMotionOrigin();
        var open = Math.Clamp(openMs / Motion.WindowOpenMs, 0, 1);
        var close = Math.Clamp(closeMs / Motion.WindowCloseMs, 0, 1);
        var closeEase = Motion.EaseIn.Ease(close);
        _chrome.Opacity = Motion.SoftOut.Ease(open) * (1 - closeEase);
        var scale = (Motion.WindowScaleFrom + (1 - Motion.WindowScaleFrom) * Motion.Settle.Ease(open)) *
                    (1 - (1 - Motion.WindowScaleFrom) * closeEase);
        _motion.Scale.ScaleX = scale;
        _motion.Scale.ScaleY = scale;
        if (_motion.Lift is { } lift)
            lift.Y = Motion.RisePx * (1 - Motion.SoftOut.Ease(open));
    }

    private Border Build()
    {
        var titleBar = BuildTitleBar();
        var bottom = BuildBottomBar();
        // Hairline separators make the three zones read as one frame instead of a floating stack.
        var titleHost = new Border
        {
            Child = titleBar,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
        var bottomHost = new Border
        {
            Child = bottom,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(0, 1, 0, 0),
        };
        // The content used to sit flush against the card edge while the title and actions were inset.
        // One shared gutter now lines up every tool, including the seven media views.
        _contentHost = new Border
        {
            // A visible rounded content card: without it a dark tool view (Lock) blended into the chrome
            // and read as a square window even though the outer frame was rounded.
            Background = Theme.Card,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Design.Radius.Card),
            Padding = new Thickness(Design.Gutter, Design.Space.Md, Design.Gutter, Design.Space.Md),
            Margin = new Thickness(Design.Space.Md, 0, Design.Space.Md, 0),
            Child = _content.View,
        };
        _contentHost.SizeChanged += (_, _) =>
        {
            if (_contentHost.ActualWidth <= 0 || _contentHost.ActualHeight <= 0)
                return;
            _contentHost.Clip = new RectangleGeometry(
                new Rect(0, 0, _contentHost.ActualWidth, _contentHost.ActualHeight),
                Design.Radius.Card, Design.Radius.Card);
        };

        var grid = new Grid { ClipToBounds = true };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(titleHost, 0);
        Grid.SetRow(_contentHost, 1);
        Grid.SetRow(bottomHost, 2);
        grid.Children.Add(titleHost);
        grid.Children.Add(_contentHost);
        grid.Children.Add(bottomHost);

        // WPF does not clip a Border's children to its CornerRadius. Clip the real content to a slightly
        // smaller radius so previews and action bars never paint over the rounded window corners, while
        // the outer border and its shadow stay outside the clip.
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
        return _chrome = new Border
        {
            CornerRadius = new CornerRadius(Design.Radius.Window),
            Background = Theme.Panel,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            Child = grid,
            // 24 DIP leaves room for the 38 DIP dark halo inside the transparent window bounds.
            Margin = new Thickness(FrameMargin),
            Effect = WindowShadow(),
        };
    }

    /// <summary>
    /// A black drop shadow disappears on the dark page, so dark mode gets the same soft violet halo as
    /// Settings; light mode keeps the neutral shadow.
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

    /// <summary>Closes with the open pop reversed: a 120 ms anchored fade and 1 → 0.97 scale.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        // RC-01: a close during Apply would orphan the running write and can leave a partial final file.
        // Defer the close; ApplyAsync closes the window itself once the write has completed or failed.
        if (_applyState.BlocksClose)
        {
            e.Cancel = true;
            base.OnClosing(e);
            return;
        }

        if (!_closeMotionRunning && _chrome is not null && _motion is not null
            && Motion.Enabled && IsVisible && !Dispatcher.HasShutdownStarted)
        {
            e.Cancel = true;
            var chrome = _chrome;
            var motion = _motion;
            AnchorMotionOrigin();
            Motion.Play(Motion.Close(chrome, motion, () =>
            {
                Motion.Release(chrome, motion);
                if (!Dispatcher.HasShutdownStarted)
                    Close();
            }, Motion.WindowScaleFrom), chrome);
            _closeMotionRunning = true;
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_chrome is not null && _motion is not null)
            Motion.Release(_chrome, _motion);
        base.OnClosed(e);
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
                FontSize = Design.Icon.Inline,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Theme.Text,
            },
            Cursor = Cursors.Hand,
            ToolTip = T("tool.window.close"),
        };
        // Close on press, not release: the title bar starts DragMove on press, which captures the mouse
        // and swallows the following ButtonUp (the old × therefore often did nothing).
        close.Focusable = true;
        KeyboardNavigation.SetIsTabStop(close, true);
        AutomationProperties.SetName(close, T("tool.window.close"));
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

        var title = new TextBlock
        {
            Text = T(_spec.TitleKey),
            FontSize = Design.Type.Title,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var bar = new DockPanel
        {
            // WPF only hit-tests a panel where it paints; transparent keeps the whole title bar draggable.
            Background = Brushes.Transparent,
            Margin = new Thickness(Design.Gutter, Design.Space.Xl, Design.Gutter, Design.Space.Md),
        };
        DockPanel.SetDock(close, Dock.Left);
        bar.Children.Add(close);
        bar.Children.Add(title);
        bar.MouseLeftButtonDown += (_, _) =>
        {
            // Never start a window drag from the close button.
            if (close.IsMouseOver)
                return;
            if (Mouse.LeftButton == MouseButtonState.Pressed)
                DragMove();
        };
        return bar;
    }

    private FrameworkElement BuildBottomBar()
    {
        var reset = Controls.Button(T("tool.window.reset"), () => _content.Reset());
        var applyLabel = (_content.View as IToolActionLabel)?.PrimaryLabelKey ?? _spec.PrimaryLabelKey;
        var apply = Controls.Button(T(applyLabel), () => _ = ApplyAsync(), primary: true);
        _applyButton = apply;
        if (_content.View is IToolActionGate gate)
        {
            apply.IsEnabled = gate.CanApply;
            gate.CanApplyChanged += () =>
            {
                if (_applyButton is not null)
                    _applyButton.IsEnabled = gate.CanApply;
            };
        }

        var bar = new DockPanel
        {
            Margin = new Thickness(Design.Gutter, Design.Space.Md, Design.Gutter, Design.Gutter),
            LastChildFill = false,
        };
        DockPanel.SetDock(reset, Dock.Left);
        DockPanel.SetDock(apply, Dock.Right);
        bar.Children.Add(reset);
        bar.Children.Add(apply);
        return bar;
    }

    /// <summary>Updates the action bar once the real tool view exists (the placeholder has no gate).</summary>
    private void RefreshActionBar()
    {
        if (_applyButton is null)
            return;
        var label = (_content.View as IToolActionLabel)?.PrimaryLabelKey ?? _spec.PrimaryLabelKey;
        Controls.SetText(_applyButton, T(label));
        if (_content.View is IToolActionGate gate)
        {
            _applyButton.IsEnabled = gate.CanApply;
            gate.CanApplyChanged += () =>
            {
                if (_applyButton is not null)
                    _applyButton.IsEnabled = gate.CanApply;
            };
        }
    }

    /// <summary>
    /// Changes the primary button's text at runtime, for labels that depend on state
    /// (e.g. "Save 3 Frames"). Tools call this from their own view; the window never reads tool state.
    /// </summary>
    public void SetPrimaryLabel(string text)
    {
        if (_applyButton is not null)
            Controls.SetText(_applyButton, text);
    }

    private async Task ApplyAsync()
    {
        if (!_applyState.Begin())
            return;

        if (_applyButton is not null)
            _applyButton.IsEnabled = false;
        string? created = null;
        try
        {
            created = await _content.Apply();
            if (!string.IsNullOrEmpty(created))
            {
                Notifications.Show(T("tool.window.done.title"), T("tool.window.done", Bidi.Value(Path.GetFileName(created))));
                // Success pulse on the primary action, then the normal close fade. In reduced-motion mode
                // the window closes straight away, exactly as before 2.4.0.
                if (Motion.Enabled && _applyButton is not null)
                {
                    Motion.Play(Motion.Pulse(_applyButton), _applyButton);
                    await Task.Delay((int)(Motion.PulseMs * 0.55));
                }
            }
        }
        catch (Exception ex)
        {
            Notifications.Show(T("app.error.title"), ex.Message, error: true);
        }
        finally
        {
            _applyState.End();
            if (_applyButton is not null)
                _applyButton.IsEnabled = true;
        }

        // Only close after the apply state has ended, so OnClosing cannot defer this success close.
        if (!string.IsNullOrEmpty(created))
            Close();
    }

    private readonly record struct WorkArea(double Left, double Top, double Width, double Height, double Scale)
    {
        public double WidthInPixels => Width * Scale;
        public double HeightInPixels => Height * Scale;
    }

    /// <summary>The work area of the monitor under the cursor, in DIPs, with its DPI scale.</summary>
    private bool TryGetWorkArea(out WorkArea area)
    {
        area = default;
        try
        {
            var monitor = IntPtr.Zero;
            if (_cursorAnchor is { } anchor)
                monitor = MonitorFromPoint(anchor, 2 /* MONITOR_DEFAULTTONEAREST */);
            else if (GetCursorPos(out var cursor))
                monitor = MonitorFromPoint(cursor, 2 /* MONITOR_DEFAULTTONEAREST */);
            if (monitor == IntPtr.Zero)
                return false;

            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info))
                return false;

            // Use WPF's own DPI for the window. GetDpiForWindow returns 96 before per-monitor awareness
            // is applied on some systems, which put the near-cursor window at the wrong offset.
            var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            if (scale <= 0)
                scale = 1.0;

            area = new WorkArea(
                info.Work.Left / scale,
                info.Work.Top / scale,
                (info.Work.Right - info.Work.Left) / scale,
                (info.Work.Bottom - info.Work.Top) / scale,
                scale);
            return area.Width > 0 && area.Height > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Opens the tool next to the pointer, not in the middle of the monitor. The anchor is captured once,
    /// so the card keeps its place while the media probe resizes it.
    /// </summary>
    private void PlaceNearCursor()
    {
        try
        {
            if (_cursorAnchor is null && GetCursorPos(out var cursor))
                _cursorAnchor = cursor;
            if (!TryGetWorkArea(out var work))
                return;

            var width = ActualWidth > 0 ? ActualWidth : Width;
            var height = ActualHeight > 0 ? ActualHeight : Height;
            var (left, top) = Placement(width, height, work);
            Left = left;
            Top = top;
            ApplyBackdrop();
        }
        catch
        {
            // Placement is cosmetic; the default position is still usable.
        }
    }

    /// <summary>Near-cursor position in DIPs, clamped to the monitor's work area.</summary>
    private (double Left, double Top) Placement(double width, double height, WorkArea work)
    {
        if (_cursorAnchor is { } anchor)
        {
            var cursor = new Point(anchor.X / work.Scale, anchor.Y / work.Scale);
            var at = ToolPlacement.NearCursor(
                cursor, width, height, new Rect(work.Left, work.Top, work.Width, work.Height));
            return (at.X, at.Y);
        }

        // No pointer reading (rare session): keep the old centred fallback.
        var fallbackLeft = work.Left + (work.Width - width) / 2;
        var fallbackTop = work.Top + (work.Height - height) / 2;
        return (
            Math.Clamp(fallbackLeft, work.Left, Math.Max(work.Left, work.Left + work.Width - width)),
            Math.Clamp(fallbackTop, work.Top, Math.Max(work.Top, work.Top + work.Height - height)));
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
