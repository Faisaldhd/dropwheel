# DropWheel 2.4.0 — Localization & RTL audit

Lane: **TEAM B lane 2 (Localization / RTL)** · Branch `audit/al10n` · Base `origin/main 268a2d7`
Scope: all 1,094 string keys in `src/FileAction.Core/Strings/en.json` + `ar.json`, every WPF window and
tool window, the wheel, and bidi handling of version numbers / mixed Latin–Arabic runs.

## Headline

- **Key parity: 1,094 / 1,094** — exact both directions, no key only in one table, 0 empty, 0 raw-key
  values, 0 untranslated prose, placeholder parity clean.
- **Append-only: 0 keys removed** — v2.1.0 263 → v2.2.0 346 → v2.3.0 369 → 2.4.0 **1,094** (725 added
  in 2.4.0, none removed at any tag).
- **Versions isolated:** 11/11 version-bearing Arabic keys wrap `{0}` in `U+2066 … U+2069`.
- **RTL:** 17/17 windows and 4/4 shared surfaces set RTL at construction. One real defect found and
  fixed (F-01, two checkboxes).
- **Release-claim warning (F-02):** CI does **not** label-assert the tool windows in Arabic. The honest
  claim is “**rendered in Arabic: wheel, Settings (all six tabs), shelf, jobs, wheel editor, parameter
  dialog**”. **No release note may claim “every tool window verified in Arabic”** — 25 of 26 tool
  windows are rendered only in English (motion-measured, not language-verified) and the Lock/Protect/
  Rescuer dialogs are not rendered at all. Details in §5.2 and F-02.

**Verdict: PASS with follow-ups.** One RTL defect fixed; F-02 is an evidence/claim gap, not a string
defect.

No application binary, installer, `--self-test` or `--diagnose` was executed locally (company laptop).
Everything below is static analysis, JSON parsing, `git` and CI.

---

## 1. Evidence inventory

| # | Source / check | What it establishes |
|---|---|---|
| E-01 | `src/FileAction.Core/Strings/en.json` (1,094 keys) | English source of truth |
| E-02 | `src/FileAction.Core/Strings/ar.json` (1,094 keys) | Arabic table |
| E-03 | `FileAction.Core/Loc.cs:196` `Table(Language)` | the same parser the app ships (embedded JSON, `JsonDocument`, no reflection) |
| E-04 | `tests/FileAction.Tests/LocalizationTests.cs:135-396` | pre-existing parity/placeholder/plural/bidi guards |
| E-05 | `tests/FileAction.Tests/LocaleUsageTests.cs:68` | every key used in code exists in both tables |
| E-06 | `tests/FileAction.Tests/LocalizationAuditTests.cs` *(new, this PR)* | the machine checks in §2, §3, §5.5 — runs on every push |
| E-07 | `git show v2.1.0…v2.3.0:src/…/en.json` + `ar.json` | historical append-only proof (§3) |
| E-08 | `UI/LanguageManager.cs:20-24,69`, `UI/UiSetup.cs:17` | the single RTL switch every window uses |
| E-09 | `UI/Tools/ToolWindow.cs:91,104` | tool-window base sets RTL for all 26 tools |
| E-10 | `UI/Bidi.cs:19-27` | runtime bidi isolation helper (`Bidi.Value` / `Bidi.Number`) |
| E-11 | 134 `Bidi.Value`/`Bidi.Number` call sites (grep) | dynamic technical values are isolated at runtime |
| E-12 | `SelfTest.cs:118-194,449-483` | what the CI screenshot job actually renders |
| E-13 | `docs/ar-strings-review.md`, `docs/glossary-ar.md` | translator conventions used as the review rubric |

Not run locally: any build, test, exe, installer, self-test, diagnose, registry or signing operation.
The new test and the product fix are verified by the `build-test` CI job only.

---

## 2. Key parity (both directions, per group)

Method: `ar.keys == en.keys` as ordered sets, plus per-key checks. All data below is from E-01/E-02 and
is re-asserted on every push by E-06 `Tables_have_exact_key_parity_in_both_directions`,
`No_key_exists_in_only_one_table`, `No_value_is_empty_or_a_raw_localization_key`,
`No_user_facing_prose_is_left_untranslated`, `Placeholder_multisets_match_per_key`.

