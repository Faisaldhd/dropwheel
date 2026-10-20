# DropWheel creative guide

The durable design language for DropWheel 2.4.0 and after. Read this before adding a window, a wheel
surface, a control, a colour, an icon or a line of microcopy.

**Authority.** This document describes the code that ships, not an aspiration. Every colour, size, radius,
duration and string below was read out of the source and is cited as `path:line`. Where a value does not
exist yet and is being proposed, the line says **PROPOSED** and gives the reason; nothing in this file
invents a token name and presents it as real.

**Verified against** `origin/main` `268a2d7` (branch `audit/acreative`). If a citation no longer matches,
the code changed: fix the citation before you follow the rule.

**The one-sentence stance.** Purple, dark-first, Arabic-first-ready, *light and fast*: quiet surfaces, one
accent, one primary action per surface, and nothing decorative that costs a frame.

---

## 1. What we took from Tangerine, and what we did not

The reference set (`_reference/tangerine-tools.pdf`, the two demo clips) is a macOS file-conversion app.
We studied **composition and control order**, not identity:

| Reference discipline | How it appears in DropWheel |
| --- | --- |
| The media preview is the first thing in a tool window, before any control | `ToolWindow` grid rows: title bar, tool view, action bar (`src/FileAction.App/UI/Tools/ToolWindow.cs:233-242`) |
| Almost no chrome: a small title, a close control, no menu strip | `ToolWindow.cs:289-332`; the wheel has no title bar at all (`src/FileAction.App/Wheel/WheelWindow.cs:92-108`) |
| One accent, used for the selected thing and the primary action only | `Theme.AccentGradient` on the active slice, active pill, primary button (`src/FileAction.App/UI/Controls.cs:99`, `:256`; `WheelWindow.cs:1004`) |
| Control rows read *label → control → value*, value flush to the edge | Value column pattern in `Controls.Slider` (`Controls.cs:175-236`); right-aligned readout at `:178` |
| A radial menu whose labels are outcomes (PDF, PNG, HEIC), not verbs | Format slices render the code as type, no glyph (`WheelWindow.cs:864-896`) |
| Motion that confirms, never performs: short, decelerating, gone | Whole token set in `src/FileAction.App/UI/Motion/Motion.cs:30-81` |

The identity is ours: **violet**, not tangerine; **dark-first**, not warm paper; **Windows 11**, not macOS
chrome. Section 10 lists what we deliberately do not copy.

---

## 2. Colour

### 2.1 The accent is a constant

The accent does **not** change with the theme. It is the same violet in dark and light:

| token | value | cite |
| --- | --- | --- |
| `Theme.AccentColor` | `#8224D4` | `src/FileAction.App/UI/Theme.cs:63` |
| `Theme.AccentColor2` | `#B48AE0` (lavender highlight) | `Theme.cs:64` |
| `Theme.Accent` | frozen `SolidColorBrush` of `AccentColor` | `Theme.cs:69` |
| `Theme.AccentGradient` | frozen `LinearGradientBrush`, currently **two equal stops** = flat violet | `Theme.cs:67` |
| `Theme.TextOnAccent` | white | `Theme.cs:70` |
| `Theme.AccentSoft` | accent at alpha `0x38` dark / `0x26` light | `Theme.cs:102` |

Two consequences worth stating because contributors get them wrong:

- `AccentGradient` is a gradient *type* filled with one colour (`Theme.cs:67` passes `AccentColor` for both
  stops). Treat it as "the accent fill". Do not add a second stop without a measured need and a screenshot in
  both themes.
- The accent is an **indicator**, not a surface. It fills one slice, one pill, one primary button,
  one toggle knob track. Large accent areas are forbidden: on the light page the accent reaches only
  **2.75:1** as text on the dark page and reads fine as a fill, but as a big block it destroys the hierarchy
  the whole app is built on.

Measured contrast of the accent (sRGB, WCAG 2.x relative luminance):

| pair | ratio | verdict |
| --- | ---: | --- |
| white on `#8224D4` | **6.66:1** | passes AA normal text — this is why `TextOnAccent` is white |
| `#8224D4` on dark page `#181220` | 2.75:1 | **fails** — never as body text in dark |
| `#B48AE0` on dark page `#181220` | 6.68:1 | pass — use the lavender when accent text is needed in dark |
| `#8224D4` on light page `#F4F1F9` | 5.96:1 | pass |

### 2.2 Dark palette (default)

`Theme.DarkPalette`, `Theme.cs:13-27`, exposed as frozen brushes at `Theme.cs:89-102`. `Theme.IsDark`
defaults to `true` (`Theme.cs:87`) and follows the Windows app mode unless Settings forces a theme
(`Theme.cs:113-135`).

