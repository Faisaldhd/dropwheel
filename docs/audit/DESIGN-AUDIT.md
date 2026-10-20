# Design audit — floating tool windows (round 2)

**Round 2 audited revision:** lane head `f8ed027`, based on `4f23c9c` (latest `main`, all 26 tools
wired) **plus** the fixes and audits already committed on this lane. The only difference between this
tree and bare `4f23c9c` is: `UI/Tools/Qr/QrTool.cs` (+6 lines), `Core/Strings/{en,ar}.json`, and the
`docs/` deliverables — **no other UI file differs** (`git diff --stat 4f23c9c HEAD -- src` →
QrTool + en/ar only). Where that delta changes a verdict, the row says so.

**What round 2 changed since round 1:** only
`835758c` (QR primary label), `6f6c81d` (dispatch guard test) and `d801fa9` (bilingual title/primary
test). The two-line wheel wiring that made Annotate/Redact reachable landed **upstream in `4f23c9c`**,
not on this lane; the lane carries only the guard test.
**No change at all** to `ToolWindow.cs`, `Controls.cs`, `ToolControls.cs`, `ToolWindowSizer.cs` or any
tool view (`git diff --name-only a04524d HEAD -- src/FileAction.App/UI` lists only `QrTool.cs`).
Consequence: **the "gutter work" is not in this tree** — no content inset, focus, key-routing, disabled
state or scrolling change exists here, so F-01…F-10 stay open by inspection, not by assumption.

> **Correction (committed in `f8ed027`, after this audit was written).** The no-interference rules make
> `Core/Strings/{en,ar}.json` append-only for this lane, so the five Title Case edits from `2a1e627`
> were **reverted** to `main`'s wording: F-12 is **OPEN, not partial**, and both string tables now
> differ from `main` by exactly one additive key (`tool.qr.copyClose`). F-11 stays fixed (`835758c`,
> additive). Any row below that says a title casing was "fixed in `2a1e627`" describes a change that is
> additive). The revert is reflected in the rows and counts below; the casing pass is requested for
> `docs/BACKLOG.md` (fix cluster D9).

Method: static re-audit against the same reference (`_reference\tangerine-tools.pdf`, and
`crop.png`/`addbg.png` from `_reference\tangerine-demo-frames.zip`) and the same shipped captures.
No built executable run, no network, no source edits. Sizes below are **derived exactly** from
`ToolWindowSizer.Compute` (the file is unchanged, so they are the shipped numbers, not estimates);
the "measured-in-a-capture" columns that round 2 asked for cannot be produced without a desktop
session and are flagged as such.

## R2. Round-2 status board

**Evidence availability (asked for explicitly):**
`docs/screenshots/tools/` — **still absent** (the branch has `docs/screenshots/{en,lock-badge,motion,site}`).
`_local/selftest-art/tools/SIZES.md` — **present but unchanged since round 1** (486 B, 2 rows, crop
only) and still **not reproducible** from this code (see F-13). So round 2 again uses
`_local/selftest-art/tools/en/crop-{landscape,portrait}.png` and the five
`docs/screenshots/motion/tool-*-poster.png` renders for observed evidence, and the code-derived matrix
in §3 for sizes. No new captures appeared.

| Item | Round-1 | Round-2 | Evidence |
|---|---|---|---|
| F-01 keyboard order / accessible controls | open (critical) | **OPEN** | chrome files byte-identical since round 1 |
| F-02 `Enter` applies from a text field; `Esc` swallowed | open | **OPEN** | `CropTool.cs:243–247`, `VideoControls.cs:111–123` unchanged |
| F-03 content gutter (18 DIP) | open | **OPEN** | `ToolWindow.Build()` unchanged; 7 views still flush |
| F-04 `Background im…` truncation, units in labels | open | **OPEN** | poster unchanged; `AddBackgroundView.cs` unchanged |
| F-05 fit at 1366×768@150 %, 12 non-scrolling views | open | **OPEN** | sizer + views unchanged; clipping reproduced in §3 |
| F-06 RTL slider fill not mirrored | open | **OPEN** | `Controls.Slider` unchanged |
| F-07 `×` shares the `DragMove` surface | open | **OPEN** | `ToolWindow.cs:309, 326–330` unchanged |
| F-08 disabled controls look enabled | open | **OPEN** | `Controls.Button` unchanged |
| F-09 chips do not wrap | open | **OPEN** | `CropTool.cs:76`, `ToolControls.cs:147` unchanged |
| F-10 video join has no `▲▼` | open | **OPEN** | `VideoJoinTool.cs` unchanged |
| F-11 QR primary mislabelled `Apply` | open (low) | **FIXED** `835758c` | `QrTool.PrimaryLabelKey` → `tool.qr.copyClose` = “Copy and Close” / “نسخ وإغلاق” |
| F-12 title casing + wording + copy verbs | open (low) | **OPEN** (reverted in `f8ed027`) | the five Title Case edits from `2a1e627` were reverted to `main`'s wording because the string tables are append-only for this lane; Title Case pass + the `Annotate`/`Bleep` “Apply” primaries are requested for `docs/BACKLOG.md` |
| F-13 `SIZES.md` unreproducible | open (evidence) | **OPEN** | file unchanged, still 603×648 with a 640×427 preview |
| F-14 named reference assets missing | open (process) | **OPEN** | `_local/video-frames*`, `docs/screenshots/tools` still absent |
| **R2-A window reachability** (new) | not audited | **FIXED upstream** `4f23c9c` | `tools.annotate` and `tools.redact` were dead windows (24 of 26 reachable); both operations now in `WheelController.BuildOperations()` (**26 constructions verified at HEAD**), guarded here by `ToolDispatchTests` (`6f6c81d`) |

