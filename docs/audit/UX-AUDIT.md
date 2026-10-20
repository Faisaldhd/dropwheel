# UX / UI audit — wheel + tool windows (2.4.0)

Branch `audit/adesign`, base `origin/main` (`268a2d7` + `f833459`). Lane: UX/UI quality lead.
Scope: the drag wheel and **every registered tool window**, against the Tangerine reference set and the
captures we actually ship. Discipline: static reading only — no executable, installer, `--self-test`,
`dotnet build/test/publish` or packaging script was run by this lane. Execution belongs to CI.

**This document is the actionable layer.** The detailed window-by-window annex — every row, label,
control set and geometry recomputation — is [`DESIGN-AUDIT.md`](DESIGN-AUDIT.md) and is not repeated
here. Annex finding ids `F-01…F-14` keep their original meaning; **new findings in this document start
at `F-15`** and are never renumbered.

Reference material (read-only, never committed): `..\_reference\tangerine-tools.pdf` (2 pages, 22 tool
sheets), `..\_reference\tangerine-demo-frames.zip` (`crop.png`, `addbg.png` — the only real desktop tool
frames), `..\_reference\tangerine-shift-convert.mp4` (wheel only), `..\_reference\lock-badge-mockup.jpg`.

## 0. Count reconciliation: 25 or 26?

The brief says 25 tool windows. The build registers **26** tools (`ToolRegistry`, 26 `[ModuleInitializer]`
register calls): `tools.lock` is the 2.4.0 addition merged in `268a2d7` (*Merge pull request #21 from
Faisaldhd/feat/2.4.0-lock*). Every table below covers 26. Nothing is missing; the brief predates the
lock feature.

## 1. Verdict

| Dimension | Verdict | Note |
| --- | --- | --- |
| Chrome anatomy (card, radius, shadow, `×` left, centred title, Reset left / primary right) | **PASS** | identical in all 26 and to `crop.png` |
| Proportions / adaptive sizing | **PASS** (1080p) | 29–49 % width, 43–78 % of work area; fails at 1366×768 @150 % (F-05) |
| Content gutter (spacing scale) | **FAIL** | 7 windows flush to the card edge, 4 at 18 DIP, 15 at 16–18 on inner containers (F-03, F-24) |
| Control order (preview → controls → action bar) | **FAIL** | `annotate` inverts it; everything else matches (annex row 3) |
| Typography | **FAIL** | no type scale: 19 distinct font sizes; 9 distinct icon sizes (F-16, F-25) |
| Colour tokens, dark/light parity | **PASS with caveat** | 26/26 use `Theme` for chrome; audio/video/canvas add three secondary palettes (F-15) |
| Iconography | **FAIL** | sizes 9–20 px, literal `\uE` glyphs inline in views, no icon-size token (F-25) |
| Primary/secondary hierarchy | **PASS with defect** | colour + weight carry it, but no disabled visual (F-08) and no default-action affordance |
| Empty states | **PARTIAL** | 7 tools author empty copy; 19 rely on a disabled primary (F-26) |
| Error states | **FAIL** | the generic path shows a raw `ex.Message` toast (F-18) |
| Busy / progress states | **FAIL** | no in-window progress or cancel (F-19) |
| RTL readiness | **PASS with defect** | window `FlowDirection`, `Bidi.Value`, full `ar` string parity; slider fill is not mirrored (F-06) |
| Status-badge contrast (WCAG AA) | **FAIL — fixed here** | light 2.02:1 / 2.79:1, dark error 3.94:1 → new theme-aware badge tokens (F-28) |
| Tool reachability from the wheel | **FAIL — fixed upstream, guarded here** | 24 of 26 windows were reachable; `tools.annotate` / `tools.redact` had no wheel operation (F-27), wired in `4f23c9c` |
| Wheel | **PASS with defects** | purple identity, modal depth, stagger; mode-switch affordance unproven (F-23), stale captures (F-22) |
| Shipped evidence | **FAIL** | `docs/screenshots/en/wheel-*.png` are pre-2.4 orange-accent captures (F-22) |

Tool rows: **14 PASS / 12 FAIL** when the shared gutter defect (F-03) is attributed to each window it
visibly affects. Counting only window-specific deviations — the annex's convention — it is
**19 PASS / 7 FAIL** (QR moved to PASS when F-11 landed in `835758c`, matching `DESIGN-AUDIT.md`
round 2). Both numbers are derived from the same table in §9.

**Evidence standard (rule 5).** Every count here comes from a named grep, file and line, or a commit;
every pixel number was measured with PIL on a committed poster (§5.1) and is labelled *capture pixels*,
not DIP. Anything that would need a rendered window is written **needs CI screenshot** rather than
estimated: that applies to F-05, F-06, F-18, F-19, F-20, F-23, F-26 and F-28, and to the 21 parity rows
that have no light/dark capture. Contrast ratios are computed, not eyeballed: WCAG 2.1 relative-luminance
formula over the composited colour, shown as the pair itself in F-28.

## 2. What the reference actually establishes

From `crop.png` and `addbg.png` (1280×720, the only real desktop frames) and the 22 sheets:

1. **One inset for everything.** In `crop.png` the title, the preview, the `Aspect ratio` row, the `W`/`H`
   row and both action buttons share the same left/right gutter (~22 px in a 460 px card). The preview is
   **not** flush to the card edge.
2. **Every control group is labelled.** `Aspect ratio  Free | 1:1 | 16:9 | 9:16 | 4:3 | 3:4`, then
   `W [2550] H [2674] px`. Units live **with the value**, never in the label.
3. **Preview first, controls under it, action bar last.** Uniform across all 22 sheets and both frames.
4. **Two action weights, one shape.** Secondary is a quiet tinted rectangle; primary is a saturated pill.
   Same height, different fill and weight. No third button style inside the card.