| brush | ARGB | reads as | primary use (cite) |
| --- | --- | --- | --- |
| `Theme.Backdrop` | `E6 15 11 22` | near-black violet, 90 % | wheel disc floor `WheelWindow.cs:768` |
| `Theme.BackdropEdge` | `33 B7 8A E7` | faint lavender rim, 20 % | disc outline `WheelWindow.cs:769` |
| `Theme.Slice` | `FF 24 1A 35` | raised violet-slate | unselected slice `WheelWindow.cs:788` |
| `Theme.SliceEdge` | `26 C0 8C F0` | 15 % lavender hairline | slice stroke `WheelWindow.cs:789` |
| `Theme.Hub` | `FF 1A 15 28` | darkest violet | centre disc `WheelWindow.cs:825` |
| `Theme.Panel` | `F7 1D 18 30` | glass body, 97 % | tool chrome glass source `ToolWindow.cs:227-228` |
| `Theme.PanelBorder` | `2E B7 8A E7` | 18 % lavender | 1 px borders everywhere (`Controls.cs:80`, `ToolWindow.cs:248`) |
| `Theme.Text` | `F5 F2 FA` | warm white | primary text |
| `Theme.Muted` | `A8 9F BF` | lavender grey | captions, subtitles |
| `Theme.Track` | `33 C0 8C F0` | 20 % lavender | segmented track, close button disc |
| `Theme.Page` | `18 12 20` | page | Settings background `SettingsWindow.cs:163` |
| `Theme.Card` | `23 21 31` | card | settings cards, tool cards |
| `Theme.Hover` | `1F C0 8C F0` | 12 % lavender | hover wash (reserved) |
| `Shadow` (not a brush) | `0.6` | shadow opacity | `Theme.Shadow` |

### 2.3 Light palette

`Theme.LightPalette`, `Theme.cs:29-43`.

| brush | ARGB | reads as |
| --- | --- | --- |
| `Theme.Backdrop` | `F0 F6 F3 FB` | white-lavender glass, 94 % |
| `Theme.BackdropEdge` | `1F 3D 35 5E` | 12 % ink rim |
| `Theme.Slice` | `FF FF FF FF` | pure white slice |
| `Theme.SliceEdge` | `1F 6A 2B B8` | 12 % deep violet hairline |
| `Theme.Hub` | `FF FF FF FF` | white hub |
| `Theme.Panel` | `FA FB F9 FE` | near-white glass |
| `Theme.PanelBorder` | `1F 3D 35 5E` | 12 % ink |
| `Theme.Text` | `1E 15 30` | near-black violet |
| `Theme.Muted` | `6E 66 80` | ink grey |
| `Theme.Track` | `1F 6A 2B B8` | 12 % violet |
| `Theme.Page` | `F4 F1 F9` | lavender page |
| `Theme.Card` | `FF FF FF` | white card |
| `Theme.Hover` | `14 6A 2B B8` | 8 % violet |
| `Shadow` | `0.22` | — |

Note the deliberate structural difference: in dark the *edges* glow (light hairline on dark), in light the
edges *ink in* (dark hairline on white). Never reuse a dark alpha on light. `Slice`/`Hub` are the same
`#FFFFFF` in light — that is intentional; separation comes from `SliceEdge`, not from a second fill.

### 2.4 Semantic colours

| token | value | rule |
| --- | --- | --- |
| `Theme.Error` | `#F05A5A` (`Theme.cs:71`) | error text, warning destination line (`WheelWindow.cs:1042`). Never a fill. |
| `Theme.Success` | `#3CC36E` (`Theme.cs:72`) | the "good" badge label (`Controls.cs:291`) |
| `Theme.LockBadgeColor` | `#8224D4` (`Theme.cs:75`) | Lock badge body, both themes |
| `Theme.LockBadgeHighlightColor` | `#B48AE0` (`Theme.cs:77`) | Lock badge highlight |
| `Theme.LockBadge` / `LockBadgeHighlight` | frozen solids (`Theme.cs:79-81`) | the badge is drawn from these, or rasterised from `branding/` |

**Measured problem you must design around (and can fix cheaply):** the badge construction
(`Controls.Badge`, `Controls.cs:286-292`) draws the *same hue* as its own 15 % tint. Measured on a light
card: `Success` on white **2.28:1**, `Error` on white **3.33:1** — both below 4.5:1. On dark cards they are
6.92:1 and 4.74:1, i.e. fine. Until a light-mode status *text* token exists (PROPOSED, §2.6), do not put
`Success`/`Error` text on a light surface at body size, and do not use them for anything the user must read
rather than glance at.

### 2.5 Cross-surface sync: the website is a second palette

`docs/site/assets/styles.css:5-16` keeps its own copy. It already drifts:

| role | app (Theme.cs) | site (styles.css) | delta |
| --- | --- | --- | --- |
| page / bg | `#181220` | `#16121F` | 3 units |
| panel / surface | `#1D1830` | `#1D1729` | 7 units in blue |
| text | `#F5F2FA` | `#F3EDFB` | 5 units |
| muted | `#A89FBF` | `#C8BAD9` | **32 units — visibly lighter** |
| accent | `#8224D4` | `#8224D4` | none — correct |

