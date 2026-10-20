using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FileAction.App.UI;

/// <summary>
/// The one animation vocabulary of the app: durations, easing curves and the small set of storyboard
/// factories every surface uses. Motion is a theme-level token set (see <c>docs/MOTION.md</c>): a window,
/// a wheel or a button never hard-codes a duration or a curve of its own.
///
/// Rules enforced here rather than in every call site:
/// <list type="bullet">
/// <item>Only <see cref="UIElement.OpacityProperty"/> and render transforms are animated; the single
/// width/height animation is <see cref="Resize"/>, for the media-orientation resize of a tool window.</item>
/// <item>Per-frame work allocates nothing: the repeatedly used storyboards and easing functions are
/// frozen once and re-applied through <see cref="Animatable.BeginAnimation(DependencyProperty, AnimationTimeline)"/>.</item>
/// <item>Animations stop and release their clocks when a surface hides or closes (<see cref="Release"/>).</item>
/// <item>Everything is disabled when Windows' "Animation effects" setting is off or when the
/// Settings › Animations toggle is off. Callers then take their instant path, so the app has no
/// animation timer at all in reduced-motion mode.</item>
/// </list>
/// </summary>
internal static class Motion
{
    // ------------------------------------------------------------------ tokens (milliseconds)

    /// <summary>Wheel open: scale + fade from the cursor.</summary>
    public const double WheelOpenMs = 150;

    /// <summary>Wheel close: fade + slight scale down.</summary>
    public const double WheelCloseMs = 100;

    /// <summary>Delay between two neighbouring slices while the wheel opens. Budget: ≤ 20 ms.</summary>
    public const double StaggerMs = 18;

    /// <summary>One slice opening (fade + scale).</summary>
    public const double SliceMs = 150;

    /// <summary>The highlight travelling from the old slice to the new one. Budget: 90–120 ms.</summary>
    public const double HighlightMs = 100;

    /// <summary>Pressed micro-scale on a slice, button, chip or toggle.</summary>
    public const double PressMs = 90;

    /// <summary>Wheel mode switch: fade out, rebuild, fade + rotate in. Budget: ≤ 180 ms.</summary>
    public const double SwitchMs = 170;

    /// <summary>Tool/settings/dialog open: fade + 8 px rise + scale 0.97 → 1. Budget: 160–200 ms.</summary>
    public const double WindowOpenMs = 180;

    /// <summary>Tool/settings/dialog close fade.</summary>
    public const double WindowCloseMs = 120;

    /// <summary>The one allowed width/height animation: media-orientation resize of a tool window.</summary>
    public const double ResizeMs = 180;

    /// <summary>Hover / press feedback on buttons, sliders, toggles, chips, segmented pills.</summary>
    public const double HoverMs = 100;

    /// <summary>The toggle knob sliding between off and on.</summary>
    public const double ToggleMs = 120;

    /// <summary>Success pulse after an operation completes.</summary>
    public const double PulseMs = 320;

    /// <summary>Badge pop (Lock/success/error).</summary>
    public const double PopMs = 260;

    /// <summary>How far a floating surface rises into place.</summary>
    public const double RisePx = 8;

    /// <summary>Scale a tool/settings window opens from.</summary>
    public const double WindowScaleFrom = 0.97;

    /// <summary>Scale the wheel opens from (the open easing peaks at ≈ +2 % above 1, i.e. ≤ 4 % overshoot).</summary>
    public const double WheelScaleFrom = 0.88;

    /// <summary>How far the wheel rotates while switching modes.</summary>
    public const double SwitchRotateDegrees = 4;

    // ------------------------------------------------------------------ easing (frozen once)