| Group (required) | en | ar | only en | only ar | empty | raw-key value | untranslated prose | placeholder mismatch |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `settings.` (General/Wheels/Quality/Integration/Updates/About) | 111 | 111 | 0 | 0 | 0 | 0 | 0 | 0 |
| *of which* `settings.about.` (the "about" group; no top-level `about.` exists) — subset of `settings.` | 6 | 6 | 0 | 0 | 0 | 0 | 0 | 0 |
| `tool.` (25 tools + protection flows) | 742 | 742 | 0 | 0 | 0 | 0 | 0 | 0 |
| `error.` | 39 | 39 | 0 | 0 | 0 | 0 | 0 | 0 |
| `update.` | 17 | 17 | 0 | 0 | 0 | 0 | 0 | 0 |
| `jobs.` + `job.` | 1 + 12 | 1 + 12 | 0 | 0 | 0 | 0 | 0 | 0 |
| all other groups (`action`, `app`, `catalog`, `editor`, `fileType`, `help`, `hotkey`, `items`, `language`, `ocr`, `param`, `preview`, `sheet`, `shelf`, `suffix`, `toast`, `tray`, `wheel`) | 172 | 172 | 0 | 0 | 0 | 0 | 0 | 0 |
| **Total** | **1,094** | **1,094** | **0** | **0** | **0** | **0** | **0** | **0** |

Rules enforced mechanically:

1. **Exact key parity** — the two key lists, sorted ordinally, must be equal.
2. **No empty values** — no null/whitespace value, no U+FFFD replacement character.
3. **No raw key rendered** — no value matches `^[a-z][A-Za-z0-9]*(\.[A-Za-z0-9]+){2,}$`
   (the class of bug that shipped `settings.title` in 2.2.0). Runtime enforcement already exists in
   `SelfTest.SaveWindow` → `CheckNoRawKeys` (`SelfTest.cs:405-412,522`) for every rendered surface.
4. **No untranslated Arabic prose** — an Arabic value may equal English only if it is in the 17-entry
   `EqualByDesign` allow-list, and every other Arabic value must contain at least one Arabic letter.
   The allow-list is exactly: `PDF`, `Markdown`, `Word`, `PowerPoint`, `Excel`, two hotkey names,
   `English`, `Windows`, five crop aspect ratios, and `العربية` (the Arabic word for Arabic).
   There is **no English prose left in the Arabic table**.
5. **Placeholder parity** — `{0}`, `{1}`, … multisets match per key, including multiplicity.
   The only allowed difference is Arabic CLDR `zero`/`one`/`two` plural forms, which intentionally drop
   `{0}` (`items.count.*`, `job.doneCount.*`: “عنصر واحد”, “عنصران”). They may never invent a placeholder
   the group does not have.
6. **Arabic-Indic digits are forbidden** — 0 values contain U+0660–0669 or U+06F0–06F9; counts, sizes,
   versions and percentages are Western `0-9` (also asserted by `LocalizationTests.Arabic_plural_digits_are_western`).

---

## 3. Append-only rule

**Rule.** A released key is never removed or renamed. A new string is added to `en.json` **and**
`ar.json` in the same change. If wording must change, the value changes, never the key. If a key is
truly dead it stays (harmless) until a major cleanup that also updates this document; the shipped
tables are append-only between releases.

**Exact checks that enforce it** (CI, every push):

| Check | File | Fails when |
|---|---|---|
| `No_english_key_was_removed_from_the_arabic_table` | `LocalizationAuditTests.cs` | any key in `en.json` is missing from `ar.json` |
| `No_key_exists_in_only_one_table` | `LocalizationAuditTests.cs` | symmetric difference non-empty (a key added to one table only) |
| `Tables_have_exact_key_parity_in_both_directions` | `LocalizationAuditTests.cs` | sorted key lists differ, or group floors (`settings`, `tool`, `error`, `update`, `job`, `about`, …) are missing |
| `Every_key_used_in_source_exists_in_both_tables` | `LocaleUsageTests.cs:68` | a call site uses a key that one table lacks |

**Historical proof** (E-07 — every released tag vs this branch):