Rule: the site may have *fewer* tokens, never *different* values. When you touch either file, copy the
value from `Theme.cs` and note the pair in the PR. (A docs-only sync is a zero-cost change; see
`docs/IDEAS.md` X3.)

### 2.6 PROPOSED tokens (not in the source yet)

Each is a **static** addition: a frozen brush or two constants, resolved at startup, no per-frame cost.

| proposed | shape | why |
| --- | --- | --- |
| `Theme.Focus` | frozen `Pen`-able brush, accent at full alpha | custom-drawn controls (`Controls.Button`, `ToolControls.Chips`, `Toggle`) have **no visible keyboard focus** today; `Motion.Interactive` only animates opacity/scale, never focus. A 1.5 px accent ring drawn on `IsKeyboardFocused` closes a real accessibility gap at zero frame cost. |
| `Theme.Keycap` | brush pair: fill `Track`, border `PanelBorder`, text `Text` | the wheel's modifier tabs (`WheelWindow.cs:948-961`) and any shortcut hint need one consistent keycap look; see `docs/IDEAS.md` W1. |
| `Theme.StatusText` | one brush **per theme** per status (`Error`, `Success`) | fixes the 2.28:1 / 3.33:1 light-mode problem in §2.4 without changing the badge background. Light value should be the status hue darkened until it reaches ≥ 4.5:1 on `Theme.Card`. |
| `Theme.Divider` | 1 px, `PanelBorder` at half alpha | section separation inside a card when a card cannot be split; today contributors invent `Track` borders. |

Do **not** add `Theme.Primary`, `Theme.Secondary`, `Theme.Background`, `Theme.Foreground` or Material-style
naming. The palette is named by *where it is used* (Backdrop, Slice, Hub, Panel, Page, Card, Track), and that
is why one a second contributor can read `WheelWindow.Build` and know what each brush means.

---

## 3. Space and shape

### 3.1 Spacing scale

There is **no spacing token class** — values are inline `Thickness`. A count of the first `Thickness`
argument across `src/FileAction.App/**/*.cs` gives the de-facto scale by frequency:
`0` ×294, `2` ×40, `8` ×34, `10` ×24, `12` ×17, `18` ×12, `6` ×9, `14` ×7, `16` ×5, `4` ×3, `32` ×4,
`24` ×1. That is the whole vocabulary; anything else is a one-off. The scale, by role:

| step | meaning | where it is visible |
| ---: | --- | --- |
| 0 | flush | most `Margin` left/top |
| 2 | hairline separation | card title→description `Controls.cs:44`; segmented track padding `Controls.cs:130`; badge text pad `Controls.cs:289` |
| 4 | between stacked cards | card bottom margin `Controls.cs:84`; chip gap `ToolControls.cs:168` |
| 6 | inside a chip row / label→control | `ToolControls.cs:63` |
| 8 | standard inner gap | tool card padding top/bottom `ToolControls.cs:54`; checkbox label `ToolControls.cs:103` |
| 10 | card padding (compact) | `ToolControls.cs:54` (`12,10,12,10`) |
| 12 | card padding x (compact), chip padding x | `ToolControls.cs:54`, `:167` |
| 14 | settings card padding y, button padding x | `Controls.cs:83` (`18,14,18,14`), `:263` (`14,6,14,7`) |
| 16 | icon→text, control→label in a card row | `Controls.cs:58`, `:66` |
| 18 | settings card padding x, tool chrome gutters | `Controls.cs:83`; `ToolWindow.cs:322`, `:350` |
| 26 | section separation above a header — the one deliberate off-scale value | `Controls.cs:33` (`2,26,0,8`) |
| 32 | page gutter (settings failure panel) | `SettingsWindow.cs:150` |

Rule: pick from this list. If a value is not on it, the layout is probably wrong, not the scale. The two
`28`s in the source are shadow blur radii (`WheelWindow.cs:770`, `ToolWindow.cs:254`), not spacing.

### 3.2 The wheel is geometry, not layout

`WheelWindow.cs:30-39`:

| constant | value | note |
| --- | ---: | --- |
| `OuterRadius` | 196 | disc outer |
| `InnerRadius` | 80 | hub radius |
| `Rim` | 9 | ring thickness outside the outer radius |
| `Gap` | 6 | constant-width gap between slices, applied as an *angle* offset (`WheelWindow.cs:700-731`) |
| `Corner` | 11 | slice corner radius, clamped by the arc length |
| `PopOut` | 7 | how far the highlighted slice travels along its mid-angle |
| `Pad` | 16 | room for the shadow inside the window |
| `Size` | 442 | `2 * (OuterRadius + Rim + Pad)` — the 442×442 measured in `docs/PERF.md:47` |

Label geometry: the label sits at 54 % of the ring (`WheelWindow.cs:780`) and is clamped to
`48…110 px` wide (`:781`), so a long label shrinks instead of spilling. Do not change those two numbers
without a screenshot of a 12-slice wheel in Arabic.

