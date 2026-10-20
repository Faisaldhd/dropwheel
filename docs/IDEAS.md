# Ideas — the wheel and the tool windows

Concrete, buildable ideas for DropWheel 2.4.0, judged against the owner's rule: **an idea ships only if it
costs zero performance.** Everything here is either a static change (layout, token, string, copy, icon) or
it is labelled with what it costs and why it cannot ship yet.

Read with `docs/CREATIVE-GUIDE.md` (the design language this list extends) and `docs/MOTION.md` /
`docs/PERF.md` (the budgets it is judged against).

---

## 0. How an idea earns a verdict

**SHIP-NOW** — provably zero runtime cost. All four must be true:

1. no new `DispatcherTimer`, background thread, `Task.Delay` loop, or `CompositionTarget.Rendering` hook;
2. no work proportional to frames or to item count *per frame* — only work at build/open time, bounded by
   the elements the surface already creates;
3. no new work before the first frame (nothing read at startup, no new file, no new probe);
4. no new dependency and no new bytes in the installer.

…and the change itself is static: a token, a brush, a string, a glyph, a `Thickness`, a property set once.
A SHIP-NOW change cannot fail the motion gate (`docs/PERF.md:24`) because nothing new ticks; it can only
move the *build-time* element count, which the measured UI-frame p95 (`docs/PERF.md:47-52`, 0.03–0.07 ms)
absorbs by two orders of magnitude.

**LATER** — real frame time or real memory, but plausible inside budget on paper. Needs a measurement
before it ships; the mechanism that would make it cheap is named.

**REJECT** — breaks a specific budget by construction. The budget is cited.

**Effort** (S/M/L) is separate from the verdict: an S idea can be REJECT and an L idea can be SHIP-NOW.

**Surface count.** 26 tool windows are registered (`ToolRegistry.Register`, 26 call sites under
`src/FileAction.App/UI/Tools/`) and **all 26 are reachable from the wheel** as of `4f23c9c`, which lists
`AnnotateOperation` and `RedactOperation` in `WheelController.BuildOperations()`
(`src/FileAction.App/Wheel/WheelController.cs:93-94`). Earlier in the 2.4.0 train those two were registered
but absent from that list; the gap is closed, so this list treats the wheel as a 26-surface product. The
lesson still holds for new tools: register the tool **and** add its operation to that list — a registered
tool that is missing from it is invisible, and nothing in the UI says so.

---

## 1. The wheel

The whole disc is one `Canvas` of per-slice `Canvas` groups (`WheelWindow.cs:755-840`). Every idea below
either changes a property that is already written, or adds elements at `Build()` time — which runs on open
and on mode switch only, never per frame.

### W1. Keycap tabs in the hub — **SHIP-NOW (S)**

**Pitch.** The three wheel tabs (Convert / Tools / Actions) become small keycaps, so "Shift switches the
wheel" is legible without a tooltip.
**Why it fits.** The reference puts the shortcut in a keycap chip at the bottom of the disc
(`_reference/tangerine-shift-convert-frames.zip`, frames 26–40), and its control order is
*hint → disc → action*. We already have the hint text (`Shift`, `Ctrl`, `Alt` from `AppSettings.KeyName`),
just set as bare 10.5 px text.
**Where.** `src/FileAction.App/Wheel/WheelWindow.cs:941-973` (`BuildHub`, the tab loop).
**Sketch.**
- Wrap the tab's `TextBlock` in the existing `Border` (already there, `:948-962`) and set
  `BorderThickness = new Thickness(1)`; the active tab keeps `Background = Theme.AccentGradient` and
  `BorderBrush = Brushes.Transparent`; an inactive one gets `Background = Theme.Track`,
  `BorderBrush = Theme.PanelBorder`.
- Keep `CornerRadius(8)` (`:950`) and `Padding(7,1,7,2)` (`:951`); add `Margin = new Thickness(0,0,4,0)` so
  three keycaps do not touch.
- Keep the existing `ToolTip` (`:954`).
**Frame cost 0.** Same element count +0; two property writes per tab, executed in `BuildHub`, which only
runs on open / switch / relocalize. No clock, no timer, no new visual type.