5. **One type ramp.** Title ~15–16 semibold, row label ~12, value ~12, hint ~11. Nothing at 11.5 vs 12 vs
   12.5 for the same role.

Those five rules are the yardstick in §4–§8. A difference is *justified* when Windows or a DropWheel-only
feature requires it (annex §7); it is a *defect* when it changes what the user can see or reach.

## 3. The design system as it exists

### 3.1 Colour tokens

`UI/Theme.cs` is the chrome palette: 14 slots per theme (`Backdrop`, `Panel`, `Card`, `Text`, `Muted`,
`Track`, `Slice`, `Hover`, …) plus `AccentColor #8224D4` / `AccentColor2 #B48AE0`, `Error #F05A5A`,
`Success #3CC36E`. Dark and light are the **same accent**, different surfaces — correct for a purple
identity, and the reason both themes read as one product.

Three secondary palettes sit outside it:

| File | Owns | Windows touched |
| --- | --- | --- |
| `UI/Tools/Audio/AudioTheme.cs:16,19,108–114` | orange trim handles `#F28B2E`, waveform dim, visualizer backdrop/pens | 5 audio tools |
| `UI/Tools/Video/VideoPalette.cs:18–26`, `VideoThemeTint.cs:72` | redaction colours, black/white, theme tint mapping | 7 video tools |
| on-canvas literals (`CollageView.cs:344,355`, `EditPhotosView.cs:128,140`, `AnnotateView.cs:359`) | scrims over media | collage, edit photos, annotate |

Consequence: **light/dark parity is guaranteed only for `Theme` chrome** (F-15). The media overlays are
deliberately theme-invariant (they sit on the photo), so they are not defects; the audio orange and the
video redaction palette are, because they mix with `Theme` chrome in the same window.

### 3.2 Type, spacing, icon, motion scales

| Scale | State | Evidence |
| --- | --- | --- |
| Type | **none** — 19 distinct `FontSize` values: 9, 10, 10.5, 11, 11.5, 12, 12.5, 13, 13.5, 14, 15, 16, 18, 19, 20, 24, 26, 48 | grep over `src/FileAction.App/**/*.cs` |
| Spacing | **none** — margins/paddings are free literals 2, 4, 5, 6, 8, 10, 11, 12, 14, 16, 18, 26 | `ToolWindow.cs:322,350`, `Controls.cs:83,110,263`, `ToolControls.cs:54,167` |
| Icon | **none** — `Theme.Icons` glyphs at 9, 10, 11, 12, 13, 14, 16, 18, 20 px | grep `FontFamily = Theme.Icons` |
| Motion | **tokenised** — `UI/Motion/Motion.cs`, documented in `docs/MOTION.md` | PASS |

Motion is the one complete scale and the model the other three should follow: one file, named tokens,
no surface hard-codes a duration. The same move is needed for spacing (`Metrics.Gutter = 18`,
`Metrics.RowGap = 8`, …), type (`Type.Title = 15`, `Type.Body = 12.5`, …) and icons
(`Icon.Small = 12`, `Icon.Inline = 16`, `Icon.Card = 20`).

### 3.3 Two implementations of the same control

`Controls.Segmented` (`Controls.cs:90–133`) and `ToolControls.Chips` (`ToolControls.cs:142–193`) build the
same pill row with **different metrics**:

| Property | `Controls.Segmented` | `ToolControls.Chips` |
| --- | --- | --- |
| pill padding | `12,5,12,6` (`:110`) | `11,5,11,6` (`:167`) |
| label size | `12.5` (`:106`) | `12` (`:163`) |
| pill margin | none | `0,0,4,0` (`:168`) |
| corner radius | 7 (`:109`) | 7 (`:166`) |

Same story for cards: `Controls.Card` pads `18,14,18,14` (`Controls.cs:83`), `ToolControls.Card` pads
`12,10,12,10` (`ToolControls.cs:54`). A user sees two "chips" and two "cards" that are 1 px / 0.5 pt
apart for no reason (F-16).

## 4. The wheel

Source: `Wheel/WheelWindow.cs`, `Wheel/WheelController.cs`, `WheelEditorWindow.cs`. Captures:
`docs/screenshots/motion/wheel-dark-poster.png`, `wheel-light-poster.png` (2.4.0, purple).

| Property | Observation | Verdict |
| --- | --- | --- |
| Identity | selected slice filled with `Theme.AccentGradient` (`WheelWindow.cs:1004`), hub icon `Theme.Accent` (`:924`), unselected `Theme.Slice` + `SliceEdge`; both themes | **PASS** — matches the purple identity in both posters |
| Anatomy | outer disc, `n` equal slices, hub with file icon + operation name + file name, modifier chips `Shift/Ctrl/Alt`, mode tabs in the hub track (`:58–59, 123, 130`) | **PASS** |
| Read order | title above subtitle inside the slice; subtitle muted (`:1010`); two-level hierarchy matches the reference chips | **PASS** |
| Depth/motion | 150 ms open with 2.6 % overshoot, 18 ms slice stagger, 100 ms highlight, 90 ms press (`docs/MOTION.md` §Wheel) | **PASS** |
| RTL | disc pinned LTR, only text and tabs mirror (`WheelWindow.cs:110–114, 300`) — correct: mirroring the disc would move every slice | **PASS** |
| Iconography | slices are **text-only** in both 2.4.0 captures; the retired `docs/screenshots/en/wheel-image-tools.png` shows per-slice glyphs | **needs CI screenshot** — if glyphs are gone by design, PASS; if lost in the 2.4 refactor, a discoverability regression (F-23) |
| Mode affordance | 3 wheels (Convert / Tools / Actions) switch on Tab, modifier key or tab click (`WheelWindow.cs:617`, `:966`); **no tab strip is visible in either shipped capture** | **needs CI screenshot** of click-mode; if tabs are drag-mode-hidden, switching has no visible affordance (F-23) |
| Labels | slice label + subtitle, e.g. `PNG` / `PDF · document`; format names stay Latin in `ar` (correct, matches `docs/glossary-ar.md` conventions) | **PASS** |
| Empty/error | the wheel cannot open without a file; a blocked drop target shows `wheel.saveBlocked` (`WheelController.cs:182`) | **PASS** |