### 3.3 Radius ladder

| radius | use | cite |
| ---: | --- | --- |
| 2 | progress track, slider fill | `Controls.cs:179`, `:191` |
| 4 | checkbox box | `ToolControls.cs:96` |
| 6 | status badge | `Controls.cs:288` |
| 7 | segmented pill, chip pill | `Controls.cs:109`, `ToolControls.cs:166` |
| 8 | button, wheel tab, text box (via control height) | `Controls.cs:262`, `WheelWindow.cs:950` |
| 9 | segmented/chipping track | `Controls.cs:129`, `ToolControls.cs:187` |
| 10 | card, toggle track | `Controls.cs:82`, `:143`; `ToolControls.cs:53` |
| 16 | round close button (32×32) | `ToolWindow.cs:295` |
| 20 | tool-window chrome | `ToolWindow.cs:246` |

Nesting rule: a control inside a **10**-radius card uses **7–8**; inside the **20**-radius chrome the
outermost button is **8** and the round close is **16**. Never nest two 10s; never put a 20 inside a 10.

### 3.4 Card anatomy

Two card constructions exist and both are correct for their context:

- **Settings / wheel-editor card** — `Controls.Card` (`Controls.cs:37-87`): padding `18,14,18,14`, 1 px
  `PanelBorder`, `Theme.Card` fill, 10 radius, `Margin(0,0,0,4)`, optional 18 px leading glyph, control
  docked right with a 16 px gap.
- **Tool card** — `ToolControls.Card` / `Section` (`ToolControls.cs:48-74`): padding `12,10,12,10`, same
  1 px border, same 10 radius, muted 12 px title, optional 11.5 px hint at 85 % opacity.

A tool window's content root is a vertical stack of tool cards. A settings page is a vertical stack of
settings cards under `SectionHeader`s. Do not mix them inside one scroll area.

---

## 4. Typography

### 4.1 Families

| token | family string | cite |
| --- | --- | --- |
| `Theme.UiFont` | `Segoe UI Variable Text, Segoe UI` | `Theme.cs:84` |
| `Theme.DisplayFont` | `Segoe UI Variable Display, Segoe UI` | `Theme.cs:85` |
| `Theme.Icons` | `Segoe Fluent Icons, Segoe MDL2 Assets` | `Theme.cs:83` |

No custom font ships. That is a package-size decision (the +6 MiB installer budget,
`docs/PERF.md:5`) and a startup decision (no font file to load), not a taste decision. Display is used for
*labels the user reads as a name* (section headers, slice labels); Text is used for everything else.

### 4.2 Roles

| role | size / weight / family | colour | cite |
| --- | --- | --- | --- |
| Window title (tool) | 15 SemiBold / UiFont | `Text` | `ToolWindow.cs:312-320` |
| Page section header | 15 SemiBold / **DisplayFont**, margin `2,26,0,8` | `Text` | `Controls.cs:26-34` |
| Wheel hub title | 16 SemiBold | `Text` | `WheelWindow.cs:55` |
| Slice label (operation) | 13 SemiBold / **DisplayFont** | `Text` / `TextOnAccent` when active | `WheelWindow.cs:890-891` |
| Slice label (format code) | 19 SemiBold / **DisplayFont**, forced LTR | same | `WheelWindow.cs:864`, `:872`, `:890` |
| Body | 14 Normal | `Text` | `Controls.cs:16`, `:40` |
| Control label | 12.5–13 | `Text` | `Controls.cs:106`, `:255`; `ToolControls.cs:101` |
| Card description | 12 | `Muted` | `Controls.cs:43` |
| Muted caption / hint | 12 / 11.5 at 85 % | `Muted` | `ToolControls.cs:15`, `:67-69` |
| Wheel subtitle | 10.5 | `Muted` (85 % when active) | `WheelWindow.cs:899-909` |
| Wheel tab / keycap | 10.5 SemiBold | `Muted` → `TextOnAccent` active | `WheelWindow.cs:958-960` |
| Destination line | 10.5 | `Muted`, `Error` when the folder refuses | `WheelWindow.cs:57`, `:1042` |
| Status badge | 11.5 SemiBold | `Success` / `Error` | `Controls.cs:291` |
| Tools-only large number | 48/26/24/20 | depends | prefer 20 max outside the wheel hub |

### 4.3 Rules

- **Sentence case everywhere.** "Save next to the file", not "Save Next To The File". The only uppercase
  strings are format identifiers (`MP4`, `PNG`, `PDF`) and the wheel's slice codes, which are *data*
  (`WheelWindow.cs:864`). PROPOSED eyebrows (§11, X5) are the sole stylistic exception.
- **Wrap or elide, never clip.** `Controls.Text` wraps by default and switches to
  `TextTrimming.CharacterEllipsis` when `wrap: false` (`Controls.cs:22-23`). A single-line `TextBlock`
  created by hand must set trimming itself.