### W2. Slice numerals for 1–9 — **SHIP-NOW (S)**

**Pitch.** Show a small muted `1`…`9` in the corner of each slice when the wheel has ≤ 9 slices, because
`D1`–`D9` already run that slice (`WheelWindow.cs:633-636`) and nothing on screen says so.
**Why it fits.** The reference teaches its radial model with tiny affordances on the disc, never with a
manual. Here the affordance is already implemented and invisible.
**Where.** `WheelWindow.SliceLabel` (`:862-912`) and `Build` (`:782-819`).
**Sketch.**
- Pass the slice index into `SliceLabel` (it currently takes only `Operation`).
- When `_ops.Count <= 9`, prepend a
  `TextBlock { Text = (index + 1).ToString(CultureInfo.InvariantCulture), FontSize = 10,
  Foreground = Theme.Muted, HorizontalAlignment = Center, Margin = new Thickness(0,0,0,2) }`
  so it sits above the glyph.
- Western digits only (`docs/glossary-ar.md:78`); no new string table entry.
**Frame cost 0.** One extra `TextBlock` per slice *at build*; ≤ 9 of them; the label `StackPanel` already
exists with 2–3 children.

### W3. Subtitle only on the highlighted slice — **SHIP-NOW (M)**

**Pitch.** Every slice currently prints its subtitle (`WheelWindow.cs:897-910`), so a 6-slice wheel shows
12 lines of text at 10.5 px. Show the subtitle only on the slice under the pointer / keyboard selection —
the reference's wedges carry one word each, and the detail appears in the hub.
**Why it fits.** It is the reference's information hierarchy: **labels on the disc, detail in the centre.**
We already print the same subtitle in the hub (`UpdateHubText`, `:1057-1063`), so nothing is lost.
**Where.** `WheelWindow.SliceLabel` (build the subtitle, start `Visibility.Collapsed`) and
`WheelWindow.Paint` (`:1001-1012`, toggle it with the highlight).
**Sketch.**
- The subtitle `TextBlock` already sets `Tag = "subtitle"` (`:902`); use that.
- In `Paint(index, on)`: `subtitle.Visibility = on ? Visibility.Visible : Visibility.Collapsed`.
- Keep it visible when `_ops.Count <= 2` (a two-slice wheel has room) and in the repeat view.
**Frame cost 0.** `Paint` already walks `_labels[index].Children` and writes foreground/opacity
(`:1007-1012`); this is one more property write on the same walk, on pointer movement only.
**Gate.** Needs four screenshots (en/ar × dark/light) before merge — it changes density, not behaviour.

### W4. The empty hub teaches the shortcut — **SHIP-NOW (S)**

**Pitch.** When the wheel is open with no file, the hub says "Drag a file here" (`wheel.dropHere`) and
nothing else. Add a keycap row underneath with the actual hotkey, which is exactly when the user needs it.
**Why it fits.** The reference's keycap chip is this: the shortcut, at the point of use, where the eye
already is.
**Where.** `WheelWindow.BuildHub` (`:914-975`) + `UpdateHubText` (`:1046-1071`).
**Sketch.**
- Add one `Border` (`Theme.Track`, radius 8, padding `7,1,7,2`, child `TextBlock` 10.5 SemiBold,
  `Theme.Muted`) to `_hubContent` once, after `_destination` (`:129`).
- In `UpdateHubText`, when `_items.Count == 0`, set its text to
  `AppSettings.HotkeyName(_settings.Hotkey)` and `Visibility.Visible`; otherwise `Collapsed`.
- No new strings: the `hotkey.*` keys already exist (`AppSettings.cs:228-235`).
**Frame cost 0.** One element built once; one visibility/text write per hub update (a pointer move already
does several).

### W5. A destination glyph on the "save to" line — **SHIP-NOW (S)**

