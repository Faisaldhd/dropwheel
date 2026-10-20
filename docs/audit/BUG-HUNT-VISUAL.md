# 2.4.0 visual bug hunt — en/ar RTL, light/dark, DPI

Lane: TEAM B bug hunt · Branch `audit/bughunt-visual` · Evidence: `ui-coverage` artifact from
run **39379321230** (`32757e9`, self-test exit 0, 114/114 surfaces, 90 PNGs: 44 en + 44 ar + 2 controls),
re-downloaded and inspected at 100 % DPI, dark theme. Time-boxed audit: **no src/build/string edits**.

What the gate asserts vs. what it cannot see: the coverage pass checks **raw keys, empty titles, Arabic
root FlowDirection, no Arabic text forced LTR, and size fit**. It does **not** assert overlap, clipping,
truncation, icon mirroring, physical left/right alignment, mixed-direction runs inside a label, or any
DPI above 150 %. Those blind spots are the rows below.

**Totals: 0 P0 · 2 P1 · 8 P2 · 1 P3.**

> **Claim integrity (VI-01):** until the post-tag fix lands, the honest claim is
> **“112/114 surfaces genuinely en+ar; 2 surfaces (wheel, parameter dialog) are English renders in the
> ar pass.”** The gate's Arabic coverage for those two surfaces is **void** (English text trivially passes
> a raw-key/empty-label assertion, so “114/114” is really **112 genuine + 2 vacuous**), and the two
> committed `ar/` PNGs are **false evidence** that must not be cited as proof of Arabic rendering.
>
| ID | P | symptom | evidence | file:line | suggested fix | owner |
|---|---|---|---|---|---|---|
| VI-01 | **P1** | Arabic `ui-coverage` **wheel** and **parameter dialog** screenshots are actually **English** ("Copy/Move/Float", "PDF document", "Start/End", "e.g. 0:30…", "empty = until the end") while their buttons are Arabic. Two distinct consequences: **(a) the gate's Arabic coverage for those two surfaces is void** — English text trivially passes a raw-key/empty-label assertion, so “114/114 en+ar” is really **112 genuine + 2 vacuous**; **(b) the two committed `ar/` PNGs are false evidence** — nobody may cite them as proof of Arabic rendering. This is an evidence/claim defect, not a product defect: the app relocalizes live. | artifact `ar/wheel-actions.png`, `ar/wheel-image-convert.png` (subtitle "document"), `ar/parameter-dialog.png`; compare `ar/settings.png` (correct Arabic). Gate report still says ar PASS because the assertion only looks for Arabic-in-LTR, not for missing Arabic. | `src/FileAction.App/SelfTestUiCoverage.cs:76-78` builds `WheelController.BuildOperations()` / `FileActions.Build` **once, before** the en/ar loop at `:81`; `Operation.Label/Subtitle/Parameters` are fixed at construction. Product path is correct: `App.cs:233-240` calls `_controller.Relocalize()` on language change. | Build core/actions **inside** each culture iteration (or call a relocalize helper) before rendering the wheel and ParameterDialog; add an assertion that the ar wheel contains Arabic on every slice and the ar parameter dialog localizes Start/End; re-render those two pairs. | audit/selftest (B) — **status: post-tag** |
| VI-02 | **P1** | At **200 % DPI** the protection dialogs are taller than the work area and their bottom actions fall off-screen: LockSettings **520×660**, RescuerSetup **460×560**, LockWarning **520×540** DIP vs **540 DIP** usable height at 1920×1080@200 %. On 1280×800@200 % (400 DIP) also ChangePassword **460×470**, RescuerSession **450×390**. No scroll, no clamp; the custom close X stays reachable but "إغلاق"/"التالي"/"تشفير" do not. This is precisely the risk the gate's uncovered **200 % dimension** left unnamed: the fit matrix stops at 150 %, so no CI check would ever have caught it. | `LockUi.Configure` assigns `window.Width = width; window.Height = height` with **no** work-area clamp (unlike `UiSetup.FitToWorkArea`, used by Settings); values from the call sites listed in the "DPI table" below. Confirmed by code + the 100 % screenshots; 200 % is the gate's named-uncovered dimension. | `src/FileAction.App/Shell/Protection/LockUi.cs:258-263` | Clamp to `SystemParameters.WorkArea` (reuse `UiSetup.FitToWorkArea(this, width, height, …)`) and put the body in a `ScrollViewer`; add 200 % to the fit matrix for dialogs. | Team A (Lock/UI) — **status: post-tag** |
| VI-03 | P2 | **Toggle switches never mirror.** In Arabic the knob still travels left→right (off at left, on at right), so the "on" side is the trailing edge instead of the leading edge. | `ar/settings.png`: "مراقبة الماوس…" off knob is left, "الحركات" on knob is right; en is identical. Physical-coordinate animation, no `IsRtl` branch. | `src/FileAction.App/UI/Controls.cs:138` (knob `HorizontalAlignment.Left`), `:156` (`var x = on ? 23.0 : 3.0`) | Mirror the animation for RTL: `x = on ? (rtl ? 3 : 23) : (rtl ? 23 : 3)`, or set the track's `FlowDirection` and anchor logically. | UX/UI |
| VI-04 | P2 | **Jobs progress bar fills from the left in Arabic** (running card's purple bar is anchored at x=0 of the track). The rest of the card is correctly RTL. | `ar/jobs.png` running card: purple segment starts at the left edge, dark track continues to the right. WPF does not mirror an explicitly left-anchored child. | `src/FileAction.App/Jobs/JobsWindow.cs:101` (`_fill = new Border { HorizontalAlignment = Left, Width = 0 }`), `:146` (`_fill.Width = _bar.ActualWidth * _value`) | Anchor the fill on the logical start: for RTL use `HorizontalAlignment.Right`/`FlowDirection.RightToLeft` on `_bar` (verify with screenshot), or compute margin from the right. | UX/UI + jobs |
| VI-05 | P2 | **Mixed run splits the output file name in Arabic audio bleep.** The rendered name reads like `wav… مُصغّر tone` — `.wav` detaches from `tone` because the Arabic suffix is placed **inside** an LTR isolate. | `ar/tool-tools-audiobleep.png`, bottom-centre output name (crop kept in the lane scratch); compare `en/...` where it reads `tone … .wav` cleanly. | `src/FileAction.App/UI/Tools/Audio/AudioBleepTool.cs:403` `Bidi.Value($"{_item.BaseName} {T("tool.audiobleep.suffix")}.{format}")` — isolates the whole mixed string, not just the Latin parts | Isolate only the Latin parts: `Bidi.Value(BaseName) + " " + T(suffix) + "." + format`, or compose separate `Run`s (same pattern in audio-channels/visualizer if they add an Arabic suffix). | Audio tools |
| VI-06 | P2 | **Audio bleep lane captions sit on the far left** in Arabic ("صافرة" / "الصوت" at the left margin, outside the lane), while the window and every other label are RTL. The lane grid is LTR for its time axis, but the captions inherited it. | `ar/tool-tools-audiobleep.png`: both lane titles at x≈80 while lane surfaces start at x≈118; en is identical by design. | `src/FileAction.App/UI/Tools/Audio/AudioBleepTool.cs:169-173` (whole `Grid` `FlowDirection.LeftToRight`), `:198,:201` (`Place(bleepTitle,0,0)` / `audioTitle,2,0`) | Keep the time axis LTR only for the waveform/time rows; place captions in a separate RTL column (or set the title blocks' `FlowDirection` **and** put the title column at the leading edge for RTL). | Audio tools |
| VI-07 | P2 | **Wheel has a fixed 442×442 DIP size and no work-area clamp.** At 200 % on a 1280×800 panel (400 DIP of work height) the bottom of the disc is off-screen; there is no scroll and no scale-down. | `WheelWindow.cs:39` `Size = 2*(196+9+16) = 442`; `:99-100` `Width = Size; Height = Size`; `:109` `UiSetup.Configure(this, animate:false)` — configuration path does not call `FitToWorkArea`. 200 % screenshot is the gate's uncovered dimension. | `src/FileAction.App/Wheel/WheelWindow.cs:39,99-100,109` | `UiSetup.FitToWorkArea(this, Size, Size)` plus a `LayoutTransform`/canvas scale when the work area is smaller than the disc. | Wheel/UI |
| VI-08 | P2 | **Tool windows at 200 % compress below their design minimum.** At 1080p@200 % the sizer caps height at `0.90 × 540 = 486` DIP; the default chrome is 270 DIP, leaving **216 DIP** of preview against a declared `PreviewMinHeight` of **320**. Tools with fixed internal rows (AudioBleep two lanes + two sliders, VideoTrim timeline + transport, PDF organise list, Metadata list) will overlap/clip; no 200 % screenshot exists. | `ToolSizes.cs:15-23` (Default: ChromeHeight 270, PreviewMin 320/320, Default 350×450); `ToolWindowSizer.cs:49-54,92-94` (hard clamp, min not enforced after clamping). | `src/FileAction.App/UI/Tools/ToolSizes.cs:15-23`, `src/FileAction.Core/ToolWindowSizer.cs:49-54,92-94` | Add 1080p@200 % to the fit matrix (report-only first), then give each tool a `ScrollViewer` fallback or lower the chrome height at high DPI. | Tools/UI |
| VI-09 | P2 | **Settings sliders are left-anchored in Arabic.** The accent fill and thumb use physical `HorizontalAlignment.Left` plus a left margin, so in Arabic the fill grows left→right and the thumb starts at the left end; the numeric label is right-aligned. Not visible on the General tab screenshot, but the same `Controls.Slider` builds Quality/Integration sliders. | `src/FileAction.App/UI/Controls.cs:179` (fill `HorizontalAlignment.Left`), `:187` (thumb `Left`), `:200` (`thumb.Margin = Thickness(t*(width-18),0,0,0)`); VI-04 proves WPF does not physically mirror this. | `src/FileAction.App/UI/Controls.cs:175-235` | Anchor fill/thumb on the logical start (`FlowDirection.RightToLeft` on the `surface` when RTL, or right margin `(width-18)-t*(width-18)`), and align the value label to the logical trailing edge. | UX/UI |
| VI-10 | P2 | **Mixed-language Metadata window in Arabic.** Section headers are Arabic ("الصورة", "بيانات ExifIFD") but the field labels are English (`format`, `width`, `height`, `size`, `Make`, `Model`, `Latitude`, `Longitude`) and the values are unisolated Latin runs; also the subtitle reads `PNG · عدد الحقول: 8` with the Latin token mid-sentence. | `ar/tool-tools-metadata.png`; search label is correctly Arabic ("البحث في البيانات الوصفية"). Whether these are EXIF tag identifiers (data) or UI labels needs a product decision. | `src/FileAction.App/UI/Tools/Meta/MetadataView.cs:134-139` localizes only the search label; field names originate from `MetadataModel` (data model, not from `en/ar.json`) | Decide data-vs-UI: if UI, add keys for the synthetic fields (`format/width/height/size`) and keep real EXIF tags Latin/isolated; isolate each Latin value with `Bidi.Value`. | Metadata |
| VI-11 | P3 | **Video-trim transport row edge asymmetry.** In Arabic the right-hand "البداية" label is flush against the window edge (no 18 DIP chrome margin), while the left-hand group has margin; at 150 % it will touch/clip. The chevron order is also visually asymmetric between the two time groups. | `ar/tool-tools-videotrim.png` bottom row, right edge (crop: label ends ≈2 px from the border); en comparison shows symmetric margins. | `src/FileAction.App/UI/Tools/Video/VideoTrimView.cs:120-123` (frame back/forward buttons) and the transport `DockPanel`/`StackPanel` around them | Use the same margin on both ends and mirror the group order; verify at 150 %. | Video tools |

## 200 % DPI risk table (the uncovered dimension)

DIP work area = physical pixels ÷ scale. A 200 % panel therefore has half the DIP height:

| Physical panel @200 % | DIP work area | Fits? |
|---|---:|---|
| 1920×1080 | 960×540 | Wheel 442 ✔ (VI-07 only at 1280×800); tool height cap 486 → preview 216 < min 320 (VI-08); LockSettings 660 ✘, RescuerSetup 560 ✘, LockWarning 540 ✘, ChangePassword 470 ✔, Session 390 ✔ |
| 1280×800 | 640×400 | Wheel 442 ✘ (clipped); tool cap 360 → preview 90 DIP (severe); LockSettings 660 ✘, RescuerSetup 560 ✘, LockWarning 540 ✘, ChangePassword 470 ✘, Session 390 ✘ borderline |

Settings itself is safe: `UiSetup.FitToWorkArea(this, 680, 820, 520, 360)` plus a `ScrollViewer`
(`SettingsWindow.cs:63,157`). The dialogs above are the gap.

## Gate blind spots (why these escaped)

1. Assertions never inspect pixels: overlap, clipping, ellipsis and icon mirroring are invisible.
2. `ar` PASS only proves "no Arabic text is forced LTR" — it cannot prove "Arabic text exists", which is
   exactly how VI-01 (English wheel in the ar folder) passed.
3. Fit is asserted at 100 %/150 % only; fixed-size dialogs are checked at 1280×800@100 % only.
4. WPF mirrors **panel order**, not physical `HorizontalAlignment`/margins — VI-03/04/07/09 are all
   physical-coordinate bugs that an RTL `FlowDirection` cannot fix.
5. No light-theme Arabic screenshots were reviewed in this pass (the artifact contains ar-light only for
   the Settings/wheel family via `RenderAll`; the new `ui-coverage` set is dark-only). Light/dark token
   contrast is therefore **not asserted** here and needs the existing screenshot job.

## What was checked and is clean

- Arabic Settings (General): tab order, text, gesture chips and mirrored card rows are correct.
- Wheel hub/tabs RTL, slice captions RTL except format identifiers (by design), selected slice state.
- Shelf: Arabic title, RTL close, Arabic hint; file names correctly isolated.
- Lock warning: Arabic body wraps without clipping at 100 %, checkbox indicator on the right, buttons
  mirrored (cancel right, encrypt left).
- Parameter dialog layout is correct in English; only its Arabic localization is stale (VI-01).

## Method / limits

Artifact images inspected (en + ar): settings, shelf, jobs, wheel ×3, parameter dialog, 10 protection
dialogs, audio bleep, metadata, video trim; other tools reviewed by code and the shared `ToolWindow`
chrome. No binaries were run locally; no 200 % screenshot exists in the artifact, so DPI rows are
code-derived from the declared sizes above. Suggested fixes are one-file, minimal; this lane made no
source edits.