| Tag | en keys | ar keys | keys present at the tag but missing now |
|---|---:|---:|---:|
| v2.1.0 | 263 | 263 | **0** |
| v2.1.1 | 289 | 289 | **0** |
| v2.2.0 | 346 | 346 | **0** |
| v2.2.1 | 346 | 346 | **0** |
| v2.2.2 | 347 | 347 | **0** |
| v2.2.3 | 352 | 352 | **0** |
| v2.3.0 | 369 | 369 | **0** |
| 2.4.0 (`audit/al10n`) | 1,094 | 1,094 | — |

2.4.0 **added 725 keys to both tables** (369 → 1,094) and removed none. This audit adds **0 new keys**
and renames/deletes none.

**Keys present in only one table: none.** Note for future work: the schema deliberately has no
top-level `about.` group — About strings live under `settings.about.*`.

---

## 4. Version numbers and bidi isolates

Rule: in Arabic, a version (or any interpolated technical run) must be wrapped in
`U+2066 LRI … U+2069 PDI` so `2.4.0` cannot reorder inside the sentence. Mechanically verified for all
11 version-bearing keys by `Version_interpolations_are_wrapped_in_bidi_isolates`:

| Key | Arabic value | Placeholder isolated |
|---|---|:--:|
| `settings.about.version` | `الإصدار ⁦{0}⁩` | ✔ |
| `settings.updates.available` | `يتوفر تحديث: ⁦{0}⁩` | ✔ |
| `settings.updates.current` | `الإصدار الحالي: ⁦{0}⁩` | ✔ |
| `settings.updates.lastChecked` | `آخر تحقّق: ⁦{0}⁩` | ✔ |
| `settings.updates.upToDate` | `لديك أحدث إصدار (⁦{0}⁩)` | ✔ |
| `update.available.body` | `DropWheel ⁦{0}⁩ – انقر هنا للتثبيت.` | ✔ |
| `update.check.upToDate` | `لديك أحدث إصدار (⁦{0}⁩)` | ✔ |
| `update.download.title` | `جارٍ تنزيل DropWheel ⁦{0}⁩` | ✔ |
| `update.error.body` | `⁦{0}⁩` | ✔ |
| `update.menu` | `التحديث إلى الإصدار ⁦{0}⁩` | ✔ |
| `update.updated.body` | `تم تحديث DropWheel إلى ⁦{0}⁩` | ✔ |

Also verified: 0 unbalanced LRI/PDI pairs in the whole Arabic table; 0 legacy embeddings/overrides
(`U+202A–U+202E`), 0 LRM/RLM marks; 0 literal version strings (`x.y.z`) outside an isolate.

Dynamic values (file names, paths, sizes, timecodes, percentages) are isolated at **runtime** by
`Bidi.Value`/`Bidi.Number` (`UI/Bidi.cs:19-27`), used at 134 call sites (E-11), e.g.
`JobRunner.cs:97`, `ToolWindow.cs:376`, `UpdatedWindow.cs:23`, `App.cs:379`. This is the intended
division of responsibility: the tables isolate *version* keys; the code isolates *dynamic* values.
Table placeholders that are counts (`{0} عناصر`) or already-isolated at the call site are intentionally
left bare — see F-03 for the residual risk of that convention being broken by a future call site.

---

## 5. RTL coverage

### 5.1 Windows and shared surfaces

Legend — **FlowDirection**: window sets RTL at construction (`LanguageManager.Apply`, `UiSetup.Configure`
or `LockUi.Configure`). **Alignment**: text inherits the mirrored layout; no hard-coded `TextAlignment`
or `FlowDirection.LeftToRight` around Arabic prose (F-01 was the only exception and is fixed).
**Chrome**: title-bar close button is docked on the leading edge and flips with the window; no physical
`Left`/`Right` positioning for chrome. WPF mirrors layout **and** alignment when `FlowDirection` is RTL,
so inherited `TextAlignment`/`HorizontalAlignment` values are logically start-aligned in both languages.

