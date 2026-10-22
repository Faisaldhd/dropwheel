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

/// <summary>Where a pointer position falls inside the two-ring wheel.</summary>
internal enum WheelZone
{
    None,
    Hub,
    Inner,
    Outer,
}

/// <summary>What one Escape press does: step back one level, then close.</summary>
internal enum WheelEscape
{
    CancelDwell,
    ClearInner,
    Close,
}

/// <summary>
/// One of the inner-ring choices: an operation, or the compact "more" slice that turns the page when a
/// category owns more children than the inner ring can show at once. <see cref="Hidden"/> is how many
/// siblings are on the other pages, so the slice can read "+2" without a new localised string.
/// </summary>
internal readonly record struct InnerChoice(Operation? Operation, int Hidden)
{
    public bool IsMore => Operation is null;
}

/// <summary>
/// The radial menu. One instance per appearance. Two concentric rings share the window: the outer ring
/// always shows the view's slices (the seven intent categories on the Tools wheel), and the inner ring
/// appears between the hub and the outer ring with one category's children. Nothing is swapped or
/// hidden: the category keeps its slice, the children are an overlay, and Escape peels the overlay first.
/// </summary>
internal sealed class WheelWindow : Window
{
    /// <summary>What a drag release over the wheel does.</summary>
    internal enum DropAction
    {
        /// <summary>The hub, the rim, the empty background or a "+n" pager: the drop cancels.</summary>
        Nothing,

        /// <summary>An operation slice or an inner child: run that operation.</summary>
        Run,

        /// <summary>An outer category slice: open its chooser, exactly like a click.</summary>
        OpenCategory,
    }

    /// <summary>The operation a drop targets and whether it runs directly or opens a category.</summary>
    internal readonly record struct DropTarget(DropAction Action, Operation? Operation);

    public delegate IReadOnlyList<Operation> OperationSource(WheelKind wheel, IReadOnlyList<FileItem> items);

    // ------------------------------------------------------------------ geometry
    // The ring is deliberately bigger than the 2.4.4 disc (442 DIP): the owner prefers a larger wheel
    // over smaller type. Every label is clipped to its own slice, so text can never leave its frame.
    internal const double OuterRadius = 206;
    internal const double OuterInnerRadius = 130;
    internal const double InnerOuterRadius = 122;
    internal const double InnerInnerRadius = 74;
    internal const double HubRadius = 68;
    internal const double Rim = 9;
    private const double Gap = 6;
    private const double Corner = 11;
    private const double PopOut = 7;
    private const double InnerPopOut = 5;

    /// <summary>Room around the disc for its shadow.</summary>
    private const double Pad = 16;

    /// <summary>The canvas edge. 462 DIP ≈ 693 px at 150 % scaling (the 2.4.4 disc was 663 px).</summary>
    private const double Size = 2 * (OuterRadius + Rim + Pad);
    private static readonly WheelKind[] WheelOrder = [WheelKind.Convert, WheelKind.Tools, WheelKind.Actions];

    // ------------------------------------------------------------------ label budgets
    internal const double OuterLabelRadius = (OuterInnerRadius + OuterRadius) / 2;
    // 0.84 instead of 0.78: two lines at the 10 DIP floor plus an icon and a subtitle only just fit the
    // 76 DIP outer band at the old budget, so long names had to be shrunk to unreadable type. The extra
    // 4.5 DIP still leaves more than 5 DIP to either ring edge.
    internal const double OuterLabelMaxHeight = (OuterRadius - OuterInnerRadius) * 0.84;
    internal const double InnerLabelRadius = (InnerInnerRadius + InnerOuterRadius) / 2;
    internal const double InnerLabelMaxHeight = (InnerOuterRadius - InnerInnerRadius) * 0.7;

    /// <summary>Clearance between a label box and the ring circles, in DIP.</summary>
    internal const double LabelEdgeInset = 2.5;

    /// <summary>
    /// Fitting reserve: labels are laid out this much shorter than the geometric ceiling. Attaching a
    /// measured TextBlock to the visual tree can add a few tenths of a DIP through layout rounding; the
    /// reserve keeps that growth from pushing the box past the ring edge.
    /// </summary>
    internal const double LabelBuildMargin = 1.0;

    internal const int OuterLabelMaxLines = 3;
    internal const int InnerLabelMaxLines = 2;

    internal const double OuterTitleMaxFont = 13;
    internal const double OuterTitleMinFont = 9.5;
    internal const double OuterFormatMaxFont = 19;
    internal const double OuterFormatMinFont = 12;
    internal const double OuterCompactMaxFont = 11.5;
    internal const double OuterCompactMinFont = 9.0;
    internal const double OuterSubtitleMaxFont = 10.5;
    internal const double OuterSubtitleMinFont = 8.5;
    internal const double OuterIconMaxFont = 19;
    internal const double OuterIconMinFont = 14;
    internal const double InnerMaxFont = 10.5;
    internal const double InnerMinFont = 8.5;
    internal const double MoreMaxFont = 14;
    internal const double MoreMinFont = 10;

    /// <summary>At most five children plus the "more" slice: six labels on a 60° step, never crowded.</summary>
    internal const int InnerCapacity = 6;

    internal const int InnerPerPage = 5;

    /// <summary>
    /// Keeps the current inner slice selected for this many degrees past its midpoint. Without it the
    /// highlight flips on every pixel at a slice boundary and the small ring feels twitchy.
    /// </summary>
    internal const double InnerHysteresisDeg = 8;

    // ------------------------------------------------------------------ motion budget
    /// <summary>Hovering a category opens it only after the pointer rests on it this long.</summary>
    internal const double DwellMs = 450;

    /// <summary>
    /// With an inner ring open, crossing another outer slice must not swap the category on a single
    /// mouse sample: the pointer often crosses the rim/hub on its way to the far inner tools. Swap only
    /// after this short rest.
    /// </summary>
    internal const double SwapDwellMs = 180;

    /// <summary>First frame of an inner ring is already visible; the entrance finishes inside 120 ms.</summary>
    internal const double InnerEnterMs = 90;

    internal const double InnerEnterStaggerMs = 5;
    internal const double InnerRevealFrom = 0.4;
    internal const double InnerScaleFrom = 0.9;

    /// <summary>A category swap fades the old ring and grows the new one in 100 ms in total.</summary>
    internal const double InnerSwapOutMs = 45;

    internal const double InnerSwapInMs = 55;

    /// <summary>Escape clears the inner ring in under 100 ms; the wheel itself stays open.</summary>
    internal const double InnerExitMs = 90;

    internal const double DwellTickMs = 33;