**Counts.** Tool rows: **19 PASS / 7 FAIL** (QR moved to PASS on F-11). Window-wide: **13 open,
1 fixed** of F-01…F-14, plus one round-2 fix (R2-A).

## 0. Evidence inventory

| Source | Status | Used for |
|---|---|---|
| `_reference\tangerine-tools.pdf` (2 pages: 540×20702 pt, 1080×7475 pt; 22 tool sheets + marketing cards, page 2 at 2×) | present, rendered to 23 tiles | reference control set, order, labels, chrome anatomy |
| `_reference\tangerine-demo-frames.zip` → `crop.png`, `addbg.png` (1280×720) | present | the **only** desktop (macOS) tool-window frames: proportions, inset, title bar, action bar |
| `_reference\tangerine-shift-convert-frames.zip` (80× 540×960) | present | wheel demo only, no tool windows |
| `_local\video-frames`, `_local\video-frames2` | **absent** | named by the brief; not in this checkout |
| `docs\screenshots\tools\` | **absent** (round 2 re-checked) | named by the brief; not in this branch |
| `_local\selftest-art\tools\SIZES.md` | present, unchanged (486 B, crop only) | **stale** — F-13 |
| `_local\selftest-art\tools\en\crop-{landscape,portrait}.png` | present | observed crop rendering (inset, clipping) |
| `docs\screenshots\motion\tool-{crop,addbackground,annotate,editphotos,redact}-{light,dark}-poster.png` | present | observed rendering of 5 tools |
| `docs\audit\UX-AUDIT.md` (485 lines, other lane) | present | not modified, not duplicated here |
| Source: `UI/Tools/**`, `ToolWindow.cs`, `Controls.cs`, `ToolControls.cs`, `Core/ToolWindowSizer.cs`, `Core/Strings/{en,ar}.json`, `Wheel/WheelController.cs` | present | geometry, chrome, control order, labels, keyboard, RTL, reachability |

Not claimed: real pixel widths after font shaping, live clipping, actual Tab traversal, focus-ring
contrast. Those need a desktop session (§8).

## 1. The reference model, distilled

From `crop.png` / `addbg.png` (desktop) and the 22 tool sheets in the PDF:

* **Anatomy:** floating card, radius ≈ 18–20 DIP, drop shadow, no OS title bar → `×` close at the
  **left**, **centred** semibold title, content, action bar with the **secondary bottom-left**
  (`Reset`) and the **primary bottom-right** (`Apply`, `Save with Background`, …).
* **Proportions:** `crop.png` card = 460×700 in a 1280×720 canvas → **36 % of width, 97 % of height**;
  `addbg.png` = 585×704 → **46 % W, 98 % H**. Window follows the media aspect; preview is the star
  area, controls a fixed block under it.
* **Content order:** every sheet, mobile and desktop, puts the **preview first**, controls **under**
  it, action bar last.
* **Inset:** control rows and preview share one 18–20 DIP inset, aligned with the title bar.
* **Control vocabulary:** chips for exclusivity (`Free 1:1 16:9 9:16 4:3 3:4`), labelled sliders with
  the value at the row end (`Padding ——— 160 px`), dropdowns with the current value, toggles,
  `n`-of-`m` counters, per-row `↺` (Edit Photo), per-row `▲▼` (Merge/Join), `⊕/⊖` on metadata fields,
  and a one-line behaviour hint under busy controls.
* **Labels:** Title Case titles (`Crop Image`, `Add BG`, `Merge PDFs`), sentence case in the body,
  units **with the value** (`160 px`, `0.00 EV`, `-1.5 dBTP`), not in the label.

## 2. Shared chrome — verdict (unchanged in round 2)

| Property | Implementation | Verdict |
|---|---|---|
| Borderless transparent card, radius 20, 12 DIP margin, 28 px shadow | `ToolWindow.cs:79–86, 244–258` | **PASS** |
| `Topmost`, `ShowInTaskbar=false`, `WindowStartupLocation.Manual`, centred on cursor monitor | `ToolWindow.cs:83–86, 105, 439–453` | **PASS** |
| `DragMove()` on the title bar | `ToolWindow.cs:326–330` | **PASS with defect** — F-07 |
| `×` left, title centred, `Reset` left / primary right | `ToolWindow.cs:289–355` | **PASS** |
| `Esc` closes | `ToolWindow.cs:111–116` | **PASS** — shadowed in some tools (F-02) |
| `Enter` applies | `ToolWindow.cs:117–122` | **FAIL** — F-02 |
| Adaptive size: media aspect, 95 % × 90 % clamp, 200 % upscale cap | `ToolWindowSizer.cs:38–95` | **PASS** |
| DPI conversion through `GetDpiForWindow` | `ToolWindow.cs:404–437` | **PASS** |
| RTL: window `FlowDirection` + `Bidi` isolates | `ToolWindow.cs:91`, `Bidi.cs:19–27` | **PASS with defect** — F-06 |
| Keyboard order / focus | none | **FAIL** — F-01 |
| Disabled state | none | **FAIL** — F-08 |
| Content inset | 0–18 DIP, per tool | **FAIL** — F-03 |
| Every registered window reachable from the wheel | 26 operations, ids asserted | **PASS** since `4f23c9c` (R2-A, upstream) |

## 3. Round-2 size and proportion matrix (derived, all 26 tools)

Exact re-implementation of `ToolWindowSizer.Compute` (`ToolWindowSizer.cs:38–95`, unchanged), 16:9
landscape media (1920×1080), work areas 1920×1040 and 1366×728 (40 px taskbar). Cell =
**W×H DIP · W% of the physical screen**; `*` = height hit the 90 % cap (the preview is squeezed);
`!` = chrome is taller than the window, so control rows are clipped.

| Tool | 1080p@100 | 1080p@125 | 1080p@150 | 768p@100 | 768p@125 | 768p@150 |
|---|---|---|---|---|---|---|
| crop | 676×630 · 35% | 676×630 · 35% | 665×624 · 35%* | 676×630 · 49% | 488×524 · 36% | 336×437 · 25%* |
| addbackground | 556×650 · 29% | 556×650 · 29% | 523×624 · 27%* | 556×650 · 41% | 416×524 · 30% | 416×437 · 30%* |
| annotate | 668×671 · 35% | 668×671 · 35% | 585×624 · 30%* | 640×655 · 47% | 407×524 · 30% | 368×437 · 27%* |
| redact | 676×650 · 35% | 676×650 · 35% | 630×624 · 33%* | 676×650 · 49% | 452×524 · 33% | 316×437 · 23%* |
| editphotos | 500×768 · 26% | 467×749 · 24%* | 420×624 · 22%* | 420×655 · 31% | 420×524 · 31% | 420×437 · 31%**!** |
| collage | 560×747 · 29% | 560×747 · 29% | 360×624 · 19%* | 396×655 · 29% | 360×524 · 26% | 360×437 · 26%**!** |
| compress | 596×455 · 31% | 596×455 · 31% | 596×455 · 31% | 596×455 · 44% | 596×455 · 44% | 564×437 · 41%* |
| metadata | 656×489 · 34% | 656×489 · 34% | 656×489 · 34% | 656×489 · 48% | 656×489 · 48% | 564×437 · 41%* |
| qr | 756×725 · 39% | 756×725 · 39% | 576×624 · 30%* | 632×655 · 46% | 399×524 · 29% | 336×437 · 25%* |
| pdfmerge | 940×806 · 49% | 838×749 · 44%* | 616×624 · 32%* | 671×655 · 49% | 438×524 · 32% | 400×437 · 29%* |
| pdforganize | 940×806 · 49% | 838×749 · 44%* | 616×624 · 32%* | 671×655 · 49% | 438×524 · 32% | 400×437 · 29%* |
| videotrim | 720×715 · 38% | 720×715 · 38% | 596×624 · 31%* | 614×655 · 45% | 596×524 · 44% | 596×437 · 44%* |
| videocrop | 720×725 · 38% | 720×725 · 38% | 596×624 · 31%* | 596×655 · 44% | 596×524 · 44% | 596×437 · 44%* |
| videospeed | 720×695 · 38% | 720×695 · 38% | 596×624 · 31%* | 649×655 · 48% | 596×524 · 44% | 596×437 · 44%* |
| videosplit | 856×731 · 45% | 856×731 · 45% | 665×624 · 35%* | 720×655 · 53% | 596×524 · 44% | 596×437 · 44%* |
| videojoin | 856×661 · 45% | 856×661 · 45% | 790×624 · 41%* | 845×655 · 62% | 612×524 · 45% | 596×437 · 44%* |
| videoframes | 720×705 · 38% | 720×705 · 38% | 596×624 · 31%* | 632×655 · 46% | 596×524 · 44% | 596×437 · 44%* |
| videoredact | 720×715 · 38% | 720×715 · 38% | 596×624 · 31%* | 614×655 · 45% | 596×524 · 44% | 596×437 · 44%* |
| audionormalize | 783×650 · 41% | 783×650 · 41% | 736×624 · 38%* | 783×650 · 57% | 559×524 · 41% | 456×437 · 33%* |
| audiotrim | 783×650 · 41% | 783×650 · 41% | 736×624 · 38%* | 783×650 · 57% | 559×524 · 41% | 456×437 · 33%* |
| audiochannels | 463×540 · 24% | 463×540 · 24% | 463×540 · 24% | 463×540 · 34% | 434×524 · 32% | 336×437 · 25%* |
| audiobleep | 427×610 · 22% | 427×610 · 22% | 427×610 · 22% | 427×610 · 31% | 356×524 · 26% | 356×437 · 26%* |
| audiovisualizer | 520×552 · 27% | 520×552 · 27% | 520×552 · 27% | 520×552 · 38% | 470×524 · 34% | 316×437 · 23%* |
| protect | 652×449 · 34% | 652×449 · 34% | 652×449 · 34% | 652×449 · 48% | 652×449 · 48% | 631×437 · 46%* |
| unlock | 652×449 · 34% | 652×449 · 34% | 652×449 · 34% | 652×449 · 48% | 652×449 · 48% | 631×437 · 46%* |
| lock | 676×560 · 35% | 676×560 · 35% | 676×560 · 35% | 676×560 · 49% | 612×524 · 45% | 457×437 · 33%* |

Reading of the matrix (round 2):

1. **Proportions vs the reference.** At 1920×1080@100 % the windows are **22–49 % of screen width**
   (audiobleep 22 %, pdfmerge/organize 49 %) and **43–78 % of the work area tall**. The reference band
   measured from the two desktop frames is **36–46 % W / 94–98 % H**, so the media tools cluster
   correctly (crop 35 %, redact 35 %, video 38–45 %) while the small-audio and document tools sit at
   the edges: `audiobleep` at 22 % is narrower than any reference sheet (its preview maxes at
   620×220); `pdfmerge`/`pdforganize` at 49 % are the widest content windows.
2. **Screen scaling.** Windows are absolutely bounded (95 % × 90 % of the work area), not scaled to the
   screen, so on 1366×768@100 % the same window occupies **29–62 % of width** (videojoin 62 %,
   audionormalize 57 %) and **62–90 % of the work area height** — on a small screen our windows take a
   much larger share than the reference does on its canvas. Coherent as a desktop rule, but the
   opposite direction from the reference's fixed-feel sheet.
3. **150 % DPI.** At 1920×1080@150 % the work area is 1280×693 DIP and the cap is 624 DIP: most
   windows hit the cap and lose preview height (never controls). At 1366×768@150 % the cap is
   **436.8 DIP** and every window opens at it; 13 of 26 are ≤ 460 DIP wide, narrowest **316**.
4. **Clipping (`!`).** Only two windows have chrome taller than the window at 1366×768@150 %:
   `editphotos` (chrome 520 in 437 → **83 DIP of control rows clipped**) and `collage` (455 → **18 DIP
   clipped**). Both are non-scrolling, so the lost rows are unreachable; every other window squeezes
   the preview instead.
5. **Chip-row risk.** `crop` at 1366×768@150 % is **336 DIP** wide while its six chips need
   ≈ 340–376 DIP (`CropTool.cs:76, 204–230`, non-wrapping `StackPanel`) — the row clips; every other
   chips row (video crop 596, addbg 416, editphotos 420) fits.

## 4. One row per tool (round 2)

Legend — **ok**: chrome, order, labels and fit match the reference or a justified Windows/feature
adaptation. **deviations**: at least one user-visible defect. Size column = 1080p@100 → 768p@150.

| # | Tool (id) | Title / primary | Size | Status | Deviations vs reference | Fix PR |
|---|---|---|---|---|---|---|
| 1 | Crop `tools.crop` | Crop Image / Apply | 676×630 → 336×437* | **deviations** | chips row has **no `Aspect ratio` label**; `W`/`H` row has **no trailing `px`**; control rows sit at the card edge — the leading **`W` is clipped** in `_local\selftest-art\tools\en\crop-landscape.png`; chips do not wrap → clip at 336 DIP; `Enter` in W/H also applies | D2, D3 |
| 2 | Add Background `tools.addbackground` | Add BG / Save with Background | 556×650 → 416×437* | **deviations** | **`Background im…` truncated** in `docs/screenshots/motion/tool-addbackground-light-poster.png`; units in the label (`Padding (px)`) instead of with the value (`160 px`) | D4 |
| 3 | Annotate `tools.annotate` | Annotate Image / **Apply** | 668×671 → 368×437* | **deviations** | controls **above** the preview; `Fill` toggle styled as an eighth colour chip inside the colour row; stroke-width and font-size selects share one row; no behaviour hint; primary `Apply` where the reference says `Save Annotated Copy` | D5, D9 |
| 4 | Redact Image `tools.redact` | Redact image / Save Redacted Copy | 676×650 → 316×437* | **deviations** | reference has `Detect [Faces, Text]`; ours has `Detect text` only while the wheel card advertises “Detect faces and text locally”. **R2:** the title still reads `Redact image` (casing fix reverted in `f8ed027`); the affordance gap remains | D7, D9 |
| 5 | Edit Photos `tools.editphotos` | Edit Photos / Save Edited Copy | 500×768 → 420×437**!** | **deviations** | values are bare (`0`) vs `0.00 EV` / `+0` / `0°`; **no per-row `↺`**; at 768p@150 % 83 DIP of control rows are clipped, @125 % the preview is 4 DIP | D6, D8 |
| 6 | Collage `tools.collage` | Collage / Create Collage | 560×747 → 360×437**!** | **deviations** | at 768p@150 % 18 DIP of the `Order` row clips; only horizontal scrolling exists (`CollageView.cs:281–284`); title wording vs the sheet's `Create Collage` | D6, D9 |
| 7 | Video trim `tools.videotrim` | Trim Video / Apply | 720×715 → 596×437* | ok | preview → filmstrip → start/end → transport matches; `Fast copy` is our justified extra | — |
| 8 | Video crop `tools.videocrop` | Crop Video / Apply | 720×725 → 596×437* | ok | chips, `W/H`, scrub, transport, action bar match; 596 DIP minimum keeps the chip row safe | D3 (shared wrap) |
| 9 | Video speed `tools.videospeed` | Change Speed / Apply | 720×695 → 596×437* | ok | reference tick row implemented as preset chips + slider | — |
| 10 | Video split `tools.videosplit` | Split Video / Split Video | 856×731 → 596×437* | ok | `Equal parts` + stepper + `Set`, split-point hint | — |
| 11 | Video join `tools.videojoin` | Join Videos / Join Videos | 856×661 → 596×437* | **deviations** | reference rows carry visible `▲▼`; ours is drag-only — no discoverable affordance, no keyboard route, while Merge PDFs does expose `moveUp`/`moveDown` | D10 |
| 12 | Save Frames `tools.videoframes` | Save frames / Save N Frames | 720×705 → 596×437* | ok | dynamic primary and `n`-of-`m` chips match; still `Save frames` (casing reverted, nit) | — |
| 13 | Redact Video `tools.videoredact` | Redact video / Save Redacted Copy | 720×715 → 596×437* | ok | lanes + style + colour + times match; still `Redact video` (casing reverted, nit) | — |
| 14 | Normalize Audio `tools.audionormalize` | Normalize Audio / Apply | 783×650 → 456×437* | ok | three labelled sliders + helper note are the reference's; 2-column body justified by width | — |
| 15 | Trim Audio `tools.audiotrim` | Trim Audio / Apply | 783×650 → 456×437* | ok | silence toggle + `Play Selected Range` + start/end match | — |
| 16 | Audio Channels `tools.audiochannels` | Audio Channels / Apply | 463×540 → 336×437* | ok | `Mono`/`Stereo`, L/R volumes, solo preview — superset, same order | — |
| 17 | Bleep Audio `tools.audiobleep` | Bleep Audio / Apply | 427×610 → 356×437* | ok | `Bleeps n`, lanes, hint, `Original`/`With Bleeps` match; primary `Apply` vs the sheet's `Save Bleeped Copy` (nit) | D9 |
| 18 | Audio Visualizer `tools.audiovisualizer` | Audio Visualizer / Apply | 520×552 → 316×437* | ok | style + shape dropdowns match; background picker is ours | — |
| 19 | Merge to PDF `tools.pdfmerge` | Merge to PDF / Merge PDFs | 940×806 → 400×437* | ok | `Document order` + rows + `▲▼` + `Remove`; title/primary wording disagree (nit) | D9 |
| 20 | Organize Pages `tools.pdforganize` | Organize Pages / Save Organized PDF | 940×806 → 400×437* | ok | page tiles + `Reset Order` + same-width note; title differs from `Organize PDF` (nit) | D9 |
| 21 | Compress `tools.compress` | Compress File / Compress | 596×455 → 564×437* | ok | preset segments, image-size dropdown, target-size toggle, estimate — superset (nit: title) | D9 |
| 22 | Edit Metadata `tools.metadata` | Edit Metadata / Save | 656×489 → 564×437* | ok | search, grouped fields, per-field remove, `Remove All` + note, `Save` (nit: title) | D9 |
| 23 | QR Code `tools.qr` | QR Code / **Copy and Close** | 756×725 → 336×437* | ok | **R2: F-11 fixed** (`835758c`) — primary no longer claims `Apply`. Nit: the view's own `Copy` and the primary `Copy and Close` are two copy affordances | — |
| 24 | Protect `tools.protect` | Password protection / **Apply** | 652×449 → 631×437* | ok | our feature; title still `Password protection` (nit) and primary `Apply` is the weakest label (nit) | D9 |
| 25 | Unlock `tools.unlock` | Remove password / Apply | 652×449 → 631×437* | ok | our feature; title still `Remove password` (nit) | D9 |
| 26 | Lock `tools.lock` | Lock / Encrypt | 676×560 → 457×437* | ok | our feature; `Encrypt`/`Open` via `IToolActionLabel`; `Esc`/`Enter` handled inside the tool | — |

**Tool rows: 19 PASS / 7 FAIL** — the 7 are crop, addbackground, annotate, redact, editphotos,
collage, videojoin. Window-wide defects that touch every row are counted in §5, not here.

## 5. FAIL list — round-2 status per item

Fix-PR clusters referenced from §4:

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
| **D9** | `strings: reference wording and primary-verb pass` | `Core/Strings/{en,ar}.json` | F-12 (remainder) |
| **D10** | `videojoin: visible reorder buttons and keyboard reorder` | `VideoJoinTool.cs` | F-10 |
| **D11** | `qr: one copy affordance` | `QrTool.cs`, `QrView.cs` | row 23 nit — **F-11 done** `835758c` |
| **D12** | `rtl: mirror slider fill and hit-test` | `Controls.cs`, `AudioSlider.cs`, `VideoTimeline.cs` | F-06 |
| **D13** | `chrome: close button must not start a drag` | `ToolWindow.cs` | F-07 |
| **D14** | `evidence: regenerate SIZES.md for all 26 tools` | `_local/selftest-art/tools`, brief | F-13, F-14 |

### F-01 — No keyboard order and no accessible controls · **critical** · **OPEN**
Every interactive builder is a `Border`/`StackPanel` with `MouseLeftButtonUp`
(`Controls.cs:90–275`, `ToolControls.cs:80–193`); none is `Focusable`/`IsTabStop`, has a focus visual
or `AutomationProperties.Name`. Only stray text boxes and three view elements are focusable
(`PasswordViewParts.cs:111–112`, `BleepLane.cs:47`, `VideoSplitTool.cs:271`). **Tab reaches text fields
only.** Round 2: unchanged — `git diff a04524d HEAD -- src/FileAction.App/UI` lists only `QrTool.cs`.
*Fix (unchanged):* focusable control kit, tab order in visual order, focus ring, automation names.

### F-02 — `Enter` applies the tool from inside a text field; `Esc` swallowed · **high** · **OPEN**
`ToolWindow.cs:117–122` applies on any `Enter`; `CropTool.cs:243–247` commits the W/H box **without**
`e.Handled`, so typing a width and pressing `Enter` also **saves a file**. `VideoTimeBox` consumes
`Esc` to revert the box (`VideoControls.cs:118–122`), so `Esc` does not close those windows.
*Fix (unchanged):* apply only when the focused element is not a `TextBox`; set `e.Handled` in the crop
commit; let `Esc` close unless an in-canvas editor is open.

### F-03 — Content gutter inconsistent, zero in 7 tools · **high** · **OPEN**
Title/action bars are inset 18 DIP (`ToolWindow.cs:322, 350`), the view row is not; `crop`,
`videotrim`, `videocrop`, `videospeed`, `videosplit`, `videojoin`, `videoframes` declare no horizontal
inset. Observed: the leading **`W` is cut off** at the card edge in `crop-landscape.png`. **Measured independently** by the sibling lane on `tool-crop-light-poster.png` (480×508): the card spans x=10–468, the preview starts at x=15 (**5 px** in), the primary ends at x=454 (**14 px** in) — the content sits ~3× closer to the edge than the actions meant to share its gutter.
**Round 2: no gutter change exists in this tree** (no diff in `ToolWindow.cs` or any view).
*Fix (unchanged):* one 18 DIP gutter applied by the window; delete per-view insets.

### F-04 — `Background image` truncated; units in labels · **medium** · **OPEN**
`tool-addbackground-light-poster.png` shows `Background im…`; the reference keeps the label to one
short word and puts the image picker in a small icon at the row end.
*Fix (unchanged):* wrapping label column or a dedicated row; units with the value.

### F-05 — Two windows cannot fit their chrome at 1366×768@150 %; 12 views cannot scroll · **high** · **OPEN**
`editphotos` declares 520 DIP of chrome, `collage` 455 (`EditPhotosTool.cs:28`, `CollageTool.cs:21`);
the cap at 1366×768@150 % is **436.8 DIP** → 83 / 18 DIP of control rows clipped (matrix `!` in §3).
Both views are non-scrolling (Collage scrolls horizontally only). Twelve more views have no vertical
scroller: `crop`, `addbackground`, `annotate`, `redact`, `videotrim`, `videocrop`, `videospeed`,
`audionormalize`, `audiotrim`, `audiochannels`, `audiobleep`, `audiovisualizer`.
*Fix (unchanged):* scroll the control block, or measure chrome at `Loaded`; keep the preview the
collapsible row.

### F-06 — RTL slider fill is not mirrored · **medium** · **OPEN**
`Controls.Slider` uses `HorizontalAlignment.Left` + positive `TranslateTransform.X`
(`Controls.cs:179–213`); under `FlowDirection.RightToLeft` the row mirrors but the fill still grows
left→right. `AudioSlider`/`VideoTimeline` need the same check.
*Fix (unchanged):* compute `t` against `FlowDirection`, mirror the hit-test, add an RTL test.

### F-07 — The `×` shares the drag surface · **medium** · **OPEN**
`ToolWindow.cs:309` closes on `MouseLeftButtonUp`; the bar starts `DragMove()` on
`MouseLeftButtonDown` (`326–330`) for any press inside the `DockPanel`, including on `×`, so a press
that moves captures the mouse and drags instead of closing.
*Fix (unchanged):* ignore presses whose `OriginalSource` is inside the close button; add pressed/hover.

### F-08 — Disabled controls look enabled · **medium** · **OPEN**
`Controls.Button` sets static brushes and has no disabled visual; `Motion.Interactive` only guards
hover. `IsEnabled` is the only gate in 20+ places, including the shared primary
(`ToolWindow.cs:342–346`), `Detect text` (`RedactView.cs:768`), QR `Copy`/`Open` (`QrView.cs:330`),
Lock `Encrypt` (`LockTool.cs:468–470`). Some views compensate (Annotate `Undo` renders muted), so the
style is inconsistent.
*Fix (unchanged):* one disabled style in the shared builders.

### F-09 — Chips and tool rows do not wrap · **medium** · **OPEN**
`CropTool.BuildChips` and `ToolControls.Chips` use a horizontal `StackPanel` (`CropTool.cs:76`,
`ToolControls.cs:147`). At 336 DIP the crop chips need ≈ 340–376 DIP → clipped.
*Fix (unchanged):* `WrapPanel` or a scrollable strip; 360 DIP minimum width in the sizer.

### F-10 — Video join has no visible reorder affordance · **medium** · **OPEN**
Reference rows carry `▲▼`; ours says “Drag a clip to reorder it” and offers only `Remove`
(`VideoJoinTool.cs:293–296`), while Merge PDFs exposes `moveUp`/`moveDown`.
*Fix (unchanged):* per-clip up/down buttons + `Ctrl+↑/↓`.

### F-11 — QR primary mislabelled · **low** · **FIXED** in `835758c`
`QrTool.PrimaryLabelKey => "tool.qr.copyClose"` = “Copy and Close” / “نسخ وإغلاق”, so a read-only
window no longer shows `Apply`. Remaining nit: the view's `Copy` button and the primary do the same
copy. Note: on **bare `4f23c9c` this fix is not present** (it exists only on this lane).
*Follow-up (D11):* one copy affordance, or label the in-view button “Copy without closing”.

### F-12 — Label/casing/wording pass · **low** · **OPEN** (fix reverted in `f8ed027`)
The five sentence-case titles (`Password protection`, `Remove password`, `Save frames`, `Redact video`,
`Redact image`) were title-cased in `2a1e627` and **reverted in `f8ed027`** (the string tables are
append-only for this lane), so the pass is requested for `docs/BACKLOG.md`. Still open: title wording vs
the reference (`Merge to PDF`/`Merge PDFs`, `Organize Pages`/`Organize PDF`, `Compress File`/
`Compress`, `Edit Metadata`/`Metadata`, `Change Speed`/`Change Video Speed`, `Annotate Image`/
`Annotate Photo`) and the `Annotate` / `Bleep Audio` primaries that still say `Apply` where the
reference names the copy (`Save Annotated Copy`, `Save Bleeped Copy`; `tool.annotate.suffix` and
`tool.audiobleep.save` exist). *Fix (D9):* one pass over both tables, once the lane may touch them.
### F-13 — `SIZES.md` is not reproducible · **evidence** · **OPEN**
Still 486 B, two crop rows, reporting `603×648` with a `640×427` preview — a preview wider than its
own window. Recomputing with `ToolSizes.Crop` (chrome 36/270, preview max 640) and 1200×800 media gives
**676×697** at 1920×1080@100 %, and no cell in the §3 matrix yields 603. It also covers 2 of 26 tools.
*Fix (D14):* regenerate from `--self-test` for all 26 tools × two shapes, or delete it.

### F-14 — Reference assets named by the brief are missing · **process** · **OPEN**
`_local\video-frames`, `_local\video-frames2` and `docs/screenshots/tools` do not exist in this
checkout (re-checked for round 2). The only desktop reference frames are inside
`_reference\tangerine-demo-frames.zip`.
*Fix (D14):* check the frames in, or stop naming them in the brief.

### R2-A — Two of 26 tool windows were unreachable · **high** · **FIXED upstream** in `4f23c9c`
`tools.annotate` and `tools.redact` had declared operations that were never constructed, so 24 of 26
registered windows could not be opened from the wheel. Both are now appended to
`WheelController.BuildOperations()` (26 constructions, `WheelController.cs:88–116`), and
`ToolDispatchTests.EveryRegisteredToolWindowIsReachableFromTheWheel` asserts identity + interactivity
for every registry entry. (Tracked by the UX lane as F-27; recorded here because window reachability
is part of “every tool window”.)

## 6. PASS list (round 2)

1. **Chrome anatomy** — borderless card, shadow, `×` left, centred title, `Reset` left / primary right
   in all 26 windows, identical to `crop.png`/`addbg.png`.
2. **Floating behaviour** — `WindowStyle.None`, `AllowsTransparency`, `Topmost`, no taskbar,
   `ResizeMode.NoResize`, centred on the monitor under the cursor.
3. **Esc closes** the window (except where a field eats it — F-02).
4. **Adaptive sizing** — media aspect fit, 200 % upscale cap, 95 %/90 % clamp, DPI conversion;
   Collage 560×745 and Edit Photos 500×745 reproduce the documented design sizes
   (`docs/FUNCTION-MATRIX.md` C-9, E-5).
5. **Proportions** — 22–49 % of a 1920 screen wide, 43–78 % of the work area tall at 100 %; the media
   tools (35–45 %) sit inside the reference's 36–46 % band.
6. **Reset on every tool** — the PDF shows a bottom-bar `Reset` only on a minority of sheets; our
   uniformity matches the desktop frames.
7. **Primary-action vocabulary** — `Save with Background`, `Save Redacted Copy`, `Save Edited Copy`,
   `Create Collage`, `Save Organized PDF`, `Save N Frames` match the reference where it has one, and
   `Copy and Close` now names the QR action (**round 2**).
8. **Reference control sets** — Merge order rows, Organize page tiles + `Reset Order` + same-width
   note, Trim Audio silence toggle + `Play Selected Range`, Bleep lanes + `Original`/`With Bleeps`,
   Normalize's three sliders + helper text, Split's `Equal parts` + `Set`, Frames' `n`-of-`m` chips.
9. **RTL plumbing** — window `FlowDirection`, `Bidi.Value/Number`, LTR pinning of previews, sliders and
   filmstrips where mirroring would corrupt content (`VideoTimeBox.cs:108`, `AddBackgroundView`,
   `EditPhotosView`).
10. **Motion** — one open/close transform set on the chrome, reduced-motion aware, resize follows the
    media orientation.
11. **Reachability (round 2)** — 26 operations for 26 registered windows, guarded by the dispatch test
    (R2-A fixed).

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
| `Select`, `Highlighter`, `Front/Back` in Annotate | justified | our features; superset |
| QR / Protect / Unlock / Lock windows | outside the reference | no Tangerine equivalent; judged against our own chrome |
| `Blur` / `Fast copy` / `Preview →` extras | justified | our features |
| Chips above the preview (Annotate) | **defect** | reading order differs from every reference sheet and our other 25 windows |
| `W` at the card edge, `Background im…` | **defect** | clipped/truncated text is never a convention |
| `Apply` on a read-only QR window | **defect — fixed** | misleading label (`835758c`) |
| Missing `Aspect ratio` label | **defect** | the control loses its identity |
| `pdfmerge` at 49 % width vs reference 36–46 % | **noted, not a defect** | absolute bounds are the Windows rule; it still fits 1366@100 |

## 8. Counts, and what still needs a desktop session

* Tool rows: **19 PASS / 7 FAIL** (round 1: 18 / 8; QR moved to PASS).
* Window-wide: **13 OPEN, 1 FIXED** of F-01…F-14, plus **R2-A fixed upstream** (unreachable windows).
  windows).
* Fixed since round 1: F-11 (`835758c`) and R2-A (`4f23c9c`, upstream). The F-12 casing fix
  (`2a1e627`) was reverted in `f8ed027`, so F-12 is open, not partial.
* The “gutter work” the round-2 brief refers to is **not present** in this tree: every chrome, control,
  sizer and view file is byte-identical to round 1. The only code delta is `QrTool.cs` + strings.
* Highest-value open items, in order: **F-01** (keyboard/a11y), **F-02** (`Enter` saves from a text
  box), **F-03** (gutter), **F-05** (150 % clipping + scrolling), then the label pass (F-12 remainder).

Not verifiable from this lane (needs a desktop session; no server, no exe run by this auditor):
pixel-exact clipping after shaping, real Tab traversal, focus-ring contrast in both themes, hit
targets at 150 %, and the `ar` visual pass for F-06. Round 2 could not use `docs/screenshots/tools`
(absent) or a fresh `SIZES.md` (unchanged), so sizes are code-derived rather than screen-measured.
Recommended acceptance run: `--self-test` captures of all 26 windows at 1920×1080 and 1366×768 in
100/125/150 % and `en`/`ar`, diffed against §1–§3 and this document.