| Window | FlowDirection ok | Alignment ok | Mirrored chrome ok | FAIL id | Verdict |
|---|:--:|:--:|:--:|---|---|
| `WheelWindow` (`Wheel/WheelWindow.cs:109,298`) | ✔ | ✔ hub/tabs RTL, slices centered | ✔ | — | PASS |
| `SettingsWindow` (`UI/SettingsWindow.cs:60`) | ✔ | ✔ six tabs | ✔ | — | PASS |
| `JobsWindow` (`Jobs/JobsWindow.cs:26`) | ✔ | ✔; window x-position mirrored (`:91`) | ✔ | — | PASS |
| `ShelfWindow` (`Actions/ShelfWindow.cs:32`) | ✔ | ✔; shelf x-position mirrored (`:139`) | ✔ | — | PASS |
| `WheelEditorWindow` (`UI/WheelEditorWindow.cs:49`) | ✔ | ✔ | ✔ | — | PASS |
| `ParameterDialog` (`UI/ParameterDialog.cs:27`) | ✔ | ✔ | ✔ | — | PASS |
| `UpdatedWindow` (`Updates/UpdatedWindow.cs:36`) | ✔ | ✔ version isolated (`:23`) | n/a | — | PASS |
| `AddBackgroundColorWindow` (`…/Frame/AddBackgroundColorDialog.cs:61`) | ✔ | ✔ hex field LTR by design (`:87`) | ✔ | — | PASS |
| `LockOpenWindow` (`Shell/Protection/LockCli.cs:26`) | ✔ | ✔ | ✔ | — | PASS |
| `LockSettingsWindow` (`…/LockSettingsWindow.cs:26`) | ✔ | ✔ | ✔ | — | PASS |
| `SecretPromptWindow` (`…/LockSettingsWindow.cs:281`) | ✔ | ✔ | ✔ | — | PASS |
| `LockRecoveryWindow` (`…/UnlockedPanel.cs:188`) | ✔ | ✔ | ✔ | — | PASS |
| `RescuerSetupWindow` (`…/RescuerWindows.cs:73`) | ✔ | ✔ (F-01 fixed) | ✔ | — | PASS |
| `RescuerSessionWindow` (`…/RescuerWindows.cs:516`) | ✔ | ✔ | ✔ | — | PASS |
| `RescuerPasswordWindow` (`…/RescuerWindows.cs:617`) | ✔ | ✔ | ✔ | — | PASS |
| `LockWarningDialog` (`UI/Tools/Lock/LockTool.cs:428`) | ✔ | ✔ (F-01 fixed) | ✔ | F-01 | PASS |
| `ChangeFilePasswordWindow` (`UI/Tools/Lock/LockTool.cs:734`) | ✔ | ✔ | ✔ | — | PASS |
| Message boxes (`UI/Dialogs.cs:13`) | n/a | ✔ `RtlReading \| RightAlign` | n/a | — | PASS |
| Tray menu (`Shell/TrayIcon.cs:132`) | ✔ | ✔ | n/a | — | PASS |
| Drag overlay (`UI/ForegroundHelper.cs:41`) | ✔ | ✔ | n/a | — | PASS |
| Update banner (`UI/UpdateBanner.cs:98,116`) | ✔ | ✔ progress bar inherits RTL | n/a | — | PASS |

**17/17 windows and 4/4 shared surfaces set RTL.** No window is left at WPF's default LTR.

### 5.2 Tool windows — all 26 registered `IToolWindowSpec` tools

Every tool is hosted in `ToolWindow`, which sets `FlowDirection` from `LanguageManager`
(`UI/Tools/ToolWindow.cs:91`), rebuilds its chrome RTL and keeps the close button on the leading edge
(`ToolWindow.cs:322-325`). Per-tool specifics below; “LTR domain” means a deliberate
`FlowDirection.LeftToRight` on a canvas/timeline/slider whose coordinate system is technical, with the
labels around it still RTL.