**Pitch.** The hub already prints where the result will go (`WheelController.UpdateDestination`,
`:171-186`) — as words. Put the matching glyph in front of it: a folder for a chosen folder, an arrow for
"next to the file", a warning glyph when the folder refuses.
**Why it fits.** Reference control order always pairs an icon with the value it describes; ours is the only
line in the hub with no icon.
**Where.** `WheelWindow` field `_destination` (`:57`), construction (`:129`), `SetDestination`
(`:1038-1044`).
**Sketch.**
- Replace the bare `_destination` in `_hubContent` with a horizontal `StackPanel` holding a glyph
  `TextBlock` (`Theme.Icons`, 10.5 px) and `_destination`.
- In `SetDestination`: folder → `Glyphs.Folder` (`Glyphs.cs:11`); next-to-file → `Glyphs.Move` (`:17`);
  blocked → a new constant `Warning = "\uE7BA"` in `Glyphs` (exists in both Fluent and MDL2) and the
  existing `Theme.Error` text colour.
- The glyph follows the text colour: `Muted`, `Error` when blocked.
**Frame cost 0.** One element built once; `SetDestination` already writes text/colour/visibility.

### W6. Full file name in the hub tooltip — **SHIP-NOW (S)**

**Pitch.** The hub title ellipsizes at `InnerRadius * 1.65` ≈ 132 px (`WheelWindow.cs:752`), so a long
name shows `IMG_2024…`. Put the whole `Bidi.Value(name)` in the title's `ToolTip`.
**Why it fits.** Quiet surface, detail on demand — the same rule as the reference's truncated file chips.
**Where.** `WheelWindow.UpdateHubText` (`:1046-1071`).
**Sketch.** Where `_title.Text` is set from the item, also set
`_title.ToolTip = _items.Count == 1 ? Bidi.Value(_items[0].Name) : null;`. WPF creates tooltips lazily, so
nothing is measured until hover.
**Frame cost 0.** A string property on a path that already writes text; `_destination.ToolTip` does the
same (`:1041`).

### W7. Wheel: an accent halo on the hub when a drop is armed — **LATER**

**Pitch.** While a drag is over the disc with a valid target, the hub could carry a soft accent ring.
**Why it fits.** The reference arms the drop target with a small badge; a hub ring is our version.
**Cost.** `Theme.Glow()` is a `DropShadowEffect`; a second effect on the hub costs an extra render pass
every frame the wheel is open. The wheel's software-raster proxy is already 39.7 ms (`docs/PERF.md:47`), so
the gate (GPU) would pass but the design budget is closer than it looks.
**Make it cheap.** Enable it only while `DragOver` is active and remove it on `DragLeave`
(`WheelWindow.cs:143-149`) — an effect present for the duration of a drag, never at rest, never on open.
Then it needs a measurement; LATER, not SHIP-NOW.

### W8. Wheel: type-to-filter in the hub — **LATER**

**Pitch.** In click mode, typing filters the slices by label.
**Why it fits.** A 24-operation product eventually needs it, and the reference avoids it by keeping each
wheel small.
**Cost.** A `TextBox` in the hub is a **focus-grabbing control inside a `WS_EX_NOACTIVATE` drag window**
(`WheelWindow.cs:132-136`): in drag mode it can never work, and in click mode it would take keyboard focus
away from the wheel's own key handling (`OnKeyDown`, `:598-641`). Rendering cost is zero; the interaction
cost is a redesign of the key map. LATER, and probably never — the wheel editor already lets the user hide
operations.

### W9. Wheel: dim the desktop behind the disc — **REJECT**

**Pitch.** A full-screen 30 % scrim behind the wheel, like a modal.
**Why it breaks.** It needs a second topmost window sized to the virtual screen (extra window creation on
the hot path → cold-start budget, `docs/PERF.md:6`), and `AllowsTransparency` composition of a
screen-sized layer is exactly the CPU-side path the app avoids. The wheel's point is that it does not
disturb what is behind it.

### W10. Wheel: particle/confetti confirm — **REJECT**