- **Numbers are Western and invariant.** `Controls.Slider` formats with
  `CultureInfo.InvariantCulture` (`Controls.cs:201-202`) so Arabic shows `0–100`, not `٠١٢`. Follow
  `docs/glossary-ar.md:78`.
- **Values align right, labels align left (start).** The slider readout is a 40 px right-aligned block
  (`Controls.cs:178`). In RTL the alignment mirrors with `FlowDirection`, so do not hard-code
  `TextAlignment.Right` on a label; use `Right` only for numeric readouts.
- **Emphasis is weight or colour, never decoration.** `SemiBold` for the active state
  (`Controls.cs:101`), `Muted` for de-emphasis. No underline links, no italics.
- **One type size per job.** If two adjacent elements differ by less than 1 px, they are one role.

---

## 5. Elevation

Shadows are a **theme function**, not per-window art:

```
Theme.Shadow(blur)  -> DropShadowEffect { BlurRadius = blur, ShadowDepth = 3, Direction = 270, Opacity = Shadow }
Theme.Glow()        -> DropShadowEffect { BlurRadius = 14, ShadowDepth = 0, Opacity = 0.45 dark / 0.3 light, Color = AccentColor }
```
(`src/FileAction.App/UI/Theme.cs:105-110`.)

| level | element | effect | cite |
| --- | --- | --- | --- |
| L0 | cards, buttons, chips, badges | none | `Controls.cs:77-86`, `ToolControls.cs:48-56` |
| L1 | floating surface | `Theme.Shadow(28)` wheel disc, `Theme.Shadow(12)` hub | `WheelWindow.cs:770`, `:829` |
| L1 | tool window chrome | `BlurRadius 28, ShadowDepth 6, Opacity 0.35` (its own literal — the one place not using `Theme.Shadow`) | `ToolWindow.cs:252-257` |
| L2 | the highlighted slice | `Theme.Glow()` | `WheelWindow.cs:1006` |
| L2 | slider thumb | `Theme.Shadow(6)` | `Controls.cs:188` |

Rules:

- **One shadow per floating surface.** The wheel's shadow lives on the disc ellipse, the tool window's on
  the chrome border; nothing inside them casts a shadow except the slider thumb (a 18 px object that must
  read as draggable).
- **A glow means "this is the selection".** It is applied in `Paint(index, true)` and removed in
  `Paint(index, false)` (`WheelWindow.cs:1001-1012`) — it is state, not decoration. Never glow a hover.
- **Light mode is flatter.** `Shadow` is `0.22` in light vs `0.6` in dark (`Theme.cs:27`, `:43`). Do not
  compensate with a heavier blur.
- **No inner shadows, no bevels, no specular strokes.** The reference's glossy wedges are exactly what
  makes it feel like a different product.
- Elevation is a *property write*, not an animation: nothing in the app animates `BlurRadius` or
  `Opacity` of an effect. Keep it that way (animating an effect property re-renders the element every
  frame on the CPU).

---

## 6. Iconography

- **Family:** Segoe Fluent Icons with the MDL2 fallback (`Theme.cs:83`). Both ship with Windows; nothing is
  bundled. Consequence: a glyph must exist in **both** code pages, or it renders as a box on older builds.
- **Catalogue:** every wheel/tool icon is a named constant in `src/FileAction.Core/Glyphs.cs:6-37`
  (`Glyphs.Photo = "\uE91B"`, `Glyphs.Extract = "\uE896"`, …). Never inline a code point in a window; add it
  to `Glyphs` with a name that says the *action*, not the picture.
- **Sizes:** 10 in the checkbox tick (`ToolControls.cs:86`), 12 in buttons (`Controls.cs:249`), 18 in
  settings cards (`Controls.cs:55`), 20 on wheel slice labels (`WheelWindow.cs:880`), 24 in the wheel hub
  (`WheelWindow.cs:54`).
- **Colour:** glyphs inherit the text colour of their slot — `Text` on cards and slices, `TextOnAccent` on a
  primary button (`Controls.cs:242-253`), `Accent` for the hub's empty/repeat state
  (`WheelWindow.cs:924`, `:937`). A glyph is never `Muted` unless its label is.
- **Alignment:** a leading glyph takes a 7 px right margin in a button (`Controls.cs:252`) and 16 px in a
  card row (`Controls.cs:58`). Trailing glyphs are not used.
- **One icon per action.** No icon families, no illustration, no emoji, no SVG. The app draws with
  `Path`/`Ellipse`/glyph text only (`WheelWindow.cs:700-741`).
- **Format codes are type, not icons.** `isFormat` slices render 19 px text with no glyph and forced LTR
  (`WheelWindow.cs:864-896`) because `MP4` is an identifier that must not mirror in Arabic.
- **Image vs glyph:** the hub shows a file-type *image* when items exist and a glyph otherwise
  (`WheelWindow.cs:920-939`). Never show both at once.

---

## 7. Motion

