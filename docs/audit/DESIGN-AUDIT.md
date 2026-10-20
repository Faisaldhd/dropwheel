# Design audit — floating tool windows

Branch: `audit/adesign` (from `main`, head `268a2d7`). Auditor: design lane, read-only on source.
Deliverable: this file only.

Method: every window was compared **statically** against the reference and against the shipped
captures. No built executable was run, no network, nothing outside `Downloads\DropWheel_1`.
Where a number is derived it says so; where it is observed in a capture the capture is named.

## 0. Evidence inventory (what this audit could and could not use)

| Source | Status | Used for |
|---|---|---|
| `_reference\tangerine-tools.pdf` (2 pages: 540×20702 pt and 1080×7475 pt; 22 tool sheets plus marketing cards, the second page at 2×) | present, rendered to 23 tiles | reference control set, order, labels, chrome anatomy of **all** tools |
| `_reference\tangerine-demo-frames.zip` → `crop.png`, `addbg.png` (1280×720) | present | the **only** desktop (macOS) tool-window frames: proportions, inset, title bar, action bar |
| `_reference\tangerine-shift-convert-frames.zip` (80× 540×960) | present | wheel demo only, no tool windows — no design value |
| `_local\video-frames`, `_local\video-frames2` | **absent** | task named them; they do not exist in this checkout, so the two zip frame sets above were used instead |
| `docs\screenshots\tools\` | **absent** | task named it; the branch has `docs/screenshots/{en,lock-badge,motion,site}` |
| `_local\selftest-art\tools\SIZES.md` | present, 2 rows (crop only) | **stale** — see §5 F-13 |
| `_local\selftest-art\tools\en\crop-{landscape,portrait}.png` | present | observed crop rendering (inset, clipping) |
| `docs\screenshots\motion\tool-{crop,addbackground,annotate,editphotos,redact}-{light,dark}-poster.png` | present | observed rendering of 5 tools (text truncation, row order) |
| Source: `UI/Tools/**`, `UI/Tools/ToolWindow.cs`, `UI/Controls.cs`, `Core/ToolWindowSizer.cs`, `Core/Strings/{en,ar}.json` | present | geometry, chrome, control order, labels, keyboard, RTL |

Not reachable statically, and therefore **not** claimed: real pixel widths after font shaping, actual
clipping at a given DPI on a live desktop, keyboard traversal in practice. Those need a desktop
session (see §8).

## 1. The reference model, distilled

From `crop.png` / `addbg.png` (desktop) and the 22 tool sheets in the PDF:

* **Anatomy (identical in every desktop frame):** floating card, radius ≈ 18–20 DIP, drop shadow,
  no OS title bar → `×` close at the **left**, **centred** semibold title, content, then an action
  bar with the **secondary action bottom-left** (`Reset`) and the **primary bottom-right**
  (`Apply`, `Save with Background`, `Create Collage`, …).
* **Proportions:** `crop.png` card = 460×700 in a 1280×720 canvas → **36 % of width, 97 % of height**;
  `addbg.png` = 585×704 → **46 % W, 98 % H**. The window follows the media aspect; the preview is the
  star area, the controls are a fixed block under it.
* **Content order:** every sheet, mobile (PDF) and desktop (`crop.png`, `addbg.png`), puts the
  **preview first**, controls **under** it, action bar last.
* **Inset:** control rows and the preview share one 18–20 DIP inset, aligned with the title bar.
* **Control vocabulary:** chips/segments for exclusivity (`Free 1:1 16:9 9:16 4:3 3:4`), labelled
  sliders with the value at the row end (`Padding ——— 160 px`), dropdowns with the current value,
  toggles, `n`-of-`m` counters, per-row `↺` (Edit Photo), per-row `▲▼` reorder (Merge/Join),
  `⊕/⊖` on metadata fields, and a one-line behaviour hint under busy controls.
* **Labels:** title case for titles (`Crop Image`, `Add BG`, `Merge PDFs`), sentence case inside the
  body, units shown **with the value** (`160 px`, `0.00 EV`, `-1.5 dBTP`), not in the label.

## 2. Shared chrome — verdict

Implementation: `UI/Tools/ToolWindow.cs`, `Core/ToolWindowSizer.cs`, `UI/Controls.cs`,
`UI/Tools/ToolControls.cs`, `UI/Bidi.cs`.

| Property | Implementation | Verdict |
|---|---|---|
| `WindowStyle.None`, `AllowsTransparency`, radius 20 card, 12 DIP margin, 28 px shadow | `ToolWindow.cs:79–86, 244–258` | **PASS** — matches the reference card |
| `Topmost = true`, `ShowInTaskbar = false`, `WindowStartupLocation.Manual`, centred on the cursor's monitor | `ToolWindow.cs:83–86, 105, 439–453` | **PASS** — correct Windows adaptation of a modal sheet; keeps the window above Explorer |
| `DragMove()` on the title bar | `ToolWindow.cs:326–330` | **PASS with defect** — see F-07 |
| `×` left, title centred, `Reset` left / primary right | `ToolWindow.cs:289–355` | **PASS** — identical to `crop.png`, and to the PDF's `×`/title |
| `Esc` closes | `ToolWindow.cs:111–116` | **PASS** — but shadowed in some tools, see F-02 |
| `Enter` applies | `ToolWindow.cs:117–122` | **FAIL** — see F-02 |
| Adaptive size: preview follows media aspect, clamped to 95 % × 90 % of the work area, 200 % upscale cap | `ToolWindowSizer.cs:38–95` | **PASS** — the model is right and unit-tested |
| DPI: work area converted through `GetDpiForWindow` | `ToolWindow.cs:404–437` | **PASS** |
| RTL: window `FlowDirection`; `Bidi.Value` isolates technical values | `ToolWindow.cs:91`, `Bidi.cs:19–27` | **PASS with defect** — see F-06 |
| Keyboard order / focus | none | **FAIL** — see F-01 |
| Disabled state | none | **FAIL** — see F-08 |
| Content inset | per tool, 0–18 DIP | **FAIL** — see F-03 |

## 3. Window geometry against the screen matrix (derived)

Recomputed exactly from `ToolWindowSizer.Compute` for work areas 1920×1040 and 1366×728 (40 px
taskbar). Values are window DIP.

| Tool | `ChromeHeight` | 1080p@100 (16:9 media) | 1080p@100 (9:16 media) | 768p@125 (16:9) | 768p@150 (16:9) | cap at 768p@150 |
|---|---|---|---|---|---|---|
| crop | 270 | 676×630 | 396×910 | 488×524 | 336×437 | 436.8 |
| addbackground | 350 | 556×650 | 416×780 | 416×524 | 416×437 | 436.8 |
| annotate | 322 | 668×671 | 368×842 | 407×524 | 368×437 | 436.8 |
| redact | 290 | 676×650 | 396×930 | 452×524 | 316×437 | 436.8 |
| editphotos | **520** | 500×768 | 420×900 | 420×524 | **420×437** | 436.8 |
| collage | **455** | 560×747 | 360×936 | 360×524 | **360×437** | 436.8 |
| compress | 140 | 596×455 | 385×760 | 596×455 | 564×437 | 436.8 |
| metadata | 140 | 656×489 | 430×840 | 656×489 | 564×437 | 436.8 |
| qr | 320 | 756×725 | 336×840 | 399×524 | 336×437 | 436.8 |
| pdfmerge | 300 | 940×806 | 400×936 | 438×524 | 400×437 | 436.8 |
| pdforganize | 300 | 940×806 | 400×936 | 438×524 | 400×437 | 436.8 |
| videotrim | 330 | 720×715 | 596×770 | 596×524 | 596×437 | 436.8 |
| videocrop | 340 | 720×725 | 596×770 | 596×524 | 596×437 | 436.8 |
| videospeed | 310 | 720×695 | 596×750 | 596×524 | 596×437 | 436.8 |
| videosplit | 270 | 856×731 | 596×790 | 596×524 | 596×437 | 436.8 |
| videojoin | 200 | 856×661 | 596×760 | 612×524 | 596×437 | 436.8 |
| videoframes | 320 | 720×705 | 596×705 | 596×524 | 596×437 | 436.8 |
| videoredact | 330 | 720×715 | 596×715 | 596×524 | 596×437 | 436.8 |
| audionormalize | 230 | 783×650 | 456×650 | 559×524 | 456×437 | 436.8 |
| audiotrim | 230 | 783×650 | 456×650 | 559×524 | 456×437 | 436.8 |
| audiochannels | 300 | 463×540 | 336×540 | 434×524 | 336×437 | 436.8 |
| audiobleep | 390 | 427×610 | 356×610 | 356×524 | 356×437 | 436.8 |
| audiovisualizer | 280 | 520×552 | 316×700 | 470×524 | 316×437 | 436.8 |
| protect | 100 | 652×449 | 332×620 | 652×449 | 631×437 | 436.8 |
| unlock | 100 | 652×449 | 332×620 | 652×449 | 631×437 | 436.8 |
| lock | 200 | 676×560 | 396×840 | 612×524 | 457×437 | 436.8 |

Reading of the matrix:

1. **At 1920×1080 @100/125/150 %** every window fits, is 29–49 % of the screen wide and 43–78 % of
   the work area tall — inside the reference's 36–46 % width band, so proportions **PASS**.
   `MaxUpscale = 2` correctly stops a 100 px icon from becoming a 640 px window.
2. **At 1366×768** the sizer's 90 % cap is 655.2 DIP @100 %, 524.2 DIP @125 %, **436.8 DIP @150 %**.
   All 26 windows open at the cap at 150 %, and 13 of them open **≤ 460 DIP wide**, as narrow as
   316 DIP (`redact`, `audiovisualizer`).
3. Two windows are **taller than their own chrome** at 1366×768 @150 %: `editphotos` needs 520 DIP of
   chrome in a 437 DIP window (83 DIP overflow) and `collage` 455 (18 DIP) → their last control rows
   are clipped by the window edge. See F-05.
4. `crop` at 1366×768 @150 % is 336 DIP wide while its six chips need ≈ 340–376 DIP
   (`CropTool.cs:76, 204–230`, non-wrapping `StackPanel`) → the last chip(s) clip. Same class of risk
   for `videocrop` (596 wide, safe) and any translated chip label that grows.

## 4. One row per tool

Legend — **ok**: chrome, order, labels and fit match the reference or a justified Windows/feature
adaptation. **deviations**: at least one user-visible defect (missing identifying label, wrong order,
clipping, missing control the reference has, misleading label). `PR` names the fix cluster in §5.

| # | Tool (id) | Title / primary | Status | Deviations vs reference | Fix PR |
|---|---|---|---|---|---|
| 1 | Crop `tools.crop` | Crop Image / Apply | **deviations** | chips row has **no `Aspect ratio` label** (reference: label + 6 chips); `W`/`H` row has **no trailing `px`** (reference `… 2674 px`); control rows sit at the card edge (no 18 DIP inset) — the leading **`W` is clipped** in `_local\selftest-art\tools\en\crop-landscape.png`; chips do not wrap → clip at 336 DIP; `Enter` in W/H also applies | D2, D3 |
| 2 | Add Background `tools.addbackground` | Add BG / Save with Background | **deviations** | **`Background im…` is truncated** in the shipped capture `docs/screenshots/motion/tool-addbackground-light-poster.png` (label column too narrow for “Background image”); units are in the label (`Padding (px)`) instead of with the value (`160 px`) as in `addbg.png` | D4 |
| 3 | Annotate `tools.annotate` | Annotate Image / **Apply** | **deviations** | controls are **above** the preview (every reference sheet and every other tool put the preview first); the `Fill` toggle is styled exactly like the colour chips and sits **inside the colour row** (`Color` + 7 swatches + `Fill`), so it reads as an eighth colour; the stroke-width and font-size single-selects share one row separated only by the muted `Font` caption, so the row reads as one six-value selector; no behaviour hint (reference: “Drag from the tail toward the arrowhead…”); primary is `Apply` where the reference says `Save Annotated Copy` (`tool.annotate.suffix` already exists) | D5, D9 |
| 4 | Redact image `tools.redact` | **Redact image** / Save Redacted Copy | **deviations** | reference control set is `Style` / `Color` / `Detect [Faces, Text]`; ours has `Detect text` only, while the wheel card advertises “Detect faces and text locally” — affordance promised and absent; sentence-case title (one of three, F-12) | D7, D9 |
| 5 | Edit Photos `tools.editphotos` | Edit Photos / Save Edited Copy | **deviations** | values are bare (`0`) where the reference shows units/format (`0.00 EV`, `+0`, `0°`); **no per-row `↺`** (reference has one per slider); at 1366×768@150 % the window is 437 DIP against 520 DIP of chrome → last rows clipped, and @125 % the preview is 4 DIP (tabs vs stacked sections: accepted adaptation, §7) | D6, D8 |
| 6 | Collage `tools.collage` | Collage / Create Collage | **deviations** | at 1366×768@150 % the window is 437 DIP against 455 DIP of chrome → the `Order` row clips; only horizontal scrolling exists (`CollageView.cs:281–284`), so a short window cannot reach the lower controls; title is `Collage` where the reference sheet is `Create Collage` (primary already says it) | D6, D9 |
| 7 | Video trim `tools.videotrim` | Trim Video / Apply | ok | order (preview → filmstrip → start/end → transport) matches the sheet; `Fast copy` toggle is our justified extra | — |
| 8 | Video crop `tools.videocrop` | Crop Video / Apply | ok | chips, `W/H`, scrub, transport, action bar all match; 596 DIP minimum keeps the chip row safe | D3 (shared chips wrap) |
| 9 | Video speed `tools.videospeed` | Change Speed / Apply | ok | reference's tick row `0.25× … 4×` is implemented as preset chips + slider; `Pitch is kept` / `A silent video works too` replace the sheet's marketing bullets | — |
| 10 | Video split `tools.videosplit` | Split Video / Split Video | ok | `Equal parts` + stepper + `Set` present; split-point hint present | — |
| 11 | Video join `tools.videojoin` | Join Videos / Join Videos | **deviations** | reference rows carry **visible `▲▼` reorder buttons**; ours is drag-only (“Drag a clip to reorder it”) — no discoverable affordance and no keyboard route; `Merge PDFs` shows the same pattern done correctly with `moveUp/moveDown` keys, so the inconsistency is internal too | D10 |
| 12 | Save frames `tools.videoframes` | Save frames / Save N Frames | ok | dynamic primary, `n`-of-`m` chips, `Add Frame` — equivalent to the sheet | — |
| 13 | Redact video `tools.videoredact` | **Redact video** / Save Redacted Copy | ok (nit) | lanes + style + colour + times match the sheet; title sentence case (nit, D9) | D9 |
| 14 | Normalize Audio `tools.audionormalize` | Normalize Audio / Apply | ok | three labelled sliders + helper note are the reference's, near verbatim; desktop uses a 2-column body (waveform beside sliders) instead of the sheet's stack — justified by width | — |
| 15 | Trim Audio `tools.audiotrim` | Trim Audio / Apply | ok | `Trim silent ends automatically` + `Play Selected Range` + start/end match the sheet | — |
| 16 | Audio Channels `tools.audiochannels` | Audio Channels / Apply | ok | `Mono` / `Stereo`, L/R volumes, solo preview — superset of the sheet, same order | — |
| 17 | Bleep Audio `tools.audiobleep` | Bleep Audio / Apply | ok | `Bleeps n`, bleep lane, audio lane, hint, `Original` / `With Bleeps` all match; primary says `Apply` where the sheet says `Save Bleeped Copy` (nit, D9) | D9 |
| 18 | Audio Visualizer `tools.audiovisualizer` | Audio Visualizer / Apply | ok | style + shape dropdowns match; extra background picker is our feature | — |
| 19 | Merge to PDF `tools.pdfmerge` | **Merge to PDF** / Merge PDFs | ok (nit) | `Document order` + rows + `▲▼` + `Remove` implement the sheet correctly; title and primary disagree (`Merge to PDF` vs `Merge PDFs`) | D9 |
| 20 | Organize Pages `tools.pdforganize` | Organize Pages / Save Organized PDF | ok (nit) | page tiles with ordinal + duplicate + rotate/delete, `Reset Order`, `Make all pages the same width` + note — all present; title wording differs from the sheet's `Organize PDF` | D9 |
| 21 | Compress `tools.compress` | **Compress File** / Compress | ok (nit) | preset segments, image-size dropdown, target-size toggle, estimate — superset in the sheet's order; title longer than the sheet's `Compress` | D9 |
| 22 | Edit Metadata `tools.metadata` | Edit Metadata / Save | ok (nit) | search field, grouped fields, per-field remove, `Remove All` + note, `Save` — matches the sheet; title longer than `Metadata` | D9 |
| 23 | QR Code `tools.qr` | QR Code / **Apply** | **deviations** | read-only inspector whose bottom-right action says `Apply` but **copies the text and closes** (`QrView.cs:350–356`); the view already has `Copy`/`Open`, so the window shows two competing copy affordances and an "Apply" that writes nothing | D11 |
| 24 | Protect `tools.protect` | Password protection / **Apply** | ok (nit) | no reference (our feature); controls follow the app's own conventions; primary `Apply` is the weakest label of the set — “Create protected copy” would name the result | D9 |
| 25 | Unlock `tools.unlock` | Remove password / Apply | ok (nit) | our feature; password + show/hide + note match `PasswordViewParts` conventions | D9 |
| 26 | Lock `tools.lock` | Lock / Encrypt | ok | our feature; `Encrypt`/`Open` switch through `IToolActionLabel`, `Esc`/`Enter` handled inside the tool (`LockTool.cs:478–487`) | — |

**Tool rows: 18 PASS / 8 FAIL.** Window-wide defects that touch every row are listed separately in §5
and are not double-counted above.

## 5. FAIL list (each with a concrete fix)

Fix-PR clusters referenced from §4. Each is one reviewable PR against `audit/adesign` base `main`.

| PR | Title | Touches | Closes |
|---|---|---|---|
| **D1** | `ui: focusable, tabbable, announced tool controls` | `UI/Controls.cs`, `UI/Tools/ToolControls.cs`, `ToolWindow.cs` | F-01, F-08 |
| **D2** | `tools: one 18 DIP content gutter; Enter/Esc routed per control` | `ToolWindow.cs`, `CropTool.cs`, the 7 flush views | F-02, F-03 |
| **D3** | `crop: Aspect ratio label, px unit, wrapping chips` | `CropTool.cs`, `ToolControls.Chips` | F-09 (+ row 1) |
| **D4** | `addbackground: stop truncating the Background image row` | `AddBackgroundView.cs` | F-04 |
| **D5** | `annotate: preview above controls; separate Fill from the colour chips` | `AnnotateView.cs` | row 3 |
| **D6** | `tools: fit and scroll at 1366×768 @150 %` | `ToolWindowSizer.cs`, `EditPhotosView.cs`, `CollageView.cs`, 12 views | F-05 |
| **D7** | `redact: face detection affordance (or drop the claim)` | `RedactView.cs`, `Strings/*.json`, wheel card copy | row 4 |
| **D8** | `editphotos: unit-formatted values and per-row reset` | `EditPhotosView.cs`, `Strings/*.json` | row 5 |
| **D9** | `strings: title casing and primary-verb pass (en + ar)` | `Core/Strings/{en,ar}.json` | F-12 |
| **D10** | `videojoin: visible reorder buttons and keyboard reorder` | `VideoJoinTool.cs` | F-10 |
| **D11** | `qr: one copy affordance, no Apply on a read-only window` | `QrTool.cs`, `QrView.cs` | F-11 |
| **D12** | `rtl: mirror slider fill and hit-test` | `Controls.cs`, `AudioSlider.cs`, `VideoTimeline.cs` | F-06 |
| **D13** | `chrome: close button must not start a drag` | `ToolWindow.cs` | F-07 |
| **D14** | `evidence: regenerate SIZES.md for all 26 tools` | `_local/selftest-art/tools`, brief | F-13, F-14 |

### F-01 — No keyboard order and no accessible controls anywhere in a tool window · **critical**
`Controls.Button/Toggle/Slider/Segmented` and `ToolControls.CheckBox/Chips/Box` build
`Border`/`StackPanel` with `MouseLeftButtonUp` handlers (`Controls.cs:90–133, 136–172, 175–236,
239–275`; `ToolControls.cs:80–136, 142–193`). None is `Focusable`, `IsTabStop`, has a focus visual or
`AutomationProperties.Name`; only the stray `TextBox`es and three view-specific elements are focusable
(`PasswordViewParts.cs:111–112`, `BleepLane.cs:47`, `VideoSplitTool.cs:271`). So **Tab reaches text
fields only**, `Space`/`←`/`→` do nothing, there is no focus rectangle, and a screen reader sees an
empty window. The reference's controls are all keyboard-reachable platform controls.
*Fix:* make every interactive builder return a real `Control` (or set `Focusable = true`,
`KeyboardNavigation.SetIsTabStop`, `KeyboardNavigation.TabIndex` in visual order, handle
`Key.Space`/`Key.Enter` as activation and `←/→` on sliders/chips), add a 2 px accent focus ring, and
copy the existing label text into `AutomationProperties.Name`. Acceptance: Tab cycles
chip → chip → box → box → Reset → primary, and every control activates with Space.

### F-02 — `Enter` applies the tool from inside a text field; `Esc` is swallowed by some fields · **high**
`ToolWindow.cs:117–122` applies on any `Enter` that reaches the window. `CropTool.cs:243–247` commits
the W/H box on `Enter` **without** `e.Handled`, so typing a width and pressing `Enter` also **saves a
file**. Other tools shadow the key inconsistently: `VideoControls.cs:111–123` and
`ProtectTool.cs:265–267` consume `Enter`, and `VideoTimeBox` consumes **`Esc`** to revert the box
(`VideoControls.cs:118–122`) so `Esc` no longer closes those windows, while `AnnotateView` deliberately
does the same only while the text editor is open (`AnnotateView.cs:508–522`, documented).
*Fix:* in `ToolWindow`, apply on `Enter` only when `Keyboard.FocusedElement is not TextBox`
(or route through an `IsDefault` button); set `e.Handled = true` in the crop box commit; let `Esc`
close unless a modal in-canvas editor is open. Acceptance: `Enter` in W/H commits only; `Esc` in any
time box closes the window; annotate's editor still cancels with `Esc`.

### F-03 — Content inset is inconsistent and in 7 tools is zero · **high**
The title bar and action bar are inset 18 DIP (`ToolWindow.cs:322, 350`) but the view row is not, and
the views disagree: `crop`, `videotrim`, `videocrop`, `videospeed`, `videosplit`, `videojoin`,
`videoframes` declare **no horizontal inset**; `editphotos`, `compress`, `metadata`, `qr` use 18;
`audionormalize/trim` 18; `addbackground`, `annotate`, `collage`, `redact` use 16–18 on inner
containers. Observed consequence in `crop-landscape.png`: the leading **`W` of the width box is cut
off** at the card edge (its left stroke is missing), while `Reset`, `Apply` and the title sit 18 DIP
in. The reference aligns all three.
*Fix:* give the content row `Margin = new Thickness(18, 0, 18, 0)` in `ToolWindow.Build()` and delete
the per-view insets (or standardise a `ToolView.Pad` helper). Acceptance: preview, controls, title and
action bar share one 18 DIP gutter in all 26 windows, at 100/125/150 %.

### F-04 — `Background image` is truncated, and units live in labels · **medium**
`docs/screenshots/motion/tool-addbackground-light-poster.png` shows `Background im…` because the
label column is fixed while the row (label + `Choose image…` + `Blur`) needs more width. The reference
keeps the label to one short word (`Background`) and puts the image picker in a small icon at the end
of the swatch row.
*Fix:* let the label column wrap to two lines (`ToolControls.Label` already wraps) or move
`Choose image…`/`Blur` to their own row; print units with the value (`160 px`) and drop `(px)` from
the labels. Acceptance: no `…` in any 150 % capture; slider values read like the reference.

### F-05 — Two windows cannot fit their own controls at 1366×768 @150 %, and 12 views cannot scroll · **high**
`editphotos` declares 520 DIP of chrome and `collage` 455 (`EditPhotosTool.cs:28`,
`CollageTool.cs:21`); the sizer's cap at 1366×768 @150 % is **436.8 DIP** (`ToolWindowSizer.cs:32,
47–54`), so `height = min(preview + chrome, cap)` cuts 83 / 18 DIP off the view. Both views are
non-scrolling (Collage scrolls horizontally only, `CollageView.cs:281–284`). Twelve more views have no
vertical scroller at all: `crop`, `addbackground`, `annotate`, `redact`, `videotrim`, `videocrop`,
`videospeed`, `audionormalize`, `audiotrim`, `audiochannels`, `audiobleep`, `audiovisualizer`.
*Fix:* wrap each tool's control block in a `ScrollViewer` (`VerticalScrollBarVisibility=Auto`) and let
the preview be the collapsible row; and/or measure the chrome at `Loaded` instead of trusting the
constant. Acceptance: at 1366×768 @150 % every control is reachable in all 26 windows.

### F-06 — RTL: slider fill direction is not mirrored · **medium**
`Controls.Slider` paints a `HorizontalAlignment.Left` fill and a positive `TranslateTransform.X`
(`Controls.cs:179–200`) and inverts `e.GetPosition` linearly (`204–213`). Under
`FlowDirection.RightToLeft` the row mirrors (surface right, value left) but the fill still grows
left→right, so the thumb starts at the *value* end. `AudioSlider`/`VideoTimeline` implement their own
surfaces and must be checked the same way.
*Fix:* compute `t` against `FlowDirection` (`fill.Width` from the right, thumb margin from
`width - t*(width-18)`) and mirror the hit-test; add one RTL visual test per surface.
Acceptance: `ar` captures show the fill growing from the right for every slider.

### F-07 — The `×` shares the drag surface · **medium**
`ToolWindow.cs:309` closes on `MouseLeftButtonUp`, and the title bar starts `DragMove()` on
`MouseLeftButtonDown` (`326–330`) for **any** press inside the `DockPanel`, including on `×`. A press
that moves one pixel captures the mouse and drags the window instead of closing it.
*Fix:* ignore presses whose `e.OriginalSource` is inside the close button (or `e.Source != bar`), and
give `×` a pressed/hover state. Acceptance: click-drag from `×` never moves the window.

### F-08 — Disabled controls look enabled · **medium**
`Controls.Button` sets static `Background = AccentGradient` / `Foreground = TextOnAccent` and no
disabled visual; `Motion.Interactive` only guards *hover* with `element.IsEnabled`
(`Motion.cs` `Interactive`). Yet `IsEnabled` is used as the only gate in 20+ places, including the
primary action (`ToolWindow.cs:342–346`), `Detect text` (`RedactView.cs:768`), QR `Copy/Open`
(`QrView.cs:330`), Lock's `Encrypt` (`LockTool.cs:468–470`) and Protect's password gate. Some views
compensate (Annotate's `Undo` renders muted in the poster) so the style is also inconsistent.
*Fix:* add an `IsEnabled` style/trigger to the shared builders (0.4 opacity + muted foreground) and
remove per-view one-offs. Acceptance: every gated primary/secondary is visibly distinct when inert.

### F-09 — Chips and tool rows do not wrap · **medium**
`CropTool.BuildChips` and `ToolControls.Chips` use a horizontal `StackPanel`
(`CropTool.cs:76`, `ToolControls.cs:147`). At 336 DIP (1366×768 @150 %) the crop chip row needs
≈ 340–376 DIP, so the row clips; translated labels are longer still.
*Fix:* `WrapPanel` (or a horizontally scrollable strip) for chip rows, and a 360 DIP minimum window
width in `ToolWindowSizer`. Acceptance: at 336–360 DIP every chip is reachable.

### F-10 — Video join has no visible reorder affordance · **medium**
Reference rows carry `▲▼`; ours says “Drag a clip to reorder it” (`tool.videojoin.hint`) and has only
a `Remove` button (`VideoJoinTool.cs:293–296`). Merge PDFs, by contrast, exposes
`tool.pdfmerge.moveUp/moveDown` — the same job done two ways.
*Fix:* add up/down buttons per clip card (reuse the PDF merge builder) and keyboard `Ctrl+↑/↓`.
Acceptance: clip order changes without a drag.

### F-11 — QR window's primary action is mislabelled · **low**
`QrTool` inherits `Apply`; `QrView.ApplyAsync` copies the text and closes (`QrView.cs:350–356`). The
window therefore shows `Copy`, `Open` **and** `Apply` for the same operation.
*Fix:* either hide the shared action bar for read-only tools (add `IToolNoPrimary`) or label the
primary `Copy and close` (`tool.qr.copyClose`). Acceptance: one copy affordance, no `Apply` on a
read-only window.

### F-12 — Title/casing/label nits across the set · **low**
Sentence-case outliers (`Redact image`, `Redact video`, `Save frames`) next to Title Case everywhere
else; title-vs-reference wording (`Crop Video` ✓ but `Merge to PDF`/`Merge PDFs`,
`Organize Pages`/`Organize PDF`, `Compress File`/`Compress`, `Edit Metadata`/`Metadata`,
`Change Speed`/`Change Video Speed`, `Annotate Image`/`Annotate Photo`); `Annotate` and
`Bleep Audio` end in `Apply` where the reference names the copy (`Save Annotated Copy`,
`Save Bleeped Copy`) though `tool.annotate.suffix`/`tool.audiobleep.save` already exist.
*Fix:* one pass over `Strings/en.json` + `ar.json` (parity is already enforced by tests).
Acceptance: every title Title Case, every create-a-copy primary names the copy.

### F-13 — SIZES.md is not reproducible from this branch · **evidence**
`_local\selftest-art\tools\SIZES.md` reports crop landscape `603×648` with a `640×427` preview — a
preview **wider than its own window**. Recomputing with `ToolSizes.Crop` (chrome 36/270, preview max
640) and 1200×800 media gives **676×697** at 1920×1080 @100 %, and no work area in the 1080p/768p
matrix yields 603. The file also covers only crop, while 26 tools are registered.
*Fix:* regenerate it from `--self-test` (the in-repo harness already drives `ToolWindow` for crop,
`SelfTest.cs:449–483`) for all 26 tools and two shapes each, or delete it. Acceptance: every row
reproduces from `ToolWindowSizer.Compute`.

### F-14 — Reference assets named by the task are missing · **process**
`_local\video-frames`, `_local\video-frames2` and `docs/screenshots/tools` do not exist; the only
desktop tool-window reference frames live inside `_reference\tangerine-demo-frames.zip`
(`crop.png`, `addbg.png`). This audit used those.
*Fix:* either check the frames into `docs/screenshots/reference/` or stop naming them in the brief.

## 6. PASS list

1. **Chrome anatomy** — borderless, topmost, taskbar-less, rounded card, shadow, `×` left, centred
   title, `Reset` left / primary right: identical to `crop.png`/`addbg.png` in all 26 windows.
2. **Floating behaviour** — `WindowStyle.None`, `AllowsTransparency`, `Topmost`, `ShowInTaskbar=false`,
   `ResizeMode.NoResize`, `WindowStartupLocation.Manual`, centred on the monitor under the cursor
   (`ToolWindow.cs:79–86, 105, 439–453`).
3. **Esc closes** the window (except where a field deliberately eats it — F-02).
4. **Adaptive sizing** — media aspect fit, 200 % upscale cap, 95 %/90 % work-area clamp, DPI conversion;
   Collage 560×745 and Edit Photos 500×745 reproduce the documented design sizes
   (`docs/FUNCTION-MATRIX.md` C-9, E-5).
5. **Proportions** — 29–49 % of a 1920 screen wide, 43–78 % of the work area tall, inside the
   reference's 36–46 % band; smallest window 316 DIP (`redact`, `audiovisualizer` at 150 %).
6. **Reset on every tool** — the PDF's bottom bar shows `Reset` only on a minority of sheets
   (Annotate, Add BG, Crop, Redact photo, Video crop, Audio trim, Collage's in-content `Reset Layout`),
   while the desktop frames always show it; our uniform `Reset` matches the desktop frame.
7. **Primary-action vocabulary** — `Save with Background`, `Save Redacted Copy`, `Save Edited Copy`,
   `Create Collage`, `Save Organized PDF`, `Save N Frames` match the reference wording where the
   reference has one.
8. **Reference control sets reproduced** — PDF merge order rows, Organize page tiles + `Reset Order` +
   same-width note, Trim Audio silence toggle + `Play Selected Range`, Bleep lanes + `Original/With
   Bleeps`, Normalize's three sliders and helper text, Split's `Equal parts` + `Set`, Frames'
   `n`-of-`m` chips.
9. **RTL plumbing** — window `FlowDirection`, `Bidi.Value/Number` isolates, LTR pinning of previews,
   sliders and filmstrips where mirroring would corrupt content (`AddBackgroundView`, `EditPhotosView`,
   `VideoTimeBox.cs:108`).
10. **Motion** — one open/close transform set on the chrome, reduced-motion aware, resize animation
    follows the media orientation (`ToolWindow.cs:125–137, 170–182, 261–280`).

## 7. Windows-vs-macOS adaptation rule

> A difference from Tangerine is **not** a defect when it is required by Windows conventions or by a
> feature DropWheel has and Tangerine does not. It **is** a defect when it changes what the user can
> see or reach: missing identifying label, wrong reading order, clipped/truncated text, a promised but
> absent affordance, or a control the reference exposes that we hide.

Applied:

| Difference | Verdict | Why |
|---|---|---|
| Floating topmost card instead of an attached sheet; no dimmed scrim | justified | Windows has no sheet concept; a tray app must stay above Explorer |
| `Topmost` + no taskbar button | justified | keeps the tool visible after the drop; Alt+Tab is not the route |
| `Reset` on every tool while most mobile sheets hide it | justified | the desktop frames show `Reset`; consistency wins |
| Tabs in Edit Photos vs stacked sections | justified (noted) | 16 sliders at 500 DIP need the shorter surface |
| 2-column audio body vs stacked sheet | justified | desktop width |
| `Select`, `Highlighter`, `Front/Back` tools in Annotate | justified | our features; superset, same order after `Select` |
| QR / Protect / Unlock / Lock windows | out of scope of the reference | no Tangerine equivalent; judged against the app's own chrome and the PASS list |
| `Blur`/`Fast copy`/`Preview →` extras | justified | our features |
| Chips above the preview (Annotate) | **defect** | reading order differs from every reference sheet and from our other 25 windows |
| `W` at the card edge, `Background im…` | **defect** | clipped/truncated text is never a convention |
| `Apply` on a read-only QR window | **defect** | misleading label |
| Missing `Aspect ratio` label | **defect** | a control loses its identity (the reference and our own `Ratio` row both label it) |

## 8. Counts and what still needs a desktop session

* Tool rows: **18 PASS / 8 FAIL**.
* Window-wide FAILs: **14** (F-01…F-14); 12 are product defects, 2 are evidence/process
  (F-13, F-14).
* Highest-value fixes in order: **F-01** (keyboard/a11y), **F-02** (`Enter` saves from a text box),
  **F-03** (inset), **F-05** (150 % clipping + scrolling), then the label pass.

Not verifiable from this lane (needs a desktop session, still no server, no exe run by this auditor):
pixel-exact clipping after shaping, actual Tab traversal, focus-ring contrast in both themes, hit
targets under 150 % scaling, and the `ar` visual pass for F-06. The recommended acceptance run is a
`--self-test` capture of all 26 windows at 100/125/150 % in `en` and `ar`, diffed against the rules in
§2–§3.