    public static readonly IEasingFunction EaseOut = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });

    public static readonly IEasingFunction EaseIn = Freeze(new CubicEase { EasingMode = EasingMode.EaseIn });

    public static readonly IEasingFunction EaseInOut = Freeze(new CubicEase { EasingMode = EasingMode.EaseInOut });

    /// <summary>Soft deceleration for cross-fades; never overshoots.</summary>
    public static readonly IEasingFunction SoftOut = Freeze(new QuadraticEase { EasingMode = EasingMode.EaseOut });

    /// <summary>
    /// Ease-out with a small settle. With the wheel's 0.88 → 1 scale the peak is ≈ 1.026 (2.6 % overshoot),
    /// inside the 4 % budget; for the 0.97 → 1 window scale the peak is ≈ 1.0008.
    /// </summary>
    public static readonly IEasingFunction Settle = Freeze(new BackEase { Amplitude = 0.7, EasingMode = EasingMode.EaseOut });

    // ------------------------------------------------------------------ reduced motion

    private static bool _settingsEnabled = true;
    private static bool _systemEnabled = ReadSystemAnimation();
    private static bool? _override;

    /// <summary>Set from <see cref="AppSettings.Animations"/> when the app loads or saves settings.</summary>
    public static void SetSettingsEnabled(bool enabled) => _settingsEnabled = enabled;

    /// <summary>Self-test override: true = animate, false = reduced motion, null = follow the system.</summary>
    public static bool? Override
    {
        get => _override;
        set => _override = value;
    }

    /// <summary>
    /// True when motion may run: the Settings toggle is on and Windows' "Animation effects"
    /// (<see cref="SystemParameters.ClientAreaAnimation"/>) is on. Read live, so turning the Windows
    /// setting off stops every animation without a restart.
    /// </summary>
    public static bool Enabled => _override ?? (_settingsEnabled && _systemEnabled);

    /// <summary>True when Windows' own animation-effects setting currently allows motion.</summary>
    public static bool SystemAnimationsAvailable => _systemEnabled;

    private static bool ReadSystemAnimation()
    {
        try
        {
            return SystemParameters.ClientAreaAnimation;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // A locked-down session may refuse the query; motion is a nicety, so assume it is allowed.
            return true;
        }
    }

    static Motion()
    {
        // The Windows accessibility setting can change while the app is running.
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(SystemParameters.ClientAreaAnimation), StringComparison.Ordinal))
                _systemEnabled = ReadSystemAnimation();
        };
    }

    // ------------------------------------------------------------------ transforms

    /// <summary>The transform pair a floating surface animates. Created once per surface.</summary>
    internal sealed class Handle(ScaleTransform scale, TranslateTransform? lift)
    {
        public ScaleTransform Scale { get; } = scale;

        public TranslateTransform? Lift { get; } = lift;

        /// <summary>True when the surface currently sits at its resting pose.</summary>
        public bool AtRest => Math.Abs(Scale.ScaleX - 1) < 0.0001 && Math.Abs(Scale.ScaleY - 1) < 0.0001 &&
                              (Lift is null || (Math.Abs(Lift.X) < 0.01 && Math.Abs(Lift.Y) < 0.01));
    }

    /// <summary>Gives <paramref name="element"/> a frozen-at-rest scale (+ optional lift) transform.</summary>
    public static Handle Prepare(FrameworkElement element, bool lift = true)
    {
        var scale = new ScaleTransform(1, 1);
        var group = new TransformGroup();
        group.Children.Add(scale);
        TranslateTransform? move = null;
        if (lift)
        {
            move = new TranslateTransform();
            group.Children.Add(move);
        }
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = group;
        return new Handle(scale, move);
    }

    /// <summary>Puts a surface back at rest without animating (reduced motion, or after a cancel).</summary>
    public static void Reset(Handle handle)
    {
        handle.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        handle.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        handle.Scale.ScaleX = 1;
        handle.Scale.ScaleY = 1;
        if (handle.Lift is { } lift)
        {
            lift.BeginAnimation(TranslateTransform.XProperty, null);
            lift.BeginAnimation(TranslateTransform.YProperty, null);
            lift.X = 0;
            lift.Y = 0;
        }
    }

    /// <summary>Stops every clock this helper may have put on a surface and leaves it at rest.</summary>
    public static void Release(FrameworkElement element, Handle? handle = null)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        if (handle is not null)
        {
            Reset(handle);
            return;
        }
        if (element.RenderTransform is TransformGroup group)
        {
            foreach (var transform in group.Children)
            {
                if (transform is ScaleTransform scale)
                {
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                }
                else if (transform is TranslateTransform move)
                {
                    move.BeginAnimation(TranslateTransform.XProperty, null);
                    move.BeginAnimation(TranslateTransform.YProperty, null);
                }
            }
        }
        else if (element.RenderTransform is ScaleTransform onlyScale)
        {
            onlyScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            onlyScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }
    }

    // ------------------------------------------------------------------ open / close

    /// <summary>
    /// A floating surface (tool window, settings window, dialog) fades in, rises <see cref="RisePx"/>
    /// and settles from <see cref="WindowScaleFrom"/> to 1.
    /// </summary>
    public static Storyboard Open(FrameworkElement surface, Handle handle)
    {
        var storyboard = new Storyboard();
        surface.Opacity = 1;
        handle.Scale.ScaleX = 1;
        handle.Scale.ScaleY = 1;
        if (handle.Lift is { } resting)
        {
            resting.X = 0;
            resting.Y = 0;
        }
        Add(storyboard, surface, UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(WindowOpenMs)) { EasingFunction = SoftOut, FillBehavior = FillBehavior.Stop });
        AddScale(storyboard, handle.Scale, WindowScaleFrom, 1, WindowOpenMs);
        if (handle.Lift is { } lift)
            AddLift(storyboard, lift, RisePx, 0, WindowOpenMs);
        return storyboard;
    }

    /// <summary>The wheel opens from the cursor: the window fades in while the disc scales from 0.88 with a settle.</summary>
    public static Storyboard OpenWheel(Window window, FrameworkElement disc, Handle handle)
    {
        var storyboard = new Storyboard();
        window.Opacity = 1;
        handle.Scale.ScaleX = 1;
        handle.Scale.ScaleY = 1;
        Add(storyboard, window, UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(WheelOpenMs)) { EasingFunction = SoftOut, FillBehavior = FillBehavior.Stop });
        AddScale(storyboard, handle.Scale, WheelScaleFrom, 1, WheelOpenMs, Settle);
        return storyboard;
    }

    /// <summary>The wheel closes: the window fades out over 100 ms while the disc scales down slightly.</summary>
    public static Storyboard CloseWheel(Window window, Handle handle, Action completed)
    {
        var storyboard = new Storyboard();
        Add(storyboard, window, UIElement.OpacityProperty,
            new DoubleAnimation(window.Opacity, 0, TimeSpan.FromMilliseconds(WheelCloseMs)) { EasingFunction = EaseIn, FillBehavior = FillBehavior.HoldEnd });
        AddScale(storyboard, handle.Scale, handle.Scale.ScaleX, 0.965, WheelCloseMs, EaseIn);
        var done = false;
        storyboard.Completed += (_, _) =>
        {
            if (done)
                return;
            done = true;
            Release(window, handle);
            window.Opacity = 0;
            completed();
        };
        return storyboard;
    }

    /// <summary>Close: fade out over <paramref name="durationMs"/>, then run <paramref name="completed"/> exactly once.</summary>
    public static Storyboard Close(FrameworkElement surface, Handle handle, Action? completed, double toScale = 0.985,
        double durationMs = WindowCloseMs)
    {
        var storyboard = new Storyboard();
        Add(storyboard, surface, UIElement.OpacityProperty,
            new DoubleAnimation(surface.Opacity, 0, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = EaseIn, FillBehavior = FillBehavior.HoldEnd });
        AddScale(storyboard, handle.Scale, handle.Scale.ScaleX, toScale, durationMs, EaseIn);
        var done = false;
        storyboard.Completed += (_, _) =>
        {
            if (done)
                return;
            done = true;
            Release(surface, handle);
            surface.Opacity = 0;
            completed?.Invoke();
        };
        return storyboard;
    }

    // ------------------------------------------------------------------ generic windows

    /// <summary>
    /// Fades a whole window in while its content rises and settles. Applied by <see cref="UiSetup.Configure"/>
    /// to Settings, Jobs, the shelf, the wheel editor and every dialog, so none of them owns a copy.
    /// </summary>
    public static Storyboard WindowIn(Window window, FrameworkElement content, Handle handle)
    {
        var storyboard = new Storyboard();
        window.Opacity = 1;
        handle.Scale.ScaleX = 1;
        handle.Scale.ScaleY = 1;
        if (handle.Lift is { } resting)
        {
            resting.X = 0;
            resting.Y = 0;
        }
        Add(storyboard, window, UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(WindowOpenMs)) { EasingFunction = SoftOut, FillBehavior = FillBehavior.Stop });
        AddScale(storyboard, handle.Scale, WindowScaleFrom, 1, WindowOpenMs);
        if (handle.Lift is { } lift)
            AddLift(storyboard, lift, RisePx, 0, WindowOpenMs);
        return storyboard;
    }

    /// <summary>Fades a window out over <see cref="WindowCloseMs"/> and releases its clocks.</summary>
    public static Storyboard WindowOut(Window window, FrameworkElement? content, Handle? handle, Action completed)
    {
        var storyboard = new Storyboard();
        Add(storyboard, window, UIElement.OpacityProperty,
            new DoubleAnimation(window.Opacity, 0, TimeSpan.FromMilliseconds(WindowCloseMs)) { EasingFunction = EaseIn, FillBehavior = FillBehavior.HoldEnd });
        var done = false;
        storyboard.Completed += (_, _) =>
        {
            if (done)
                return;
            done = true;
            if (content is not null && handle is not null)
                Release(content, handle);
            window.BeginAnimation(UIElement.OpacityProperty, null);
            window.Opacity = 0;
            completed();
        };
        return storyboard;
    }

    // ------------------------------------------------------------------ slices, highlight, feedback

    /// <summary>One wheel slice opening, delayed by its index (≤ 20 ms per segment).</summary>
    public static Storyboard Stagger(FrameworkElement group, int index, double fromScale = 0.82)
    {
        var delay = TimeSpan.FromMilliseconds(StaggerMs * Math.Max(0, index));
        var storyboard = new Storyboard();
        group.Opacity = 1;
        Add(storyboard, group, UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(SliceMs)) { BeginTime = delay, EasingFunction = EaseOut, FillBehavior = FillBehavior.Stop });
        if (ScaleOf(group) is { } scale)
        {
            scale.ScaleX = 1;
            scale.ScaleY = 1;
            Add(storyboard, scale, ScaleTransform.ScaleXProperty,
                new DoubleAnimation(fromScale, 1, TimeSpan.FromMilliseconds(SliceMs)) { BeginTime = delay, EasingFunction = EaseOut, FillBehavior = FillBehavior.Stop });
            Add(storyboard, scale, ScaleTransform.ScaleYProperty,
                new DoubleAnimation(fromScale, 1, TimeSpan.FromMilliseconds(SliceMs)) { BeginTime = delay, EasingFunction = EaseOut, FillBehavior = FillBehavior.Stop });
        }
        return storyboard;
    }

    /// <summary>The highlighted slice travels outwards over <see cref="HighlightMs"/>.</summary>
    public static void Slide(TranslateTransform move, double x, double y) =>
        Slide(move, x, y, HighlightMs);

    /// <summary>Slides a translate transform to a new place; used for the wheel's travelling highlight.</summary>
    public static void Slide(TranslateTransform move, double x, double y, double durationMs)
    {
        if (!Enabled)
        {
            move.BeginAnimation(TranslateTransform.XProperty, null);
            move.BeginAnimation(TranslateTransform.YProperty, null);
            move.X = x;
            move.Y = y;
            return;
        }
        var duration = TimeSpan.FromMilliseconds(durationMs);
        move.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(move.X, x, duration) { EasingFunction = EaseOut, FillBehavior = FillBehavior.HoldEnd });
        move.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(move.Y, y, duration) { EasingFunction = EaseOut, FillBehavior = FillBehavior.HoldEnd });
    }

    /// <summary>Fades the wheel's highlight ring / slice in or out without touching layout.</summary>
    public static void Fade(UIElement element, double to, double durationMs = HoverMs)
    {
        if (!Enabled)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = to;
            return;
        }
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(element.Opacity, to, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = SoftOut, FillBehavior = FillBehavior.HoldEnd });
    }

    /// <summary>Success pulse: a quick, small scale bounce (never affects layout).</summary>
    public static Storyboard Pulse(FrameworkElement element)
    {
        var storyboard = new Storyboard();
        if (EnsureScale(element) is not { } scale)
            return storyboard;
        var frames = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(PulseMs) };
        frames.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(0)));
        frames.KeyFrames.Add(new EasingDoubleKeyFrame(1.06, KeyTime.FromPercent(0.35), EaseOut));
        frames.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1), EaseOut));
        Add(storyboard, scale, ScaleTransform.ScaleXProperty, frames);
        var framesY = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(PulseMs) };
        framesY.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(0)));
        framesY.KeyFrames.Add(new EasingDoubleKeyFrame(1.06, KeyTime.FromPercent(0.35), EaseOut));
        framesY.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1), EaseOut));
        Add(storyboard, scale, ScaleTransform.ScaleYProperty, framesY);
        return storyboard;
    }

    /// <summary>Badge pop: grows from nothing with a light settle. Used for job results and the Lock badge.</summary>
    public static Storyboard PopBadge(FrameworkElement badge) => Pop(badge);

    /// <summary>Badge pop: grows from nothing with a light settle.</summary>
    public static Storyboard Pop(FrameworkElement element)
    {
        var storyboard = new Storyboard();
        element.Opacity = 1;
        Add(storyboard, element, UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(WindowCloseMs)) { EasingFunction = SoftOut, FillBehavior = FillBehavior.Stop });
        if (EnsureScale(element) is { } scale)
        {
            scale.ScaleX = 1;
            scale.ScaleY = 1;
            AddScale(storyboard, scale, 0.7, 1, PopMs, Settle);
        }
        return storyboard;
    }

    // ------------------------------------------------------------------ hover / press (pre-frozen)

    private static readonly DoubleAnimation HoverIn = FreezeAnimation(
        new DoubleAnimation(1, 0.86, TimeSpan.FromMilliseconds(HoverMs)) { EasingFunction = EaseOut, FillBehavior = FillBehavior.HoldEnd });

    private static readonly DoubleAnimation HoverOut = FreezeAnimation(
        new DoubleAnimation(0.86, 1, TimeSpan.FromMilliseconds(HoverMs)) { EasingFunction = EaseOut, FillBehavior = FillBehavior.Stop });

    private static readonly DoubleAnimation PressIn = FreezeAnimation(
        new DoubleAnimation(1, 0.96, TimeSpan.FromMilliseconds(PressMs * 0.45)) { EasingFunction = EaseOut, FillBehavior = FillBehavior.HoldEnd });

    private static readonly DoubleAnimation PressOut = FreezeAnimation(
        new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(PressMs)) { EasingFunction = EaseOut, FillBehavior = FillBehavior.Stop });

    /// <summary>
    /// Adds the shared hover/press feedback to an interactive element. Handlers are installed once per
    /// element and are cheap when motion is disabled, so a Settings toggle can enable motion without
    /// rebuilding the controls.
    /// </summary>
    public static void Interactive(FrameworkElement element, bool press = true)
    {
        if ((bool)element.GetValue(InteractiveProperty))
            return;
        element.SetValue(InteractiveProperty, true);

        element.MouseEnter += (_, _) =>
        {
            if (Enabled && element.IsEnabled)
                element.BeginAnimation(UIElement.OpacityProperty, HoverIn);
        };
        element.MouseLeave += (_, _) =>
        {
            if (Enabled)
                element.BeginAnimation(UIElement.OpacityProperty, HoverOut);
            PressScale(element, false);
        };
        if (!press)
            return;

        // Install the press transform now, so a later success pulse always has a scale to animate.
        EnsureScale(element);
        element.MouseLeftButtonDown += (_, _) => PressScale(element, true);
        element.MouseLeftButtonUp += (_, _) => PressScale(element, false);
        element.LostMouseCapture += (_, _) => PressScale(element, false);
    }

    /// <summary>Pressed micro-scale for a slice or any element that owns its scale transform.</summary>
    public static void Press(ScaleTransform scale, bool down)
    {
        if (!Enabled)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = 1;
            scale.ScaleY = 1;
            return;
        }
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, down ? PressIn : PressOut, HandoffBehavior.SnapshotAndReplace);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, down ? PressIn : PressOut, HandoffBehavior.SnapshotAndReplace);
    }

    private static void PressScale(FrameworkElement element, bool down)
    {
        if (!Enabled || !element.IsEnabled)
            return;
        if (element.GetValue(PressScaleProperty) is not ScaleTransform scale)
            return;
        Press(scale, down);
    }

    /// <summary>The scale transform an interactive element presses with, or null when it must not scale.</summary>
    private static ScaleTransform? EnsureScale(FrameworkElement element)
    {
        if (element.GetValue(PressScaleProperty) is ScaleTransform existing)
            return existing;

        ScaleTransform? scale = null;
        if (element.RenderTransform is null || ReferenceEquals(element.RenderTransform, Transform.Identity))
        {
            scale = new ScaleTransform(1, 1);
            element.RenderTransform = scale;
            element.RenderTransformOrigin = new Point(0.5, 0.5);
        }
        else if (element.RenderTransform is ScaleTransform own && element.RenderTransformOrigin == new Point(0.5, 0.5))
        {
            scale = own;
        }
        else if (element.RenderTransform is TransformGroup group)
        {
            scale = group.Children.OfType<ScaleTransform>().FirstOrDefault();
        }
        if (scale is not null)
            element.SetValue(PressScaleProperty, scale);
        return scale;
    }

    /// <summary>Finds a scale transform already installed by <see cref="Prepare"/> or <see cref="EnsureScale"/>.</summary>
    private static ScaleTransform? ScaleOf(FrameworkElement element) =>
        element.RenderTransform switch
        {
            ScaleTransform scale => scale,
            TransformGroup group => group.Children.OfType<ScaleTransform>().FirstOrDefault(),
            _ => null,
        };

    private static readonly DependencyProperty InteractiveProperty = DependencyProperty.RegisterAttached(
        "Interactive", typeof(bool), typeof(Motion), new PropertyMetadata(false));

    private static readonly DependencyProperty PressScaleProperty = DependencyProperty.RegisterAttached(
        "PressScale", typeof(ScaleTransform), typeof(Motion), new PropertyMetadata(null));

    // ------------------------------------------------------------------ window resize (the one exception)

    /// <summary>
    /// The only width/height animation in the app: a tool window follows the media orientation after its
    /// preview probe. Values are placed as the base value and animated from the observed one with
    /// <see cref="FillBehavior.Stop"/>, so no clock is left behind after the resize.
    /// </summary>
    public static void Resize(Window window, double left, double top, double width, double height)
    {
        if (!Enabled)
        {
            window.Width = width;
            window.Height = height;
            window.Left = left;
            window.Top = top;
            return;
        }
        AnimateTo(window, Window.WidthProperty, width);
        AnimateTo(window, Window.HeightProperty, height);
        AnimateTo(window, Window.LeftProperty, left);
        AnimateTo(window, Window.TopProperty, top);
    }

    private static void AnimateTo(Window window, DependencyProperty property, double value)
    {
        var from = (double)window.GetValue(property);
        if (Math.Abs(from - value) < 0.5)
            return;
        window.SetValue(property, value);
        window.BeginAnimation(property, new DoubleAnimation(from, value, TimeSpan.FromMilliseconds(ResizeMs))
        {
            EasingFunction = EaseInOut,
            FillBehavior = FillBehavior.Stop,
        });
    }

    // ------------------------------------------------------------------ storyboard plumbing

    /// <summary>Starts a factory storyboard against <paramref name="host"/> (its name scope owns the clocks).</summary>
    public static void Play(Storyboard storyboard, FrameworkElement host)
    {
        if (storyboard.Children.Count == 0)
            return;
        storyboard.Begin(host);
    }

    private static void Add(Storyboard storyboard, DependencyObject target, DependencyProperty property, Timeline animation)
    {
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(property));
        storyboard.Children.Add(animation);
    }

    private static void AddScale(Storyboard storyboard, ScaleTransform scale, double from, double to, double durationMs,
        IEasingFunction? easing = null)
    {
        var duration = TimeSpan.FromMilliseconds(durationMs);
        var ease = easing ?? EaseOut;
        Add(storyboard, scale, ScaleTransform.ScaleXProperty,
            new DoubleAnimation(from, to, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        Add(storyboard, scale, ScaleTransform.ScaleYProperty,
            new DoubleAnimation(from, to, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
    }

    private static void AddLift(Storyboard storyboard, TranslateTransform lift, double from, double to, double durationMs)
    {
        var duration = TimeSpan.FromMilliseconds(durationMs);
        Add(storyboard, lift, TranslateTransform.YProperty,
            new DoubleAnimation(from, to, duration) { EasingFunction = SoftOut, FillBehavior = FillBehavior.Stop });
    }

    private static IEasingFunction Freeze(EasingFunctionBase easing)
    {
        easing.Freeze();
        return easing;
    }

    private static DoubleAnimation FreezeAnimation(DoubleAnimation animation)
    {
        animation.Freeze();
        return animation;
    }

    /// <summary>Animation clock tier for the self-test/diagnostics: 2 = full hardware acceleration.</summary>
    public static int RenderTier
    {
        get
        {
            try
            {
                return (int)RenderCapability.Tier >> 16;
            }
            catch (InvalidOperationException)
            {
                return 0;
            }
        }
    }
}