The token set, the curves and the per-surface tables live in `docs/MOTION.md` and
`src/FileAction.App/UI/Motion/Motion.cs:30-98`. Two laws govern every surface; both are enforced in code,
not by review alone.

### Law 1 — only `RenderTransform` and `Opacity` are animated

- Every duration lives in `Motion.cs:30-81`: `WheelOpenMs 150`, `WheelCloseMs 100`, `StaggerMs 18`,
  `SliceMs 150`, `HighlightMs 100`, `PressMs 90`, `SwitchMs 170`, `WindowOpenMs 180`, `WindowCloseMs 120`,
  `ResizeMs 180`, `HoverMs 100`, `ToggleMs 120`, `PulseMs 320`, `PopMs 260`, plus the distances
  `RisePx 8`, `WindowScaleFrom 0.97`, `WheelScaleFrom 0.88`, `SwitchRotateDegrees 4`.
- The transforms a surface animates are a `ScaleTransform` + `TranslateTransform` pair
  (`Motion.Prepare`, `Motion.cs:164-178`); the wheel adds a `RotateTransform` on the disc
  (`WheelWindow.cs:117-122`). Poses are plain property writes (`WheelWindow.Pose`, `:471-511`;
  `ToolWindow.Pose`, `:209-223`) — that is what makes the self-test measurable.
- **The single exception** is `Motion.Resize` (`Motion.cs:561-593`), the media-orientation resize of a tool
  window, which animates `Window.Width/Height/Left/Top` for 180 ms because the layout *is* the transition.
  Do not add a second one. If you think you need one, the design is wrong.
- Never animate: `Margin`, `Padding`, `Width`/`Height` of an element, `Color` of a brush, `BlurRadius`,
  `CornerRadius`, `GridLength`, or a layout transform. The wheel uses a `LayoutTransform` **once** for
  fit-to-work-area (`WheelWindow.cs:247`) and never animates it.

### Law 2 — reduced motion starts no storyboard

- `Motion.Enabled` is `Override ?? (settingsToggle && SystemParameters.ClientAreaAnimation)`
  (`Motion.cs:121`), read live and re-read on `SystemParameters.StaticPropertyChanged`
  (`Motion.cs:139-147`). Settings toggle: `AppSettings.Animations` via `Motion.SetSettingsEnabled`
  (`Motion.cs:107`).
- "No storyboard" means exactly that: callers take an instant path *before* creating one. The canonical
  shape is `if (!Motion.Enabled) { set final pose; return; }` — see `ToolWindow.StartOpenMotion`
  (`ToolWindow.cs:126-137`), `WheelWindow.ShowAt` (`:194-203`), `Motion.Slide`/`Fade` (`Motion.cs:380-408`),
  `Motion.Interactive` whose handlers check `Enabled` *inside* the handler so a settings change takes effect
  without a rebuild (`Motion.cs:467-492`).
- Reduced motion must still look finished: final pose, final size, no success-pulse delay
  (`ToolWindow.cs:376-384`), `Motion.Resize` becomes a single layout change (`Motion.cs:568-575`).
- Nothing polls to decide this. An animation that is off must cost nothing.

### How to use it

- Any hover/press affordance: call `Motion.Interactive(element)` once
  (`Motion.cs:467`; used by `Controls.Button`, `Segmented`, `Toggle`, `Slider`, `ToolWindow` close).
- Custom chips built by hand: `Motion.Interactive(chip)` — stated as the one obligation of a new tool in
  `docs/MOTION.md:67-70`.
- A new duration or curve is a **token change** in `Motion.cs`, reviewed as such. Do not pass millisecond
  literals.

### Known debt (do not add more)

| site | what it hard-codes | why it matters |
| --- | --- | --- |
| `src/FileAction.App/Jobs/JobsWindow.cs:197-198` | 220 ms rise + fade | should be `WindowOpenMs`/`RisePx` |
| `src/FileAction.App/Jobs/JobsWindow.cs:220` | 700 ms **infinite** `AutoReverse` opacity pulse | a perpetual compositor animation while a job runs; not covered by the motion gate because the gate measures open/close |
| `src/FileAction.App/UI/Tools/Password/PasswordViewParts.cs:342` | 120 ms | should be `HoverMs`/`WindowCloseMs` |
| `src/FileAction.App/UI/Tools/Video/VideoThemeWatcher.cs:30-33` | 500 ms polling timer, **never stopped** (no `Stop()`; started by three video views) | contradicts `docs/MOTION.md:115` ("no polling timers") and is the only timer that outlives its surface |

---

## 8. RTL and bilingual layout