    private readonly AppSettings _settings;
    private readonly OperationSource _source;
    private readonly Action<Operation, IReadOnlyList<FileItem>, DropTargets?> _execute;
    private readonly DropTargets? _targets;
    private readonly Canvas _canvas = new() { Width = Size, Height = Size };
    private readonly ScaleTransform _canvasScale = new(1, 1);
    private readonly RotateTransform _canvasRotate = new(0, Size / 2, Size / 2);
    private readonly ScaleTransform _innerScale = new(1, 1, Size / 2, Size / 2);
    private readonly Canvas _innerLayer = new() { Width = Size, Height = Size, IsHitTestVisible = false };
    private readonly Canvas _innerFadeLayer = new() { Width = Size, Height = Size, IsHitTestVisible = false };
    private readonly Canvas _hubLayer = new() { Width = Size, Height = Size, IsHitTestVisible = false };
    private readonly Motion.Handle _motion;
    private readonly List<Path> _slices = [];
    private readonly List<StackPanel> _labels = [];
    private readonly List<Canvas> _groups = [];
    private readonly List<Canvas> _innerGroups = [];
    private readonly List<Path> _innerSlices = [];
    private readonly List<StackPanel> _innerLabels = [];
    private readonly TextBlock _iconGlyph = new() { FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 3) };
    // The hub shows the format of the selection, not the Windows shell icon: one item is its extension
    // (JPG, PDF, MP4), a folder is DIR and a multi-file drop is FILES. Drawn as a small folder card.
    private readonly Grid _formatBadge = new()
    {
        Width = 46,
        Height = 31,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 0, 0, 3),
        IsHitTestVisible = false,
    };
    private readonly Border _formatTab = new()
    {
        Width = 17,
        Height = 8,
        CornerRadius = new CornerRadius(4, 4, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(5, 0, 0, 0),
    };
    private readonly Border _formatBody = new()
    {
        Width = 46,
        Height = 23,
        CornerRadius = new CornerRadius(5),
        BorderThickness = new Thickness(1),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Bottom,
    };
    private readonly TextBlock _formatText = new()
    {
        FontFamily = Theme.DisplayFont,
        FontSize = 10,
        FontWeight = FontWeights.Bold,
        Foreground = Theme.TextOnAccent,
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxWidth = 39,
        IsHitTestVisible = false,
    };
    private readonly TextBlock _title = HubText(13.5, FontWeights.SemiBold);
    private readonly TextBlock _detail = HubText(10, FontWeights.Normal);
    private readonly TextBlock _destination = HubText(9, FontWeights.Normal);
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
    private readonly Border _tabTrack = new() { CornerRadius = new CornerRadius(9), Padding = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
    private readonly StackPanel _hubContent = new() { Width = HubRadius * 1.4 };
    private Action? _afterClose;
    private Storyboard? _reveal;
    private Storyboard? _innerStoryboard;
    private DispatcherTimer? _dwellTimer;
    private DispatcherTimer? _swapTimer;
    private int _swapIndex = -1;
    private Path? _dwellArc;
    private PathFigure? _dwellFigure;
    private ArcSegment? _dwellArcSegment;
    private double _dwellStartAngle;
    private double _dwellSpan;
    private int _dwellIndex = -1;
    private long _dwellStarted;
    private int _suppressIndex = -1;

    private IReadOnlyList<FileItem> _items;
    private IReadOnlyList<Operation> _ops = [];
    private IReadOnlyList<Operation> _innerChildren = [];
    private IReadOnlyList<InnerChoice> _innerChoices = [];
    private string? _innerGroupId;
    private int _innerPage;
    private WheelKind _wheel;
    private int _highlight = -1;
    private int _innerHighlight = -1;
    private Win32.POINT _anchor;
    private bool _finished;
    private bool _repeat;
    private bool _switching;
    private bool _closing;
    private int _pressed = -1;
    private double _fit = 1.0;
    private double? _revealFrom;
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
        // The inner ring fades/scales as one layer when it is cleared or swapped; the children carry their
        // own per-slice reveal scale so their labels never move sideways into a neighbour.
        _innerLayer.RenderTransform = _innerScale;
        // The hub is a disc, so its text is clipped to a disc too: a long category name or file name
        // ellipsizes instead of running over the inner ring.
        _hubLayer.Clip = new EllipseGeometry(new Point(Size / 2, Size / 2), HubRadius - 5, HubRadius - 5);
        _tabTrack.Child = _tabs;
        _iconGlyph.FontFamily = Theme.Icons;
        _formatBody.Child = _formatText;
        _formatBadge.Children.Add(_formatTab);
        _formatBadge.Children.Add(_formatBody);
        _hubContent.Children.Add(_formatBadge);
        _hubContent.Children.Add(_iconGlyph);
        _hubContent.Children.Add(_title);
        _hubContent.Children.Add(_detail);
        _hubContent.Children.Add(_destination);
        _hubContent.Children.Add(_tabTrack);
        _hubLayer.Children.Add(_hubContent);

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
        DragLeave += OnDragLeave;
        Drop += OnDrop;
        // Both modes follow the pointer: Click gets MouseMove, Drag gets its pointer from
        // DragEnter/DragOver as well. Sharing the handler keeps the 450 ms dwell identical in both.
        MouseMove += (_, e) => PointerMove(e.GetPosition(_canvas));
        MouseLeave += (_, _) =>
        {
            CancelDwell();
            CancelSwapDwell();
            SetHighlight(-1);
            SetInnerHighlight(-1);
            // In Click mode leaving the window means leaving the disc: the ring goes with it. During a
            // drag the inner ring is seeded by the drop and a leave may just be the OLE drag churn, so
            // only a real exit past the rim clears it (see OnDragLeave).
            if (Mode == WheelMode.Click && !_finished && !KeepsInnerRing(HitAt(Mouse.GetPosition(_canvas)).Zone))
                ClearInner();
        };
        MouseLeftButtonUp += OnClick;
        // Pressed micro-scale (90 ms): confirms the pointer landed on the slice before the action runs.
        PreviewMouseLeftButtonDown += (_, _) => PressHighlight(true);
        PreviewMouseLeftButtonUp += (_, _) => PressHighlight(false);
        KeyDown += OnKeyDown;
        Closed += (_, _) =>
        {
            CancelDwell();
            CancelSwapDwell();
        };
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

    /// <summary>
    /// Opens a category chooser after a drop. A drop has already closed the parent window, so the
    /// controller re-opens the same two-ring wheel at the same anchor with this category's inner ring
    /// seeded: every outer slice is still there, nothing is replaced.
    /// </summary>
    public Action<WheelGroupOperation, IReadOnlyList<FileItem>, int, double>? OpenGroup { get; init; }

    public bool IsFinished => _finished;

    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    /// <summary>
    /// Shows the wheel. A category id seeds the same window with that category's inner ring already open
    /// (used after a drop closed the parent); the outer ring is always the full view.
    /// </summary>
    public void ShowAt(Win32.POINT screenPoint, string? groupId = null, int parentIndex = -1,
        double? revealFromAngle = null)
    {
        _anchor = screenPoint;
        _revealFrom = revealFromAngle;
        if (groupId is not null)
        {
            var index = parentIndex >= 0 ? parentIndex : GroupIndex(_ops, groupId);
            if (index >= 0 && _ops[index] is WheelGroupOperation group)
            {
                _innerGroupId = group.GroupId;
                _innerPage = 0;
                _innerChildren = group.Children;
                _revealFrom = revealFromAngle ?? SliceAngle(index, _ops.Count);
                LoadOperations();
                Build();
            }
        }

        Show();
        FitToWorkArea();
        PositionAtAnchor();
        // The HWND settles its new size on the next layout pass; position once more before the
        // fade-in becomes visible (the window starts at Opacity 0).
        Dispatcher.BeginInvoke(PositionAtAnchor);
        if (Motion.Enabled)
        {
            // A seeded category grows out of its parent slice instead of out of the disc centre.
            if (_revealFrom is { } reveal)
                SetCanvasScaleOrigin(reveal);
            Motion.Play(Motion.OpenWheel(this, _canvas, _motion), this);
        }
        else
        {
            Motion.Release(_canvas, _motion);
            Opacity = 1;
        }
        AnimateSlicesIn(_revealFrom);
        if (_innerGroupId is not null)
            AnimateInnerIn(_revealFrom ?? 0);

        if (Mode == WheelMode.Click)
        {
            ForegroundHelper.Force(this);
            Keyboard.Focus(this);
            if (_ops.Count > 0)
            {
                // A seeded category keeps its own slice highlighted; the plain wheel starts at the top.
                var start = _innerGroupId is { } id ? Math.Max(0, GroupIndex(_ops, id)) : 0;
                SetHighlight(Math.Min(start, _ops.Count - 1));
            }
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
            ClearInnerState();
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
        ApplyView(() =>
        {
            ClearInnerState();
            _repeat = true;
        });
    }

    /// <summary>
    /// Re-renders the open wheel after a live language switch. The geometry is untouched: only the
    /// labels, hub and tabs are rebuilt, and the open category keeps its inner ring.
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
    /// Escape steps back one level: a pending dwell is cancelled first, then an open inner ring is
    /// cleared (the wheel stays exactly where it is), and only the plain main wheel closes.
    /// </summary>
    public void Escape()
    {
        switch (EscapeActionFor(_innerGroupId is not null, _dwellIndex >= 0))
        {
            case WheelEscape.CancelDwell:
                CancelDwell();
                break;
            case WheelEscape.ClearInner:
                ClearInner();
                break;
            default:
                Finish();
                break;
        }
    }

    internal static WheelEscape EscapeActionFor(bool innerOpen, bool dwelling) =>
        innerOpen ? WheelEscape.ClearInner : dwelling ? WheelEscape.CancelDwell : WheelEscape.Close;

    /// <summary>Only the plain main wheel opens on hover, in both modes, and only after the dwell.</summary>
    internal static bool ShouldDwell(bool innerOpen, bool sameCategory, bool suppressed) =>
        !innerOpen && !sameCategory && !suppressed;

    /// <summary>Once an inner ring is open, moving to another category swaps its children immediately.</summary>
    internal static bool ShouldSwap(bool innerOpen, bool sameCategory) => innerOpen && !sameCategory;

    /// <summary>
    /// Rebuilds the wheel's outer content. With motion on, the disc fades to 25 % and leans 4° while the
    /// wheel kind changes, then fades back. Category choosers never come through here: opening or swapping
    /// an inner ring only touches <see cref="_innerLayer"/> and stays inside its 120 ms / 100 ms budgets.
    /// </summary>
    private void ApplyView(Action mutate, Action? after = null, bool selectFirst = true)
    {
        CancelDwell();
        CancelSwapDwell();
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
                    ResetCanvasPose();
                });
            });
            return;
        }

        if (_switching)
            CancelSwitch();
        CommitView(mutate, after, selectFirst);
        ResetCanvasPose();
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
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        var storyboard = new Storyboard();
        var fade = new DoubleAnimation(_canvas.Opacity, opacity, duration)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd,
        };
        Storyboard.SetTarget(fade, _canvas);
        Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));
        storyboard.Children.Add(fade);
        var rotate = new DoubleAnimation(_canvasRotate.Angle, angle, duration)
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
        ResetCanvasPose();
    }

    /// <summary>Stops every canvas pose clock and returns the disc to its resting transform.</summary>
    private void ResetCanvasPose()
    {
        _canvas.BeginAnimation(OpacityProperty, null);
        _canvas.Opacity = 1;
        _canvasRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        _canvasRotate.Angle = 0;
        _canvasScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _canvasScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _canvasScale.ScaleX = 1;
        _canvasScale.ScaleY = 1;
        _canvasScale.CenterX = 0;
        _canvasScale.CenterY = 0;
    }

    /// <summary>The drag ended somewhere: close unless the drop landed on us.</summary>
    public void OnDragEnded()
    {
        if (Mode != WheelMode.Drag || _finished)
            return;
        // The button came up outside the wheel (or the release never reached the drop target): a pending
        // dwell must not open an inner ring behind the close timer. Dropping on the wheel goes through
        // OnDrop, which finishes the window first, so this only cancels a drop that is already leaving.
        CancelDwell();
        CancelSwapDwell();
        _closeTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Normal, (_, _) => Finish(), Dispatcher);
        _closeTimer.Start();
    }

    public void Finish(Action? afterClose = null)
    {
        if (afterClose is not null)
            _afterClose = afterClose;
        if (_finished)
            return;
        _finished = true;
        _closeTimer?.Stop();
        CancelSwapDwell();
        CancelDwell();
        if (_closing)
            return;
        _closing = true;
        CancelSwitch();
        StopInnerStoryboard();
        ReleaseReveal();
        if (!Motion.Enabled || !IsVisible || Dispatcher.HasShutdownStarted)
        {
            Close();
            return;
        }
        // ~100 ms: fade the window while the disc scales down by 3.5 %.
        Motion.Play(Motion.CloseWheel(this, _motion, Close), this);
    }

    /// <summary>
    /// The chosen action runs only after the wheel's close animation has released the window, so a tool
    /// window never pops on top of a still-visible wheel.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        var after = _afterClose;
        _afterClose = null;
        base.OnClosed(e);
        try
        {
            after?.Invoke();
        }
        catch
        {
            // A tool failing to open must not take the app down after the wheel closed.
        }
    }

    /// <summary>Renders the wheel to a PNG without showing it (used by --self-test).</summary>
    public void RenderPreview(string file, int highlight)
    {
        ReleaseReveal();
        ResetCanvasPose();
        CancelDwell();
        SettleInner();
        // The website/preview shot of a category should show the inner ring, not the empty gap the
        // dwell leaves behind. Build it at its settled pose; no timers or storyboards survive capture.
        if (highlight >= 0 && highlight < _ops.Count && _ops[highlight] is WheelGroupOperation)
        {
            OpenInner(highlight, SliceAngle(highlight, _ops.Count));
            SettleInner();
        }
        _canvas.LayoutTransform = Transform.Identity;
        SettleSlices();
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
        ReleaseReveal();
        CancelDwell();
        SettleInner();
        SettleSlices();
        Content = null;
        var root = new Grid { Background = SelfTest.Desktop(), Width = Size, Height = Size, FlowDirection = LanguageManager.FlowDirection };
        root.Children.Add(_canvas);
        _canvas.HorizontalAlignment = HorizontalAlignment.Center;
        _canvas.VerticalAlignment = VerticalAlignment.Center;
        return root;
    }

    /// <summary>
    /// Shows the slices at their final pose, with no animation clock left behind. The selected slice keeps
    /// its 7 px pop-out: the reveal completion must not pull the highlight back under the pointer.
    /// </summary>
    private void SettleSlices()
    {
        var count = _ops.Count;
        for (var i = 0; i < _groups.Count; i++)
        {
            var group = _groups[i];
            group.BeginAnimation(OpacityProperty, null);
            group.Opacity = 1;
            var scale = (ScaleTransform)((TransformGroup)group.RenderTransform).Children[0];
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = 1;
            scale.ScaleY = 1;
            var move = (TranslateTransform)((TransformGroup)group.RenderTransform).Children[1];
            move.BeginAnimation(TranslateTransform.XProperty, null);
            move.BeginAnimation(TranslateTransform.YProperty, null);
            var distance = i == _highlight && count > 1 ? PopOut : 0;
            var angle = count <= 1 ? 0 : i * 360.0 / count * Math.PI / 180;
            move.X = distance * Math.Sin(angle);
            move.Y = -distance * Math.Cos(angle);
        }
    }

    /// <summary>The inner ring at its resting pose, with every animation clock released.</summary>
    private void SettleInner()
    {
        _innerLayer.BeginAnimation(OpacityProperty, null);
        _innerLayer.Opacity = 1;
        _innerScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _innerScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _innerScale.ScaleX = 1;
        _innerScale.ScaleY = 1;
        _innerFadeLayer.BeginAnimation(OpacityProperty, null);
        _innerFadeLayer.Opacity = 1;
        foreach (var group in _innerGroups)
        {
            group.BeginAnimation(OpacityProperty, null);
            group.Opacity = 1;
            var scale = (ScaleTransform)((TransformGroup)group.RenderTransform).Children[0];
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = 1;
            scale.ScaleY = 1;
            var move = (TranslateTransform)((TransformGroup)group.RenderTransform).Children[1];
            move.BeginAnimation(TranslateTransform.XProperty, null);
            move.BeginAnimation(TranslateTransform.YProperty, null);
            var distance = _innerGroups.IndexOf(group) == _innerHighlight && _innerGroups.Count > 1 ? InnerPopOut : 0;
            var angle = _innerGroups.Count <= 1 ? 0 : _innerGroups.IndexOf(group) * 360.0 / _innerGroups.Count * Math.PI / 180;
            move.X = distance * Math.Sin(angle);
            move.Y = -distance * Math.Cos(angle);
        }
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
        _canvasScale.CenterX = 0;
        _canvasScale.CenterY = 0;
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
        var root = _items.Count == 0 ? []
            : _repeat ? (LastOperation?.Invoke(_items) is { } last ? [last] : [])
            : _source(_wheel, _items);
        _ops = root;
        if (_innerGroupId is { } id)
        {
            _innerChildren = ChildrenOf(root, id, out var found);
            if (!found)
            {
                _innerGroupId = null;
                _innerChildren = [];
                _innerPage = 0;
            }
        }
        else
        {
            _innerChildren = [];
            _innerPage = 0;
        }
        _highlight = -1;
        _innerHighlight = -1;
    }

    /// <summary>
    /// The children behind a category id, without replacing the view. Extracted so the inner ring's
    /// contents can be checked without a window: the outer <paramref name="root"/> is returned unchanged
    /// when the category is not in this view, which is exactly the "no view swap" rule.
    /// </summary>
    internal static IReadOnlyList<Operation> ChildrenOf(IReadOnlyList<Operation> root, string? groupId, out bool found)
    {
        found = groupId is null;
        if (groupId is null)
            return root;
        if (root.OfType<WheelGroupOperation>().FirstOrDefault(group => group.GroupId == groupId) is { } group)
        {
            found = true;
            return group.Children;
        }
        return root;
    }

    private static int GroupIndex(IReadOnlyList<Operation> ops, string groupId)
    {
        for (var i = 0; i < ops.Count; i++)
        {
            if (ops[i] is WheelGroupOperation group && group.GroupId == groupId)
                return i;
        }
        return -1;
    }

    /// <summary>The centre angle of slice <paramref name="index"/>, clockwise from 12 o'clock.</summary>
    internal static double SliceAngle(int index, int count) => count <= 0 ? 0 : index * 360.0 / count;

    /// <summary>The slice under an angle, or -1 when there is no ring.</summary>
    internal static int SliceIndexAt(double angle, int count)
    {
        if (count <= 0)
            return -1;
        var step = 360.0 / count;
        return (int)(((angle + step / 2) % 360) / step) % count;
    }

    /// <summary>
    /// The order the slices reveal in. Without an origin the reveal runs clockwise from the top (the
    /// authored order). A category starts at its own slice's angle and sweeps clockwise, so its children
    /// visibly grow out of that slice.
    /// </summary>
    internal static int[] RevealOrder(int count, double? fromAngle)
    {
        var order = Enumerable.Range(0, Math.Max(0, count)).ToArray();
        if (fromAngle is not { } origin || count < 2)
            return order;
        return order
            .OrderBy(index => ((SliceAngle(index, count) - origin) % 360 + 360) % 360)
            .ThenBy(index => index)
            .ToArray();
    }

    /// <summary>A category's inner ring: all children when they fit, otherwise five plus a compact "+n".</summary>
    internal static IReadOnlyList<InnerChoice> InnerRing(IReadOnlyList<Operation> children, int page)
    {
        if (children.Count == 0)
            return [];
        if (children.Count <= InnerCapacity)
            return children.Select(operation => new InnerChoice(operation, 0)).ToArray();
        var pages = InnerPageCount(children.Count);
        page = ((page % pages) + pages) % pages;
        var shown = children.Skip(page * InnerPerPage).Take(InnerPerPage).ToList();
        var choices = shown.Select(operation => new InnerChoice(operation, 0)).ToList();
        choices.Add(new InnerChoice(null, children.Count - shown.Count));
        return choices;
    }

    internal static int InnerPageCount(int children) =>
        children <= InnerCapacity ? 1 : (children + InnerPerPage - 1) / InnerPerPage;

    /// <summary>The point at the middle of the outer ring at <paramref name="angle"/>.</summary>
    private static Point SlicePoint(double angle) => Polar((OuterInnerRadius + OuterRadius) / 2, angle);

    /// <summary>Puts the canvas scale's effective fixed point on the parent slice.</summary>
    private void SetCanvasScaleOrigin(double angle)
    {
        var point = SlicePoint(angle);
        _canvasScale.CenterX = point.X - Size / 2;
        _canvasScale.CenterY = point.Y - Size / 2;
    }

    private void ReleaseReveal()
    {
        var storyboard = _reveal;
        _reveal = null;
        if (storyboard is null)
            return;
        try
        {
            storyboard.Stop(_canvas);
        }
        catch (InvalidOperationException)
        {
            // Already completed or never started: nothing is holding a clock.
        }
    }

    // ------------------------------------------------------------------ inner ring

    /// <summary>A click on an outer category opens its inner ring immediately; no dwell is needed.</summary>
    private void EnterCategory(int index)
    {
        if (_finished || _switching || index < 0 || index >= _ops.Count || _ops[index] is not WheelGroupOperation group)
            return;
        if (_innerGroupId == group.GroupId)
        {
            SetHighlight(index);
            return;
        }
        CancelDwell();
        if (_innerGroupId is not null)
        {
            // The ring is already open for another category: swap its children in place.
            SwapInner(index, SliceAngle(index, _ops.Count));
            return;
        }
        OpenInner(index, SliceAngle(index, _ops.Count));
    }

    /// <summary>First opening: the children grow out of the category slice, one after another.</summary>
    private void OpenInner(int index, double fromAngle)
    {
        if (_finished || index < 0 || index >= _ops.Count || _ops[index] is not WheelGroupOperation group)
            return;
        CancelDwell();
        _innerGroupId = group.GroupId;
        _innerPage = 0;
        _innerChildren = group.Children;
        _innerHighlight = -1;
        SetHighlight(index);
        StopInnerStoryboard();
        _innerFadeLayer.Children.Clear();
        _innerScale.ScaleX = 1;
        _innerScale.ScaleY = 1;
        BuildInner();
        AnimateInnerIn(fromAngle);
        UpdateHubText();
    }

    /// <summary>
    /// Hovering another category while the inner ring is open: the old children fade out in 45 ms and the
    /// new ones fade in during the remaining 55 ms, so the swap never exceeds 100 ms and the two label
    /// sets are never on screen together.
    /// </summary>
    private void SwapInner(int index, double fromAngle)
    {
        if (_finished || _switching || index < 0 || index >= _ops.Count || _ops[index] is not WheelGroupOperation group)
            return;
        CancelDwell();
        CancelSwapDwell();
        SetHighlight(index);
        _innerGroupId = group.GroupId;
        _innerPage = 0;
        _innerChildren = group.Children;
        _innerHighlight = -1;
        SwapInnerChildren(fromAngle);
        UpdateHubText();
    }

    /// <summary>Turns to the next page of an overflowing category, keeping the same category.</summary>
    private void NextInnerPage()
    {
        if (_innerGroupId is null || _innerChildren.Count == 0)
            return;
        CancelDwell();
        _innerPage = (_innerPage + 1) % InnerPageCount(_innerChildren.Count);
        _innerHighlight = -1;
        SwapInnerChildren(SliceAngle(Math.Max(0, GroupIndex(_ops, _innerGroupId)), _ops.Count));
    }

    private void SwapInnerChildren(double fromAngle)
    {
        StopInnerStoryboard();
        // A rapid second category change can interrupt a held fade at opacity 0. Reset both layers
        // before rebuilding; otherwise the ring can stay invisible until the pointer leaves and re-enters.
        _innerLayer.BeginAnimation(OpacityProperty, null);
        _innerLayer.Opacity = 1;
        _innerFadeLayer.BeginAnimation(OpacityProperty, null);
        _innerFadeLayer.Opacity = 1;
        _innerFadeLayer.Children.Clear();
        while (_innerLayer.Children.Count > 0)
        {
            var child = _innerLayer.Children[0];
            _innerLayer.Children.RemoveAt(0);
            _innerFadeLayer.Children.Add(child);
        }
        _innerScale.ScaleX = 1;
        _innerScale.ScaleY = 1;
        BuildInner();
        if (_innerGroups.Count == 0)
        {
            _innerFadeLayer.Children.Clear();
            _innerFadeLayer.BeginAnimation(OpacityProperty, null);
            _innerFadeLayer.Opacity = 1;
            SettleInner();
            return;
        }
        if (!Motion.Enabled || !IsVisible)
        {
            SettleInner();
            _innerFadeLayer.Children.Clear();
            return;
        }

        // First frame after a swap: the old ring is still visible while it fades, and the new ring is
        // built underneath at opacity 0 so the two label sets never overlap. It starts at 45 ms.
        _innerLayer.Opacity = 0;
        var storyboard = new Storyboard();
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(InnerSwapOutMs))
        {
            EasingFunction = Motion.EaseIn,
            FillBehavior = FillBehavior.HoldEnd,
        };
        Target(storyboard, _innerFadeLayer, UIElement.OpacityProperty, fadeOut);
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(InnerSwapInMs))
        {
            BeginTime = TimeSpan.FromMilliseconds(InnerSwapOutMs),
            EasingFunction = Motion.EaseOut,
            FillBehavior = FillBehavior.HoldEnd,
        };
        Target(storyboard, _innerLayer, UIElement.OpacityProperty, fadeIn);
        storyboard.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_innerStoryboard, storyboard))
                return;
            _innerStoryboard = null;
            _innerFadeLayer.Children.Clear();
            _innerFadeLayer.BeginAnimation(OpacityProperty, null);
            _innerFadeLayer.Opacity = 1;
            _innerLayer.BeginAnimation(OpacityProperty, null);
            _innerLayer.Opacity = 1;
        };
        _innerStoryboard = storyboard;
        storyboard.Begin(_canvas, true);
    }

    /// <summary>
    /// Builds the inner ring's visuals from <see cref="_innerChoices"/>. The labels are clipped to their
    /// slice and fitted per slice angle: the narrow diagonal slices get a smaller box than the top and
    /// bottom ones, so no name is cut by the clip or runs into a neighbour gap.
    /// </summary>
    private void BuildInner()
    {
        _innerLayer.Children.Clear();
        _innerGroups.Clear();
        _innerSlices.Clear();
        _innerLabels.Clear();
        _innerChoices = _innerGroupId is null || _innerChildren.Count == 0
            ? []
            : InnerRing(_innerChildren, _innerPage);
        _innerHighlight = -1;
        var n = _innerChoices.Count;
        if (n == 0)
            return;

        var step = 360.0 / n;
        for (var i = 0; i < n; i++)
        {
            var a0 = i * step - step / 2;
            var path = new Path
            {
                Data = n == 1 ? Ring(InnerInnerRadius, InnerOuterRadius) : Sector(a0, a0 + step, InnerInnerRadius, InnerOuterRadius),
                Fill = Theme.Slice,
                Stroke = Theme.SliceEdge,
                StrokeThickness = 1,
            };
            var label = BuildInnerLabel(_innerChoices[i], n, i);
            label.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            var mid = n == 1 ? new Point(Size / 2, Size / 2 - InnerLabelRadius) : Polar(InnerLabelRadius, i * step);
            Canvas.SetLeft(label, mid.X - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, mid.Y - label.DesiredSize.Height / 2);

            var inner = n == 1 ? new Point(Size / 2, Size / 2) : Polar(InnerInnerRadius, i * step);
            var group = new Canvas { Width = Size, Height = Size, IsHitTestVisible = false };
            var transforms = new TransformGroup();
            transforms.Children.Add(new ScaleTransform(1, 1, inner.X, inner.Y));
            transforms.Children.Add(new TranslateTransform());
            group.RenderTransform = transforms;
            // Only the label is clipped: clipping the slice path would shave its own 1 px outline.
            var labelLayer = new Canvas { Width = Size, Height = Size, Clip = path.Data, IsHitTestVisible = false };
            labelLayer.Children.Add(label);
            group.Children.Add(path);
            group.Children.Add(labelLayer);
            _innerSlices.Add(path);
            _innerLabels.Add(label);
            _innerGroups.Add(group);
            _innerLayer.Children.Add(group);
        }
    }

    /// <summary>Fade/scale the inner ring in, starting on the pointer event that opened it.</summary>
    private void AnimateInnerIn(double fromAngle)
    {
        StopInnerStoryboard();
        _innerFadeLayer.Children.Clear();
        _innerLayer.BeginAnimation(OpacityProperty, null);
        _innerLayer.Opacity = 1;
        _innerScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _innerScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _innerScale.ScaleX = 1;
        _innerScale.ScaleY = 1;
        if (_innerGroups.Count == 0)
            return;
        if (!Motion.Enabled || !IsVisible)
        {
            SettleInner();
            return;
        }

        var order = RevealOrder(_innerGroups.Count, fromAngle);
        var rank = new int[_innerGroups.Count];
        for (var i = 0; i < order.Length; i++)
            rank[order[i]] = i;

        var storyboard = new Storyboard();
        var duration = TimeSpan.FromMilliseconds(InnerEnterMs);
        for (var i = 0; i < _innerGroups.Count; i++)
        {
            var group = _innerGroups[i];
            var scale = (ScaleTransform)((TransformGroup)group.RenderTransform).Children[0];
            // The base values are the first visible frame: a delayed slice is already on screen at 40 %,
            // never a blank slot that pops in later.
            group.Opacity = InnerRevealFrom;
            scale.ScaleX = InnerScaleFrom;
            scale.ScaleY = InnerScaleFrom;
            var delay = TimeSpan.FromMilliseconds(rank[i] * InnerEnterStaggerMs);
            Target(storyboard, group, UIElement.OpacityProperty,
                new DoubleAnimation(InnerRevealFrom, 1, duration) { BeginTime = delay, EasingFunction = Motion.EaseOut, FillBehavior = FillBehavior.HoldEnd });
            Target(storyboard, scale, ScaleTransform.ScaleXProperty,
                new DoubleAnimation(InnerScaleFrom, 1, duration) { BeginTime = delay, EasingFunction = Motion.EaseOut, FillBehavior = FillBehavior.HoldEnd });
            Target(storyboard, scale, ScaleTransform.ScaleYProperty,
                new DoubleAnimation(InnerScaleFrom, 1, duration) { BeginTime = delay, EasingFunction = Motion.EaseOut, FillBehavior = FillBehavior.HoldEnd });
        }

        _innerStoryboard = storyboard;
        storyboard.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_innerStoryboard, storyboard))
                return;
            _innerStoryboard = null;
            SettleInner();
        };
        storyboard.Begin(_innerLayer, true);
    }

    /// <summary>Escape: fade the children out and leave the outer wheel untouched.</summary>
    private void ClearInner(bool animate = true)
    {
        if (_innerGroupId is null)
            return;
        CancelDwell();
        CancelSwapDwell();
        _suppressIndex = _highlight;
        ClearInnerState();
        UpdateHubText();
        StopInnerStoryboard();
        if (!animate || !Motion.Enabled || !IsVisible)
        {
            ClearInnerVisuals();
            return;
        }

        var storyboard = new Storyboard();
        var fade = new DoubleAnimation(_innerLayer.Opacity, 0, TimeSpan.FromMilliseconds(InnerExitMs))
        {
            EasingFunction = Motion.EaseIn,
            FillBehavior = FillBehavior.HoldEnd,
        };
        Target(storyboard, _innerLayer, UIElement.OpacityProperty, fade);
        var scale = new DoubleAnimation(1, 0.94, TimeSpan.FromMilliseconds(InnerExitMs))
        {
            EasingFunction = Motion.EaseIn,
            FillBehavior = FillBehavior.HoldEnd,
        };
        Target(storyboard, _innerScale, ScaleTransform.ScaleXProperty, scale);
        Target(storyboard, _innerScale, ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, 0.94, TimeSpan.FromMilliseconds(InnerExitMs)) { EasingFunction = Motion.EaseIn, FillBehavior = FillBehavior.HoldEnd });
        storyboard.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_innerStoryboard, storyboard))
                return;
            _innerStoryboard = null;
            ClearInnerVisuals();
        };
        _innerStoryboard = storyboard;
        storyboard.Begin(_canvas, true);
    }

    private void ClearInnerState()
    {
        _innerGroupId = null;
        _innerChildren = [];
        _innerChoices = [];
        _innerPage = 0;
        _innerHighlight = -1;
    }

    private void ClearInnerVisuals()
    {
        StopInnerStoryboard();
        _innerLayer.Children.Clear();
        _innerFadeLayer.Children.Clear();
        _innerGroups.Clear();
        _innerSlices.Clear();
        _innerLabels.Clear();
        _innerLayer.BeginAnimation(OpacityProperty, null);
        _innerLayer.Opacity = 1;
        _innerFadeLayer.BeginAnimation(OpacityProperty, null);
        _innerFadeLayer.Opacity = 1;
        _innerScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _innerScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _innerScale.ScaleX = 1;
        _innerScale.ScaleY = 1;
    }

    private void StopInnerStoryboard()
    {
        var storyboard = _innerStoryboard;
        _innerStoryboard = null;
        if (storyboard is null)
            return;
        try
        {
            storyboard.Stop(_canvas);
        }
        catch (InvalidOperationException)
        {
            // Already completed: nothing is holding a clock.
        }
    }

    // ------------------------------------------------------------------ dwell

    /// <summary>Starts the short dwell on a category and paints its progress arc.</summary>
    private void BeginDwell(int index)
    {
        if (index < 0 || index >= _ops.Count)
            return;
        if (_dwellIndex == index && _dwellTimer is { IsEnabled: true })
            return;
        CancelDwell();
        _dwellIndex = index;
        _dwellStarted = Environment.TickCount64;
        var step = 360.0 / _ops.Count;
        _dwellStartAngle = SliceAngle(index, _ops.Count) - step / 2 + 3;
        _dwellSpan = Math.Max(4, step - 6);
        _dwellArcSegment = new ArcSegment
        {
            Point = Polar(OuterRadius - 4, _dwellStartAngle + 0.4),
            Size = new System.Windows.Size(OuterRadius - 4, OuterRadius - 4),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = false,
        };
        _dwellFigure = new PathFigure
        {
            StartPoint = Polar(OuterRadius - 4, _dwellStartAngle),
            IsClosed = false,
            IsFilled = false,
        };
        _dwellFigure.Segments.Add(_dwellArcSegment);
        _dwellArc = new Path
        {
            Data = new PathGeometry([_dwellFigure]),
            Stroke = Theme.Accent,
            StrokeThickness = 3,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
            Opacity = 0.85,
        };
        var innerAt = _canvas.Children.IndexOf(_innerLayer);
        if (innerAt < 0)
            _canvas.Children.Add(_dwellArc);
        else
            _canvas.Children.Insert(innerAt, _dwellArc);
        _dwellTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(DwellTickMs), DispatcherPriority.Render, (_, _) => DwellTick(), Dispatcher);
        _dwellTimer.Start();
    }

    private void DwellTick()
    {
        if (_dwellIndex < 0 || _finished)
        {
            CancelDwell();
            return;
        }
        var elapsed = Environment.TickCount64 - _dwellStarted;
        var progress = Math.Clamp((double)elapsed / DwellMs, 0, 1);
        if (_dwellArcSegment is not null)
        {
            var swept = _dwellSpan * progress;
            _dwellArcSegment.Point = Polar(OuterRadius - 4, _dwellStartAngle + swept);
            _dwellArcSegment.IsLargeArc = swept > 180;
        }
        if (progress < 1)
            return;
        var index = _dwellIndex;
        _dwellTimer?.Stop();
        _dwellIndex = -1;
        RemoveDwellArc();
        if (index >= 0 && index < _ops.Count && _ops[index] is WheelGroupOperation)
            OpenInner(index, SliceAngle(index, _ops.Count));
    }

    private void CancelDwell()
    {
        _dwellTimer?.Stop();
        _dwellIndex = -1;
        RemoveDwellArc();
    }

    /// <summary>Starts the short rest needed before an open ring swaps to another outer category.</summary>
    private void BeginSwapDwell(int index)
    {
        if (_innerGroupId is null || index < 0 || index >= _ops.Count)
            return;
        if (_swapIndex == index && _swapTimer is { IsEnabled: true })
            return;
        CancelSwapDwell();
        _swapIndex = index;
        _swapTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(SwapDwellMs),
            DispatcherPriority.Input, (_, _) => SwapTick(), Dispatcher);
        _swapTimer.Start();
    }

    private void SwapTick()
    {
        var index = _swapIndex;
        CancelSwapDwell();
        if (_finished || _switching || _innerGroupId is null || index < 0 || index >= _ops.Count)
            return;
        var hit = HitAt(Mouse.GetPosition(_canvas));
        if (hit.Zone != WheelZone.Outer || hit.Index != index)
            return;
        if (_ops[index] is not WheelGroupOperation group || group.GroupId == _innerGroupId)
            return;
        SwapInner(index, SliceAngle(index, _ops.Count));
    }

    private void CancelSwapDwell()
    {
        _swapTimer?.Stop();
        _swapIndex = -1;
    }

    private void RemoveDwellArc()
    {
        if (_dwellArc is not null)
            _canvas.Children.Remove(_dwellArc);
        _dwellArc = null;
        _dwellFigure = null;
        _dwellArcSegment = null;
    }

    // ------------------------------------------------------------------ drag & drop

    /// <summary>
    /// The drag stopped being over the wheel. WPF raises DragLeave for child-to-child moves too (the
    /// backdrop, hub and the inner ring are separate visuals), so only a pointer that is truly past the
    /// rim may dismiss the inner ring or cancel a dwell: moving from a category into its children keeps
    /// both the ring and the 450 ms clock alive.
    /// </summary>
    private void OnDragLeave(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var point = e.GetPosition(_canvas);
        if (!_finished && (KeepsInnerRing(HitAt(point).Zone) || InsideCanvas(point)))
        {
            // Drag churn inside the disc: refresh the hover instead of throwing away the dwell.
            // InsideCanvas also covers the transparent shadow pad, which OLE reports as a leave.
            PointerMove(point);
            return;
        }
        CancelDwell();
        SetHighlight(-1);
        SetInnerHighlight(-1);
        ClearInner();
    }

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
        var point = e.GetPosition(_canvas);
        PointerMove(point);
        var hit = HitAt(point);
        // The centre hub is part of the small inner ring while a category is open: it highlights the
        // nearest child and must accept the drop on that child, not become a dead "no drop" island.
        var zone = hit.Zone;
        var index = hit.Index;
        if (hit.Zone == WheelZone.Hub && _innerGroupId is not null && _innerChoices.Count > 0)
        {
            zone = WheelZone.Inner;
            index = _innerHighlight >= 0 && _innerHighlight < _innerChoices.Count
                ? _innerHighlight
                : InnerHoverIndex(AngleAt(point), _innerChoices.Count, -1);
        }
        var overChild = zone == WheelZone.Inner && index >= 0 && index < _innerChoices.Count && !_innerChoices[index].IsMore;
        var overSlice = zone == WheelZone.Outer && index >= 0 && index < _ops.Count;
        // Crossing the hub/text while dragging must not become a "no drop" island: OLE then stops feeding
        // DragOver and the pointer can no longer reach the inner ring. Keep the effect alive over the hub
        // while a category is open; the drop still does nothing until it reaches a child.
        var overHub = hit.Zone == WheelZone.Hub && _innerGroupId is not null;
        // Never report None anywhere inside the disc while a category is open: OLE stops feeding
        // DragOver after one None, which is exactly what broke dragging through the centre hub.
        var keepDragging = _innerGroupId is not null && InsideCanvas(point);
        e.Effects = overChild || overSlice || overHub || keepDragging
            ? SafeEffect(e.AllowedEffects)
            : DragDropEffects.None;
    }

    /// <summary>
    /// Copy keeps the original untouched. Never report Move: the drag source would delete the file.
    /// </summary>
    private static DragDropEffects SafeEffect(DragDropEffects allowed) =>
        (allowed & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy
        : (allowed & DragDropEffects.Link) != 0 ? DragDropEffects.Link
        : DragDropEffects.None;

    /// <summary>
    /// Resolves a drag release without touching the window: an inner child or a plain outer slice runs
    /// its operation, an outer category opens its chooser, and the hub, the rim, a "+n" pager or an
    /// out-of-range index cancels. Kept pure so the drop rules are tested without an OLE drag.
    /// </summary>
    internal static DropTarget DropTargetAt(WheelZone zone, int index,
        IReadOnlyList<Operation> operations, IReadOnlyList<InnerChoice> choices)
    {
        if (zone == WheelZone.Outer && index >= 0 && index < operations.Count)
        {
            return operations[index] is WheelGroupOperation
                ? new DropTarget(DropAction.OpenCategory, operations[index])
                : new DropTarget(DropAction.Run, operations[index]);
        }
        if (zone == WheelZone.Inner && index >= 0 && index < choices.Count && !choices[index].IsMore)
            return new DropTarget(DropAction.Run, choices[index].Operation);
        return new DropTarget(DropAction.Nothing, null);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var point = e.GetPosition(_canvas);
        var hit = HitAt(point);
        // The visible hover is sticky for 8 degrees; activate exactly what is highlighted. Dropping on
        // the hub while a category is open belongs to the nearest inner child.
        var zone = hit.Zone;
        var index = hit.Index;
        if (hit.Zone == WheelZone.Hub && _innerGroupId is not null && _innerChoices.Count > 0)
        {
            zone = WheelZone.Inner;
            index = _innerHighlight >= 0 && _innerHighlight < _innerChoices.Count
                ? _innerHighlight
                : InnerHoverIndex(AngleAt(point), _innerChoices.Count, -1);
        }
        else if (hit.Zone == WheelZone.Inner && _innerHighlight >= 0 && _innerHighlight < _innerChoices.Count)
        {
            index = _innerHighlight;
        }
        var target = DropTargetAt(zone, index, _ops, _innerChoices);
        var op = target.Operation;
        e.Effects = target.Action == DropAction.Nothing ? DragDropEffects.None : SafeEffect(e.AllowedEffects);
        // Virtual files and images can only be read while the drop is in progress.
        var content = target.Action == DropAction.Nothing ? null : DropContent.Read(e.Data, materialize: true);
        var parentAngle = SliceAngle(Math.Max(0, index), zone == WheelZone.Inner ? _innerChoices.Count : _ops.Count);
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
                if (target.Action == DropAction.OpenCategory && op is WheelGroupOperation group)
                    OpenGroup?.Invoke(group, content.Items, hit.Index, parentAngle);
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
        var point = e.GetPosition(_canvas);
        var hit = HitAt(point);
        switch (hit.Zone)
        {
            case WheelZone.Hub:
                // The hub is a crossing pad, not a button. Clicking it does nothing; Escape or a
                // click outside the wheel is the way out.
                break;
            case WheelZone.Inner when hit.Index >= 0 && hit.Index < _innerChoices.Count:
                // Match the sticky hover: the child the user sees highlighted is the child that runs.
                RunInner(_innerHighlight >= 0 && _innerHighlight < _innerChoices.Count
                    ? _innerHighlight
                    : hit.Index);
                break;
            case WheelZone.Outer when hit.Index >= 0 && hit.Index < _ops.Count:
                Run(hit.Index);
                break;
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        e.Handled = true;
        switch (key)
        {
            case Key.Escape:
                Escape();
                break;
            case Key.Back when _innerGroupId is not null:
                ClearInner();
                break;
            case Key.Left or Key.Up:
                Move(-1);
                break;
            case Key.Right or Key.Down:
                Move(1);
                break;
            case Key.Enter or Key.Space:
                if (_innerGroupId is not null && _innerHighlight >= 0 && _innerHighlight < _innerChoices.Count)
                    RunInner(_innerHighlight);
                else if (_highlight >= 0)
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
                var digit = key - Key.D1;
                if (_innerGroupId is not null && digit < _innerChoices.Count)
                    RunInner(digit);
                else if (digit < _ops.Count)
                    Run(digit);
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
        if (_innerGroupId is not null && _innerChoices.Count > 0)
        {
            SetInnerHighlight(_innerHighlight < 0 ? 0 : ((_innerHighlight + delta) % _innerChoices.Count + _innerChoices.Count) % _innerChoices.Count);
            return;
        }
        if (_ops.Count == 0)
            return;
        SetHighlight(_highlight < 0 ? 0 : ((_highlight + delta) % _ops.Count + _ops.Count) % _ops.Count);
    }

    private void Run(int index)
    {
        var op = _ops[index];
        var items = _items;
        if (op is WheelGroupOperation)
        {
            // A category never closes the wheel: its children appear in the inner ring, the outer ring
            // stays exactly as it is, and Escape clears the children instead of closing everything.
            EnterCategory(index);
            return;
        }
        Finish(() => Dispatch(op, items));
    }

    private void RunInner(int index)
    {
        if (index < 0 || index >= _innerChoices.Count)
            return;
        var choice = _innerChoices[index];
        if (choice.IsMore)
        {
            NextInnerPage();
            return;
        }
        var op = choice.Operation!;
        var items = _items;
        Finish(() => Dispatch(op, items));
    }

    private void Dispatch(Operation op, IReadOnlyList<FileItem> items)
    {
        if (_repeat && RunRepeat is not null)
            RunRepeat(items);
        else
            _execute(op, items, _targets);
    }

    private double AngleAt(Point point)
    {
        var dx = point.X - Size / 2;
        var dy = point.Y - Size / 2;
        return (Math.Atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
    }

    /// <summary>
    /// Nearest inner slice, but sticky: the current slice stays selected until the pointer is
    /// <see cref="InnerHysteresisDeg"/> degrees past its midpoint. This removes the boundary flicker
    /// without changing click/drop hit-testing, which still uses the exact slice.
    /// </summary>
    internal static int InnerHoverIndex(double angle, int count, int current)
    {
        if (count <= 1 || current < 0 || current >= count)
            return SliceIndexAt(angle, count);
        var step = 360.0 / count;
        var delta = Math.Abs((angle - SliceAngle(current, count) + 540) % 360 - 180);
        return delta <= step / 2 + InnerHysteresisDeg ? current : SliceIndexAt(angle, count);
    }

    /// <summary>
    /// The single hover rule of the two-ring wheel, shared by Click and Drag mode: outer slices dwell
    /// for <see cref="DwellMs"/> when no ring is open; once it is open, moving to another category swaps
    /// immediately; over the inner ring the child under the pointer is highlighted and the category
    /// stays put.
    /// </summary>
    private void PointerMove(Point point)
    {
        if (_finished || _switching)
            return;
        var hit = HitAt(point);
        switch (hit.Zone)
        {
            case WheelZone.Inner:
                CancelDwell();
                CancelSwapDwell();
                _suppressIndex = -1;
                SetInnerHighlight(InnerHoverIndex(AngleAt(point), _innerChoices.Count, _innerHighlight));
                break;
            case WheelZone.Outer:
                SetInnerHighlight(-1);
                if (hit.Index != _suppressIndex)
                    _suppressIndex = -1;
                if (_innerGroupId is { } open)
                {
                    CancelDwell();
                    if (hit.Index >= 0 && hit.Index < _ops.Count && _ops[hit.Index] is WheelGroupOperation group)
                    {
                        if (group.GroupId == open)
                        {
                            // Already on the open category: only the outer highlight follows.
                            SetHighlight(hit.Index);
                            CancelSwapDwell();
                        }
                        else
                        {
                            // Do not move the highlight to the candidate yet. Keep the old category
                            // highlighted until the 180 ms rest actually commits the swap; passing over
                            // another slice on the way to the inner ring must not leave a stale ring.
                            BeginSwapDwell(hit.Index);
                        }
                    }
                    else
                    {
                        CancelSwapDwell();
                    }
                }
                else
                {
                    SetHighlight(hit.Index);
                    if (hit.Index >= 0 && hit.Index < _ops.Count && _ops[hit.Index] is WheelGroupOperation
                        && ShouldDwell(innerOpen: false, sameCategory: false, suppressed: hit.Index == _suppressIndex))
                    {
                        CancelSwapDwell();
                        BeginDwell(hit.Index);
                    }
                    else
                    {
                        CancelDwell();
                        CancelSwapDwell();
                    }
                }
                break;
            default:
                CancelDwell();
                CancelSwapDwell();
                // Crossing the hub is how the pointer reaches the far side of the small inner ring.
                // Follow the nearest child only inside the hub; the shadow pad outside the disc clears.
                SetInnerHighlight(hit.Zone == WheelZone.Hub && _innerGroupId is not null
                    ? InnerHoverIndex(AngleAt(point), _innerChoices.Count, _innerHighlight)
                    : -1);
                // Keep the open category highlighted while crossing the hub; only a closed wheel
                // clears the outer highlight.
                if (_innerGroupId is null)
                    SetHighlight(-1);
                break;
        }
    }

    // ------------------------------------------------------------------ geometry

    private static Point Polar(double radius, double degrees)
    {
        var rad = degrees * Math.PI / 180;
        return new Point(Size / 2 + radius * Math.Sin(rad), Size / 2 - radius * Math.Cos(rad));
    }

    private static double Deg(double radians) => radians * 180 / Math.PI;

    /// <summary>Which ring a radius belongs to. The gap between the rings is split evenly.</summary>
    internal static WheelZone ZoneForRadius(double radius) =>
        radius < HubRadius ? WheelZone.Hub
        : radius < (InnerOuterRadius + OuterInnerRadius) / 2 ? WheelZone.Inner
        : radius <= OuterRadius + Rim ? WheelZone.Outer
        : WheelZone.None;

    /// <summary>
    /// The inner ring belongs to the whole disc, not to the outer band: a pointer in the hub, the inner
    /// ring or an outer slice is still inside the wheel and must not dismiss it. Only a real exit (or
    /// Escape/deactivation) may clear it.
    /// </summary>
    internal static bool KeepsInnerRing(WheelZone zone) => zone != WheelZone.None;

    /// <summary>True anywhere in the window canvas, including the transparent shadow pad.</summary>
    internal static bool InsideCanvas(Point point) =>
        point.X >= 0 && point.Y >= 0 && point.X <= Size && point.Y <= Size;

    private readonly record struct Hit(WheelZone Zone, int Index);

    private Hit HitAt(Point point)
    {
        var dx = point.X - Size / 2;
        var dy = point.Y - Size / 2;
        var radius = Math.Sqrt(dx * dx + dy * dy);
        var zone = ZoneForRadius(radius);
        if (zone is WheelZone.None or WheelZone.Hub || _ops.Count == 0)
            return new Hit(zone, -1);
        var angle = (Math.Atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
        if (zone == WheelZone.Outer)
            return new Hit(WheelZone.Outer, SliceIndexAt(angle, _ops.Count));
        // The inner band belongs to the children while the ring is open. With no ring open it is still
        // inside the disc, so it aims at the outer slice in that direction rather than doing nothing.
        if (_innerGroupId is not null && _innerChoices.Count > 0)
            return new Hit(WheelZone.Inner, SliceIndexAt(angle, _innerChoices.Count));
        return new Hit(WheelZone.Outer, SliceIndexAt(angle, _ops.Count));
    }

    /// <summary>
    /// A ring segment between angles <paramref name="a0"/> and <paramref name="a1"/> with a constant-width gap
    /// to its neighbours and rounded corners.
    /// </summary>
    private static Geometry Sector(double a0, double a1, double inner, double outer)
    {
        // Angle of a point on an edge that is shifted sideways by half the gap, at radius r.
        double Edge(double r, double a, int side) => a + side * Deg(Math.Asin(Math.Min(0.9, Gap / 2 / r)));

        var outerStart = Edge(outer, a0, 1);
        var outerEnd = Edge(outer, a1, -1);
        var innerStart = Edge(inner, a0, 1);
        var innerEnd = Edge(inner, a1, -1);
        var cOut = Math.Min(Corner, Rad(outerEnd - outerStart) * outer * 0.45);
        var cIn = Math.Min(Corner * 0.7, Rad(innerEnd - innerStart) * inner * 0.45);
        var dOut = Deg(cOut / outer);
        var dIn = Deg(cIn / inner);

        var start = Polar(outer, outerStart + dOut);
        var figure = new PathFigure { StartPoint = start, IsClosed = true, IsFilled = true };
        figure.Segments.Add(new ArcSegment(Polar(outer, outerEnd - dOut), new System.Windows.Size(outer, outer), 0,
            outerEnd - outerStart - 2 * dOut > 180, SweepDirection.Clockwise, true));
        figure.Segments.Add(new QuadraticBezierSegment(Polar(outer, outerEnd), Polar(outer - cOut, Edge(outer - cOut, a1, -1)), true));
        figure.Segments.Add(new LineSegment(Polar(inner + cIn, Edge(inner + cIn, a1, -1)), true));
        figure.Segments.Add(new QuadraticBezierSegment(Polar(inner, innerEnd), Polar(inner, innerEnd - dIn), true));
        figure.Segments.Add(new ArcSegment(Polar(inner, innerStart + dIn), new System.Windows.Size(inner, inner), 0,
            innerEnd - innerStart - 2 * dIn > 180, SweepDirection.Counterclockwise, true));
        figure.Segments.Add(new QuadraticBezierSegment(Polar(inner, innerStart), Polar(inner + cIn, Edge(inner + cIn, a0, 1)), true));
        figure.Segments.Add(new LineSegment(Polar(outer - cOut, Edge(outer - cOut, a0, 1)), true));
        figure.Segments.Add(new QuadraticBezierSegment(Polar(outer, outerStart), start, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;

        static double Rad(double degrees) => degrees * Math.PI / 180;
    }

    private static Geometry Ring(double inner, double outer)
    {
        var center = new Point(Size / 2, Size / 2);
        var ring = new CombinedGeometry(GeometryCombineMode.Exclude,
            new EllipseGeometry(center, outer, outer),
            new EllipseGeometry(center, inner, inner));
        ring.Freeze();
        return ring;
    }

    /// <summary>
    /// The style's width ceiling for an outer label: the chord of the slice at its narrowest radius less
    /// an 8 % margin, never above 118 DIP. This is only a ceiling; <see cref="OuterSliceLabelWidth"/> takes
    /// the smaller of it and the exact room the label's horizontal box has at its own angle.
    /// </summary>
    internal static double LabelWidth(int count, double centerRadius, double maxHeight)
    {
        var chord = ChordAt(count, centerRadius - maxHeight / 2);
        return Math.Clamp(chord * 0.92, 42, 118);
    }

    internal static double ChordAt(int count, double radius)
    {
        if (count <= 0 || radius <= 0)
            return 0;
        var step = 360.0 / count;
        return 2 * radius * Math.Sin(step / 2 * Math.PI / 180);
    }

    internal static double InnerLabelWidth => LabelWidth(InnerCapacity, InnerLabelRadius, InnerLabelMaxHeight);

    /// <summary>
    /// The widest label box an outer slice can hold at <paramref name="height"/>: measured against the two
    /// ring circles and the slice's own gap-shifted edges, not just the chord through the mid-angle. A
    /// label on the left or right of the disc has only the ring's radial thickness as horizontal room, so
    /// a chord-only box would either be cut by the outer circle or run into the neighbour gap.
    /// </summary>
    internal static double OuterSliceLabelWidth(int count, int index) =>
        Math.Min(
            count <= 2 ? 120 : LabelWidth(count, OuterLabelRadius, OuterLabelMaxHeight),
            MaxLabelWidth(count, index, OuterInnerRadius, OuterRadius, OuterLabelRadius, OuterLabelMaxHeight));

    /// <summary>The same measured ceiling for one inner slice at the six-child worst case height.</summary>
    internal static double InnerSliceLabelWidth(int count, int index) =>
        Math.Min(
            InnerLabelWidth,
            MaxLabelWidth(count, index, InnerInnerRadius, InnerOuterRadius, InnerLabelRadius, InnerLabelMaxHeight));

    /// <summary>
    /// True when a horizontal <paramref name="width"/> × <paramref name="height"/> label box centred on
    /// the mid-angle of a slice lies completely inside the ring band. All four corners are checked against
    /// the inner/outer circles and the two slice edges (shifted inward by the neighbour gap); because the
    /// box is convex and the origin is outside it, those corners are the extremes of both radius and angle.
    /// </summary>
    internal static bool LabelBoxFits(int count, int index, double innerRadius, double outerRadius,
        double centerRadius, double width, double height)
    {
        if (count <= 0 || index < 0 || index >= count || width < 0 || height < 0)
            return false;
        // A third of a DIP of sub-pixel slack: the measured box and the binary-searched ceiling agree,
        // but WPF's text measurement rounds to 1/300 DIP and an exact fit must not be rejected.
        var innerLimit = innerRadius + LabelEdgeInset - 0.35;
        var outerLimit = outerRadius - LabelEdgeInset + 0.35;
        if (innerLimit >= outerLimit)
            return false;

        var angle = SliceAngle(index, count);
        var radians = angle * Math.PI / 180;
        var sin = Math.Sin(radians);
        var cos = Math.Cos(radians);
        var half = 180.0 / count;
        var halfWidth = width / 2;
        var halfHeight = height / 2;
        foreach (var dx in new[] { -halfWidth, halfWidth })
        foreach (var dy in new[] { -halfHeight, halfHeight })
        {
            var x = centerRadius * sin + dx;
            var y = -centerRadius * cos + dy;
            var radius = Math.Sqrt(x * x + y * y);
            if (radius < innerLimit || radius > outerLimit)
                return false;
            if (count == 1)
                continue;
            // The Sector edge is moved sideways by Gap / 2 at every radius; keep the same angular
            // clearance plus half a DIP so the 1 DIP outline never touches the label.
            var clearance = Math.Asin(Math.Min(0.9, (Gap / 2 + 0.75) / Math.Max(radius, 1))) * 180 / Math.PI;
            var delta = Math.Abs(SignedDegrees(Math.Atan2(x, -y) * 180 / Math.PI - angle));
            if (delta > half - clearance)
                return false;
        }
        return true;
    }

    /// <summary>
    /// The widest horizontal label box that fits at <paramref name="height"/>. The search is monotonic in
    /// width (every corner only moves outwards or towards a slice edge), so a binary search gives the exact
    /// chord-like room the slice offers at that angle.
    /// </summary>
    internal static double MaxLabelWidth(int count, int index, double innerRadius, double outerRadius,
        double centerRadius, double height)
    {
        if (count <= 0 || index < 0 || index >= count || height < 0)
            return 0;
        if (!LabelBoxFits(count, index, innerRadius, outerRadius, centerRadius, 0, height))
            return 0;
        var low = 0.0;
        var high = 2 * outerRadius;
        if (LabelBoxFits(count, index, innerRadius, outerRadius, centerRadius, high, height))
            return high;
        for (var i = 0; i < 40; i++)
        {
            var middle = (low + high) / 2;
            if (LabelBoxFits(count, index, innerRadius, outerRadius, centerRadius, middle, height))
                low = middle;
            else
                high = middle;
        }
        return low;
    }

    private static double SignedDegrees(double degrees) => (degrees + 540) % 360 - 180;

    // ------------------------------------------------------------------ visuals

    private static TextBlock HubText(double size, FontWeight weight) => new()
    {
        FontSize = size,
        FontWeight = weight,
        TextAlignment = TextAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        HorizontalAlignment = HorizontalAlignment.Center,
        MaxWidth = HubRadius * 1.4,
    };

    private void Build()
    {
        ReleaseReveal();
        CancelDwell();
        StopInnerStoryboard();
        _innerLayer.BeginAnimation(OpacityProperty, null);
        _innerLayer.Opacity = 1;
        _innerFadeLayer.BeginAnimation(OpacityProperty, null);
        _innerFadeLayer.Opacity = 1;
        _innerScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _innerScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _innerScale.ScaleX = 1;
        _innerScale.ScaleY = 1;
        _canvas.Children.Clear();
        _slices.Clear();
        _labels.Clear();
        _groups.Clear();
        _innerFadeLayer.Children.Clear();

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
        // drop the secondary lines and keep only the operation name. The categories (seven on an image
        // selection) never take this path.
        var compact = n > 15;
        var labelRadius = OuterLabelRadius;
        for (var i = 0; i < n; i++)
        {
            var a0 = i * step - step / 2;
            var path = new Path
            {
                Data = n == 1 ? Ring(OuterInnerRadius, OuterRadius) : Sector(a0, a0 + step, OuterInnerRadius, OuterRadius),
                Fill = Theme.Slice,
                Stroke = Theme.SliceEdge,
                StrokeThickness = 1,
            };
            var label = BuildOuterLabel(_ops[i], compact, n, i);
            label.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            var mid = n == 1 ? new Point(Size / 2, Size / 2 - labelRadius) : Polar(labelRadius, i * step);
            Canvas.SetLeft(label, mid.X - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, mid.Y - label.DesiredSize.Height / 2);

            // Slice and label move together when the slice pops out. The scale centre is the slice's
            // inner edge, so opening a slice grows it outwards across the ring instead of shrinking it
            // towards the middle of the disc.
            var inner = n == 1 ? new Point(Size / 2, Size / 2) : Polar(OuterInnerRadius, i * step);
            var group = new Canvas { Width = Size, Height = Size, IsHitTestVisible = false };
            var transforms = new TransformGroup();
            transforms.Children.Add(new ScaleTransform(1, 1, inner.X, inner.Y));
            transforms.Children.Add(new TranslateTransform());
            group.RenderTransform = transforms;
            // The label is clipped to its slice; the slice path itself is not, so its 1 px outline stays
            // whole. Text therefore cannot render outside its own slice, even mid-transition.
            var labelLayer = new Canvas { Width = Size, Height = Size, Clip = path.Data, IsHitTestVisible = false };
            labelLayer.Children.Add(label);
            group.Children.Add(path);
            group.Children.Add(labelLayer);
            _slices.Add(path);
            _labels.Add(label);
            _groups.Add(group);
            _canvas.Children.Add(group);
        }

        _canvas.Children.Add(_innerLayer);
        _canvas.Children.Add(_innerFadeLayer);

        var hubSize = 2 * (HubRadius - 6);
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
        _canvas.Children.Add(_hubLayer);

        BuildInner();
        BuildHub();
        _hubContent.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_hubContent, Size / 2 - _hubContent.DesiredSize.Width / 2);
        Canvas.SetTop(_hubContent, Size / 2 - _hubContent.DesiredSize.Height / 2);
    }

    /// <summary>Scale a slice opens from; its scale centre sits on the ring's inner edge.</summary>
    private const double RevealScaleFrom = 0.55;

    /// <summary>
    /// A real circular reveal: slices are hidden before their turn, then grow outwards and fade in one
    /// after another around the ring (18 ms apart). A delayed animation leaves the base value visible, so
    /// the resting opacity must be zero until the clock starts; the storyboard holds the final value and
    /// is released when it completes. In a category view the reveal starts at the category slice's angle.
    /// </summary>
    private void AnimateSlicesIn(double? fromAngle = null)
    {
        ReleaseReveal();
        if (_groups.Count == 0)
            return;
        if (!Motion.Enabled)
        {
            SettleSlices();
            return;
        }

        var order = RevealOrder(_groups.Count, fromAngle);
        var rank = new int[_groups.Count];
        for (var i = 0; i < order.Length; i++)
            rank[order[i]] = i;

        var storyboard = new Storyboard();
        var duration = TimeSpan.FromMilliseconds(Motion.SliceMs);
        for (var i = 0; i < _groups.Count; i++)
        {
            var group = _groups[i];
            var scale = (ScaleTransform)((TransformGroup)group.RenderTransform).Children[0];
            var delay = TimeSpan.FromMilliseconds(rank[i] * Motion.StaggerMs);
            group.Opacity = 0;
            scale.ScaleX = 1;
            scale.ScaleY = 1;
            Target(storyboard, group, UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, duration) { BeginTime = delay, EasingFunction = Motion.EaseOut, FillBehavior = FillBehavior.HoldEnd });
            Target(storyboard, scale, ScaleTransform.ScaleXProperty,
                new DoubleAnimation(RevealScaleFrom, 1, duration) { BeginTime = delay, EasingFunction = Motion.EaseOut, FillBehavior = FillBehavior.HoldEnd });
            Target(storyboard, scale, ScaleTransform.ScaleYProperty,
                new DoubleAnimation(RevealScaleFrom, 1, duration) { BeginTime = delay, EasingFunction = Motion.EaseOut, FillBehavior = FillBehavior.HoldEnd });
        }

        _reveal = storyboard;
        storyboard.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_reveal, storyboard))
                return;
            _reveal = null;
            SettleSlices();
        };
        // Controllable so a rebuild/finish can stop the clocks instead of leaving them on removed slices.
        storyboard.Begin(_canvas, true);
    }

    private static void Target(Storyboard storyboard, DependencyObject target, DependencyProperty property, Timeline animation)
    {
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(property));
        storyboard.Children.Add(animation);
    }

    // ------------------------------------------------------------------ label fitting

    /// <summary>
    /// The result of fitting one text run into a box. <see cref="FontSize"/> is never below the caller's
    /// readable floor; <see cref="Lines"/> is the number of visible lines (at most the caller's limit);
    /// <see cref="Trimmed"/> says the last line is ellipsized; <see cref="MaxHeight"/> is the height the
    /// TextBlock must be capped at in the visual tree (infinity when the whole run fits).
    /// </summary>
    internal readonly record struct LabelTextFit(
        double FontSize,
        int Lines,
        bool Trimmed,
        double Width,
        double Height,
        double LineHeight,
        double MaxHeight);

    /// <summary>
    /// Fits <paramref name="text"/> into a box by first lowering the font size (within
    /// [<paramref name="minFontSize"/>, <paramref name="maxFontSize"/>]) and only then capping the line
    /// count and ellipsizing the last line. A candidate font is accepted only when no word is wider than
    /// the box, so WPF never breaks a word in the middle. Arabic and English use the same path; the only
    /// difference is the <see cref="FlowDirection"/> passed to the TextBlock.
    /// </summary>
    internal static LabelTextFit FitLabelText(string text, double maxWidth, double maxHeight,
        double maxFontSize, double minFontSize, int maxLines, FontWeight weight, FlowDirection flow)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 0 || maxHeight <= 0 || maxFontSize <= 0)
            return new(Math.Max(0, minFontSize), 0, false, 0, 0, 0, double.PositiveInfinity);

        var floor = Math.Max(0.5, minFontSize);
        var lineCap = Math.Max(1, maxLines);
        var steps = Math.Max(0, (int)Math.Floor((maxFontSize - floor) / 0.5 + 0.0001));
        for (var i = 0; i <= steps; i++)
        {
            var size = maxFontSize - i * 0.5;
            var lineHeight = MeasureLineHeight(text, size, weight, flow);
            if (lineHeight <= 0)
                continue;
            var allowed = Math.Min(maxHeight, lineCap * lineHeight);
            if (allowed + 0.01 < lineHeight)
                continue;
            var wraps = WordsFit(text, maxWidth, size, weight, flow);
            if (!wraps && TextNaturalWidth(text, size, weight, flow) > maxWidth + 0.5)
                continue;
            var measured = MeasureText(text, maxWidth, size, weight, flow,
                wraps ? TextWrapping.Wrap : TextWrapping.NoWrap, double.PositiveInfinity);
            var lines = wraps ? CountLines(measured.Height, lineHeight) : 1;
            // The line budget is exact: accepting even half a DIP more would put the whole stack over
            // the slice height once the icon and subtitle are added.
            if (lines <= lineCap && measured.Height <= allowed + 0.05)
                return new(size, lines, false, measured.Width, measured.Height, lineHeight, double.PositiveInfinity);
        }

        // Even the floor cannot show the run whole: keep the floor, show as many complete lines as the
        // height budget allows and let WPF ellipsize the last one.
        var floorLineHeight = MeasureLineHeight(text, floor, weight, flow);
        var floorAllowed = Math.Min(maxHeight, lineCap * floorLineHeight);
        var floorLines = Math.Max(1, Math.Min(lineCap, (int)Math.Floor(floorAllowed / floorLineHeight + 0.01)));
        var floorWraps = WordsFit(text, maxWidth, floor, weight, flow);
        var floorText = MeasureText(text, maxWidth, floor, weight, flow,
            floorWraps ? TextWrapping.Wrap : TextWrapping.NoWrap, double.PositiveInfinity);
        var floorTrimmed = !floorWraps
            || TextNaturalWidth(text, floor, weight, flow) > maxWidth + 0.5
            || floorText.Height > floorAllowed + 0.05;
        var visibleHeight = floorWraps ? Math.Min(floorText.Height, floorAllowed) : floorText.Height;
        return new(floor, floorWraps ? floorLines : 1, floorTrimmed, floorText.Width, visibleHeight, floorLineHeight, floorAllowed);
    }

    /// <summary>
    /// Right-to-left only when the UI is Arabic and the string actually contains Arabic letters. Format
    /// names, measurements and "+n" stay left-to-right so the bidi algorithm cannot turn "1920 px" into
    /// "px 1920" or "+7" into "7+".
    /// </summary>
    internal static FlowDirection LabelFlow(string? text) =>
        LanguageManager.IsRtl && HasArabic(text)
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

    private static bool HasArabic(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        foreach (var c in text)
            if (c is >= '\u0600' and <= '\u06FF')
                return true;
        return false;
    }

    private static TextBlock LabelText(string text, double fontSize, FontWeight weight, FlowDirection flow, TextWrapping wrapping) => new()
    {
        Text = text,
        FontFamily = Theme.DisplayFont,
        FontSize = fontSize,
        FontWeight = weight,
        FlowDirection = flow,
        TextAlignment = TextAlignment.Center,
        TextWrapping = wrapping,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private readonly record struct TextMeasureKey(
        string Text, double MaxWidth, double FontSize, FontWeight Weight, FlowDirection Flow,
        TextWrapping Wrapping, double MaxHeight);

    // The label fitter asks for the same word widths and line heights at many budgets; WPF measurement
    // is the expensive part of building a wheel, so keep the results for the lifetime of the UI thread.
    [ThreadStatic] private static Dictionary<TextMeasureKey, Size>? _measureCache;

    private static Size MeasureText(string text, double maxWidth, double fontSize, FontWeight weight,
        FlowDirection flow, TextWrapping wrapping, double maxHeight)
    {
        var key = new TextMeasureKey(text, maxWidth, fontSize, weight, flow, wrapping, maxHeight);
        var cache = _measureCache ??= [];
        if (cache.TryGetValue(key, out var cached))
            return cached;
        if (cache.Count > 20_000)
            cache.Clear();

        var block = LabelText(text, fontSize, weight, flow, wrapping);
        if (!double.IsPositiveInfinity(maxWidth))
            block.MaxWidth = maxWidth;
        if (!double.IsPositiveInfinity(maxHeight))
            block.MaxHeight = maxHeight;
        block.Measure(new Size(maxWidth, maxHeight));
        var size = block.DesiredSize;
        cache[key] = size;
        return size;
    }

    private static double MeasureLineHeight(string text, double fontSize, FontWeight weight, FlowDirection flow) =>
        MeasureText(text, double.PositiveInfinity, fontSize, weight, flow, TextWrapping.NoWrap, double.PositiveInfinity).Height;

    private static double TextNaturalWidth(string text, double fontSize, FontWeight weight, FlowDirection flow) =>
        MeasureText(text, double.PositiveInfinity, fontSize, weight, flow, TextWrapping.NoWrap, double.PositiveInfinity).Width;

    private static int CountLines(double height, double lineHeight) =>
        lineHeight <= 0 ? 1 : Math.Max(1, (int)Math.Round(height / lineHeight, MidpointRounding.AwayFromZero));

    /// <summary>
    /// False when one whitespace-separated word is wider than the box. WPF's Wrap would break that word
    /// at a character boundary; the caller then keeps the label on one line and ellipsizes it instead,
    /// which is clearer than a split word. Arabic words are measured with the same RTL flow as the label.
    /// </summary>
    private static bool WordsFit(string text, double maxWidth, double fontSize, FontWeight weight, FlowDirection flow)
    {
        foreach (var word in text.Split([' ', '\u00A0', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            // Half a DIP of spare room: a word that exactly fills the box can make WPF wrap the last
            // glyph onto a second line because of layout rounding.
            if (TextNaturalWidth(word, fontSize, weight, flow) > maxWidth - 0.5)
                return false;
        }
        return true;
    }

    [ThreadStatic] private static Dictionary<(string Glyph, double Size), Size>? _iconCache;

    private static Size MeasureIcon(string glyph, double fontSize)
    {
        var cache = _iconCache ??= [];
        if (cache.TryGetValue((glyph, fontSize), out var cached))
            return cached;
        var block = new TextBlock
        {
            Text = glyph,
            FontFamily = Theme.Icons,
            FontSize = fontSize,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = block.DesiredSize;
        cache[(glyph, fontSize)] = size;
        return size;
    }

    internal static Action<string>? LabelTrace;

    /// <summary>One outer label whose font sizes, line count and whole box have been measured to fit.</summary>
    private static StackPanel BuildOuterLabel(Operation op, bool compact, int count, int index)
    {
        StackPanel? best = null;
        TextBlock? bestTitle = null;
        // Explore a few height budgets instead of only the full ring band. A shorter budget gives the
        // label a wider chord (a one-line layout) and can also drop the subtitle/icon, which is exactly
        // what a long name on a narrow diagonal slice needs. Each panel is measured, and only a box that
        // really fits the slice may win.
        var top = OuterLabelMaxHeight - LabelBuildMargin;
        for (var budget = top; budget >= 18; budget -= 5)
        {
            var width = OuterLabelWidthAt(count, index, Math.Min(OuterLabelMaxHeight, budget + LabelBuildMargin));
            var panel = SliceLabel(op, compact, width, budget);
            panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var title = panel.Children.OfType<TextBlock>().FirstOrDefault(block => block.FontWeight == FontWeights.SemiBold);
            if (title is not null
                && panel.DesiredSize.Height <= OuterLabelMaxHeight + 0.05
                && LabelBoxFits(count, index, OuterInnerRadius, OuterRadius, OuterLabelRadius,
                    panel.DesiredSize.Width, panel.DesiredSize.Height)
                && BetterLabel(panel, title, ref best, ref bestTitle))
            {
                best = panel;
                bestTitle = title;
            }
        }
        if (best is not null)
            return best;
        return MinimalOuterLabel(op, count, index);
    }

    /// <summary>
    /// Last-resort outer label: one line of the name at the floor, no icon or subtitle. Used only when
    /// even the smallest measured stack cannot be squeezed into a very narrow slice.
    /// </summary>
    private static StackPanel MinimalOuterLabel(Operation op, int count, int index)
    {
        var width = Math.Max(1, OuterLabelWidthAt(count, index, OuterLabelMaxHeight));
        var flow = LabelFlow(op.Label);
        var fit = FitLabelText(op.Label, width, OuterLabelMaxHeight - LabelBuildMargin,
            OuterTitleMinFont, OuterTitleMinFont, 1, FontWeights.SemiBold, flow);
        var panel = new StackPanel
        {
            MaxWidth = width,
            IsHitTestVisible = false,
            Opacity = op is Shell.NeedsToolOperation ? 0.6 : 1,
            FlowDirection = flow,
        };
        panel.Children.Add(TitleBlock(op.Label, fit, flow, width));
        return panel;
    }

    /// <summary>
    /// True when <paramref name="panel"/> is a better label than the best one so far: a complete label
    /// beats a truncated one, then the larger font wins, then more visible lines, then a wider box.
    /// </summary>
    private static bool BetterLabel(StackPanel panel, TextBlock title, ref StackPanel? best, ref TextBlock? bestTitle)
    {
        if (best is null || bestTitle is null)
            return true;
        var trimmed = !double.IsPositiveInfinity(title.MaxHeight);
        var bestTrimmed = !double.IsPositiveInfinity(bestTitle.MaxHeight);
        if (trimmed != bestTrimmed)
            return !trimmed;
        if (title.FontSize != bestTitle.FontSize)
            return title.FontSize > bestTitle.FontSize;
        if (trimmed && title.DesiredSize.Height != bestTitle.DesiredSize.Height)
            return title.DesiredSize.Height > bestTitle.DesiredSize.Height;
        return panel.DesiredSize.Width > best.DesiredSize.Width + 0.5;
    }

    private static double OuterLabelWidthAt(int count, int index, double height) =>
        Math.Min(
            count <= 2 ? 120 : LabelWidth(count, OuterLabelRadius, OuterLabelMaxHeight),
            MaxLabelWidth(count, index, OuterInnerRadius, OuterRadius, OuterLabelRadius, height));

    /// <summary>
    /// Builds one outer label. The icon, every text line and the optional subtitle are measured against
    /// the width of the box; the title is fitted at the largest readable size that keeps the whole stack
    /// inside the slice, and only then is the last line ellipsized. The subtitle drops out when keeping it
    /// would force the name below the floor, so a long tool name stays readable.
    /// </summary>
    internal static StackPanel SliceLabel(Operation op, bool compact, double maxWidth, double maxHeight)
    {
        var isFormat = op.Wheel == WheelKind.Convert && op.Label.Length <= 6 && op.Label.ToUpperInvariant() == op.Label;
        var isCategory = op is WheelGroupOperation;
        // Slices that first download a tool are shown a little faded. Format names such as MP4 stay
        // left-to-right even in Arabic, because they are identifiers rather than prose. Pure Latin
        // labels ("1920 px", "TAR.GZ", "+7") do the same so bidi cannot reorder them.
        var flow = LabelFlow(op.Label);
        var hasIcon = !isFormat && !compact;
        var subtitleText = !compact && !isCategory && !string.IsNullOrEmpty(op.Subtitle) ? op.Subtitle : null;
        var subtitleFlow = LabelFlow(subtitleText);
        var subtitle = subtitleText is null
            ? default
            : FitLabelText(subtitleText, maxWidth, double.PositiveInfinity,
                OuterSubtitleMaxFont, OuterSubtitleMinFont, 1, FontWeights.Normal, subtitleFlow);

        var titleMax = isFormat ? OuterFormatMaxFont : compact ? OuterCompactMaxFont : OuterTitleMaxFont;
        var titleMin = isFormat ? OuterFormatMinFont : compact ? OuterCompactMinFont : OuterTitleMinFont;
        var iconMax = isCategory ? 16.0 : OuterIconMaxFont;
        var iconMin = isCategory ? 14.0 : OuterIconMinFont;
        var chevronHeight = isCategory ? MeasureIcon("\uE76C", 10).Height + 1 : 0;

        LabelTextFit bestTitle = default;
        var bestSubtitle = false;
        var bestIcon = 0.0;
        var bestHeight = double.MaxValue;
        var haveBest = false;

        bool Better(LabelTextFit title, bool showSubtitle, double iconSize, double totalHeight)
        {
            if (!haveBest)
                return true;
            // A complete label beats a truncated one, then the larger (more readable) type wins. When
            // every candidate has to ellipsize, the one that shows more lines wins before the font size.
            if (title.Trimmed != bestTitle.Trimmed)
                return !title.Trimmed;
            if (!title.Trimmed)
            {
                if (title.FontSize != bestTitle.FontSize)
                    return title.FontSize > bestTitle.FontSize;
            }
            else
            {
                if (title.Lines != bestTitle.Lines)
                    return title.Lines > bestTitle.Lines;
                if (title.FontSize != bestTitle.FontSize)
                    return title.FontSize > bestTitle.FontSize;
            }
            if (showSubtitle != bestSubtitle)
                return showSubtitle;
            if (iconSize != bestIcon)
                return iconSize > bestIcon;
            return totalHeight < bestHeight - 0.01;
        }

        foreach (var iconSize in IconSizes(hasIcon, iconMax, iconMin))
        {
            var iconChrome = iconSize > 0 ? MeasureIcon(op.Glyph, iconSize).Height + (isCategory ? 3 : 4) : 0;
            foreach (var showSubtitle in subtitleText is null ? [false] : new[] { true, false })
            {
                var chrome = iconChrome + chevronHeight + (showSubtitle ? subtitle.Height + 1 : 0);
                var titleBudget = maxHeight - chrome;
                if (titleBudget < titleMin)
                    continue;
                for (var lines = OuterLabelMaxLines; lines >= 1; lines--)
                {
                    var title = FitLabelText(op.Label, maxWidth, titleBudget, titleMax, titleMin, lines,
                        FontWeights.SemiBold, flow);
                    if (title.Lines == 0)
                        continue;
                    var totalHeight = chrome + title.Height;
                    if (totalHeight > maxHeight + 0.05)
                        continue;
                    if (!Better(title, showSubtitle, iconSize, totalHeight))
                        continue;
                    bestTitle = title;
                    bestSubtitle = showSubtitle;
                    bestIcon = iconSize;
                    bestHeight = totalHeight;
                    haveBest = true;
                }
            }
        }

        if (!haveBest)
        {
            // No combination of icon/subtitle/chevron fits; the name itself is the one thing that must
            // stay. A single line at the readable floor always fits the remaining height.
            var fit = FitLabelText(op.Label, maxWidth, maxHeight, titleMin, titleMin, 1,
                FontWeights.SemiBold, flow);
            var minimal = new StackPanel
            {
                MaxWidth = maxWidth,
                IsHitTestVisible = false,
                Opacity = op is Shell.NeedsToolOperation ? 0.6 : 1,
                FlowDirection = flow,
            };
            minimal.Children.Add(TitleBlock(op.Label, fit, flow, maxWidth));
            return minimal;
        }

        // The whole stack is centred by the Canvas just like the old Viewbox; only its font sizes are now
        // chosen by measurement instead of a uniform scale-down.
        var panel = new StackPanel
        {
            MaxWidth = maxWidth,
            IsHitTestVisible = false,
            Opacity = op is Shell.NeedsToolOperation ? 0.6 : 1,
            FlowDirection = flow,
        };
        if (hasIcon && bestIcon > 0)
        {
            // A category is an icon + name: its subtitle ("5 items") is replaced by the inner ring and the
            // hub count, and dropping it keeps the category type large enough for the narrower ring.
            panel.Children.Add(new TextBlock
            {
                Text = op.Glyph,
                FontFamily = Theme.Icons,
                FontSize = bestIcon,
                Foreground = Theme.Text,
                HorizontalAlignment = HorizontalAlignment.Center,
                FlowDirection = System.Windows.FlowDirection.LeftToRight,
                Margin = new Thickness(0, 0, 0, isCategory ? 3 : 4),
            });
        }
        panel.Children.Add(TitleBlock(op.Label, bestTitle, flow, maxWidth));
        if (bestSubtitle && subtitleText is not null)
        {
            var block = LabelText(subtitleText, subtitle.FontSize, FontWeights.Normal, subtitleFlow, TextWrapping.NoWrap);
            block.Tag = "subtitle";
            block.Foreground = Theme.Muted;
            block.HorizontalAlignment = HorizontalAlignment.Center;
            block.MaxWidth = maxWidth;
            block.MaxHeight = subtitle.MaxHeight;
            block.Margin = new Thickness(0, 1, 0, 0);
            panel.Children.Add(block);
        }
        if (isCategory)
        {
            // The chevron says this slice opens a chooser instead of running something itself.
            panel.Children.Add(new TextBlock
            {
                Text = "\uE76C",
                FontFamily = Theme.Icons,
                FontSize = 10,
                Foreground = Theme.Muted,
                HorizontalAlignment = HorizontalAlignment.Center,
                FlowDirection = System.Windows.FlowDirection.LeftToRight,
                Margin = new Thickness(0, 1, 0, 0),
            });
        }
        return panel;
    }

    private static IEnumerable<double> IconSizes(bool hasIcon, double max, double min)
    {
        if (!hasIcon)
        {
            yield return 0;
            yield break;
        }
        yield return max;
        var middle = (max + min) / 2;
        if (middle > min + 0.001)
            yield return middle;
        yield return min;
        // The icon is decoration; dropping it frees a whole line of height on a narrow slice and is
        // always preferable to ellipsizing the tool name.
        yield return 0;
    }

    private static TextBlock TitleBlock(string text, LabelTextFit fit, FlowDirection flow, double maxWidth)
    {
        var block = LabelText(text, fit.FontSize, FontWeights.SemiBold, flow,
            fit.Lines > 1 ? TextWrapping.Wrap : TextWrapping.NoWrap);
        block.Foreground = Theme.Text;
        block.HorizontalAlignment = HorizontalAlignment.Center;
        block.MaxWidth = maxWidth;
        if (!double.IsPositiveInfinity(fit.MaxHeight))
            block.MaxHeight = fit.MaxHeight;
        return block;
    }

    /// <summary>One inner label: the same measurement/fitting path, with the inner ring's own type scale.</summary>
    internal static StackPanel InnerLabel(InnerChoice choice, double maxWidth, double maxHeight)
    {
        // "+n" is language-neutral. LTR keeps the plus before the number in Arabic instead of mirroring it
        // to "7+", which reads like subtraction; the same rule keeps "1920 px" and "TAR.GZ" intact.
        var text = choice.IsMore ? "+" + choice.Hidden : InnerLabelText(choice.Operation!);
        var flow = LabelFlow(text);
        var fit = FitLabelText(text, maxWidth, maxHeight,
            choice.IsMore ? MoreMaxFont : InnerMaxFont,
            choice.IsMore ? MoreMinFont : InnerMinFont,
            choice.IsMore ? 1 : InnerLabelMaxLines,
            FontWeights.SemiBold, flow);
        var panel = new StackPanel
        {
            MaxWidth = maxWidth,
            IsHitTestVisible = false,
            FlowDirection = flow,
        };
        var block = TitleBlock(text, fit, flow, maxWidth);
        block.Foreground = choice.IsMore ? Theme.Muted : Theme.Text;
        panel.Children.Add(block);
        return panel;
    }

    /// <summary>Builds one inner label, widening the box once its measured height is known.</summary>
    private static StackPanel BuildInnerLabel(InnerChoice choice, int count, int index)
    {
        StackPanel? safe = null;
        StackPanel? best = null;
        TextBlock? bestTitle = null;
        var top = InnerLabelMaxHeight - LabelBuildMargin;
        for (var budget = top; budget >= 12; budget -= 4)
        {
            var width = Math.Min(InnerLabelWidth,
                MaxLabelWidth(count, index, InnerInnerRadius, InnerOuterRadius, InnerLabelRadius,
                    Math.Min(InnerLabelMaxHeight, budget + LabelBuildMargin)));
            var panel = InnerLabel(choice, width, budget);
            panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            safe ??= panel;
            var title = panel.Children.OfType<TextBlock>().FirstOrDefault();
            if (title is not null
                && panel.DesiredSize.Height <= InnerLabelMaxHeight + 0.05
                && LabelBoxFits(count, index, InnerInnerRadius, InnerOuterRadius, InnerLabelRadius,
                    panel.DesiredSize.Width, panel.DesiredSize.Height)
                && BetterLabel(panel, title, ref best, ref bestTitle))
            {
                best = panel;
                bestTitle = title;
            }
        }
        return best ?? safe ?? InnerLabel(choice, InnerSliceLabelWidth(count, index), InnerLabelMaxHeight);
    }

    /// <summary>
    /// What an inner slice says. Two choices can share the same label ("Archive" for ZIP and TAR.GZ), and
    /// long labels do not fit the narrow band, so the subtitle or a shorter form is used. Rotation keeps
    /// its direction (the shared "Rotate" label made left and right indistinguishable).
    /// </summary>
    internal static string InnerLabelText(Operation op)
    {
        if (op.Id == "image.rotate.right")
            return "Rotate R";
        if (op.Id == "image.rotate.left")
            return "Rotate L";
        if (op.Id.StartsWith("pack.", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(op.Subtitle))
            return op.Subtitle.Trim();
        return op.Id switch
        {
            "tools.metadata" => T("wheel.category.metadata"),
            "image.max1920" => Bidi.Value("1920 px"),
            _ => ShortenInnerLabel(op.Label),
        };
    }

    private static string ShortenInnerLabel(string label)
    {
        if (label.Length > 5 && label.StartsWith("Edit ", StringComparison.OrdinalIgnoreCase))
            return label[5..];
        if (label.Length > 4 && label.StartsWith("Max ", StringComparison.OrdinalIgnoreCase))
            return label[4..];
        if (label.Length > 5 && label.StartsWith("Read ", StringComparison.OrdinalIgnoreCase))
            return label[5..];
        return label;
    }

    /// <summary>
    /// The format badge in the hub: the extension of a single file, DIR for a folder, FILES for a
    /// multi-selection or the empty drag wheel. It deliberately ignores the category/tool on screen.
    /// </summary>
    internal static string FormatBadgeText(IReadOnlyList<FileItem> items)
    {
        if (items.Count != 1)
            return "FILES";
        var item = items[0];
        if (item.IsDirectory)
            return "DIR";
        return item.Format is { Length: > 0 } format ? Formats.Display(format) : "FILE";
    }

    private void BuildHub()
    {
        _title.Foreground = Theme.Text;
        _detail.Foreground = Theme.Muted;
        _tabTrack.Background = Theme.Track;
        _formatBody.Background = Theme.AccentGradient;
        _formatBody.BorderBrush = Theme.PanelBorder;
        _formatTab.Background = Theme.AccentGradient;
        _formatText.Foreground = Theme.TextOnAccent;

        if (_repeat)
        {
            _formatBadge.Visibility = Visibility.Collapsed;
            _iconGlyph.Text = "\uE8EE"; // RepeatAll
            _iconGlyph.Foreground = Theme.Accent;
            _iconGlyph.Visibility = Visibility.Visible;
        }
        else
        {
            // The format of what will be acted on: extension for one file, DIR for a folder, FILES for a
            // multi-selection (or the empty drag wheel, before the file arrives). Never the category.
            _formatBadge.Visibility = Visibility.Visible;
            _formatText.Text = FormatBadgeText(_items);
            _iconGlyph.Visibility = Visibility.Collapsed;
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
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(5, 0, 5, 1),
                Background = active ? Theme.AccentGradient : Brushes.Transparent,
                Cursor = Mode == WheelMode.Click ? Cursors.Hand : null,
                ToolTip = UiSetup.Tip(AppSettings.WheelName(wheel)),
                Child = new TextBlock
                {
                    Text = key == Input.ModifierKey.None ? AppSettings.WheelName(wheel) : AppSettings.KeyName(key),
                    FontSize = 9,
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

    private void SetInnerHighlight(int index)
    {
        if (index == _innerHighlight)
            return;
        if (index < 0 || index >= _innerChoices.Count)
            index = -1;
        if (_innerHighlight >= 0 && _innerHighlight < _innerSlices.Count)
            PaintInner(_innerHighlight, false);
        _innerHighlight = index;
        if (_innerHighlight >= 0 && _innerHighlight < _innerSlices.Count)
            PaintInner(_innerHighlight, true);
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

    private void PaintInner(int index, bool on)
    {
        var slice = _innerSlices[index];
        slice.Fill = on ? Theme.AccentGradient : Theme.Slice;
        slice.Stroke = on ? Brushes.Transparent : Theme.SliceEdge;
        slice.Effect = on ? Theme.Glow() : null;
        foreach (var child in _innerLabels[index].Children.OfType<TextBlock>())
            child.Foreground = on ? Theme.TextOnAccent : _innerChoices[index].IsMore ? Theme.Muted : Theme.Text;

        var n = _innerChoices.Count;
        var angle = n <= 1 ? 0 : index * 360.0 / n * Math.PI / 180;
        var distance = on && n > 1 ? InnerPopOut : 0;
        var move = (TranslateTransform)((TransformGroup)_innerGroups[index].RenderTransform).Children[1];
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
        // An open inner ring names its category and the selection size in the hub. The Shift/Ctrl/Alt
        // tabs sit directly below, so the hub always answers "which category, how many items, which key".
        if (_innerGroupId is { } id)
        {
            var category = _ops.OfType<WheelGroupOperation>().FirstOrDefault(group => group.GroupId == id);
            _title.Text = category?.Label ?? AppSettings.WheelName(_wheel);
            // The category's own subtitle is its operation count ("5 items"); fall back to the selection
            // size only for a category that has no count.
            _detail.Text = category?.Subtitle
                ?? (_items.Count == 0 ? T("wheel.dropHere")
                    : _ops.Count == 0 ? T("wheel.nothingForSelection") : DescribeItems());
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