**Pitch.** A burst when a drop lands.
**Why it breaks.** Per-frame work with no end state the gate can measure, on top of the `SliceMs`/`PopMs`
budgets already spent, and it contradicts the "no decorative motion" rule (`docs/CREATIVE-GUIDE.md` §7 and
§10). Use the existing 260 ms `Motion.Pop` on the job badge (`Motion.cs:69`) instead.

### W11. Wheel: per-slice drop shadows / glossy wedges — **REJECT**

**Pitch.** Give each slice its own elevation, like the reference's bevelled wedges.
**Why it breaks.** N `DropShadowEffect`s = N extra render passes every frame, on a 442×442 surface whose
software proxy is already the slowest thing in the self-test (39.7 ms, `docs/PERF.md:47`). The 60 fps
design budget applies to a low-end laptop, not a datacenter runner.

### W12. Wheel: live preview of the output inside the hub — **REJECT**

**Pitch.** The hub shows a thumbnail of what the highlighted operation would produce.
**Why it fits.** The reference's whole pitch is preview-first.
**Why it breaks.** A real preview means decoding or running the operation on a sample while the wheel is
opening — CPU and memory inside the wheel-open budget (`docs/PERF.md:6-9`). The cheap 90 % already ships:
the hub shows the file's own icon or a per-format glyph (`WheelWindow.cs:920-939`). Do not put a decoder on
the hot path for the remaining 10 %.

---

## 2. Tool windows

The chrome is shared (`ToolWindow.Build`, `:225-259`), so a change here lands on all 26 tools at once and needs one
measurement, not 26.

### T1. Category eyebrow above the tool title — **SHIP-NOW (M)**

**Pitch.** A 10.5 px muted line above the centred title: `IMAGES`, `VIDEO`, `AUDIO`, `PDF`, `DOCUMENTS`,
`SECURITY`. The user always knows which family of tool they are in, and 24 windows stop feeling
undifferentiated.
**Why it fits.** It is the reference sheet grammar (`_reference/tangerine-tools.pdf`: category eyebrow
above the headline) translated into app chrome — and the tool title bar is exactly the place it belongs.
**Where.** `ToolWindow.BuildTitleBar` (`:289-332`), `IToolWindowSpec`
(`src/FileAction.App/UI/Tools/IToolWindowSpec.cs:14-36`).
**Sketch.**
- Add `string CategoryKey => "tool.category.other";` to `IToolWindowSpec` as a **default interface
  member**, so the 26 existing specs compile untouched.
- Override it in the tools that have a family (or, as a single-file alternative, map `Id` prefixes inside
  `ToolWindow`: `tools.video*`, `tools.audio*`, `tools.pdf*`, `tools.lock`/`protect`/`unlock`, and the
  image group listed explicitly).
- Seven new keys in **both** tables, same order: `tool.category.images` = `Images` / `الصور`;
  `…video` = `Video` / `الفيديو`; `…audio` = `Audio` / `الصوت`; `…pdf` = `PDF` / `PDF`;
  `…documents` = `Documents` / `المستندات`; `…security` = `Security` / `الأمان`;
  `…other` = `Tool` / `أداة`. Reuse the wording of the existing `app.editor.group.*` values so the app
  speaks one vocabulary.
- Title bar becomes a 2-row `StackPanel` (`eyebrow` 10.5 SemiBold `Muted`, then the existing 15 SemiBold
  title), still centred, still draggable.
**Frame cost 0.** One `TextBlock` per tool window, built once; no timer, no state, no async. The window
already builds ~30 elements.
**Gate.** `LocaleUsageTests` requires every `T("…")` key to exist in both tables
(`tests/FileAction.Tests/LocaleUsageTests.cs:67-89`), and the tool key sets are compared across languages
(`CompressMetadataTests.cs:691`).

### T2. "Reset" becomes a ghost button — **SHIP-NOW (S)**