- **The disc does not mirror.** The wheel canvas is pinned to `LeftToRight` ("the disc is circular and its
  slices run clockwise; mirroring it would move every slice"), while the hub content and the tabs follow the
  app language (`WheelWindow.cs:110-114`, repeated on relocalize at `:298-300`). Same rule for the
  `FlowDirection` of the self-test root: it follows the language, the disc under it does not
  (`WheelWindow.cs:438`, `:459`).
- **Every window** gets its direction from `LanguageManager.FlowDirection`
  (`src/FileAction.App/UI/LanguageManager.cs:22-24`) through `UiSetup.Configure`
  (`src/FileAction.App/UI/UiSetup.cs:15-23`). Do not set `FlowDirection` on a window by hand; call
  `UiSetup.Configure`. A tool window that skips it will not mirror its action bar.
- **Dynamic values are isolated.** Wrap any file name, size, percentage, format or time that lands in a
  sentence with `Bidi.Value(...)` → `U+2066 … U+2069` (`src/FileAction.App/UI/Bidi.cs:12-24`). In LTR the
  call is a no-op, so it is unconditional at the call site. `Bidi.Number` for whole numbers.
- **Tooltips need their own direction** because they are not in the window tree yet — use
  `UiSetup.Tip(text)` (`UiSetup.cs:103-107`), never `new ToolTip { Content = … }`.
- **Numbers, units, key names and format codes stay Latin.** `Shift`, `Ctrl`, `Esc`, `MP4`, `0.25x`,
  `1920×1080` (`docs/glossary-ar.md:86`; `WheelWindow.cs:864`; `AppSettings.KeyName`).
- **Punctuation follows the language**, including `،` and `؟` and Arabic quotes `«»`
  (`docs/glossary-ar.md:83-87`). The em dash in a window title is `–` (en dash) in both languages:
  `settings.title` = `DropWheel – settings` / `DropWheel – الإعدادات`.
- **Plural categories** are six in Arabic (`zero/one/two/few/many/other`) and the tables carry all six for
  every plural key (`docs/glossary-ar.md:63-76`). Use `Loc.Plural`, never string concatenation.
- **Directional arrows** use `←` in the logical direction of the text; do not mirror an arrow glyph in code.
- **Test both.** The self-test renders `en`, `en-light`, `ar`, `ar-light`
  `STATUS.md:233-237`); a change that only looks right in LTR is not done.

---

## 9. Tone of voice

The product speaks like a competent colleague who is already holding the file: short, calm, specific,
never impressed with itself.

### English

| rule | do | don't |
| --- | --- | --- |
| Sentence case, full stop only when it is a sentence | `Drag a file here` | `Drag A File Here!` |
| Imperative for actions, noun for places | `Apply`, `Reset`, `Save next to the file` | `You can apply…` |
| Name the outcome, not the mechanism | `Convert to MP4` | `Run conversion pipeline` |
| Say what happened and what is next | `Can't save here, will save next to the file` | `Error: EACCES` |
| Contractions are fine; exclamation marks never are | `Can't`, `isn't`, `won't` | `Oops!`, `Uh-oh` |
| Numbers and limits are exact | `Speed must be a number between 0.25 and 8.` | `Invalid speed.` |
| Never blame the user | `Nothing selected` | `You didn't select anything` |
| Titles use an en dash | `DropWheel – error` | `DropWheel - error` |

Live examples to copy from the shipped table: `wheel.dropHere` = `Drag a file here`;
`wheel.nothingForSelection` = `Nothing for this selection`; `wheel.repeat.noFit` = `Last action doesn't fit`;
`error.output.dropFolderFallback` = `That folder can't be written to, so the result was saved next to the
file.`; `tool.window.done` = `{0} saved.`; `job.download.prompt` = `This conversion needs {0} ({1} download).
Download it now? It's a one-time step.`

### Arabic

MSA, Windows terminology, no dialect, no diacritics except where the word needs them.

- Follow `docs/glossary-ar.md` exactly: العجلة (wheel), قطاع (slice), الرف العائم (floating shelf),
  علبة النظام (tray), تحويل (convert), ضغط (compress), اقتصاص (trim/crop), دمج (merge), تقسيم (split),
  بيانات وصفية (metadata), مجلد الإخراج (output folder), تنزيل (download — Windows term, not تحميل).
- Keep `DropWheel` untranslated; keep Latin key names and format codes as they are.
- Isolate values: `تنزيل ⁦{0}⁩` — the tables already carry the isolates (`docs/glossary-ar.md:80-81`); write
  `T("… {0} …", Bidi.Value(x))` and let the table hold the punctuation.
- Short is better than elegant: `اسحب ملفًا إلى هنا`, `لا شيء يناسب هذا التحديد`, `تم الحفظ`,
  `إعادة ضبط`, `تطبيق`.
- Two lines maximum for a description, one line for a button. If the Arabic needs three lines, the English
  was too long.

### Length budgets (from the layout, not from taste)