| Tool (window) | FlowDirection ok | Alignment ok | Mirrored chrome ok | FAIL id | Verdict |
|---|:--:|:--:|:--:|---|---|
| Add background (`AddBackgroundView.cs:80`) | ✔ | ✔ labels RTL, image layer LTR | ✔ | — | PASS* |
| Annotate (`AnnotateView.cs:407,460-461`; `AnnotationRenderer.cs:255-262`) | ✔ | ✔ per-primitive RTL text | ✔ | — | PASS |
| Audio bleep (`AudioBleepTool.cs:173`) | ✔ | ✔ labels RTL, waveform LTR | ✔ | — | PASS* |
| Audio channels (`AudioChannelsTool.cs`) | ✔ | ✔ | ✔ | — | PASS |
| Audio normalize (`AudioNormalizeTool.cs:63`; `AudioSlider.cs:73,91`) | ✔ | ✔ labels RTL, slider values LTR | ✔ | — | PASS* |
| Audio trim (`AudioTrimTool.cs:241`) | ✔ | ✔ labels RTL, waveform LTR | ✔ | — | PASS* |
| Audio visualizer (`AudioVisualizerTool.cs`) | ✔ | ✔ | ✔ | — | PASS |
| Collage (`CollageView.cs:56,60,285`) | ✔ | ✔ labels RTL, canvas/strip LTR | ✔ | — | PASS* |
| Compress (`CompressView.cs`) | ✔ | ✔ | ✔ | — | PASS |
| Crop (`CropTool.cs`) — **the only tool CI renders, and in English only** | ✔ | ✔ | ✔ | — | PASS* |
| Edit photos (`EditPhotosView.cs:332`) | ✔ | ✔ labels RTL, slider LTR | ✔ | — | PASS* |
| Lock (`UI/Tools/Lock/LockTool.cs`) | ✔ | ✔ | ✔ | — | PASS |
| Metadata (`MetadataView.cs`) | ✔ | ✔ values isolated (`:101,121,215`) | ✔ | — | PASS |
| Protect (`UI/Tools/Password/ProtectTool.cs`) | ✔ | ✔ | ✔ | — | PASS |
| Unlock (`UI/Tools/Password/UnlockTool.cs`) | ✔ | ✔ name isolated (`:106`) | ✔ | — | PASS |
| PDF merge (`PdfMergeTool.cs`) | ✔ | ✔ up/down reorder is vertical (no mirroring) | ✔ | — | PASS |
| PDF organise (`PdfOrganizeTool.cs`) | ✔ | ✔ | ✔ | — | PASS |
| QR (`QrView.cs`) | ✔ | ✔ centered payload | ✔ | — | PASS |
| Video crop (`VideoCropView.cs:197`) | ✔ | ✔ labels RTL, crop box LTR | ✔ | — | PASS* |
| Video frames (`VideoFramesTool.cs:255-257`) | ✔ | ✔ timeline LTR, chevrons unmirrored | ✔ | — | PASS* |
| Video join (`VideoJoinTool.cs`) | ✔ | ✔ clips use `Bidi.Value` (`:307,319,345`) | ✔ | — | PASS |
| Video redact (`VideoRedactTool.cs:276,314-316`) | ✔ | ✔ timeline LTR | ✔ | — | PASS* |
| Video speed (`VideoSpeedView.cs:177`) | ✔ | ✔ slider LTR | ✔ | — | PASS* |
| Video split (`VideoSplitTool.cs`) | ✔ | ✔ timeline LTR | ✔ | — | PASS* |
| Video trim (`VideoTrimView.cs:120-123`) | ✔ | ✔ timeline LTR, chevrons unmirrored | ✔ | — | PASS* |
| Redact (`RedactView.cs`) | ✔ | ✔ | ✔ | — | PASS |

`PASS*` = code-verified but **not rendered in Arabic by CI today** (F-02). No hard-coded English UI
literal was found anywhere in `FileAction.App`/`FileAction.Core`: the only string literals assigned to
`Title`/`Text`/`Content` outside `T(...)` are the product name `DropWheel`
(`WheelWindow.cs:92`, `ForegroundHelper.cs:40`, `ShareHelper.cs:54,102`).

### 5.3 The wheel

- Window and hub/tabs are RTL (`WheelWindow.cs:109,112-114,298-300`); the **canvas stays LTR** on
  purpose because slice geometry is polar (`:112`). A radial menu has no reading order, so not
  mirroring the angles is correct and keeps “clockwise from the top” stable in both languages.
- Slice captions are centered and wrap (`:862-911`); Arabic prose labels use the RTL flow direction
  while uppercase format identifiers (`MP4`, `PDF`) stay LTR (`:864,872`) — correct for an identifier.