**Pitch.** Today Reset and the primary action are the same control with the same weight
(`ToolWindow.cs:336-338`; `Controls.Button` at `Controls.cs:239-275`), so every tool window has two
competing primaries. Make Reset text-only: no fill, no border, `Muted` foreground.
**Why it fits.** One primary action per surface is the reference's most consistent rule (the accent button
is the only filled one on every sheet) and `docs/CREATIVE-GUIDE.md` §1.
**Where.** `Controls.Button` — add `bool ghost = false`; `ToolWindow.BuildBottomBar` passes `ghost: true`
for Reset.
**Sketch.** `ghost` → `Background = Brushes.Transparent`, `BorderBrush = Brushes.Transparent`,
`Foreground = Theme.Muted`; `Motion.Interactive(button)` unchanged, so hover still dims to 0.86 and that is
the affordance.
**Frame cost 0.** Two brush writes at build time.

### T3. Disabled primary action must look disabled — **SHIP-NOW (S)**

**Pitch.** `ToolWindow.BuildBottomBar` sets `apply.IsEnabled = gate.CanApply` (`:340-348`), but a `Border`
with `IsEnabled = false` looks identical to an enabled one — it only stops the animation path
(`Motion.PressScale` checks `IsEnabled`, `Motion.cs:511`). The user clicks and nothing happens.
**Why it fits.** Honest affordance is the tone of voice (`docs/CREATIVE-GUIDE.md` §9); a silently dead
button is the opposite of "say what happened and what is next".
**Where.** `ToolWindow.BuildBottomBar` (`:340-348`, the `CanApplyChanged` handler) and `Controls.Button`.
**Sketch.** Add `static void SetEnabled(Border button, bool enabled)` to `Controls`: disabled →
`button.Background = Theme.Track`, `button.Opacity = 0.6`, `button.Cursor = Cursors.Arrow`; enabled →
restore `Theme.AccentGradient` / `1.0` / `Cursors.Hand`. Call it once at build and from the existing
`gate.CanApplyChanged` handler.
**Frame cost 0.** Property writes only on state change (which already happens).

### T4. Numeric readouts align right — **SHIP-NOW (S)**

**Pitch.** Every tool's numeric field is a `TextBox` (`ToolControls.Box`, `:24-45`) that is left-aligned, so
a column of values reads ragged. Right-align the digits.
**Why it fits.** The reference's tool sheets are built on a strict *label → control → value* row with the
value flush to the trailing edge (`_reference/tangerine-tools.pdf`, the Edit Photo slider wall).
**Where.** `ToolControls.Box` (`:26-41`).
**Sketch.** Add `TextAlignment = TextAlignment.Right` to the initializer; keep `Padding(8,6,8,6)`.
`TextAlignment.Right` is physical, which is the trailing edge in both LTR and RTL — verify one tool in `ar`
before merging.
**Frame cost 0.** One property at build; text layout is already measured.

### T5. A visible keyboard focus ring on custom controls — **SHIP-NOW (M)**

**Pitch.** Give every hand-drawn control (button, chip, toggle, checkbox, close disc, wheel tab) a 1.5 px
accent ring while it holds keyboard focus.
**Why it fits.** Every control in the app is a `Border`/`Ellipse` with a mouse handler — none is a
`Button`, so WPF draws no focus visual. That is an accessibility defect in a product whose fastest path is
the keyboard (Tab / 1–9 / Esc on the wheel).
**Where.** New `Controls.Focus` helper + `Theme.Focus` (PROPOSED, `docs/CREATIVE-GUIDE.md` §2.6). Call
sites: `Controls.Button` (`:268`), `ToolControls.Chips` (`:174`), `Controls.Toggle` (`:169`),
`ToolControls.CheckBox` (`:129`), the tool close disc (`ToolWindow.cs:310`).
**Sketch.** `element.Focusable = true; KeyboardNavigation.SetIsTabStop(element, true);` then on
`IsKeyboardFocusedChanged` switch `BorderThickness` 1 → 2 and `BorderBrush = Theme.Accent`, restoring on
blur. No storyboard, no animation — a hard state switch, which is also what high-contrast users get from
the platform.
**Frame cost 0.** One or two border property writes per focus event. No clock.
**Gate.** WPF draws borders *inside* the layout box, so the 1 px growth moves nothing. Verify with motion
ON and OFF (this is not motion, so both must look identical) and once in `ar`.