The wheel is the strongest surface in the product. Its only open questions are evidence questions, not
design questions.

## 5. Shared chrome: title bar and action bar

`UI/Tools/ToolWindow.cs` — the single chrome every tool inherits.

| Element | Implementation | Verdict |
| --- | --- | --- |
| Card | `CornerRadius=20`, `BorderThickness=1`, 12 DIP outer margin, 28 blur / 6 depth / 0.35 shadow (`:244–258`) | **PASS** — matches `crop.png` |
| Backdrop | translucent panel `#E6` over `Theme.Panel` (`:228`) + DWM system backdrop (`:455–469`) | **PASS** — theme-aware |
| Title | 15 px semibold, centred, `Theme.Text` (`:312–320`) | **PASS** |
| Close | 32×32 circle, `Theme.Track` fill, 12 px `\uE711` (`:291–308`) | **PASS** with defects — no hover/focus state, shares the drag surface (F-07), not focusable (F-01) |
| Title-bar gutter | `18,16,18,10` (`:322`) | **PASS** |
| Content gutter | **none** — `_content.View` is added to row 1 with no margin (`:238, 241`) | **FAIL** (F-03) |
| Action bar | `18,8,18,18` (`:350`), `Reset` docked left, primary right (`:351–354`) | **PASS** — matches the reference and mirrors correctly in RTL |
| Primary/secondary | `Controls.Button(primary: true)` = `AccentGradient` + `TextOnAccent` + SemiBold 13; secondary = `Card` + `PanelBorder` + Normal 13; both `Padding 14,6,14,7`, radius 8 (`Controls.cs:242–267`) | **PASS** — two weights, one shape |
| Disabled | no style; `IsEnabled` is the only gate and changes nothing visually (`Controls.cs:256–268`) | **FAIL** (F-08) |
| Keyboard | `Esc` closes, `Enter` applies when the gate allows (`ToolWindow.cs:111–122`) | **FAIL** (F-02, F-01) |
| Long operation | primary disables, then either a success toast + 120 ms close or an error toast (`:369–395`) | **FAIL** — no progress, no cancel (F-19) |

### 5.1 Gutter evidence, measured on shipped captures

`tool-crop-light-poster.png` (480×508): the card spans x=10–468; the preview begins at x=15 — **5 px**
from the card edge — while the primary button ends at x=454 — **14 px** from the card edge. Same chrome,
same window, same capture: the content sits roughly 3× closer to the edge than the actions that are
supposed to share its gutter, and the `W` glyph is clipped by the rounded corner. In
`tool-addbackground-light-poster.png` (423×648) the same chrome insets its preview and control rows by a
clear margin — two windows, two gutters. That is F-03, measured without a desktop session.

*(Measured read-only with PIL on the committed posters; the numbers are capture pixels, not DIP, so only
ratios inside one capture are claimed.)*

## 6. Theme parity (purple dark / light)

- Chrome parity is structural, not duplicated: one `Palette` record, two instances, every brush frozen
  once (`Theme.cs:13–59, 150–157`). No window re-declares a chrome colour, so a token edit lands in both
  themes at once. **PASS.**
- Window `FontFamily`/`Foreground` are set from the theme at construction (`ToolWindow.cs:89–90`), and
  `Theme.Refresh()` runs before each window is built (`:88`), so a theme switch applies to the next window.
- Live repaint while a tool is open exists only for the audio family (`AudioTheme.Watch/Repaint`) and the
  video family (`VideoThemeWatcher`). **A tool window from any other family does not repaint if Windows
  flips light/dark while it is open** — it keeps the palette it was built with until it is reopened
  (F-20).
- Visual proof exists for 5 tools only (`docs/screenshots/motion/tool-{crop,addbackground,annotate,
  editphotos,redact}-{light,dark}-poster.png`). The other 21 are **PASS (static)**, not captured; parity
  claims for them are code-level, not pixel-level.

## 7. Typography

The reference has one ramp. We have 19 sizes. Distribution of literal `FontSize` assignments:

| 11.5 | 12 | 12.5 | 13 | 11 | 14 | 10 | 10.5 | 13.5 | 15 | 16 | others |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 56 | 49 | 44 | 21 | 16 | 10 | 7 | 6 | 6 | 6 | 3 | 10 |

Same role, different sizes: a chip label is `12.5` in `Controls.Segmented` and `12` in
`ToolControls.Chips`; body text defaults to `14` (`Controls.Text`), a hint is `11.5`
(`ToolControls.Label(hint, 11.5)`), a caption is `12`, a text box is `13`. The user sees 11.5, 12, 12.5
and 13 in one window (`tool-annotate-light-poster.png`: `Select/Arrow/Line` at 12.5, `2 4 8` at 12,
`Font` at 12, `Undo/Redo` at 12.5, `Color` at 12). It reads as four near-identical sizes rather than a
deliberate step (F-16).

Arabic shares the Latin stack: `UiFont = "Segoe UI Variable Text, Segoe UI"` (`Theme.cs:84`). Segoe UI
Variable's Arabic coverage is a fallback chain, not a designed Arabic face, and no line-height is set for
the taller `ar` ascenders/descenders. At 11.5–12.5 px this is the most likely place for clipped
diacritics — **needs CI screenshot** of any `ar` window at 150 % to confirm or clear (F-16 note). The
`ar` string table itself is complete (1095/1095 keys, identical counts).