- Hub text truncates with `CharacterEllipsis` (`:745-753`) and subtitles with the same trimming
  (`:897-909`). Arabic wheel labels are short: longest is `بحد أقصى 1920 بكسل` (16 chars) and the
  largest ar/en length ratio is 2.5 (`إلى اليسار` vs `left`). No Arabic wheel label/subtitle exceeds
  22 characters, so wrapping is bounded, but the narrowest slices still deserve a screenshot (§7).
- Arrow keys: `Left/Up` = previous slice, `Right/Down` = next (`:607-610`). Because the wheel is not
  mirrored, this stays visually correct in Arabic.

### 5.4 Chrome, sliders, progress, icons

| Surface | Behaviour in RTL | Verdict |
|---|---|---|
| Close button (all windows) | docked on the leading edge (`Dock.Left`), flips with the window | PASS |
| Chevrons `\uE76B`/`\uE76C` (previous/next frame) | **not** mirrored; deliberate — they belong to the LTR media timeline (`VideoTimeline.cs:56`, `VideoControls.cs:108,173`) | PASS*, screenshot §7 |
| Settings slider (`UI/Controls.cs:175-236`) | fill/thumb math is in DIPs inside an inherited-direction grid | PASS*, screenshot §7 |
| Audio/rate sliders (`AudioSlider.cs:73,91`, `VideoControls.cs:173`, `EditPhotosView.cs:332`) | explicitly LTR: “technical values stay left-to-right in Arabic too” | PASS* (documented exception) |
| Progress bars (16 sites) | no explicit direction; inherit RTL and fill right-to-left | PASS |
| Directional icons | only rotate-left/right glyphs and media chevrons exist; rotation is semantically absolute and must **not** mirror | PASS |
| Tray menu | RTL menu, version isolated (`TrayIcon.cs:132,138,166`) | PASS |

### 5.5 Digits, mixed runs, truncation

- **Arabic-Indic digits:** none in either table (asserted). Runtime numbers use
  `CultureInfo.InvariantCulture` (`Controls.cs:202`, `AudioSlider.cs:158`, `Bidi.Number`), so the UI
  shows Western digits in Arabic.
- **Mixed Latin/Arabic runs:** version keys isolated in the tables (§4); 134 dynamic-value call sites
  use `Bidi.Value`; file names in the shelf/jobs/wheel hub are isolated. Residual risk F-03.
- **Truncation/wrap:** `TextTrimming.CharacterEllipsis` on titles, hub text, file names and subtitles
  (multiple sites); wheel labels max 22 Arabic chars; `MaxWidth` + `TextWrapping` on dialog prose. Arabic is
  on average 1.3–1.6× longer than English; the longest Arabic wheel label (16 chars) wraps to at most
  3 lines at the narrowest 48-DIP slice — needs the screenshot in §7, not a code change.

---

## 6. Findings

### F-01 — P2 — Checkboxes forced LTR around Arabic text — **FIXED in `4b8c8cd`**

- `src/FileAction.App/UI/Tools/Lock/LockTool.cs:456` — protect-warning acknowledgement checkbox
  (`tool.protect.warn.checkbox`) had `FlowDirection = FlowDirection.LeftToRight`.
- `src/FileAction.App/Shell/Protection/RescuerWindows.cs:216` — rescuer “I saved the key” checkbox
  (`tool.lock.rescuer.iSaved`) had the same override.
- Effect in Arabic: check glyph stayed on the left and the wrapped Arabic sentence was laid out as
  LTR text, contradicting every other checkbox in the app.
- Fix: removed the two overrides so both inherit the window direction. Minimal (2 lines deleted), no
  string or layout change. Verified by CI build; visual confirmation still needs a screenshot (§7).

### F-02 — P2 — CI does not language-verify the tool windows; only 1 of 26 is rendered at all (and in English) (**OPEN**)

**What the CI screenshot job actually renders, exactly:**