### T6. Section labels become eyebrows — **SHIP-NOW (S)**

**Pitch.** `ToolControls.Section` prints a 12 px muted sentence-case title (`:59-74`). Make it 10.5 px
SemiBold, uppercase for Latin, still `Muted`.
**Why it fits.** The reference's in-window sections are quiet, small eyebrows ("Light", "Color"); ours are
the same idea one size too loud, and they compete with the values below.
**Where.** `ToolControls.Section` (`:62`).
**Sketch.** Build the `TextBlock` with `FontSize = 10.5`, `FontWeight = SemiBold`,
`Text = LanguageManager.IsRtl ? title : title.ToUpperInvariant()`. Arabic has no case, so
`ToUpperInvariant` is a no-op there — branch explicitly so a future casing rule can never mangle an Arabic
label.
**Frame cost 0.** One string per section at build (≤ 6 per tool).

### T7. The close control says how to close — **SHIP-NOW (S)**

**Pitch.** The close disc's tooltip is "Close" (`ToolWindow.cs:307`). Say **"Close (Esc)"**.
**Why it fits.** The window already handles Esc (`:111-122`); a shortcut the user cannot discover is a
shortcut that does not exist. Reference sheets label every control with its gesture.
**Where.** `ToolWindow.BuildTitleBar` (`:307`).
**Sketch.** `ToolTip = UiSetup.Tip(T("tool.window.close") + " (Esc)")` — `Esc` stays Latin in both
languages (`docs/glossary-ar.md:86`), and `UiSetup.Tip` gives the tooltip the right direction
(`UiSetup.cs:103-107`). No new key; the parenthetical is language-neutral.
**Frame cost 0.** One string concatenation at build.

### T8. Which file am I editing — **SHIP-NOW (S)**

**Pitch.** A muted 11.5 px second line under the tool title with the file name (ellipsized, bidi-isolated).
Copying, cropping or redacting is a decision about *a file*, and the window never says which one.
**Why it fits.** Reference tool sheets always show the media and its name together; our preview shows the
media and the chrome shows nothing.
**Where.** `ToolWindow.BuildTitleBar` (`:312-325`); the constructor already has `items` (`:75-99`).
**Sketch.** Under `title`, add
`TextBlock { Text = items.Count == 1 ? Bidi.Value(Path.GetFileName(items[0].Path)) : Loc.Items(items.Count),
FontSize = 11.5, Foreground = Theme.Muted, TextTrimming = CharacterEllipsis, MaxWidth = 320,
TextAlignment = Center }`. `Loc.Items(n)` (`src/FileAction.Core/Loc.cs:193`) already routes through the six
Arabic plural categories, so this needs **no new key**.
**Frame cost 0.** One `TextBlock`, one string per open.

### T9. Title bar shows it can be dragged — **SHIP-NOW (S)**

**Pitch.** The title bar drags the window (`ToolWindow.cs:326-330`) but the cursor stays an arrow.
**Where.** `:326`.
**Sketch.** `bar.Cursor = Cursors.SizeAll;` on the title `DockPanel` only — the close disc and the buttons
keep their hand cursor.
**Frame cost 0.** One property.

### T10. A "recent" chip row per tool — **LATER**

**Pitch.** Remember the last three parameter sets per tool and offer them as chips.
**Why it fits.** Speed is the product; the reference's preset chip row (0.25×/0.5×/1×/2×/4×) is the same
interaction.
**Cost.** New persisted state in `AppSettings` (schema migration + tests), a read at window build, and a
chip row per tool. Memory is in the low kilobytes, but it touches every tool's `Reset` semantics. Not
zero, and it needs an owner decision before any measurement.

### T11. Output thumbnail after Apply — **LATER**

**Pitch.** After a successful write, show a small thumbnail of the produced file in the closing window.
**Cost.** A second decode of the output (memory + a render pass while the close fade runs). The success
path already pulses and closes in 320 ms + 120 ms (`ToolWindow.ApplyAsync`, `:376-395`); a decode there
would put real work inside the animation budget. LATER, with a measured budget.

