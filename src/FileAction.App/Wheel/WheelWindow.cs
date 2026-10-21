using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using FileAction.App.Native;
using FileAction.App.UI;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.Wheel;

internal enum WheelMode
{
    /// <summary>Opened mid-drag: the user drops the file on a slice. Never takes focus.</summary>
    Drag,

    /// <summary>Opened from the context menu or hotkey: click a slice or use the keyboard.</summary>
    Click,
}

/// <summary>The radial menu. One instance per appearance.</summary>
internal sealed class WheelWindow : Window
{
    public delegate IReadOnlyList<Operation> OperationSource(WheelKind wheel, IReadOnlyList<FileItem> items);

    private const double OuterRadius = 196;
    private const double InnerRadius = 80;
    private const double Rim = 9;
    private const double Gap = 6;
    private const double Corner = 11;
    private const double PopOut = 7;

    /// <summary>Room around the disc for its shadow.</summary>
    private const double Pad = 16;
    private const double Size = 2 * (OuterRadius + Rim + Pad);
    private static readonly WheelKind[] WheelOrder = [WheelKind.Convert, WheelKind.Tools, WheelKind.Actions];

    private readonly AppSettings _settings;
    private readonly OperationSource _source;
    private readonly Action<Operation, IReadOnlyList<FileItem>, DropTargets?> _execute;
    private readonly DropTargets? _targets;
    private readonly Canvas _canvas = new() { Width = Size, Height = Size };
    private readonly ScaleTransform _canvasScale = new(1, 1);
    private readonly RotateTransform _canvasRotate = new(0, Size / 2, Size / 2);
    private readonly Motion.Handle _motion;
    private readonly List<Path> _slices = [];
    private readonly List<StackPanel> _labels = [];
    private readonly List<Canvas> _groups = [];
    private readonly Image _icon = new() { Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
    private readonly TextBlock _iconGlyph = new() { FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
    private readonly TextBlock _title = HubText(16, FontWeights.SemiBold);
    private readonly TextBlock _detail = HubText(11.5, FontWeights.Normal);
    private readonly TextBlock _destination = HubText(10.5, FontWeights.Normal);
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
    private readonly Border _tabTrack = new() { CornerRadius = new CornerRadius(10), Padding = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
    private readonly StackPanel _hubContent = new() { Width = InnerRadius * 1.75 };

    private IReadOnlyList<FileItem> _items;
    private IReadOnlyList<Operation> _ops = [];
    private WheelKind _wheel;
    private int _highlight = -1;
    private Win32.POINT _anchor;
    private bool _finished;
    private bool _repeat;
    private bool _switching;
    private bool _closing;
    private int _pressed = -1;
    private double _fit = 1.0;
    private DispatcherTimer? _closeTimer;

    public WheelWindow(
        WheelMode mode,
        WheelKind wheel,
        IReadOnlyList<FileItem>? items,
        AppSettings settings,
        OperationSource source,
        Action<Operation, IReadOnlyList<FileItem>, DropTargets?> execute,
        DropTargets? targets = null)
    {
        Mode = mode;
        _wheel = wheel;
        _items = items ?? [];
        _settings = settings;
        _source = source;
        _execute = execute;
        _targets = targets;

        Title = "DropWheel";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = mode == WheelMode.Click;
        AllowDrop = true;
        Width = Size;
        Height = Size;
        Left = -32000;
        Top = -32000;
        Opacity = 0;
        FontFamily = Theme.UiFont;
        Focusable = true;
        Content = _canvas;
        UiSetup.Configure(this, animate: false);
        // The disc is circular and its slices run clockwise; mirroring it would move every slice.
        // Only the text inside the slices and the hub follow the reading direction.
        _canvas.FlowDirection = System.Windows.FlowDirection.LeftToRight;
        _hubContent.FlowDirection = LanguageManager.FlowDirection;
        _tabs.FlowDirection = LanguageManager.FlowDirection;
        // One transform group for the whole disc: the open/close scale and the mode-switch rotation are
        // both RenderTransform work, so the wheel never changes its layout or allocates a visual per frame.
        var canvasTransforms = new TransformGroup();
        canvasTransforms.Children.Add(_canvasScale);
        canvasTransforms.Children.Add(_canvasRotate);
        _canvas.RenderTransformOrigin = new Point(0.5, 0.5);
        _canvas.RenderTransform = canvasTransforms;
        _motion = new Motion.Handle(_canvasScale, null);
        _tabTrack.Child = _tabs;
        _iconGlyph.FontFamily = Theme.Icons;
        _hubContent.Children.Add(_icon);
        _hubContent.Children.Add(_iconGlyph);
        _hubContent.Children.Add(_title);
        _hubContent.Children.Add(_detail);
        _hubContent.Children.Add(_destination);
        _hubContent.Children.Add(_tabTrack);

        SourceInitialized += (_, _) =>
        {
            var flags = Win32.WS_EX_TOOLWINDOW | (mode == WheelMode.Drag ? Win32.WS_EX_NOACTIVATE : 0);
            Win32.AddExStyle(Handle, flags);
        };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            FitToWorkArea();
            PositionAtAnchor();
        });

        DragEnter += OnDragOver;
        DragOver += OnDragOver;
        DragLeave += (_, e) =>
        {
            SetHighlight(-1);
            e.Handled = true;
        };
        Drop += OnDrop;
        MouseMove += (sender, e) =>
        {
            if (Mode == WheelMode.Click)
                SetHighlight(HitTest(e.GetPosition(_canvas), out bool _));
        };
        MouseLeftButtonUp += OnClick;
        // Pressed micro-scale (90 ms): confirms the pointer landed on the slice before the action runs.
        PreviewMouseLeftButtonDown += (_, _) => PressHighlight(true);
        PreviewMouseLeftButtonUp += (_, _) => PressHighlight(false);
        KeyDown += OnKeyDown;
        Deactivated += (_, _) =>
        {
            if (Mode == WheelMode.Click)
                Finish();
        };

        LoadOperations();
        Build();
    }

    public WheelMode Mode { get; }

    public WheelKind CurrentWheel => _wheel;

    /// <summary>The last operation if it fits these items (for the "repeat last" view).</summary>
    public Func<IReadOnlyList<FileItem>, Operation?>? LastOperation { get; init; }

    /// <summary>Runs the last operation again with the values it used last time.</summary>
    public Action<IReadOnlyList<FileItem>>? RunRepeat { get; init; }

    /// <summary>Opens the chooser for a grouped parent slice; groups are never dispatched themselves.</summary>
    public Action<WheelGroupOperation, IReadOnlyList<FileItem>>? OpenGroup { get; init; }

    public bool IsFinished => _finished;

    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    public void ShowAt(Win32.POINT screenPoint)
    {
        _anchor = screenPoint;
        Show();
        FitToWorkArea();
        PositionAtAnchor();
        // The HWND settles its new size on the next layout pass; position once more before the
        // fade-in becomes visible (the window starts at Opacity 0).
        Dispatcher.BeginInvoke(PositionAtAnchor);
        if (Motion.Enabled)
        {
            Motion.Play(Motion.OpenWheel(this, _canvas, _motion), this);
        }
        else
        {
            Motion.Release(_canvas, _motion);
            Opacity = 1;
        }
        AnimateSlicesIn();

        if (Mode == WheelMode.Click)
        {
            ForegroundHelper.Force(this);
            Keyboard.Focus(this);
            if (_ops.Count > 0)
                SetHighlight(0);
        }
    }

    private void PositionAtAnchor()
    {
        if (_finished || !Win32.GetWindowRect(Handle, out var rect))
            return;
        var w = rect.Right - rect.Left;
        var h = rect.Bottom - rect.Top;
        var work = Win32.WorkAreaAt(_anchor);
        var x = Math.Clamp(_anchor.X - w / 2, work.Left, Math.Max(work.Left, work.Right - w));
        var y = Math.Clamp(_anchor.Y - h / 2, work.Top, Math.Max(work.Top, work.Bottom - h));
        Win32.SetWindowPos(Handle, new IntPtr(-1), x, y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Shrinks the disc when the work area is too small for it (for example a 1366×768 laptop at
    /// 200 % scaling). A LayoutTransform keeps the Canvas coordinate system unchanged, so the
    /// angular layout and hit testing stay exact; only the presented size changes.
    /// </summary>
    private void FitToWorkArea()
    {
        if (!Win32.GetWindowRect(Handle, out var rect))
            return;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
            return;
        var dpi = width / (Size * _fit);
        var work = Win32.WorkAreaAt(_anchor);
        var availableWidth = (work.Right - work.Left) / dpi;
        var availableHeight = (work.Bottom - work.Top) / dpi;
        var fit = Math.Clamp(Math.Min((availableWidth - 12) / Size, (availableHeight - 12) / Size), 0.55, 1.0);
        if (Math.Abs(fit - _fit) < 0.01)
            return;
        _fit = fit;
        _canvas.LayoutTransform = new ScaleTransform(fit, fit);
        Width = Size * fit;
        Height = Size * fit;
        UpdateLayout();
    }

    /// <summary>True when a physical screen point lies on the wheel.</summary>
    public bool Contains(Win32.POINT p)
    {
        if (!Win32.GetWindowRect(Handle, out var rect))
            return false;
        var scale = (rect.Right - rect.Left) / Size;
        var cx = (rect.Left + rect.Right) / 2.0;
        var cy = (rect.Top + rect.Bottom) / 2.0;
        var radius = (OuterRadius + Rim) * scale;
        return Math.Pow(p.X - cx, 2) + Math.Pow(p.Y - cy, 2) <= radius * radius;
    }

    public void SwitchTo(WheelKind wheel)
    {
        if (_finished)
            return;
        ApplyView(() =>
        {
            _wheel = wheel;
            _repeat = false;
        });
    }

    /// <summary>Tapping the same key again shows a single slice that repeats the last action.</summary>
    public void ToggleRepeat()
    {
        if (_finished)
            return;
        if (_repeat)
        {
            SwitchTo(_wheel);
            return;
        }
        ApplyView(() => _repeat = true);
    }

    /// <summary>
    /// Re-renders the open wheel after a live language switch. The slice geometry is untouched:
    /// only the labels, hub and tabs are rebuilt, and the current highlight is kept.
    /// </summary>
    public void Relocalize()
    {
        if (_finished)
            return;
        var highlight = _highlight;
        FlowDirection = LanguageManager.FlowDirection;
        _hubContent.FlowDirection = LanguageManager.FlowDirection;
        _tabs.FlowDirection = LanguageManager.FlowDirection;
        ApplyView(() => { }, () => { }, selectFirst: false);
        if (highlight >= 0 && _ops.Count > 0)
            SetHighlight(Math.Min(highlight, _ops.Count - 1));
    }

    /// <summary>
    /// Rebuilds the wheel's content. With motion on, the disc fades to 25 % and leans 4° while the mode
    /// changes, then fades back: a fade-through with a rotate, 170 ms in total (budget ≤ 180 ms).
    /// </summary>
    private void ApplyView(Action mutate, Action? after = null, bool selectFirst = true)
    {
        if (Motion.Enabled && IsVisible && !_finished)
        {
            if (_switching)
                CancelSwitch();
            _switching = true;
            AnimateCanvas(0.25, -Motion.SwitchRotateDegrees, Motion.SwitchMs * 0.4, Motion.EaseIn, () =>
            {
                if (_finished)
                    return;
                CommitView(mutate, after, selectFirst);
                AnimateCanvas(1, 0, Motion.SwitchMs * 0.6, Motion.EaseOut, () =>
                {
                    _switching = false;
                    _canvas.BeginAnimation(OpacityProperty, null);
                    _canvas.Opacity = 1;
                    _canvasRotate.BeginAnimation(RotateTransform.AngleProperty, null);
                    _canvasRotate.Angle = 0;
                });
            });
            return;
        }

        if (_switching)
            CancelSwitch();
        CommitView(mutate, after, selectFirst);
        _canvas.Opacity = 1;
        _canvasRotate.Angle = 0;
    }

    private void CommitView(Action mutate, Action? after, bool selectFirst)
    {
        mutate();
        LoadOperations();
        Build();
        AnimateSlicesIn();
        after?.Invoke();
        if (selectFirst && Mode == WheelMode.Click && _ops.Count > 0)
            SetHighlight(0);
    }

    private void AnimateCanvas(double opacity, double angle, double milliseconds, IEasingFunction ease, Action? completed)
    {
        var storyboard = new Storyboard();
        var fade = new DoubleAnimation(_canvas.Opacity, opacity, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd,
        };
        Storyboard.SetTarget(fade, _canvas);
        Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));
        storyboard.Children.Add(fade);
        var rotate = new DoubleAnimation(_canvasRotate.Angle, angle, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd,
        };
        Storyboard.SetTarget(rotate, _canvasRotate);
        Storyboard.SetTargetProperty(rotate, new PropertyPath(RotateTransform.AngleProperty));
        storyboard.Children.Add(rotate);
        if (completed is not null)
            storyboard.Completed += (_, _) => completed();
        storyboard.Begin(_canvas);
    }

    private void CancelSwitch()
    {
        _switching = false;
        _canvas.BeginAnimation(OpacityProperty, null);
        _canvas.Opacity = 1;
        _canvasRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        _canvasRotate.Angle = 0;
    }

    /// <summary>The drag ended somewhere: close unless the drop landed on us.</summary>
    public void OnDragEnded()
    {
        if (Mode != WheelMode.Drag || _finished)
            return;
        _closeTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Normal, (_, _) => Finish(), Dispatcher);
        _closeTimer.Start();
    }

    public void Finish()
    {
        if (_finished)
            return;
        _finished = true;
        _closeTimer?.Stop();
        if (_closing)
            return;
        _closing = true;
        CancelSwitch();
        if (!Motion.Enabled || !IsVisible || Dispatcher.HasShutdownStarted)
        {
            Close();
            return;
        }
        // ~100 ms: fade the window while the disc scales down by 3.5 %.
        Motion.Play(Motion.CloseWheel(this, _motion, Close), this);
    }

    /// <summary>Renders the wheel to a PNG without showing it (used by --self-test).</summary>
    public void RenderPreview(string file, int highlight)
    {
        _canvas.BeginAnimation(OpacityProperty, null);
        _canvas.Opacity = 1;
        _canvasScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _canvasScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _canvasScale.ScaleX = 1;
        _canvasScale.ScaleY = 1;
        _canvasRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        _canvasRotate.Angle = 0;
        _canvas.LayoutTransform = Transform.Identity;
        foreach (var group in _groups)
        {
            // Offscreen rendering doesn't advance animations; show the final state.
            group.BeginAnimation(OpacityProperty, null);
            var scale = (ScaleTransform)((TransformGroup)group.RenderTransform).Children[0];
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = 1;
            scale.ScaleY = 1;
        }
        if (highlight >= 0 && highlight < _ops.Count)
            SetHighlight(highlight);
        Content = null;
        var root = new Grid { Background = SelfTest.Desktop(), Width = Size, Height = Size, FlowDirection = LanguageManager.FlowDirection };
        root.Children.Add(_canvas);
        _canvas.HorizontalAlignment = HorizontalAlignment.Center;
        _canvas.VerticalAlignment = VerticalAlignment.Center;
        SelfTest.SaveElement(root, file);
    }

    /// <summary>
    /// Detaches the disc for the motion self-test and the website clips. The caller owns the returned
    /// element and can render it repeatedly at different poses without a window being shown.
    /// </summary>
    internal FrameworkElement DetachForCapture()
    {
        Content = null;
        foreach (var group in _groups)
        {
            group.BeginAnimation(OpacityProperty, null);
            var scale = (ScaleTransform)((TransformGroup)group.RenderTransform).Children[0];
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }
        var root = new Grid { Background = SelfTest.Desktop(), Width = Size, Height = Size, FlowDirection = LanguageManager.FlowDirection };
        root.Children.Add(_canvas);
        _canvas.HorizontalAlignment = HorizontalAlignment.Center;
        _canvas.VerticalAlignment = VerticalAlignment.Center;
        return root;
    }

    /// <summary>
    /// Applies the exact wheel pose for a point in time with no animation clocks: the open scale/fade,
    /// the 18 ms slice stagger, the 100 ms travelling highlight and the 100 ms close. This is the same
    /// math the storyboards run, evaluated deterministically for measurement and frame capture.
    /// </summary>
    internal void Pose(double timeMs, int highlight, int previousHighlight = -1, double travel = 1, double closeMs = 0)
    {
        var open = Math.Clamp(timeMs / Motion.WheelOpenMs, 0, 1);
        var close = Math.Clamp(closeMs / Motion.WheelCloseMs, 0, 1);
        var closeEase = Motion.EaseIn.Ease(close);
        var settled = Motion.Settle.Ease(open);
        var scale = (Motion.WheelScaleFrom + (1 - Motion.WheelScaleFrom) * settled) * (1 - 0.035 * closeEase);
        _canvasScale.ScaleX = scale;
        _canvasScale.ScaleY = scale;
        _canvas.Opacity = Motion.SoftOut.Ease(open) * (1 - closeEase);

        for (var i = 0; i < _groups.Count; i++)
        {
            var local = Math.Clamp((timeMs - i * Motion.StaggerMs) / Motion.SliceMs, 0, 1);
            var eased = Motion.EaseOut.Ease(local);
            _groups[i].Opacity = eased;
            var sliceScale = (ScaleTransform)((TransformGroup)_groups[i].RenderTransform).Children[0];
            sliceScale.ScaleX = 0.82 + 0.18 * eased;
            sliceScale.ScaleY = sliceScale.ScaleX;
            var move = (TranslateTransform)((TransformGroup)_groups[i].RenderTransform).Children[1];
            move.X = 0;
            move.Y = 0;
        }

        if (highlight < 0 || highlight >= _ops.Count)
            return;
        SetHighlight(highlight);
        Offset(highlight, travel);
        if (previousHighlight >= 0 && previousHighlight != highlight)
            Offset(previousHighlight, 1 - travel);

        void Offset(int index, double amount)
        {
            if (index < 0 || index >= _groups.Count || _ops.Count <= 1)
                return;
            var angle = index * 360.0 / _ops.Count * Math.PI / 180;
            var move = (TranslateTransform)((TransformGroup)_groups[index].RenderTransform).Children[1];
            move.X = PopOut * amount * Math.Sin(angle);
            move.Y = -PopOut * amount * Math.Cos(angle);
        }
    }

    // ------------------------------------------------------------------ data

    private void LoadOperations()
    {
        _ops = _items.Count == 0 ? []
            : _repeat ? (LastOperation?.Invoke(_items) is { } last ? [last] : [])
            : _source(_wheel, _items);
        _highlight = -1;
    }


    // ------------------------------------------------------------------ drag & drop

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_items.Count == 0)
        {
            var preview = DropContent.Read(e.Data, materialize: false);
            if (preview is null)
            {
                e.Effects = DragDropEffects.None;
                return;
            }
            _items = preview.Items;
            LoadOperations();
            Build();
            AnimateSlicesIn();
        }
        _closeTimer?.Stop();
        var index = HitTest(e.GetPosition(_canvas), out _);
        SetHighlight(index);
        e.Effects = index >= 0 ? SafeEffect(e.AllowedEffects) : DragDropEffects.None;
    }

    /// <summary>
    /// Copy keeps the original untouched. Never report Move: the drag source would delete the file.
    /// </summary>
    private static DragDropEffects SafeEffect(DragDropEffects allowed) =>
        (allowed & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy
        : (allowed & DragDropEffects.Link) != 0 ? DragDropEffects.Link
        : DragDropEffects.None;

    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var index = HitTest(e.GetPosition(_canvas), out _);
        var op = index >= 0 && index < _ops.Count ? _ops[index] : null;
        e.Effects = op is null ? DragDropEffects.None : SafeEffect(e.AllowedEffects);
        // Virtual files and images can only be read while the drop is in progress.
        var content = op is null ? null : DropContent.Read(e.Data, materialize: true);
        Finish();
        if (op is not null && content is { Items.Count: > 0 })
        {
            // Return to the drag source right away; do the work afterwards.
            Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    if (content.Prepare is not null)
                        await content.Prepare();
                }
                catch (Exception ex)
                {
                    Shell.Notifications.Show(T("error.download.failed"), ex.Message, error: true);
                    return;
                }
                if (op is WheelGroupOperation group)
                    OpenGroup?.Invoke(group, content.Items);
                else
                    Dispatch(op, content.Items);
            });
        }
    }

    // ------------------------------------------------------------------ mouse & keyboard (click mode)

    private void OnClick(object sender, MouseButtonEventArgs e)
    {
        if (Mode != WheelMode.Click || e.Handled)
            return;
        var index = HitTest(e.GetPosition(_canvas), out var inHub);
        if (index >= 0)
            Run(index);
        else if (inHub)
            Finish();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        e.Handled = true;
        switch (key)
        {
            case Key.Escape:
                Finish();
                break;
            case Key.Left or Key.Up:
                Move(-1);
                break;
            case Key.Right or Key.Down:
                Move(1);
                break;
            case Key.Enter or Key.Space:
                if (_highlight >= 0)
                    Run(_highlight);
                break;
            case Key.Tab:
                var next = (Array.IndexOf(WheelOrder, _wheel) + (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 2 : 1)) % WheelOrder.Length;
                SwitchTo(WheelOrder[next]);
                break;
            case Key.LeftShift or Key.RightShift:
                SwitchToKey(Input.ModifierKey.Shift);
                break;
            case Key.LeftCtrl or Key.RightCtrl:
                SwitchToKey(Input.ModifierKey.Ctrl);
                break;
            case Key.LeftAlt or Key.RightAlt:
                SwitchToKey(Input.ModifierKey.Alt);
                break;
            case Key.R:
                ToggleRepeat();
                break;
            case >= Key.D1 and <= Key.D9:
                if (key - Key.D1 < _ops.Count)
                    Run(key - Key.D1);
                break;
            default:
                e.Handled = false;
                break;
        }
    }

    private void SwitchToKey(Input.ModifierKey key)
    {
        if (_settings.WheelFor(key) is { } wheel && wheel != _wheel)
            SwitchTo(wheel);
    }

    private void Move(int delta)
    {
        if (_ops.Count == 0)
            return;
        SetHighlight(_highlight < 0 ? 0 : ((_highlight + delta) % _ops.Count + _ops.Count) % _ops.Count);
    }

    private void Run(int index)
    {
        var op = _ops[index];
        var items = _items;
        if (op is WheelGroupOperation group)
        {
            // Keep the wheel open when there is nothing to open the chooser with, rather than losing the
            // drop on a parent that cannot expand.
            if (OpenGroup is { } open)
            {
                Finish();
                open(group, items);
            }
            return;
        }
        Finish();
        Dispatch(op, items);
    }

    private void Dispatch(Operation op, IReadOnlyList<FileItem> items)
    {
        if (_repeat && RunRepeat is not null)
            RunRepeat(items);
        else
            _execute(op, items, _targets);
    }


    // ------------------------------------------------------------------ geometry

    private static Point Polar(double radius, double degrees)
    {
        var rad = degrees * Math.PI / 180;
        return new Point(Size / 2 + radius * Math.Sin(rad), Size / 2 - radius * Math.Cos(rad));
    }

    private static double Deg(double radians) => radians * 180 / Math.PI;

    private int HitTest(Point p, out bool inHub)
    {
        var dx = p.X - Size / 2;
        var dy = p.Y - Size / 2;
        var r = Math.Sqrt(dx * dx + dy * dy);
        inHub = r < InnerRadius;
        if (inHub || r > OuterRadius + Rim || _ops.Count == 0)
            return -1;
        var angle = (Math.Atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
        var step = 360.0 / _ops.Count;
        return (int)(((angle + step / 2) % 360) / step) % _ops.Count;
    }

    /// <summary>
    /// A ring segment between angles <paramref name="a0"/> and <paramref name="a1"/> with a constant-width gap
    /// to its neighbours and rounded corners.
    /// </summary>
    private static Geometry Sector(double a0, double a1)
    {
        // Angle of a point on an edge that is shifted sideways by half the gap, at radius r.
        double Edge(double r, double a, int side) => a + side * Deg(Math.Asin(Gap / 2 / r));

        var outerStart = Edge(OuterRadius, a0, 1);
        var outerEnd = Edge(OuterRadius, a1, -1);
        var innerStart = Edge(InnerRadius, a0, 1);
        var innerEnd = Edge(InnerRadius, a1, -1);
        var cOut = Math.Min(Corner, Rad(outerEnd - outerStart) * OuterRadius * 0.45);
        var cIn = Math.Min(Corner * 0.7, Rad(innerEnd - innerStart) * InnerRadius * 0.45);
        var dOut = Deg(cOut / OuterRadius);
        var dIn = Deg(cIn / InnerRadius);

        var start = Polar(OuterRadius, outerStart + dOut);
        var figure = new PathFigure { StartPoint = start, IsClosed = true, IsFilled = true };
        figure.Segments.Add(new ArcSegment(Polar(OuterRadius, outerEnd - dOut), new System.Windows.Size(OuterRadius, OuterRadius), 0,
            outerEnd - outerStart - 2 * dOut > 180, SweepDirection.Clockwise, true));
        figure.Segments.Add(new QuadraticBezierSegment(Polar(OuterRadius, outerEnd), Polar(OuterRadius - cOut, Edge(OuterRadius - cOut, a1, -1)), true));
        figure.Segments.Add(new LineSegment(Polar(InnerRadius + cIn, Edge(InnerRadius + cIn, a1, -1)), true));
        figure.Segments.Add(new QuadraticBezierSegment(Polar(InnerRadius, innerEnd), Polar(InnerRadius, innerEnd - dIn), true));
        figure.Segments.Add(new ArcSegment(Polar(InnerRadius, innerStart + dIn), new System.Windows.Size(InnerRadius, InnerRadius), 0,
            innerEnd - innerStart - 2 * dIn > 180, SweepDirection.Counterclockwise, true));
        figure.Segments.Add(new QuadraticBezierSegment(Polar(InnerRadius, innerStart), Polar(InnerRadius + cIn, Edge(InnerRadius + cIn, a0, 1)), true));
        figure.Segments.Add(new LineSegment(Polar(OuterRadius - cOut, Edge(OuterRadius - cOut, a0, 1)), true));
        figure.Segments.Add(new QuadraticBezierSegment(Polar(OuterRadius, outerStart), start, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;

        static double Rad(double degrees) => degrees * Math.PI / 180;
    }

    private static Geometry Ring()
    {
        var center = new Point(Size / 2, Size / 2);
        var ring = new CombinedGeometry(GeometryCombineMode.Exclude,
            new EllipseGeometry(center, OuterRadius, OuterRadius),
            new EllipseGeometry(center, InnerRadius, InnerRadius));
        ring.Freeze();
        return ring;
    }

    // ------------------------------------------------------------------ visuals

    private static TextBlock HubText(double size, FontWeight weight) => new()
    {
        FontSize = size,
        FontWeight = weight,
        TextAlignment = TextAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        HorizontalAlignment = HorizontalAlignment.Center,
        MaxWidth = InnerRadius * 1.65,
    };

    private void Build()
    {
        _canvas.Children.Clear();
        _slices.Clear();
        _labels.Clear();
        _groups.Clear();

        var disc = OuterRadius + Rim;
        var backdrop = new Ellipse
        {
            Width = 2 * disc,
            Height = 2 * disc,
            Fill = Theme.Backdrop,
            Stroke = Theme.BackdropEdge,
            StrokeThickness = 1,
            Effect = Theme.Shadow(28),
        };
        Canvas.SetLeft(backdrop, Pad);
        Canvas.SetTop(backdrop, Pad);
        _canvas.Children.Add(backdrop);

        var n = _ops.Count;
        var step = n == 0 ? 360 : 360.0 / n;
        // A crowded wheel (19+ real tool slices) cannot fit icon + title + subtitle at the old radius:
        // the three lines reach back into the hub and the neighbours. Move the text outward and drop the
        // secondary lines, keeping only the operation name.
        var compact = n > 15;
        var labelRadius = InnerRadius + (OuterRadius - InnerRadius) * (compact ? 0.66 : 0.54);
        var labelWidth = n <= 2 ? 120 : Math.Clamp(
            2 * labelRadius * Math.Sin(step / 2 * Math.PI / 180) * (compact ? 0.92 : 0.84),
            compact ? 44 : 48, 110);
        for (var i = 0; i < n; i++)
        {
            var a0 = i * step - step / 2;
            var path = new Path
            {
                Data = n == 1 ? Ring() : Sector(a0, a0 + step),
                Fill = Theme.Slice,
                Stroke = Theme.SliceEdge,
                StrokeThickness = 1,
            };
            var label = SliceLabel(_ops[i], compact);
            var fit = new Viewbox
            {
                Child = label,
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                MaxWidth = labelWidth,
                MaxHeight = (OuterRadius - InnerRadius) * (compact ? 0.58 : 0.8),
                IsHitTestVisible = false,
            };
            var mid = n == 1 ? new Point(Size / 2, Size / 2 - labelRadius) : Polar(labelRadius, i * step);
            fit.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(fit, mid.X - fit.DesiredSize.Width / 2);
            Canvas.SetTop(fit, mid.Y - fit.DesiredSize.Height / 2);

            // Slice and label move together when the slice pops out.
            var group = new Canvas { Width = Size, Height = Size };
            var transforms = new TransformGroup();
            transforms.Children.Add(new ScaleTransform(1, 1, Size / 2, Size / 2));
            transforms.Children.Add(new TranslateTransform());
            group.RenderTransform = transforms;
            group.Children.Add(path);
            group.Children.Add(fit);
            _slices.Add(path);
            _labels.Add(label);
            _groups.Add(group);
            _canvas.Children.Add(group);
        }

        var hubSize = 2 * (InnerRadius - 6);
        var hub = new Ellipse
        {
            Width = hubSize,
            Height = hubSize,
            Fill = Theme.Hub,
            Stroke = Theme.PanelBorder,
            StrokeThickness = 1,
            Effect = Theme.Shadow(12),
        };
        Canvas.SetLeft(hub, Size / 2 - hubSize / 2);
        Canvas.SetTop(hub, Size / 2 - hubSize / 2);
        _canvas.Children.Add(hub);

        BuildHub();
        _hubContent.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_hubContent, Size / 2 - _hubContent.Width / 2);
        Canvas.SetTop(_hubContent, Size / 2 - _hubContent.DesiredSize.Height / 2);
        _canvas.Children.Add(_hubContent);
    }

    /// <summary>Slices grow out of the centre one after another, 18 ms apart.</summary>
    private void AnimateSlicesIn()
    {
        for (var i = 0; i < _groups.Count; i++)
        {
            var scale = (ScaleTransform)((TransformGroup)_groups[i].RenderTransform).Children[0];
            if (!Motion.Enabled)
            {
                _groups[i].BeginAnimation(OpacityProperty, null);
                _groups[i].Opacity = 1;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleX = 1;
                scale.ScaleY = 1;
                continue;
            }
            Motion.Play(Motion.Stagger(_groups[i], i), _canvas);
        }
    }

    private static StackPanel SliceLabel(Operation op, bool compact)
    {
        var isFormat = op.Wheel == WheelKind.Convert && op.Label.Length <= 6 && op.Label.ToUpperInvariant() == op.Label;
        // Slices that first download a tool are shown a little faded. Format names such as MP4 stay
        // left-to-right even in Arabic, because they are identifiers rather than prose.
        var panel = new StackPanel
        {
            MaxWidth = compact ? 74 : 96,
            IsHitTestVisible = false,
            Opacity = op is Shell.NeedsToolOperation ? 0.6 : 1,
            FlowDirection = isFormat ? System.Windows.FlowDirection.LeftToRight : LanguageManager.FlowDirection,
        };
        if (!isFormat && !compact)
        {
            panel.Children.Add(new TextBlock
            {
                Text = op.Glyph,
                FontFamily = Theme.Icons,
                FontSize = 20,
                Foreground = Theme.Text,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 5),
            });
        }
        panel.Children.Add(new TextBlock
        {
            Text = op.Label,
            FontFamily = Theme.DisplayFont,
            FontSize = isFormat ? 19 : compact ? 11.5 : 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Text,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        if (!compact && !string.IsNullOrEmpty(op.Subtitle))
        {
            panel.Children.Add(new TextBlock
            {
                Text = op.Subtitle,
                Tag = "subtitle",
                FontSize = 10.5,
                Foreground = Theme.Muted,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0),
            });
        }
        if (op is WheelGroupOperation)
        {
            // The chevron says this slice opens a chooser instead of running something itself.
            panel.Children.Add(new TextBlock
            {
                Text = "\uE76C",
                FontFamily = Theme.Icons,
                FontSize = 12,
                Foreground = Theme.Muted,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }
        return panel;
    }

    private void BuildHub()
    {
        _title.Foreground = Theme.Text;
        _detail.Foreground = Theme.Muted;
        _tabTrack.Background = Theme.Track;

        if (_repeat)
        {
            _icon.Visibility = Visibility.Collapsed;
            _iconGlyph.Text = "\uE8EE"; // RepeatAll
            _iconGlyph.Foreground = Theme.Accent;
            _iconGlyph.Visibility = Visibility.Visible;
        }
        else if (_items.Count > 0)
        {
            _icon.Source = Shell.FileIcons.Get(_items[0].Path);
            _icon.Visibility = _icon.Source is null ? Visibility.Collapsed : Visibility.Visible;
            _iconGlyph.Visibility = Visibility.Collapsed;
        }
        else
        {
            _icon.Visibility = Visibility.Collapsed;
            _iconGlyph.Text = Glyphs.Extract;
            _iconGlyph.Foreground = Theme.Accent;
            _iconGlyph.Visibility = Visibility.Visible;
        }

        _tabs.Children.Clear();
        foreach (var wheel in WheelOrder)
        {
            var key = _settings.KeyFor(wheel);
            if (key == Input.ModifierKey.None && wheel != _wheel)
                continue;
            var active = wheel == _wheel;
            var tab = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(7, 1, 7, 2),
                Background = active ? Theme.AccentGradient : Brushes.Transparent,
                Cursor = Mode == WheelMode.Click ? Cursors.Hand : null,
                ToolTip = UiSetup.Tip(AppSettings.WheelName(wheel)),
                Child = new TextBlock
                {
                    Text = key == Input.ModifierKey.None ? AppSettings.WheelName(wheel) : AppSettings.KeyName(key),
                    FontSize = 10.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = active ? Theme.TextOnAccent : Theme.Muted,
                },
            };
            if (Mode == WheelMode.Click)
            {
                var target = wheel;
                tab.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    SwitchTo(target);
                };
            }
            _tabs.Children.Add(tab);
        }
        UpdateHubText();
    }

    private void PressHighlight(bool down)
    {
        if (!Motion.Enabled || _finished)
            return;
        var index = down ? _highlight : _pressed;
        if (index < 0 || index >= _groups.Count)
            return;
        var scale = (ScaleTransform)((TransformGroup)_groups[index].RenderTransform).Children[0];
        Motion.Press(scale, down);
        _pressed = down ? index : -1;
    }

    private void SetHighlight(int index)
    {
        if (index == _highlight)
            return;
        if (_highlight >= 0 && _highlight < _slices.Count)
            Paint(_highlight, false);
        _highlight = index;
        if (_highlight >= 0 && _highlight < _slices.Count)
            Paint(_highlight, true);
        UpdateHubText();
    }

    private void Paint(int index, bool on)
    {
        var slice = _slices[index];
        slice.Fill = on ? Theme.AccentGradient : Theme.Slice;
        slice.Stroke = on ? Brushes.Transparent : Theme.SliceEdge;
        slice.Effect = on ? Theme.Glow() : null;
        foreach (var child in _labels[index].Children.OfType<TextBlock>())
        {
            var subtitle = Equals(child.Tag, "subtitle");
            child.Foreground = on ? Theme.TextOnAccent : subtitle ? Theme.Muted : Theme.Text;
            child.Opacity = on && subtitle ? 0.85 : 1;
        }

        // Pop the slice outwards along its middle angle; 100 ms, so the highlight visibly travels.
        var n = _ops.Count;
        var angle = n <= 1 ? 0 : index * 360.0 / n * Math.PI / 180;
        var distance = on && n > 1 ? PopOut : 0;
        var move = (TranslateTransform)((TransformGroup)_groups[index].RenderTransform).Children[1];
        var x = distance * Math.Sin(angle);
        var y = -distance * Math.Cos(angle);
        if (IsVisible && Motion.Enabled)
        {
            Motion.Slide(move, x, y);
        }
        else
        {
            move.BeginAnimation(TranslateTransform.XProperty, null);
            move.BeginAnimation(TranslateTransform.YProperty, null);
            move.X = x;
            move.Y = y;
        }
    }

    /// <summary>
    /// Says where the result will go before the drop: the destination folder, "next to the file", or a
    /// warning when the folder refuses a write. The full path is the tooltip.
    /// </summary>
    public void SetDestination(string? text, string? path, bool warning)
    {
        _destination.Text = text ?? string.Empty;
        _destination.ToolTip = string.IsNullOrWhiteSpace(path) ? null : path;
        _destination.Foreground = warning ? Theme.Error : Theme.Muted;
        _destination.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateHubText()
    {
        if (_repeat)
        {
            var last = _ops.Count > 0 ? _ops[0] : null;
            _title.Text = _highlight >= 0 && last is not null ? last.Label : T("wheel.repeat");
            _detail.Text = last is null
                ? T("wheel.repeat.noFit")
                : _highlight >= 0 ? T("wheel.repeat.suffix") : last.Label;
            return;
        }
        if (_highlight >= 0 && _highlight < _ops.Count)
        {
            var op = _ops[_highlight];
            _title.Text = op.Label;
            _detail.Text = op.Subtitle ?? DescribeItems();
            return;
        }
        _title.Text = AppSettings.WheelName(_wheel);
        if (_items.Count == 0)
            _detail.Text = T("wheel.dropHere");
        else if (_ops.Count == 0)
            _detail.Text = T("wheel.nothingForSelection");
        else
            _detail.Text = DescribeItems();
    }

    private string DescribeItems() => _items.Count == 1 ? Bidi.Value(_items[0].Name) : Items(_items.Count);
}