| Surface | en | en-light | ar | ar-light | Raw-key assertion |
|---|:--:|:--:|:--:|:--:|---|
| String tables loaded (1,094 keys) | ✔ | ✔ | ✔ | ✔ | n/a |
| Wheel — 7 views (`wheel-image-tools`, `wheel-video-tools`, `wheel-pdf-tools`, `wheel-sheet-convert`, `wheel-actions`, `wheel-waiting-for-drop`, custom/repeat) | ✔ | ✔ | ✔ | ✔ | ✔ via `SaveWindow`/`RenderPreview` |
| `SettingsWindow` — all 6 tabs | ✔ | ✔ | ✔ | ✔ | ✔ per tab (`SelfTest.cs:137-142`) |
| `ShelfWindow` | ✔ | ✔ | ✔ | ✔ | ✔ |
| `JobsWindow` | ✔ | ✔ | ✔ | ✔ | ✔ |
| `WheelEditorWindow` | ✔ | ✔ | ✔ | ✔ | ✔ |
| `ParameterDialog` | ✔ | ✔ | ✔ | ✔ | ✔ |
| `ToolWindow` (Crop only) | ✔ | — | **—** | **—** | ✔ (English) |
| Other 25 tools (MotionSelfTest/motion clips build them) | English only | — | **—** | **—** | **no** |
| Lock/Protect/Rescuer dialogs (9 windows) | — | — | — | — | **no** |

Evidence for the “English only” column: `SelfTest.cs:45-63` runs `RenderAll` inside the four
language/theme folders, but `SelfTest.cs:68` resets English before the later steps at `SelfTest.cs:74`
(`TestToolWindows` → `SelfTest.cs:450-483`, `MotionSelfTest.Run`, `RenderMotionClips`). So the tool
windows are built **after** the language reset. `SelfTest.cs:449` claims *“Builds every registered
floating tool offscreen, so --self-test covers their layout and strings”*, but `SelfTest.cs:465` builds
**only** `new ToolWindow(CropTool.Instance, …)`, and `SelfTest.cs:481` logs
`ok tools/crop (26 registered)`, which reads as if all 26 were covered.

`MotionSelfTest` does build **all 26 registered tools** (`MotionSelfTest.cs:106-144`, data-driven from
`ToolRegistry.All`) and `RenderMotionClips` does the same for the website clips
(`MotionClips.cs:81-99`), but both run in the **English** UI culture as shown above — they verify
motion/layout/render cost, **not Arabic labels, direction, or raw-key safety**.

Consequence: 25 of 26 tool windows are never label-asserted in Arabic, and the Lock/Protect/Rescuer
dialogs are never rendered. A missing `T(...)` or a wrongly-LTR control in those views would ship
silently. **Release-claim discipline: no release note may say “every tool window verified in Arabic”.**
The accurate statement is “rendered in Arabic: wheel, Settings (all six tabs), shelf, jobs, wheel
editor, parameter dialog; tool windows are motion-measured in English, not language-verified.”

Required before the tag **only if** the release notes make a tool-window RTL claim. Proper fix (next
lane, not this PR): build each `ToolRegistry.All` spec once per language inside the language loop with
a suitable sample `FileItem`, run `CheckNoRawKeys` on it, and save a per-tool `ar` screenshot; keep
`Pump` + the 30 s guard. This lane did not attempt it — it changes runtime self-test behaviour and CI
minutes right before the tag.

### F-03 — P3 — 122 Arabic values rely on runtime isolation of `{0}`, nothing statically enforces it (**OPEN**)

- 122 Arabic values contain digits or `{0}` outside `U+2066…U+2069` (e.g. `error.file`
  “خطأ في الملف: {0}”, `toast.dropFolder`, `tool.videojoin.output`). This is **not** a shipped visual
  bug today: the high-traffic call sites wrap the value with `Bidi.Value` (134 sites, E-11), and count
  placeholders are Western digits that render correctly.
- Risk: the convention is invisible to reviewers — a new call site that passes a Latin file name
  without `Bidi.Value` would reorder in Arabic and no test would catch it.
- Suggested follow-up (not in this PR): extend `LocalizationAuditTests` with a curated list of
  path/file-name keys and a source scan asserting every string argument to `T(key, arg)` is
  `Bidi.Value(...)`/`Bidi.Number(...)`. Documenting the pattern here is the minimum.

Severity scale: P0 crash/data loss · P1 wrong user-visible result · P2 broken UX in a language ·
P3 maintainability/consistency. No P0/P1 localization defect found.

---

## 7. What still needs a CI screenshot (do not guess)

These are code-verified but not visually confirmed. Capture in the `screenshots` workflow with
`--self-test` in the `ar`/`ar-light` folders:

1. **F-01 checkboxes** — `Lock` tool → protect warning dialog, and the rescuer key page: check glyph
   on the right, Arabic sentence reading right-to-left. (These dialogs are not in `--self-test` at all
   today — F-02.)
2. **Tool windows** — one screenshot per registered tool in Arabic; today the only tool image is
   `tools/crop.png` and it is rendered in English (F-02). Priority: Lock, Protect, Unlock, Compress,
   Metadata, QR, PDF merge/organise, Video trim/frames.
3. **Wheel** — `wheel-*` Arabic variants already render; inspect the narrow-slice labels
   `بحد أقصى 1920 بكسل`, `إنشاء أرشيف`, `مستوى الصوت` for clipped/wrapped text at 100 % and 200 % DPI.
4. **Sliders** — Settings quality slider and the audio normalise sliders: confirm fill/thumb direction
   and that the numeric readout stays adjacent to the track in Arabic.
5. **Progress** — an in-flight job card and the update banner download: confirm the bar fills from the
   right in Arabic.
6. **Chevrons** — video trim/frames transport buttons in Arabic: confirm the design accepts unmirrored
   previous/next glyphs against an LTR time axis (this is the only intentionally unmirrored icon).
7. **Tray menu + message boxes** — Arabic tray menu and a `Dialogs.Show` message box: confirm OS-level
   right alignment.

---

## 8. CI-enforced test

`tests/FileAction.Tests/LocalizationAuditTests.cs` (new) — deterministic, no UI thread, no network, no
`Loc.Current` mutation; it reads the same embedded tables the app ships via `Loc.Table` and runs in
well under a second as part of `dotnet test` in the `build-test` job.

| Fact | Enforces |
|---|---|
| `Tables_have_exact_key_parity_in_both_directions` | §2.1 + group floors + ≥1,000 keys |
| `No_english_key_was_removed_from_the_arabic_table` | §3 append-only |
| `No_key_exists_in_only_one_table` | §3 both-tables rule |
| `No_value_is_empty_or_a_raw_localization_key` | §2.2, §2.3 |
| `No_user_facing_prose_is_left_untranslated` | §2.4 (+ stale allow-list detection) |
| `Placeholder_multisets_match_per_key` | §2.5 (with the CLDR plural exception) |
| `Version_interpolations_are_wrapped_in_bidi_isolates` | §4, 11 keys + isolate balance |
| `Arabic_uses_western_digits_only` | §5.5 |
| `Bidi_isolates_are_balanced_and_no_legacy_directional_marks_are_used` | §4 hygiene |

These complement, and do not duplicate, `LocalizationTests` (runtime `Loc` API, plurals, catalog build)
and `LocaleUsageTests` (call-site keys). Nothing here changes product strings or key names.

---

## 9. Before the 11:00 tag

Must:

1. `build-test` green on this branch (build + 192+ tests incl. `LocalizationAuditTests`).
2. Enforce the F-02 claim discipline: no “every tool window verified in Arabic” wording in tag notes,
   README, CHANGES or release notes. Accurate: wheel + Settings (6 tabs) + shelf + jobs + wheel editor +
   parameter dialog rendered in Arabic; tools motion-measured in English only.
3. Merge `audit/al10n` (supervisor) — the F-01 fix should ship with 2.4.0; it is two deleted lines.

Should (not release-blocking): add per-tool Arabic rendering + raw-key checks after the tag (F-02);
accept or reject the unmirrored video chevrons after a screenshot; schedule F-03’s static guard.

---

## 10. Limitations

- No local execution: visual/bidi rendering claims are static reasoning plus the existing CI screenshot
  job; everything marked `PASS*` or §7 needs the screenshot before being called visually verified.
- WPF mirrors layout and alignment under `FlowDirection`; this audit treats inherited
  `TextAlignment`/`HorizontalAlignment` as correct and only flags explicit LTR overrides around Arabic
  prose (exactly F-01).
- The append-only proof covers all tags present locally (v2.1.0–v2.3.0) and the current branch; it is
  not a cryptographic proof for forks or force-pushed history.
- Translation *quality* (terminology choices) is reviewed by a native speaker; the open questions are
  tracked in `docs/ar-strings-review.md` and are out of scope here.
