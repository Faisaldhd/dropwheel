# DropWheel motion specification (2.4.0)

Motion is a **theme-level token set**, not decoration added window by window. Every duration, easing
curve and distance lives in one file, `src/FileAction.App/UI/Motion/Motion.cs`, and every surface asks
that file for its animation. No window, wheel or button hard-codes a duration of its own.

The rule that keeps it cheap: **only `RenderTransform` and `Opacity` are animated**. The single
exception is the media-orientation resize of a tool window, which animates width/height for 180 ms
because it is the one case where the layout itself is the transition. Nothing is animated on a timer:
WPF storyboards run on the render thread's clock, and when reduced motion is on no storyboard is
started at all, so the app has no animation work while idle.

## Tokens

| token | value | used by |
| --- | ---: | --- |
| `WheelOpenMs` | 150 ms | wheel open |
| `WheelScaleFrom` | 0.88 | wheel open scale |
| `StaggerMs` | 18 ms per slice | wheel slice stagger (budget <= 20 ms) |
| `SliceMs` | 150 ms | one slice opening |
| `HighlightMs` | 100 ms | travelling highlight between slices |
| `PressMs` | 90 ms | pressed micro-scale |
| `SwitchMs` | 170 ms | wheel mode switch (budget <= 180 ms) |
| `WheelCloseMs` | 100 ms | wheel close |
| `WindowOpenMs` | 180 ms | tool windows, settings, dialogs |
| `WindowScaleFrom` | 0.97 | window/tool open scale |
| `RisePx` | 8 px | window/tool open rise |
| `WindowCloseMs` | 120 ms | tool/settings/dialog close |
| `ResizeMs` | 180 ms | tool window media-orientation resize |
| `HoverMs` | 100 ms | hover/press on buttons, sliders, toggles, chips, segmented pills |
| `ToggleMs` | 120 ms | toggle knob slide |
| `PulseMs` | 320 ms | success pulse |
| `PopMs` | 260 ms | badge pop (Lock/success/error) |
| `SwitchRotateDegrees` | 4 deg | wheel mode switch lean |

Easings: `EaseOut` (Cubic), `EaseIn` (Cubic), `EaseInOut` (Cubic), `SoftOut` (Quadratic, cross-fades),
and `Settle` (Back, amplitude 1.0) for the small overshoot on open/close and the success pulse. All are
frozen once at startup.

## Wheel

| element | trigger | duration | easing | animated property |
| --- | --- | ---: | --- | --- |
| whole disc | wheel opens | 150 ms | `SoftOut` fade, `Settle` scale | window opacity, disc scale 0.88 -> 1 |
| slices | wheel opens / mode switch | 150 ms each, 18 ms apart | `EaseOut` | slice opacity, slice scale 0.82 -> 1 |
| highlighted slice | pointer or keyboard moves the selection | 100 ms | `EaseOut` | slice translate (7 px along its angle) + accent fill |
| pressed slice | mouse down on a slice | 90 ms | `EaseOut` | slice scale 1 -> 0.96 -> 1 |
| mode switch | Tab, modifier key, tab click, repeat toggle, language switch | 170 ms total | `EaseIn` out, `EaseOut` in | disc opacity 1 -> 0.25 -> 1, disc rotation 0 -> -4 deg -> 0 |
| whole disc | Esc, action, deactivate, drag ended | 100 ms | `EaseIn` | window opacity 1 -> 0, disc scale 1 -> 0.965 |

Overshoot budget: the open uses `BackEase(Amplitude = 0.7)` over 0.88 -> 1, which peaks at about
**1.026 (2.6 % overshoot)**, inside the 4 % budget. The wheel opens from the cursor because the window
is centred on the pointer and its `RenderTransformOrigin` is the disc centre, which is the pointer.

## Tool windows

| element | trigger | duration | easing | animated property |
| --- | --- | ---: | --- | --- |
| chrome (title bar + content root + button bar) | window opens | 180 ms | `SoftOut` fade/rise, `Settle` scale | chrome opacity 0 -> 1, translate Y 8 -> 0 px, scale 0.97 -> 1 |
| chrome | close button, Esc, Apply success, action bar | 120 ms | `EaseIn` | chrome opacity 1 -> 0, scale 1 -> 0.985 |
| window frame | media probe reports portrait/landscape | 180 ms | `EaseInOut` | width/height/left/top (the only layout animation) |
| buttons, close button, segmented pills, slider thumb, toggle, chips | hover / press | 100 ms hover, 90 ms press | `EaseOut` | opacity 1 -> 0.86, scale 1 -> 0.96 -> 1 |
| toggle knob | toggled | 120 ms | `SoftOut` | translate X |
| primary button | operation succeeded | 320 ms | `Settle` | scale 1 -> 1.06 -> 1, then the 120 ms close |
| job badge (and any Lock badge) | badge shown | 260 ms | `Settle` | opacity 0 -> 1, scale 0.7 -> 1 |