| surface | budget | why |
| --- | --- | --- |
| Slice label | ≤ 2 words, shrinks to 48–110 px | `WheelWindow.cs:781` |
| Slice subtitle | ≤ 24 chars, ellipsis | `WheelWindow.cs:906` |
| Wheel hub title | ≤ 24 chars, ellipsis at `InnerRadius*1.65` | `WheelWindow.cs:752` |
| Tool title | one line, must survive trimming in a 480–520 px window | `ToolWindow.cs:312-320` |
| Card description | ≤ 2 lines at 12 px in a 680 px settings window | `SettingsWindow.cs:57` |
| Button | ≤ 18 chars + optional glyph | `Controls.cs:263` |

---

## 10. What we deliberately do not copy

The reference is an influence, not a template. These are off the table:

1. **The warm tangerine/orange identity.** Ours is violet `#8224D4`; the reference's accent hue, its
   gradient backdrop and its marketing chrome are theirs.
2. **The mesh-gradient wallpaper behind the wheel.** It is the reference's brand surface. Our wheel floats
   over whatever the desktop shows, on a 90 % violet-glass disc (`Theme.Backdrop`, `WheelWindow.cs:763-774`).
3. **macOS window chrome** (traffic lights, centred compact title, translucent tinted body). We use a
   Windows 11 card: 20 radius, 1 px border, DWM backdrop (`ToolWindow.cs:225-259`, `:455-469`).
4. **Their glossy/bevelled wedges** with specular strokes and per-slice sheen. Our slices are flat fill +
   1 px hairline + optional accent glow (`WheelWindow.cs:785-791`, `:1004-1006`).
5. **The blue "file being dragged" chip** with its drop shadow and green plus badge. We say the destination
   in the hub in words (`WheelWindow.SetDestination`, `:1038-1044`).
6. **Their icon set and illustration style.** We use Segoe Fluent glyphs only (§6).
7. **Marketing-sheet grammar inside the app** — eyebrow + headline + two ticked feature bullets. That
   grammar belongs on the website and in this repo's docs, not in a 442 px radial menu.
8. **Their copy.** No line from the reference appears in `en.json` or `ar.json`; the voice in §9 is written
   for a Windows utility that never uploads a file.
9. **Long, preview-first windows that push the action below the fold.** Our action bar is pinned
   (`ToolWindow.cs:334-355`); the preview may scroll, the primary action may not.
10. **Any decorative motion** — sheen sweeps, idle breathing, particle confirmations (§7 Law 2, §11
    rejection list in `docs/IDEAS.md`).

---

## 11. Review checklist

Before a UI PR is reviewable:

1. New colours? They are named in `Theme.cs` for **both** palettes and cited in the PR. No literal `#RRGGBB`
   in a window.
2. New radius/spacing? It is on the ladders in §3.
3. New text? `en.json` **and** `ar.json`, same key, same order, placeholders identical, and
   `LocaleUsageTests` still green (`tests/FileAction.Tests/LocaleUsageTests.cs:67-89`).
4. New glyph? It is in `Glyphs.cs`, and it exists in both Fluent and MDL2.
5. New duration? It is a token in `Motion.cs`; the surface checks `Motion.Enabled` before building a
   storyboard; only `RenderTransform`/`Opacity` move.
6. Keyboard: every custom control is reachable and shows focus (see PROPOSED `Theme.Focus`, §2.6).
7. RTL: opened in `ar` and `ar-light`; nothing clipped, numbers Latin, no mirrored disc.
8. Reduced motion: the surface looks finished with animations off (`--self-test` measures both).
9. Elevated? One shadow per floating surface (§5).
10. Zero-cost claim: if the PR says "no perf cost", it adds no timer, no per-frame work, no startup work and
    no dependency — and the CI motion gate still reports UI frame p95 in the 0.0x ms range
    (`docs/PERF.md:47-52`).

---

## 12. Adding a surface (recipes)

**A new tool window** (26 registered, all 26 reachable from the wheel as of `4f23c9c` — see `docs/IDEAS.md` §0): implement
`IToolWindowSpec` and register it; build the view as a vertical stack of `ToolControls.Card`/`Section`
cards with the preview first; put the primary action in the window's own bottom bar by returning a primary
label. You inherit open/close/resize/reduced-motion from `ToolWindow` (`ToolWindow.cs:99-123`) and the
motion hook at `:125-137`; you must not add your own storyboards. Measure the surface: it appears in the
self-test table automatically because the harness enumerates `ToolRegistry.All` (`docs/PERF.md:55-57`).

**A new wheel operation**: a `Operation` with `Glyph` from `Glyphs` (or none if it is a format code),
`Label`, optional `Subtitle`, `Wheel`, `AppliesTo`/`IsAvailable`; add it to
`WheelController.BuildOperations()` (`src/FileAction.App/Wheel/WheelController.cs:88-118`) — a tool that
is registered but missing from that list is invisible, which is exactly the gap that hid `tools.annotate`
and `tools.redact` until `4f23c9c` closed it.

**A new settings page**: a `ScrollViewer` page built from `Controls.SectionHeader` + `Controls.Card`,
added through `AddTab` in `SettingsWindow` (`SettingsWindow.cs:186-190`), plus a self-test hook if the page
needs a screenshot.