### T12. Per-tool accent hues — **REJECT (design, not perf)**

**Pitch.** Image tools violet, video tools blue, audio tools teal.
**Why it breaks the product.** The accent is the identity *and* the selection signal
(`docs/CREATIVE-GUIDE.md` §2.1). Spending it per family destroys the one colour cue that means "this is
chosen", and it would put five untested hue pairs into a two-theme app.

---

## 3. Cross-cutting

### X1. Sync the website palette to the app palette — **SHIP-NOW (S, docs/CSS only)**

**Pitch.** `docs/site/assets/styles.css:5-16` holds a second palette that already drifts from `Theme.cs`:
page `#16121F` vs `#181220`, surface `#1D1729` vs `#1D1830`, muted `#C8BAD9` vs `#A89FBF` — the muted one
is visibly lighter and makes a screenshot of the site read as a different product.
**Sketch.** Copy the five values from `Theme.DarkPalette` (`Theme.cs:13-27`); keep `--accent: #8224d4`
(already identical). Add a comment: "values copied from src/FileAction.App/UI/Theme.cs — do not invent".
**Frame cost 0.** Not shipped code.

### X2. A source guard against hard-coded durations — **LATER**

**Pitch.** A unit test that fails when `TimeSpan.FromMilliseconds` appears outside `Motion.cs` (with an
allowlist for the tool-local preview timers).
**Why it fits.** Cheapest possible enforcement of motion Law 1 (`docs/CREATIVE-GUIDE.md` §7), CI-only, zero
runtime cost.
**Why LATER.** It fails today on four sites that must be fixed first (`docs/CREATIVE-GUIDE.md` §7 "known
debt"): `JobsWindow.cs:197-198` (220 ms), `JobsWindow.cs:220` (infinite 700 ms pulse),
`PasswordViewParts.cs:342` (120 ms), `VideoThemeWatcher.cs:30-33` (a 500 ms polling timer that is never
stopped). Make those three token swaps and one `Stop()` first; then the guard is a ~20-line test.

### X3. The reference sheet grammar for docs and the site — **SHIP-NOW (S, docs only)**

**Pitch.** Every screenshot section in `README.md`, `CHANGES.md` and `docs/site/index.html` gets the
reference's card grammar: **category eyebrow → one-line outcome → one sentence → two ticked facts**, in our
violet, with our screenshots.
**Why it fits.** It is the one thing the reference does better than us, and it is pure documents: no app
bytes. It also turns the four self-test folders (`en`, `en-light`, `ar`, `ar-light`, `STATUS.md:233-237`)
into a consistent set.
**Frame cost 0.** No app code.

### X4. Design tokens as code (`Theme.Space`, `Theme.Radius`) — **LATER**

**Pitch.** Turn the spacing table and radius ladder of `docs/CREATIVE-GUIDE.md` §3 into
`static class Space { const double Xs = 4; … }`.
**Why not now.** Zero perf effect, but it rewrites ~200 `Thickness`/`CornerRadius` literals across the UI in
the same week eight lanes are editing those files. The guide documents the scale; make it code after the
2.4.0 tag, in one branch.

### X5. `Theme.Focus`, `Theme.Keycap`, `Theme.StatusText` — **SHIP-NOW (S)**

**Pitch.** The three PROPOSED tokens in `docs/CREATIVE-GUIDE.md` §2.6, each a frozen brush set in
`Theme.cs`. They unblock T5 and W1, and they fix the light-mode badge contrast (2.28:1 today).
**Sketch.** Follow `AccentSoft` (`Theme.cs:102`) for the alpha-in-theme shape and `Accent` (`:69`) for the
freeze. `StatusText` needs a per-theme value, so it reads `IsDark` exactly like `AccentSoft` does.
**Frame cost 0.** Startup already reads two booleans (`Theme.Use`, `:130-135`); this adds no I/O.

---

## 4. Rejected by construction (full list)

| # | idea | the budget that kills it |
| --- | --- | --- |
| W9 | full-screen dim behind the wheel | extra topmost window on the hot path → cold start +5 % (`docs/PERF.md:6`) |
| W10 | confetti / particle confirmation | sustained per-frame work with no gate headroom; contradicts "no decorative motion" |
| W11 | per-slice drop shadows / glossy wedges | N effect passes per frame; surface already the slowest raster (`docs/PERF.md:47`) |
| W12 | live output preview in the hub | decode on open → +15 MB idle / 12 MB retained (`docs/PERF.md:7-9`) |
| T12 | per-tool accent hues | design: spends the single selection signal; five untested hue pairs |
| R1 | blurred / vibrancy glass on the disc | CPU-composited blur on an `AllowsTransparency` window; same raster budget as W11 |
| R2 | a bundled display font | installer +6 MiB rule (`docs/PERF.md:5`) |
| R3 | animated gradient/colour on the highlight | animating a brush colour allocates per frame ("no per-frame allocation", `docs/MOTION.md:111-113`) |
| R4 | spring-physics highlight follow | needs `CompositionTarget.Rendering` or a timer → "no polling timers" (`docs/MOTION.md:115`) |

---

## 5. Summary

| # | idea | verdict | effort |
| --- | --- | --- | --- |
| W1 | keycap tabs in the hub | SHIP-NOW | S |
| W2 | slice numerals for 1–9 | SHIP-NOW | S |
| W3 | subtitle only on the highlighted slice | SHIP-NOW (screenshots) | M |
| W4 | empty hub teaches the shortcut | SHIP-NOW | S |
| W5 | destination glyph on the "save to" line | SHIP-NOW | S |
| W6 | full file name in the hub tooltip | SHIP-NOW | S |
| W7 | armed-drop halo on the hub | LATER | S |
| W8 | type-to-filter in the hub | LATER | L |
| W9 | desktop dim behind the wheel | REJECT | — |
| W10 | confetti confirm | REJECT | — |
| W11 | per-slice shadows | REJECT | — |
| W12 | live output preview in the hub | REJECT | — |
| T1 | category eyebrow above the tool title | SHIP-NOW | M |
| T2 | Reset becomes a ghost button | SHIP-NOW | S |
| T3 | disabled primary looks disabled | SHIP-NOW | S |
| T4 | numeric readouts align right | SHIP-NOW | S |
| T5 | keyboard focus ring | SHIP-NOW | M |
| T6 | section labels become eyebrows | SHIP-NOW | S |
| T7 | close tooltip says "(Esc)" | SHIP-NOW | S |
| T8 | which file am I editing | SHIP-NOW | S |
| T9 | title bar shows it can be dragged | SHIP-NOW | S |
| T10 | recent-parameter chips | LATER | L |
| T11 | output thumbnail after Apply | LATER | M |
| T12 | per-tool accent hues | REJECT | — |
| X1 | website palette sync | SHIP-NOW | S |
| X2 | duration guard test | LATER | S |
| X3 | reference sheet grammar for docs/site | SHIP-NOW | S |
| X4 | spacing/radius tokens as code | LATER | L |
| X5 | three new theme tokens | SHIP-NOW | S |

**18 SHIP-NOW, 6 LATER, 9 REJECT** (W9–W12, T12 and the four mechanisms R1–R4). None of the 18 adds a
timer, a frame-proportional cost, startup work, a dependency or a byte.

---

## 6. If you have one hour before the tag

Ordered by (value ÷ risk); all SHIP-NOW, all in files no other lane is rewriting:

1. **X1** — website palette sync (two files; fixes how every screenshot reads).
2. **T2 + T3** — one primary per window, and a disabled button that looks disabled (honesty).
3. **T7 + T9** — two one-line affordances in the shared tool chrome.
4. **W1** — keycap tabs (the wheel's only undiscoverable control is the wheel switch).
5. **T6** — eyebrows in tool sections (one function).
6. **T5** — focus ring, if a reviewer can test keyboard traversal in `en` and `ar`; otherwise it is the
   first thing after the tag.

Do **not** start W3, T1 or X4 before the tag: they need screenshots (W3), a key-parity pass in two tables
(T1), or a repo-wide edit (X4).
