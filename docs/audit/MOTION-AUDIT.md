# Motion audit — DropWheel 2.4.0

Lane: TEAM B, lane 4 (motion auditor). Branch `audit/amotion`, PR
[#40](https://github.com/Faisaldhd/dropwheel/pull/40), base `origin/main` `4f23c9c`.
Measured code state: `17a6265` (CI run **39376236166**). Commits after it (`849f420`, `96f64ae`,
`517843e` and this doc) touch the theme-watcher timer, tests, a dead-code removal and docs — none of
them changes a motion pose, and the harness opens no video tool, so the measured table stands for the
PR head; the next CI run re-proves it green.

All numbers in §5 are read from the CI artifact `self-contained-self-test` → `motion/report.md` of that
run. Nothing was built, installed or executed locally (company-laptop/CrowdStrike rule): CI is the only
place code runs.

**Verdict.** The two motion laws hold mechanically across the whole shipped UI: only `RenderTransform`
and `Opacity` are animated, and `Motion.Resize` is the **only** layout animation, with exactly one
caller. No `ThicknessAnimation`, `ColorAnimation`, brush, colour or effect animation exists, and no
animation is driven by a timer. Three real defects were found and fixed on this branch — two
reduced-motion breaches (job cards, password switch) and one idle timer that never stopped (video theme
watcher). The claim that does **not** survive verification is coverage: the gate measures the wheel and
**11 of the 26** registered tool windows, in **English/LTR, one runner theme**. §4 states exactly what
may be claimed at the tag.

## 1. Law 1 — only `RenderTransform` and `Opacity` are animated

`docs/MOTION.md:7-11` states the law. Every animation in the app is listed below; "property" is what the
clock touches, and every duration is a token from `src/FileAction.App/UI/Motion/Motion.cs:30-87`.

### Wheel

| # | surface | trigger | animated property | duration / easing | where |
| ---: | --- | --- | --- | --- | --- |
| 1 | wheel window + disc | open | window `Opacity`; disc `ScaleX/ScaleY` 0.88 → 1 | 150 ms, `SoftOut` / `Settle` | `WheelWindow.cs:194-202` → `Motion.cs:260` |
| 2 | each slice group | wheel open, rebuild | group `Opacity`; `ScaleX/ScaleY` 0.82 → 1 | 150 ms each, 18 ms apart, `EaseOut` | `WheelWindow.cs:843-860` → `Motion.cs:362` |
| 3 | highlighted slice | pointer / keyboard move | `TranslateTransform.X/Y` (7 px along the angle) | 100 ms, `EaseOut` | `WheelWindow.cs:1021-1023` → `Motion.cs:382-393` |
| 4 | pressed slice | mouse down/up | `ScaleX/ScaleY` 1 → 0.96 → 1 | 40.5 ms in, 90 ms out, `EaseOut` | `WheelWindow.cs:977-987` → `Motion.cs:488-496` |
| 5 | whole disc | mode switch (Tab, modifier, tab click, repeat, language) | `Opacity` 1 → 0.25 → 1; `RotateTransform.Angle` 0 → −4° → 0 | 68 + 102 = 170 ms, `EaseIn`/`EaseOut` | `WheelWindow.cs:310-339`, storyboard `:352-374` |
| 6 | wheel window + disc | Esc, action, deactivate, drag ended | window `Opacity`; disc scale → 0.965 | 100 ms, `EaseIn` | `WheelWindow.cs:394-411` → `Motion.cs:273` |

### Tool windows (`UI/Tools/ToolWindow.cs`)

| # | surface | trigger | animated property | duration / easing | where |
| ---: | --- | --- | --- | --- | --- |
| 7 | chrome (title + view + button bar) | open | `Opacity` 0 → 1; `ScaleX/Y` 0.97 → 1; lift `Y` 8 → 0 | 180 ms, `SoftOut`/`Settle` | `ToolWindow.cs:126-137` → `Motion.cs:240` |
| 8 | chrome | close button, Esc, Apply | `Opacity` 1 → 0; scale → 0.985 | 120 ms, `EaseIn` | `ToolWindow.cs:262-280` → `Motion.cs:293` |
| 9 | **window frame** | media probe reports orientation | **`Width`, `Height`, `Left`, `Top`** | 180 ms, `EaseInOut` | `ToolWindow.cs:170-175` → `Motion.cs:559-586` |
| 10 | primary button | operation succeeded | `ScaleX/Y` 1 → 1.06 → 1 | 320 ms, `Settle` keyframes | `ToolWindow.cs:379-383` → `Motion.cs:404-412` |
| 11 | buttons, close, segmented pills, slider thumb, chips | hover / press | `Opacity` 1 ↔ 0.86; `ScaleX/Y` 1 ↔ 0.96 | 100 ms hover, 40.5/90 ms press, `EaseOut` | `Motion.cs:443-456`, installed by `Motion.cs:460`; call sites `Controls.cs:121,169,232,268`, `ToolWindow.cs:310`, `CropTool.cs:226` |
| 12 | toggle knob | toggled | `TranslateTransform.X` | 120 ms, `SoftOut` | `Controls.cs:157-161`; `PasswordViewParts.cs:339-352` (fixed here) |
| 13 | job badge (and Lock badge) | shown | `Opacity` 0 → 1; `ScaleX/Y` 0.7 → 1 | 120 ms fade + 260 ms `Settle` | `JobsWindow.cs:307-319` → `Motion.cs:426-434` |

### Settings, dialogs, job cards

| # | surface | trigger | animated property | duration / easing | where |
| ---: | --- | --- | --- | --- | --- |
| 14 | settings / jobs / shelf / wheel editor / dialogs | open | window `Opacity` 0 → 1; content scale 0.97 → 1, lift `Y` 8 → 0 | 180 ms, `SoftOut`/`Settle` | `UiSetup.cs:40-49` → `Motion.cs:319` |
| 15 | same windows | close | window `Opacity` 1 → 0 | 120 ms, `EaseIn` | `UiSetup.cs:50-64` → `Motion.cs:339` |
| 16 | job/progress card | added | `TranslateTransform.X` 40 → 0; `Opacity` 0 → 1 | 200 ms, `SoftOut` (`ToastMs`) | `JobsWindow.cs:189-199` (fixed here) |
| 17 | indeterminate progress fill | job starts | `Opacity` 0.35 ↔ 1 | 700 ms each way (`BreathMs`), gated by `Motion.Enabled` | `JobsWindow.cs:212-229` (fixed here) |

That is the complete inventory — 17 animated elements, every one on `UIElement.Opacity` or a
`ScaleTransform`/`TranslateTransform`/`RotateTransform`, except row 9. There are no `.xaml` files in the
repository, so nothing can escape the source scan.

### What the scan proves

- **No `ThicknessAnimation`, no `ColorAnimation`, no brush/colour/effect animation exists** anywhere in
  `src/FileAction.App` (0 matches). Nothing animates `Margin`, `Padding`, `FontSize`, `Background`,
  `Foreground`, `Fill`, `Stroke`, `BlurRadius` or `LayoutTransform`.
- Property-changing code that is **not** animated (allowed, and deliberately instant): highlight
  `Fill`/`Stroke`/`Effect` (`WheelWindow.cs:1001-1012`), segmented-pill colours (`Controls.cs:99-101`),
  toggle track/knob colours (`Controls.cs:153-155`), badge fill (`JobsWindow.cs:303`), slider fill width
  + thumb margin during a drag (`Controls.cs:199-200`, direct manipulation, no clock), wheel
  fit-to-work-area `Width`/`Height`/`LayoutTransform` (`WheelWindow.cs:247-249`, snapped).
- **The single layout animation is confirmed to be only one.** `Window.WidthProperty`,
  `HeightProperty`, `LeftProperty` and `TopProperty` appear in exactly one file (`Motion.cs:569-572`)
  and `Motion.Resize(` has exactly one caller (`ToolWindow.cs:174`), reachable only from
  `PrepareAsync(animate: true)` after the media probe. No other window, wheel, dialog or control
  animates layout — not the wheel (`FitToWorkArea` snaps), not Settings, not the shelf, not Jobs.
- A deterministic guard now enforces all of this in `dotnet test` (§6).

## 2. Law 2 — reduced motion is honoured everywhere

Mechanism: `Motion.Enabled` (`Motion.cs:127`) is `Override ?? (AppSettings.Animations &&
SystemParameters.ClientAreaAnimation)`. The Windows value is read live (`Motion.cs:132-153`) and the
Settings toggle writes through `AppSettings` → `Motion.SetSettingsEnabled` (`App.cs:109,228`,
`SettingsWindow.cs:283`). Call sites check `Motion.Enabled` **at event time**, so turning either switch
off affects the next surface without a restart.

| surface | guard when motion is off | storyboard when off? |
| --- | --- | --- |
| wheel open | `WheelWindow.cs:194-202` → `Release` + `Opacity = 1` | no |
| slice stagger | `WheelWindow.cs:848-857` → clocks cleared, values at rest | no |
| highlight travel | `WheelWindow.cs:1021-1031` → `X/Y` set directly | no |
| slice press | `WheelWindow.cs:979` + `Motion.Press` `:488-496` | no |
| wheel mode switch | `WheelWindow.cs:312-338` → `CommitView` + reset | no |
| wheel close | `WheelWindow.cs:404-407` → plain `Close()` | no |
| tool open | `ToolWindow.cs:130-135` → `Reset` + `Opacity = 1` | no |
| tool close | `ToolWindow.cs:264-265` → `base.OnClosing` | no |
| media resize | `Motion.Resize` `:559-567` → one layout assignment | no |
| success pulse | `ToolWindow.cs:379` | no |
| hover / press | `Motion.cs:468,473,488` | no |
| toggle (shared) | `Controls.cs:157` | no |
| toggle (password) | `PasswordViewParts.cs:338` (**fixed**, `ebaccc4`) | was yes → now no |
| badge pop | `JobsWindow.cs:313` | no |
| settings/dialog open+close | `UiSetup.cs:42,52` | no |
| job-card slide | `JobsWindow.cs:189` (**fixed**, `4c7bef1`) | was yes → now no |
| indeterminate breathing | `JobsWindow.cs:214` (**fixed**, `4c7bef1`) | was yes (forever) → now no |

After the fixes, **no storyboard is created and no clock is started when motion is off**; the instant
path is taken for all 17 elements. This is stronger than "zero duration": the code branches before
constructing or starting a timeline.

Residual design gap (M-09): `Motion.Play` (`Motion.cs:591`) itself does not check `Enabled`; the
guarantee rests on each call site. Adding a check there is unsafe — close storyboards run their
`Completed` callback (which closes the window), so silently not starting one would strand a window.
The new source test asserts that every file owning an animation also references `Motion.Enabled`.

## 3. Law 3 — no animation is driven by a timer or polling `DispatcherTimer`

Every `DispatcherTimer` in the shipped app was enumerated. After M-12's fix, none writes an animation
property and none survives its surface:

| timer | interval | repeats? | what the tick does | motion? |
| --- | ---: | --- | --- | --- |
| `WheelController._argsTimer` (`WheelController.cs:40-47`) | 350 ms | no (stops itself) | coalesces Explorer args from several processes, then opens the wheel | no |
| `WheelController._dragTimer` (`:52-53`, start `:65`, stop `:58`) | 150 ms | while the mouse button is held | samples the Explorer folder under the cursor for the drop-target label | no |
| `WheelWindow._closeTimer` (`WheelWindow.cs:390-391`) | 500 ms | no | ends the wheel after a drag ends | no |
| `JobsWindow.CloseLater` (`JobsWindow.cs:325`) | 8 s | no | removes a finished card | no |
| `VideoThemeWatcher` (`VideoThemeWatcher.cs:27-46`) | 500 ms | **only while a video tool is open** (**fixed**, `849f420` + guard `96f64ae`) | polls `Theme.IsDark` for a live theme change | no |
| `LockSessions` (`LockSessions.cs:63`) | 2 s | yes | protection-runtime unlock polling | no |
| `RescuerWindows` (`RescuerWindows.cs:681`) | 60 s | yes | protection-runtime retry | no |
| `EditPhotosView` debounce (`EditPhotosView.cs:58`) | 150 ms | no | re-renders the preview after an edit | no |
| `AudioPreview` (`AudioPreview.cs:14`) | 80 ms | while playing | reports the playback position to the view | no (media playback) |
| `VideoPreview` (`VideoPreview.cs:262`, tick `:322-338`) | 80 ms | while playing | reports the playback position to the view | no (media playback) |
| `MotionSelfTest.IdleCpu` (`MotionSelfTest.cs:346`) | 1 s | no | the measurement window itself | n/a |
| `SelfTest.Pump` guard (`SelfTest.cs:496`) | 30 s | no | test timeout | n/a |

Every animation in §1 is a WPF storyboard/clock started by a user or lifecycle event; none is stepped
from a tick handler. **Law 3 holds.** The idle-CPU gate is real but narrow: it samples one second with
every surface closed (`MotionSelfTest.cs:161-168`), so it can only catch a timer that outlives its
surface — which is exactly the M-12 defect, and why M-12 mattered.

`docs/MOTION.md:115-116` claimed there were only two timers. That was wrong (10 application timers, two
repeating while a surface is open); corrected in `17a6265` to say what is actually true: motion itself
schedules nothing, and none of the pre-existing timers drives an animation.

## 4. Coverage — what `MotionSelfTest` really measures

### (a) How many tool windows are measured?

`ToolRegistry.All` contains **26** ids (26 `[ModuleInitializer]` registrations under
`src/FileAction.App/UI/Tools/**`; main's newest merge also says "all 26 tools reachable"). Run
`39376236166` measured **12 surfaces: the wheel + 11 tool windows** — `collage`, `compress`, `annotate`,
`crop`, `editphotos`, `addbackground`, `redact`, `metadata`, `protect`, `pdfmerge`, `qr`. The other
**15** are named in `motion/report.md:20` and `self-test.txt`:

- 5 audio tools, 7 video tools, `tools.pdforganize`, `tools.unlock` — they reject the harness's single
  640×420 PNG (`MotionSelfTest.cs:103-114`), so they are skipped by media family;
- `tools.lock` — **skipped by a 30 s timeout**: `LockView`'s offscreen preparation never completes
  (`TimeoutException: the tool window preview did not finish within 30 s`). This costs 30 s in
  `MotionSelfTest` plus 30 s per theme in `MotionClips` (≈ 90 s per CI run) and leaves the lock window
  unmeasured (M-13).

The `tools.collage` skip was the one this lane fixed: `CollageTool.Applies` needs at least two images
(`CollageTool.cs:55-57`) and the harness fed one. `MotionSelfTest.SampleItems`
(`MotionSelfTest.cs:215-218`) now retries with two copies of the same sample, which is why collage is a
measured row in this run; the same helper is used by `MotionClips`. Side effect worth knowing:
`tools.pdfmerge` also accepts two PNGs, so it is measured with image inputs — a real window state, but
not its real PDF state.

Can the skips hide a real regression? **Partly.** All 26 tools share the `ToolWindow` chrome and the
same `Pose`/`WindowIn` path, so the animated properties, durations and the reduced-motion branch are
identical and are exercised by the 11 measured windows. What is unmeasured for the 15 skipped windows is
their **view layout cost during the pose**. With UI-frame p95 at 0.02-0.03 ms against a 16.7 ms budget
(~500× headroom) the practical risk is negligible, but "every registered tool window is measured" is
false and must not appear in release notes.

### (b) Which culture and theme?

**English, left-to-right, one theme only.** `SelfTest.Run` applies `UiLanguage.English` at
`SelfTest.cs:68` immediately before the step list (`SelfTest.cs:74`) that runs `MotionSelfTest.Run`;
`MotionSelfTest` contains no `LanguageManager`/`UiLanguage` call and leaves `Theme.Override = null`
(`SelfTest.cs:69`) — the runner's Windows theme. `MotionClips` does render dark and light
(`MotionClips.cs:53-58, 89-92`) but never switches language. The en/ar, dark/light screenshot matrix
(`SelfTest.RenderAll`, `SelfTest.cs:45-62`) is a separate preview pass and does not render animated
poses. No motion measurement exists for Arabic/RTL.

Clip assets: this run produced 24 clips (12 surfaces × dark/light, 991 KB, 480 px, 80 ms/frame) but the
committed `docs/screenshots/motion/` still holds the older 12 files (wheel + 5 tools) — the assets lag
the harness and are English only.

### (c) What may be claimed at the tag

Allowed, evidence-backed (run `39376236166`, `motion/report.md`):

> The motion gate drives the wheel plus 11 image-family tool windows (15 media-family tools are skipped
> and named in `motion/report.md`), in English/LTR on the CI runner's theme, and gates UI-frame p95,
> idle CPU and retention. All 26 tool windows share the same animated chrome code path; the unmeasured
> part is each skipped tool's view layout.

Not allowed: "every registered tool window is measured", "all 25 tools" (wrong count — it is 26),
"Arabic/RTL motion is verified", "both themes are verified for motion".

### (d) The neighbouring self-test claim

`SelfTest.cs:449-467` says it "builds every registered floating tool offscreen" but constructs only
`CropTool` (`SelfTest.cs:465`). That is a strings/layout claim, not motion; it is l10n lane's F-02
(PR #30) and selftest lane's GAP 2 (PR #37). Same overstatement pattern — reword it or iterate the
registry.

## 5. Measured on CI

Run **[39376236166](https://github.com/Faisaldhd/dropwheel/actions/runs/39376236166)** — `CI` /
`Build + test`, branch `audit/amotion`, commit `17a6265`, **completed success** (build, self-contained
`--self-test`, signing self-test, apply-update helper, `dotnet test`, artifacts). Runner:
`Microsoft Windows NT 10.0.26100.0`, 4 logical processors, WPF render tier 2. Source:
`self-contained-self-test` artifact → `motion/report.md` (12 rows, 120 frames per row, warm-up pass
excluded).

| surface | UI p95 ON (ms) | UI p95 OFF (ms) | raster proxy p95 ON (ms) | CPU ON (%) | p95 budget | verdict |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| wheel (442×442) | 0.04 | 0.00 | 38.83 | 24.9 | 16.7 ms | PASS (0.2 % of budget) |
| tool `tools.collage` | 0.02 | 0.00 | 46.45 | 25.6 | 16.7 ms | PASS |
| tool `tools.compress` | 0.02 | 0.00 | 49.85 | 25.1 | 16.7 ms | PASS |
| tool `tools.annotate` | 0.02 | 0.00 | 64.64 | 25.0 | 16.7 ms | PASS |
| tool `tools.crop` | 0.02 | 0.00 | 72.10 | 25.1 | 16.7 ms | PASS |
| tool `tools.editphotos` | 0.02 | 0.00 | 49.09 | 25.0 | 16.7 ms | PASS |
| tool `tools.addbackground` | 0.02 | 0.00 | 51.55 | 24.9 | 16.7 ms | PASS |
| tool `tools.redact` | 0.02 | 0.00 | 69.09 | 24.8 | 16.7 ms | PASS |
| tool `tools.metadata` | 0.03 | 0.00 | 70.31 | 25.1 | 16.7 ms | PASS |
| tool `tools.protect` | 0.03 | 0.00 | 53.17 | 24.9 | 16.7 ms | PASS |
| tool `tools.pdfmerge` (image inputs) | 0.02 | 0.00 | 73.66 | 24.9 | 16.7 ms | PASS |
| tool `tools.qr` | 0.02 | 0.00 | 62.99 | 24.9 | 16.7 ms | PASS |

| gate | measured | budget | verdict |
| --- | ---: | ---: | --- |
| worst UI frame p95 ON | **0.04 ms** (wheel) | ≤ 16.7 ms | PASS, 0.24 % of budget |
| UI frame p95 OFF (instant path) | **0.00 ms** on all 12 surfaces | ≤ 16.7 ms | PASS |
| raster proxy p95 (reported, not a gate) | 38.83 – 73.66 ms | none (software rasterisation) | informational |
| CPU ON (measurement loop) | 24.8 – 25.6 % | none (one of 4 cores saturated by the loop) | informational |
| **idle CPU after close** | **0.00 %** | ≤ 5 % | PASS |
| **working set retained** | **0.0 MB** | ≤ 12 MB | PASS |
| render tier | 2 (full hardware acceleration) | — | PASS |

Reading: the app-controlled frame cost (pose write + layout) is ~0.02-0.04 ms, i.e. two to three orders
of magnitude below the 16.7 ms frame budget; motion-ON and motion-OFF are indistinguishable at this
resolution. The raster proxy is deliberately not a gate — `RenderTargetBitmap` software rasterisation
of a whole window is much slower than the GPU compositor (and the old unused `RasterBudgetMs = 33`
constant contradicted it; removed in `517843e`, M-15). CPU ON ~25 % is the measurement loop
(pose + software raster every frame) occupying one of four cores, not app idle cost; idle CPU is
0.00 %, which is the number that would expose a leftover timer.

## 6. Tests added (cheap, deterministic, no execution)

`tests/FileAction.Tests/MotionSourceTests.cs` — six source-scan facts, no WPF objects, no clock, run in
the existing `dotnet test` job (verified green in run 39376236166):

1. `Only_render_transform_and_opacity_are_animated` — every `BeginAnimation(`/`SetTargetProperty(`
   argument list may only name `OpacityProperty`, `ScaleXProperty`, `ScaleYProperty`, `XProperty`,
   `YProperty` or `AngleProperty`. Catches a future `Width`/`Margin`/`Color` animation at the callsite.
2. `The_media_resize_is_the_only_layout_animation` — `Window.Width/Height/Left/TopProperty` may appear
   only in `Motion.cs`, and `Motion.Resize(` may have only its one caller, `ToolWindow.cs`.
3. `No_animation_duration_is_hard_coded_outside_the_motion_tokens` — no animation constructor outside
   `Motion.cs` may contain (`FromMilliseconds(<number>`). This is the test that would have caught the
   job-card and password-switch defects.
4. `Every_file_that_owns_an_animation_consults_Motion_Enabled` — reduced-motion tripwire for new files.
5. `Tool_windows_route_all_motion_through_the_shared_hook` — `ToolWindow.cs` must use
   `Motion.Prepare/Open/Close/Resize` and may not construct its own animation; the harness must iterate
   `ToolRegistry.All` with both `Motion.Override` values.
6. `Every_theme_watch_is_released_when_its_surface_closes` — every `VideoThemeWatcher.Start()` file must
   call `VideoThemeWatcher.Stop()`, and the watcher must have a real `Stop` that stops `_timer`
   (regression guard for M-12).

Limits, stated in the class doc: line/regex scans with balanced-parenthesis argument capture, not a C#
parser; a construct split across statements is not followed; commented-out animation is ignored. They
complement the runtime gate, not replace it.

## 7. Findings

Severity: **High** = accessibility/regression with no test able to see it; **Medium** = false claim,
coverage or drift; **Low** = hygiene. Owner is the lane that must land the fix; "fixed" = commit on
`audit/amotion` / PR #40.

| id | sev | kind | finding | evidence | owner / PR / status |
| --- | --- | --- | --- | --- | --- |
| M-01 | High | law 2 | Job cards animated with baked durations and **ignored reduced motion**: a 220 ms slide+fade gated only by the self-test flag, plus an infinite `RepeatBehavior.Forever` opacity pulse that ran even with animations off. Invisible to the gate (`SelfTest.cs:35` disables job cards; the harness never creates one). | old `JobsWindow.cs:189-198`, `:211-220` | motion lane, PR #40 — **fixed** `4c7bef1` (`Motion.Enabled` gate, `ToastMs`, `BreathMs`, `SoftOut`) |
| M-02 | Medium | law 2 | Password `SwitchRow` knob hard-coded 120 ms + `new CubicEase()`, bypassing `Motion.ToggleMs`/`SoftOut` and ignoring reduced motion — a silent fork of `Controls.Toggle`. | old `PasswordViewParts.cs:338-346` | motion lane, PR #40 — **fixed** `ebaccc4` |
| M-03 | Medium | coverage | "Every registered tool window is measured" is false: 11 of 26 tools measured; 15 skipped; `tools.collage` (densest image view) was skipped for needing >1 image and `tools.lock` silently timed out. | `motion/report.md:20` (run 39376236166); old `MotionSelfTest.cs:108`; `CollageTool.cs:55-57`; `docs/PERF.md:55-57` | motion lane, PR #40 — **fixed partly** `da41e20` (collage measured, skips in the report); doc claims still to narrow — perf lane PR #24 owns `docs/PERF.md` |
| M-04 | Medium | coverage | Motion harness runs English/LTR only, one (runner) theme; no Arabic/RTL or both-theme motion measurement exists. Committed clips are English only and stale (wheel + 5 tools vs 12 measured surfaces). | `SelfTest.cs:68,74`; no `LanguageManager` in `MotionSelfTest`; `MotionClips.cs:53-58`; `docs/screenshots/motion/` | motion lane, PR #40 — **documented** §4(c); release-notes wording — docs lane PR #36 |
| M-05 | Low | hygiene | `Motion.Fade` had **no callers** (dead token API). | old `Motion.cs:403-414`, 0 grep hits | motion lane, PR #40 — **fixed** `517843e` |
| M-06 | Medium | docs | `docs/MOTION.md:115-116` claimed only two timers exist; there are 10 application timers (two repeating while a surface is open). Law 3 still holds — none drives motion. | §3 table; `WheelController.cs:40,52`, `VideoThemeWatcher.cs:36`, `LockSessions.cs:63` | motion lane, PR #40 — **fixed** `17a6265` |
| M-07 | Low | hygiene | The wheel's mode-switch storyboard is built inline in `WheelWindow.AnimateCanvas` — the only storyboard construction outside `Motion.cs`. Tokens/guards/properties are correct. | `WheelWindow.cs:352-374` | motion lane, PR #40 — **open** (deferred refactor to `Motion.Switch(...)`) |
| M-08 | Low | docs | The motion report labelled the wheel `424×424`; the element is `442×442` (`2*(196+9+16)`), and `docs/PERF.md:47` said 442. Report now derives the size from the measured root (`442×442` in run 39376236166). | old `MotionSelfTest.cs:100`; `WheelWindow.cs:30-39` | motion lane, PR #40 — **fixed** `17a6265` |
| M-09 | Low | design | `Motion.Play` does not check `Enabled` itself; the "no storyboard when off" guarantee depends on call sites. (Adding the check is unsafe: close `Completed` callbacks close the window.) | `Motion.cs:591-597` | motion lane, PR #40 — **open**, mitigated by test 4 and the §2 table |
| M-10 | Low | design | While an indeterminate job runs, the 700 ms forever-breathing opacity animation keeps animating; the idle-CPU gate cannot see it (no job card exists during measurement). Gated by `Motion.Enabled`; not finite on purpose (a stalled-looking bar is worse). | `JobsWindow.cs:212-229`; `SelfTest.cs:35` | motion lane, PR #40 — **open, accepted** (reduced-motion-gated) |
| M-11 | Low | docs | The documented resize exception said "width/height"; it also animates `Left`/`Top`. Wording fixed. | `Motion.cs:566-593`; `docs/MOTION.md:8-9` | motion lane, PR #40 — **fixed** `17a6265` |
| M-12 | High | law 3 | `VideoThemeWatcher` started a 500 ms `DispatcherTimer` that was **never stopped**: after the first video tool, the process polled the theme forever, a real idle cost (5 % budget) outside the measurement window. Reported by the creative lane (PR #35). | old `VideoThemeWatcher.cs:22-51`; `VideoCropView.cs:139,142`, `VideoSpeedView.cs:74,77`, `VideoTrimView.cs:94,97` | motion lane, PR #40 — **fixed** `849f420` (ref-counted `Start`/`Stop`; last surface stops the timer and unhooks `SystemEvents`) + guard `96f64ae` |
| M-13 | Medium | coverage/cost | `tools.lock` is unmeasured and burns ≈ 90 s per CI run (30 s in `MotionSelfTest` + 30 s per theme in `MotionClips`); its `LockView` preparation never completes offscreen. | `motion/report.md:20`; `self-test.txt` (`TimeoutException`, 3×) | lock lane PR #27 (fix the offscreen prep) or selftest lane PR #37 (shorter probe); motion harness could skip it explicitly — **open** |
| M-14 | Low | docs | The new "Not measured" report line said the skipped tools "want a media family the harness does not create", which is wrong for the lock timeout. Now neutral and points at the per-id reason. | `MotionSelfTest.cs:189-192` | motion lane, PR #40 — **fixed** `517843e` |
| M-15 | Low | hygiene | `RasterBudgetMs = 33.0` was unused as a gate while the measured raster proxy is 38.8-73.7 ms — a constant that contradicted the artifact. Removed. | old `MotionSelfTest.cs:33-34`; `motion/report.md` raster column | motion lane, PR #40 — **fixed** `517843e` |
| M-16 | Medium | docs | `docs/PERF.md` contradicts itself on idle CPU — table text says **0.39 %** (line 54) while line 63 says **0.00 %** — and its measured table is from run 39351795353 (6 surfaces, before six tools landed). It also implies tools are measured automatically, which the 15 skips disprove. | `docs/PERF.md:39-63` | perf lane PR #24 — **open**; motion lane supplies the run-39376236166 table and §4(c) wording |
| M-17 | Medium | release | The tree still ships **2.3.0**: `FileAction.App.csproj:24` and `installer/DropWheel.iss:16`. A `v2.4.0` tag would publish a setup named/versioned 2.3.0. | `src/FileAction.App/FileAction.App.csproj:24`; `installer/DropWheel.iss:16` | version lane PR #34 — **open**, must merge before the tag |

Law-1 violations: **none**. Law-2 violations: M-01, M-02 (fixed). Law-3 violations: M-12 (fixed).
Sum of animated surfaces that ignored reduced motion before this PR: 3 of 17 (M-01 counts two
animations, M-02 one); after it, 0 of 17.

## 8. Must land before the 11:00 tag

1. **M-01 / M-02 / M-12** — merge PR #40 (motion). These are the only accibility/idle-cost defects this
   lane found; all three now have regression tests.
2. **M-03 / M-04 / M-16** — the claim, not the code: `docs/PERF.md` (perf lane, PR #24) and the release
   notes (docs lane, PR #36) must use the §4(c) wording. Do not publish "every tool window measured",
   "all 25 tools" or "Arabic motion verified".
3. **M-17** — land the 2.4.0 version bump (PR #34) before tagging; otherwise the setup is 2.3.0.
4. **M-13** — decide whether to keep paying 90 s per run for `tools.lock`; cheapest is an explicit skip
   with a reason in `MotionSelfTest`/`MotionClips`, best is fixing `LockView`'s offscreen prep.

## Appendix — reproduce

```
gh run list --repo Faisaldhd/dropwheel --branch audit/amotion -L 3
gh run download 39376236166 --repo Faisaldhd/dropwheel -n self-contained-self-test -D _local/tmp/amotion/ci
Get-Content _local/tmp/amotion/ci/motion/report.md
Get-Content _local/tmp/amotion/ci/self-test.txt   # per-tool skip reasons and clip sizes
```

Artifacts were downloaded under `DropWheel_1/_local/tmp/amotion/` (git-ignored) and deleted before every
commit. No `--self-test`, build, installer or exe was run locally.