## 8. Control order, hierarchy, states

**Order.** 25 of 26 windows read preview → controls → action bar. `annotate` reads toolbar rows → preview
(`tool-annotate-light-poster.png`), inverting the model every other surface follows, including its own
sibling `redact`. Annex row 3, fix cluster D5.

**Hierarchy.** Every window has exactly two action-bar controls, `Reset` (secondary) and one primary whose
verb names the result (`Save with Background`, `Save Redacted Copy`, `Create Collage`, `Merge PDFs`). That
is right. Three windows still ship the generic `Apply` where the result is a copy and a named string
already exists (`annotate`, `audiobleep`, `qr` — annex row 3/17/23, F-11, F-12).

**Empty states.** Only 7 tools have authored empty copy: `videoframes` ("No frames captured yet."),
`videoredact` ("No areas yet."), `annotate` ("Add at least one mark before applying."), `qr` ("No QR code
was found…" + hint), `audiotrim` ("No silence found at the edges."), plus `unlock`/`protect`
("This archive contains no files." / "Nothing to protect was found."). The other tools rely on a disabled
primary with no sentence explaining *why* it is disabled. For `videojoin`, `pdfmerge`, `pdforganize` and
`collage` — the four list-building tools — an empty list is the **first** thing a user sees, and it has no
copy (F-26).

**Error states.** Specific failures are localized (`tool.qr.copyFailed`, `tool.protect.error.empty`,
`tool.unlock.error.empty`). Everything else funnels through one catch that shows the raw exception text:
`Notifications.Show(T("app.error.title"), ex.Message, error: true)` (`ToolWindow.cs:387–390`). A Magick or
ffmpeg message is not a user-facing sentence and is not translated (F-18).

**Busy states.** `ApplyAsync` disables the primary and awaits; there is no progress, no "working…" label,
no cancel (`ToolWindow.cs:369–395`). Video trim/compress/join on a large file can take tens of seconds
with a frozen-looking window. The Jobs window exists but a tool window never points at it (F-19).

## 9. Per-window table

Legend — **Theme parity**: `PASS (cap)` = light+dark capture exists; `PASS (static)` = chrome comes from
`Theme`, no capture. **spacing/chrome**: the gutter/clipping/order verdict for that window.
`PASS/FAIL` counts any defect visible in that window (shared ids included).

| # | Tool id | en title | ar title | Theme parity | spacing / chrome | FAIL ids | PASS/FAIL |
| ---: | --- | --- | --- | --- | --- | --- | --- |
| 1 | `tools.crop` | Crop Image | قصّ الصورة | PASS (cap) | flush content, `W` clipped at card edge, no `Aspect ratio` label, no `px` | F-03, F-09 | FAIL |
| 2 | `tools.addbackground` | Add BG | إضافة خلفية | PASS (cap) | inset ok; `Background im…` truncated; units in labels | F-04 | FAIL |
| 3 | `tools.annotate` | Annotate Image | إضافة تعليقات على الصورة | PASS (cap) | controls above preview; `Font` rendered as a chip | annex r3, F-12, F-27 | FAIL |
| 4 | `tools.redact` | Redact image | حجب الصورة | PASS (cap) | inset ok; no face-detection affordance though the wheel promises it | annex r4, F-12, F-27 | FAIL |
| 5 | `tools.editphotos` | Edit Photos | تعديل الصور | PASS (cap) | 520 DIP chrome vs 436.8 DIP cap at 768p@150 % → last rows clipped | F-05 | FAIL |
| 6 | `tools.collage` | Collage | تجميع الصور | PASS (static) | 455 DIP chrome vs 436.8 DIP cap → `Order` row clips | F-05 | FAIL |
| 7 | `tools.videotrim` | Trim Video | اقتصاص الفيديو | PASS (static) | flush content (no gutter) | F-03 | FAIL |
| 8 | `tools.videocrop` | Crop Video | قصّ الفيديو | PASS (static) | flush content (no gutter) | F-03 | FAIL |
| 9 | `tools.videospeed` | Change Speed | تغيير السرعة | PASS (static) | flush content (no gutter) | F-03 | FAIL |
| 10 | `tools.videosplit` | Split Video | تقسيم الفيديو | PASS (static) | flush content (no gutter) | F-03 | FAIL |
| 11 | `tools.videojoin` | Join Videos | دمج الفيديوهات | PASS (static) | flush content; drag-only reorder, no `▲▼` | F-03, F-10 | FAIL |
| 12 | `tools.videoframes` | Save frames | حفظ الإطارات | PASS (static) | flush content; title sentence case | F-03, F-12 | FAIL |
| 13 | `tools.videoredact` | Redact video | حجب الفيديو | PASS (static) | ok; title sentence case (nit) | F-12 | PASS |
| 14 | `tools.audionormalize` | Normalize Audio | تطبيع الصوت | PASS (static) | ok (2-column body justified) | — | PASS |
| 15 | `tools.audiotrim` | Trim Audio | اقتصاص الصوت | PASS (static) | ok | — | PASS |
| 16 | `tools.audiochannels` | Audio Channels | قنوات الصوت | PASS (static) | ok | — | PASS |
| 17 | `tools.audiobleep` | Bleep Audio | استبدال الصوت بصافرة | PASS (static) | ok; primary `Apply` where a named save exists (nit) | F-12 | PASS |
| 18 | `tools.audiovisualizer` | Audio Visualizer | مُصوِّر الصوت | PASS (static) | ok | — | PASS |
| 19 | `tools.pdfmerge` | Merge to PDF | الدمج إلى PDF | PASS (static) | ok (nit: title vs primary wording) | F-12 | PASS |
| 20 | `tools.pdforganize` | Organize Pages | تنظيم الصفحات | PASS (static) | ok (nit: title wording) | F-12 | PASS |
| 21 | `tools.compress` | Compress File | ضغط الملف | PASS (static) | ok (nit: title wording) | F-12 | PASS |
| 22 | `tools.metadata` | Edit Metadata | تعديل البيانات الوصفية | PASS (static) | ok (nit: title wording) | F-12 | PASS |
| 23 | `tools.qr` | QR Code | رمز QR | PASS (static) | ok; primary now reads `Copy and Close` (F-11 fixed in `835758c`) | — | PASS |
| 24 | `tools.protect` | Password protection | الحماية بكلمة مرور | PASS (static) | ok | — | PASS |
| 25 | `tools.unlock` | Remove password | إزالة كلمة المرور | PASS (static) | ok | — | PASS |
| 26 | `tools.lock` | Lock | قفل الملفات | PASS (static) | ok; `Encrypt`/`Open` switches through `IToolActionLabel` | — | PASS |

The `PASS` rows that cite `F-12` are nits (casing/wording), not layout failures; they stay PASS because
nothing is hidden or mislabelled in a way that changes what the user can do.

## 10. FAIL list — most severe first

Severity: **P0** blocks a class of user, **P1** wrong or misleading in normal use, **P2** polish/consistency.

**Ownership (rule 3).** Every FAIL has an owner and a status. `owner: UX lane (audit/adesign, PR #31)`
means this branch; `owner: deferred -> docs/BACKLOG.md` means we are deliberately not shipping it in
2.4.0 and a one-line request has been filed with the owner; otherwise the owning lane is named.
F-17 is **not assigned** — the busy-state and error-state findings were merged as F-19 and F-18 when the
list was collapsed.

| FAIL | Sev | Owner | Status |
| --- | --- | --- | --- |
| F-01 | P0 | UX lane (audit/adesign, PR #31) | open — control-kit rewrite, D1 |
| F-02 | P1 | UX lane (audit/adesign, PR #31) | open — D2 |
| F-03 | P1 | UX lane (audit/adesign, PR #31) | open — D2 |
| F-04 | P2 | UX lane (audit/adesign, PR #31) | open — D4 |
| F-05 | P1 | UX lane (audit/adesign, PR #31) | open — D6 |
| F-06 | P2 | UX lane (audit/adesign, PR #31) | open — D12 |
| F-07 | P2 | UX lane (audit/adesign, PR #31) | open — D13 |
| F-08 | P2 | UX lane (audit/adesign, PR #31) | open — D1 |
| F-09 | P2 | UX lane (audit/adesign, PR #31) | open — D3 |
| F-10 | P2 | UX lane (audit/adesign, PR #31) | open — D10 |
| F-11 | P2 | UX lane (audit/adesign, PR #31) | **fixed** `835758c` |
| F-12 | P2 | deferred -> docs/BACKLOG.md | requested (string owner; append-only for this lane) |
| F-13 | evidence | deferred -> docs/BACKLOG.md | needs `--self-test` (CI only) |
| F-14 | process | deferred -> docs/BACKLOG.md | brief/reference housekeeping |
| F-15 | P1 | UX lane (audit/adesign, PR #31) | open — D15 |
| F-16 | P1 | UX lane (audit/adesign, PR #31) | open — D16 |
| F-18 | P1 | UX lane (audit/adesign, PR #31) | open — error surface |
| F-19 | P1 | UX lane (audit/adesign, PR #31) | open — busy/cancel |
| F-20 | P2 | UX lane (audit/adesign, PR #31) | open — live theme repaint |
| F-21 | P2 | UX lane (audit/adesign, PR #31) | open — token naming |
| F-22 | P2 | docs lane (audit/adocs) | open — stale `docs/screenshots/en/wheel-*` |
| F-23 | P2 | UX lane (audit/adesign, PR #31) | open — needs CI screenshot of click-mode wheel |
| F-24 | P2 | UX lane (audit/adesign, PR #31) | open — D16 |
| F-25 | P2 | UX lane (audit/adesign, PR #31) | open — icon tokens |
| F-26 | P2 | deferred -> docs/BACKLOG.md | needs new strings (string owner) |
| F-27 | P1 | UX lane (audit/adesign, PR #31) | **fixed upstream** `4f23c9c`; guard `6f6c81d` |
| F-28 | P1 | UX lane (audit/adesign, PR #31) | **fixed here** (§11) |

### P0

**F-01 — No tool control is focusable, tabbable or announced** · P0 · annex §5 F-01
Every `Controls`/`ToolControls` builder returns a `Border`/`StackPanel` with `MouseLeftButtonUp` and no
`Focusable`, `IsTabStop`, focus visual or `AutomationProperties.Name` (`Controls.cs:90–133, 136–172,
175–236, 239–275`; `ToolControls.cs:80–136, 142–193`). Tab reaches text boxes only; Space/arrows do
nothing; a screen reader sees an empty window. 26 windows, zero keyboard-operable controls.
Screenshot: `docs/screenshots/motion/tool-crop-light-poster.png` (all controls are mouse-only shapes).
Fix: D1.

### P1

**F-28 — Status-badge text fails WCAG AA in light mode (and the error badge in dark)** · P1 · **fixed here**
`Controls.Badge` painted `Theme.Success` / `Theme.Error` text on a 15 % (`0x26`) tint of the same hue over
`Theme.Card` (`Controls.cs:286–292`). Measured WCAG 2.1 contrast (relative-luminance formula, tint
composited over the card):

| pair | light | dark |
| --- | ---: | ---: |
| success text on its badge | **2.02:1** (2.28:1 against pure white) | 5.32:1 ✔ |
| error text on its badge | **2.79:1** (3.33:1 against pure white) | **3.94:1** ✘ |

The text is 11.5 px semibold — body text, so the requirement is **4.5:1**, not the 3:1 large-text tier
(large means ≥ 24 px, or ≥ 18.66 px bold). Fix: new tokens `Theme.BadgeSuccessText` /
`Theme.BadgeErrorText` (`Theme.cs`, theme-aware) resolve to `#3CC36E` / `#FF9A9A` in dark and
`#17703C` / `#A61B1B` in light → **5.32:1 / 6.45:1 dark, 5.44:1 / 6.31:1 light**. `Theme.Success` /
`Theme.Error` stay the mark colours for icons, strokes and the lock badge, so no other surface changes.
Screenshot: **needs CI screenshot** of a badge in each theme (no shipped capture contains a
`Controls.Badge`). Owner: UX lane (audit/adesign, PR #31).

**F-27 — Two 2.4.0 tool windows were unreachable from the wheel** · P1 · **fixed upstream (`4f23c9c`); guarded here**
`WheelController.BuildOperations()` (`Wheel/WheelController.cs:88–116`) listed 25 operations and omitted
`AnnotateOperation` and `RedactOperation` (declared at `UI/Tools/Image/AnnotateTool.cs:60` and
`UI/Tools/Image/RedactTool.cs:73`, constructed nowhere in `src`). Both tools were therefore dead windows:
registered, localized, tested — and unopenable. 24 of 26 windows were reachable. The two operations were
added upstream in `4f23c9c` while this lane was in review; this branch keeps the durable guard
(`ToolDispatchTests`) that fails if a future tool is registered without a wheel entry. Note: a second
`tools.ocr` registration exists in `src/FileAction.Linux/TesseractOcr.cs:15` and is **not**
Windows-shipped, so it is out of scope here. Screenshot: `tool-annotate-light-poster.png` — a window that
no shipped path could open before the fix.

**F-02 — `Enter` inside a text field also saves the file; `Esc` is swallowed by time boxes** · P1 · annex F-02
`ToolWindow.cs:117–122` applies on any `Enter`; `CropTool.cs:243–247` commits W/H on `Enter` without
`e.Handled`; `VideoControls.cs:111–123` consumes `Enter` and `Esc`. Typing a width then pressing `Enter`
writes a file. Screenshot: `docs/screenshots/motion/tool-crop-light-poster.png` (`W`/`H` are real text
boxes). Fix: D2.

**F-03 — The content row has no gutter, and the seven media views therefore sit on the card edge** · P1 · annex F-03
`ToolWindow.cs:238, 241` adds `_content.View` with no margin while the title bar (`:322`) and action bar
(`:350`) are inset 18. Visible: `tool-crop-light-poster.png` — preview and `W`/`H` flush, `W` clipped by
the corner; `tool-addbackground-light-poster.png` — the same chrome, inset ~14 px. Same product, two
gutters. Fix: D2 (+ F-24 tokens).

**F-05 — Two windows cannot fit their own controls at 1366×768 @150 %, and 12 views cannot scroll** · P1 · annex F-05
`editphotos` declares 520 DIP of chrome (`EditPhotosTool.cs:28`), `collage` 455 (`CollageTool.cs:21`); the
sizer caps at 436.8 DIP (`ToolWindowSizer.cs:32, 47–54`). 12 more views have no vertical scroller.
Screenshot: needs CI screenshot at 1366×768 @150 % (no such capture exists).

**F-15 — Colour tokens are split across three palettes plus inline literals** · P1 (new)
`Theme` chrome + `AudioTheme` (orange `#F28B2E`, own visualizer stage) + `VideoPalette`/`VideoThemeTint`
+ on-canvas literals. Light/dark parity is only structurally guaranteed for `Theme`. Evidence:
`AudioTheme.cs:16,19,112–114`; `VideoPalette.cs:18–26`; `VideoThemeTint.cs:72`; `Theme.cs:13–43`.
Screenshot: `tool-addbackground-light-poster.png` vs `tool-addbackground-dark-poster.png` shows `Theme`
parity holds for chrome; no capture exists for the audio orange in light theme → needs CI screenshot.
Fix: new cluster **D15** — fold the two extra content colours into `Theme`.

**F-16 — No type scale, and the same control role is 12/12.5/13/14 px in different windows** · P1 (new)
19 literal sizes; `Controls.Segmented` chip 12.5 vs `ToolControls.Chips` chip 12; body default 14
(`Controls.cs:16`), hint 11.5 (`ToolControls.cs:67`), caption 12 (`:15`), box 13 (`:30`). Evidence and
distribution in §7. Screenshot: `tool-annotate-light-poster.png` (12.5 chips, 12 chips, 12 caption in one
window). Fix: new cluster **D16** — `Type`/`Metrics`/`Icon` tokens.

**F-18 — The generic error path shows a raw exception message** · P1 (new)
`ToolWindow.cs:387–390` — `Notifications.Show(T("app.error.title"), ex.Message, error: true)`. Untranslated
engine text, no recovery action, no "nothing was changed" reassurance. Three tools have proper localized
errors; the other 23 do not. Screenshot: needs CI screenshot (error toast is not captured).
Fix: map failure classes to `tool.error.*` strings and keep the exception for the log.

**F-19 — No in-window busy, progress or cancel state** · P1 (new)
`ToolWindow.cs:369–395`: disable primary → await → close or toast. Multi-second video/PDF work looks
frozen; the user cannot cancel; the Jobs window is not referenced. Screenshot: needs CI screenshot of a
long apply. Fix: busy label + indeterminate bar in the action bar, and a cancel token into `Apply`.

### P2

**F-04 — `Background image` is truncated and units live in labels** · P2 · annex F-04
`tool-addbackground-light-poster.png` shows `Background im…`; labels read `Padding (px)`, `Corners (px)`,
`Shadow (px)` while the values `160 / 64 / 40` carry no unit. Reference: label plain, value with unit.
Fix: D4.

**F-06 — RTL slider fill is not mirrored** · P2 · annex F-06
`Controls.cs:179–213` paints a left-aligned fill and a linear hit-test; under `RightToLeft` the row mirrors
but the fill still grows left→right. Needs CI screenshot in `ar`. Fix: D12.

**F-07 — The `×` shares the drag surface** · P2 · annex F-07
`ToolWindow.cs:309` closes on mouse-up; `:326–330` starts `DragMove()` for any press in the title bar,
including on `×`. A 1 px drag becomes a window drag instead of a close. Screenshot:
`tool-crop-light-poster.png` (close button position). Fix: D13.

**F-08 — Disabled controls look enabled** · P2 · annex F-08
`Controls.Button` sets static brushes and no disabled visual (`:242–268`); `IsEnabled` gates 20+ call sites
including the primary action (`ToolWindow.cs:342–346`). Screenshot:
`tool-annotate-light-poster.png` — `Undo`/`Redo`/`Delete` look identical to enabled chips. Fix: D1.

**F-09 — Chip rows do not wrap and clip at 336 DIP** · P2 · annex F-09
`CropTool.cs:76`, `ToolControls.cs:147` use a horizontal `StackPanel`; at 1366×768 @150 % the crop window
is 336 DIP and the six chips need ≈340–376 DIP. Screenshot: `tool-crop-light-poster.png` shows the row
ending exactly at the card edge. Fix: D3.

**F-11 — QR's primary action is labelled `Apply` but copies and closes** · P2 · annex F-11 — **fixed here**
`QrTool` inherited `tool.window.apply` while `QrView.ApplyAsync` copies the text and closes
(`QrView.cs:350–356`), next to an explicit `Copy` button (`:84`). Now labelled `Copy and Close` (§11).
Fix: D11.

**F-12 — Title casing and primary verbs are inconsistent** · P2 · annex F-12 — **OPEN (not changed here)**
Three sentence-case titles (`Redact image`, `Redact video`, `Save frames`) and two more
(`Password protection`, `Remove password`) beside Title Case everywhere else. The string tables are
append-only for this lane, so the fix is **requested, not made**: it needs an owner-approved `en`-only pass
(and a matching `ar` review). The primary-verb pass (`Apply` → named copy) is open for the same reason.
Fix: D9.

**F-20 — Only audio and video windows repaint on a live light/dark switch** · P2 (new)
`Theme.Refresh()` runs when a window is built (`ToolWindow.cs:88`); live repaint is wired only through
`AudioTheme.Watch/Repaint` (`AudioTheme.cs:25–102`) and `VideoThemeWatcher`. A crop window left open while
Windows flips theme keeps the old palette. Screenshot: needs CI screenshot (theme flip with a window
open). Fix: subscribe every tool view once, in `ToolWindow`.

**F-21 — `AccentGradient` is not a gradient** · P2 (new)
`Theme.cs:66–67`: `new LinearGradientBrush(AccentColor, AccentColor, …)` — the same colour twice, while the
comment and `AccentColor2` (`:64`) promise "violet → lavender". Every primary button, selected chip and
wheel highlight is flat. Not a visual bug (flat is closest to the reference) — a token lie. Fix: rename to
`AccentFill`, or make it a real two-stop gradient that keeps white text ≥ 4.5:1.

**F-22 — Shipped wheel screenshots are pre-2.4.0 and the wrong colour** · P2 (new)
`docs/screenshots/en/wheel-image-tools.png`, `wheel-actions.png`, `wheel-light.png`,
`wheel-video-convert.png` show an **orange** accent, different slice glyphs and a different hub from the
2.4.0 purple wheel in `docs/screenshots/motion/wheel-dark-poster.png`. Two contradictory identities in one
repo. Screenshot: `docs/screenshots/en/wheel-image-tools.png` vs
`docs/screenshots/motion/wheel-dark-poster.png`. Fix: regenerate with the `screenshots` workflow or delete.

**F-23 — Wheel mode switching has no visible affordance in either shipped capture** · P2 (new)
Three wheels (`WheelOrder`, `WheelWindow.cs:40`) switch on Tab/modifier/tab-click (`:617, :966`); no tab
strip is visible in `wheel-dark-poster.png` or `wheel-light-poster.png` although `_tabs` is built into the
hub (`:58–59, 123, 130`). Screenshot: needs CI screenshot of click-mode wheel; if tabs are drag-only
hidden, add them.

**F-24 — Spacing is free-form; the gutter defect is the symptom** · P2 (new)
No spacing token file; literals 2,4,5,6,8,10,11,12,14,16,18,26 across the chrome and the two control kits
(`ToolWindow.cs:322,350`; `Controls.cs:83,110,263`; `ToolControls.cs:54,167`). Fix: `Metrics` tokens
(4/8/12/18/26) applied to `ToolWindow` + `Controls` + `ToolControls` first (D16).

**F-25 — Icon sizes 9–20 px with no scale, glyphs inline in views** · P2 (new)
`Theme.Icons` glyphs are set at 9,10,11,12,13,14,16,18,20 px across the app (`FontSize = 9` in status
chips, `20` on cards, `12` in the close button). Literal `\uE…` strings live in the views (`QrView.cs:84`,
`ToolControls.cs:84`, `ToolWindow.cs:299`). Screenshot: `tool-addbackground-light-poster.png` (16 px icons
in `Choose image…`, 12 px in the toggle row). Fix: `Icon` size tokens + one `Glyphs` table.

**F-26 — Four list-building tools have no empty-state copy** · P2 (new)
`videojoin`, `pdfmerge`, `pdforganize`, `collage` open on an empty list with a disabled primary and no
sentence explaining what to add; only 7 of 26 tools author empty copy (`tool.videoframes.empty`,
`tool.videoredact.empty`, `tool.annotate.empty`, `tool.qr.empty` + hint, `tool.audiotrim.silence.none`).
Screenshot: needs CI screenshot of `pdfmerge` with no rows. Fix: one `tool.*.empty` + hint per list tool.

### Evidence / process (inherited)

**F-13 — `SIZES.md` is not reproducible** · evidence · annex F-13 — regenerating it needs `--self-test`
(CI only), so this lane cannot close it. **F-14 — reference paths named by the brief do not exist**
(`_local\video-frames`, `docs\screenshots\tools`) · process · annex F-14.

## 11. What this lane changed

Additive string, docs and tests only. No layout, behaviour, pixel value or existing string VALUE was
changed; anything that needs a rendered window to verify is listed in §12 instead of guessed at.

| Commit | Change | Closes |
| --- | --- | --- |
| `docs(audit): static design audit…` | committed the annex as delivered | — |
| `docs(audit): UX audit — wheel + 26 tool windows` | this document | — |
| `fix(qr): label the read-only primary 'Copy and Close'` | **new** `tool.qr.copyClose` in `en`+`ar` (additive), `QrTool.PrimaryLabelKey`; behaviour unchanged (already copied + closed) | F-11 |
| `test(ux): bilingual titles and primary labels for every tool` | 2 deterministic string-contract facts, §13 | string-table regressions |
| `test(wheel): every registered tool window has a dispatchable operation` | 2 deterministic dispatch facts, §13 | F-27 regressions |
| `fix(a11y): readable status-badge text in both themes` | `Theme.BadgeSuccessText` / `Theme.BadgeErrorText` (theme-aware tokens) and `Controls.Badge` consuming them; no string, layout or behaviour change | F-28 |

The wheel wiring itself is **not** in this diff: it landed upstream as `4f23c9c` while the PR was in
review, so this branch ships only the guard that keeps it true.

## 12. Deliberately NOT fixed here

These are real but cannot be verified without a rendered window, and a blind edit would be a guess:

1. **The 18 DIP gutter (F-03).** Adding `Margin(18,0,18,0)` to the content row would double-inset the
   four views that already pad 18 and narrow the seven flush views by 36 DIP — which at 336 DIP would make
   F-09 clipping *worse*. It needs the CI capture matrix (100/125/150 %, both themes) to land safely.
2. **Chips wrap + minimum window width (F-09).** `WrapPanel` changes row height, which changes the chrome
   height constants that `ToolWindowSizer` trusts — needs the same capture pass.
3. **Scroll containers at 150 % (F-05).** Structural change to 14 views.
4. **Focus/automation (F-01).** Cross-cutting control-kit rewrite; it is the right next PR but it is not a
   token edit.
5. **Disabled/busy/error surfaces (F-08, F-18, F-19).** Behaviour plus new strings, and three views already
   hand-roll their own disabled opacity (`AudioNormalizeTool.cs:450`, `AudioTrimTool.cs:427`,
   `AudioBleepTool.cs:404`); fixing it centrally requires removing those one-offs in the same change.

## 13. Tests added (cheap, deterministic, no UI)

`tests/FileAction.Tests/UxContractTests.cs` — two facts, all pure data:

1. **Every registered tool resolves a title and a primary label in both languages.** Touches
   `ToolRegistry.All` (26 entries), asserts non-empty `Loc.T(spec.TitleKey)` / `Loc.T(spec.PrimaryLabelKey)`
   in `en` and `ar`, that neither returns the key itself, ids are unique and `tools.`-prefixed, and
   `Layout.ChromeHeight > 0`. This catches the classic regression: a new tool that forgets a string and
   ships a window titled `tool.foo.title`.
2. **No two tools share a display title** — two windows with the same title are indistinguishable in the
   toast, the job list and the accessibility tree.

`tests/FileAction.Tests/ToolDispatchTests.cs` — two facts, pure data:

3. **Every registered `IToolWindowSpec.Id` is present in `WheelController.BuildOperations()` and its
   operation is an `IInteractiveTool`.** Matched by identity (operation id == tool id), never by
   `ToolRegistry.For(items)`, which is first-match and ambiguous. This is the guard that would have caught
   F-27 before it shipped, and it is the reason PR #31 must merge.
4. **Wheel operation ids are unique** — `LastOperationId` and the wheel editor key on the id.

All four are O(26), no file I/O beyond the embedded string tables, no WPF window, no media, no timing.
They run in the existing suite on every push and are part of the `build-test` job. Title Casing across the
existing labels is **not** enforced here: the string tables are append-only for this lane, so that rule is
requested as a separate, owner-approved pass (§11, F-12).

## 14. Acceptance for the next lane

1. `--self-test` capture matrix: all 26 tools × {100, 125, 150 %} × {light, dark} × {en, ar}. That single
   artifact set unblocks F-03, F-05, F-06, F-09, F-20, F-23, F-26 and the 21 "PASS (static)" parity rows.
2. Land D1 (focus/automation) before the tag: it is the only P0 and it affects every window.
3. Land D2 (`Enter`/`Esc` routing + one gutter) together — they touch the same two files.
4. Retire `docs/screenshots/en/wheel-*.png` or regenerate them from the same self-test that produced
   `docs/screenshots/motion/*` (F-22).
5. Keep the four contract tests green as tools are added; they are the cheap net under the string
   table, the registry and the wheel dispatch.

*Static audit; no executable was run by this lane. Where a lookup needed a rendered window the row says
"needs CI screenshot" rather than inventing a pixel value.*