The tool-window hook is **one place**: `ToolWindow` builds a single chrome `Border` that contains the
title bar, the tool's view (the content root) and the button bar, and `Motion.Prepare`/`Motion.Open`/
`Motion.Close` are applied to that border. A new tool inherits the whole open/close/resize/reduced-motion
behaviour without touching its own file; it only calls `Motion.Interactive(chip)` for custom chips.

## Settings and dialogs

`UiSetup.Configure(window)` is the single hook used by Settings, Jobs, the shelf, the wheel editor,
`ParameterDialog` and the custom-colour dialog. It attaches:

| element | trigger | duration | easing | animated property |
| --- | --- | ---: | --- | --- |
| window | opens | 180 ms | `SoftOut` fade, `Settle` scale | window opacity 0 -> 1, content translate Y 8 -> 0, scale 0.97 -> 1 |
| window | closes | 120 ms | `EaseIn` | window opacity 1 -> 0 |

`MessageBox` dialogs are native Windows windows and are not animated; the app cannot style them.

## Reduced motion

Motion is allowed only when **both** of these are true:

1. Windows' accessibility setting **Settings > Accessibility > Visual effects > Animation effects**
   (`SystemParameters.ClientAreaAnimation`) is on. `Motion` caches it, listens for
   `SystemParameters.StaticPropertyChanged`, and re-reads it live: turning it off stops future
   animations without a restart.
2. The new **Settings > General > Animations** toggle is on (`AppSettings.Animations`, default on).
   The Settings card states when Windows has overridden the app toggle.

When either is off:

- `Motion.Enabled` is false, so no storyboard is created and `Enabled` is checked inside every event
  handler (hover, press).
- Callers take their instant path: the wheel appears at its final pose, tool windows open/close at
  their final size, the media resize is a single layout change, and there is no success-pulse delay.
- Nothing polls and nothing is scheduled, so idle CPU is unchanged from 2.3.0.

The self-test forces the setting through `Motion.Override` (true = animate, false = reduced motion) and
measures both, so the reduced-motion path is covered on every CI run.

## Performance rules in the implementation

- **Frozen brushes and frozen easing/animations.** Hover and press use pre-frozen `DoubleAnimation`
  instances re-applied with `BeginAnimation`; only storyboards with completion callbacks allocate, and
  only when a surface opens or closes.
- **No per-frame allocation.** Poses are plain property writes; no LINQ, delegates or timers run per
  frame. Interactive elements attach handlers once, guarded by an attached property.
- **Stop and release.** Close storyboards clear their clocks when they complete; `Motion.Release`
  clears opacity and transform clocks. A closed window holds no animation clock.
- **No polling timers.** The only timers are the pre-existing one-shot drag-end timer and the
  self-test's measurement timer.
- **GPU rendering.** Transforms and opacity are compositor-friendly; the app does not force software
  rendering. `Motion.RenderTier` records WPF's tier in the self-test (2 = full hardware acceleration).
- **DPI and RTL** are untouched: motion uses render transforms only, never layout mirrors, and the
  wheel's disc keeps `FlowDirection.LeftToRight` while its text follows the app language.

## Verification

`DropWheel.exe --self-test <dir>` now includes a motion gate (see `docs/PERF.md`):

- opens the wheel and every registered tool window in a deterministic pose sequence and records
  **UI-thread frame p95** with motion ON versus OFF, plus a software-rasterisation proxy for one
  composed frame;
- records **CPU during** the measurement and **idle CPU** for one second after everything is closed;
- records the **working set retained** after close, budget 12 MB (inside the 15 MB idle-memory rule);
- fails the self-test when UI frame p95 > 16.7 ms, idle CPU > 5 %, or retention > 12 MB.

Startup (+5 %) and installer size (+6 MB) are guarded by the package/performance gates described in
`docs/PERF.md`; motion adds one small class, no dependency and no startup work beyond reading two
booleans.

## Website clips

`docs/screenshots/motion/` contains short animated WebP clips (480 px wide, 12.5 fps, looping) and PNG
poster frames, in dark and light:

- `wheel-{dark,light}.webp` - opening, the travelling highlight and closing, looping;
- `tool-<id>-{dark,light}.webp` - a registered tool window opening, holding and closing.

They are **renders of the real visual tree**, produced by `--self-test` through
`MotionClips`/`Pose`, using the same easing curves and durations as the live storyboards. They are not
mock-ups and not a screen recording: CI has no GPU-backed interactive desktop, so each frame is a
deterministic `RenderTargetBitmap` of the shipped UI at a sampled animation time. Re-render with
`scripts/screenshots.cmd` (or the CI screenshots job) after a motion change. Total assets are kept
small; the self-test prints the combined size.
