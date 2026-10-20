# DESIGN-SPEC — reference design language, measured

Owner: site-design. Lane: site. Purpose: the numbers the lead applies to `docs/site/index.html` /
`assets/styles.css`. Design width **1440px**. All values are traceable to a rendered tile.

## 0. How these numbers were produced (read this before quoting them)

- Reference PDF rendered at 2x by `_local/tmp/site/ref/render_pdf.py` into `p0_full.png`
  (1080×41404, tall narrow page) and `p1_full.png` (2160×14950) plus 1400px-tall tiles
  `p1_t00_y0.png … p1_t11_y14080.png`. The PDF has **no text layer** — it is a screenshot — so
  every number below is measured in pixels on the render, not read from a font table.
- `p1_*` is the **desktop/long page** and is the basis for this spec; `p0_*` is the same language
  at a narrow width (verified on `p0_t00_y0.png`, `p0_t01_y1280.png`, `p0_t06_y7680.png`) and is
  used only for cross-checks.
- **Scale rule:** `@1440 = capture_px × (1440 / 2160) = × 0.6667`. Tables give `capture px` and
  `@1440` side by side; the tile column names the tile whose y-range contains the measurement.
- The capture is a narrow viewport (it carries an iOS status bar at `p1_t00_y0.png` y 0..192).
  Layout ratios transfer cleanly; **absolute type does not** — see §4.

## 1. Layout skeleton

Measured on `p1_t02_y2560.png` (scanline y=3300) and `p1_t03_y3840.png`.

| Property | capture px | @1440 | Tile |
| --- | --- | --- | --- |
| Page background | — | `#101010` | `p1_t02_y2560.png` (x 0..115, #101010) |
| Card (panel) fill | — | `#181818` | `p1_t02_y2560.png` (x 121..2039) |
| Card border | 2 | **1** | `p1_t02_y2560.png` (x 117..119 → `#454543`) |
| Content max-width | 1924 | **1280** (89.1% of viewport) | `p1_t02_y2560.png` (card x 118..2042) |
| Outer gutter | 116 | **80** (5.4% each side) | `p1_t02_y2560.png` |
| Card corner radius | ~84 | **52** | `p1_t03_y3840.png` (top border of next card spans x 202..1957 vs body 118..2042 ⇒ r≈84) |
| Gap between cards (border to border) | 78 | **52** | `p1_t03_y3840.png` (card1 bottom y=4641, card2 top y=4719) |
| Media inset inside card | 98 | **65** | `p1_t00_y0.png` (window x 216 vs card x 118) |
| Caption text inset inside card | 130 | **87** | `p1_t02_y2560.png` (title x=261, eyebrow x=253) |
| One-line lede max measure | ~1495 | **~997** | `p1_t02_y2560.png` (lede line 1 x 256..1751) |

Grid: the capture is **one card per row** at every width checked — there is no multi-column tool
grid in the reference (`p1_t00/t02/t03/t06`, `p0_t00/t01/t06`). See §9 for the derived 2-up numbers
we need at 1440 with 26 tools.

Radius/space tokens to apply: `--dw-radius-card: 52px`, `--dw-radius-media: 28px`,
`--dw-gap-card: 52px`, `--dw-pad-media: 65px`, `--dw-pad-caption: 87px`.

## 2. Card anatomy and vertical budget (the rhythm)

Single card measured end to end on `p1_t00_y0.png` (media), `p1_t02_y2560.png` (caption),
`p1_t03_y3840.png` (rule, options, card foot). Card top y=811, card foot y=4641.

| Zone | capture px | @1440 | Tile |
| --- | --- | --- | --- |
| card top padding → media top | 100 | 67 | `p1_t00_y0.png` |
| media (window capture) height | 1734 | 1156 | `p1_t00_y0.png` (y 911..2645) |
| **media bottom → eyebrow ink** | **609** | **406** | `p1_t02_y2560.png` (y 2645→3254) |
| eyebrow ink → title ink | 124 | 83 | `p1_t02_y2560.png` |
| title ink → lede ink | 129 | 86 | `p1_t02_y2560.png` |
| lede ink → rule | 172 | 115 | `p1_t02_y2560.png` / `p1_t03_y3840.png` |
| rule → option 1 ink | 156 | 104 | `p1_t03_y3840.png` |
| option row pitch (ink to ink) | 170 | 113 | `p1_t03_y3840.png` (y 4212, 4382) |
| last option ink → card foot | 193 | 129 | `p1_t03_y3840.png` (y 4448→4641) |
| **whole card height** | **3830** | **2553** | `p1_t00`+`p1_t03` (y 811..4641) |

Whitespace budget: of 3830 capture px of card height, **1586 px (41%) is empty space** — the
single largest allocation on the page is silence. Apply the same ratio; do not fill it with
metadata chips or feature icons.

## 3. Screenshot / window framing (the premium move)

From `p1_t00_y0.png` (crop `crop_shot_top.png`, y 820..1500):

- The capture is a **light** app window (≈`#D8D8D8` UI) placed directly on the `#181818` card.
  The contrast between light window and dark card is the entire effect.
- Inset **65px** (@1440) from the card edge on all sides; window height 1156px @1440.
- Window corner radius ≈ **28px** @1440 (capture ≈42px); window has its own hairline edge, no border
  of its own colour, no drop shadow, no glow, no outer ring.
- **No** device bezel, **no** browser chrome, **no** perspective/tilt, **no** background gradient
  behind the shot. The card corner radius must be ≥ the window radius so the window never touches
  the card's corner arc.
- Window content is cropped by the window frame, not by the card: the capture bleeds to the window
  edges and is never letterboxed with a coloured mat.

## 4. Type scale

Measured ink heights (cap-to-baseline / ascender-to-descender) and derived font sizes.

| Role | capture ink | derived font (capture) | ratio vs body | @1440 if scaled proportionally | **Apply @1440** | Tile |
| --- | --- | --- | --- | --- | --- | --- |
| Card title (h2) | 138 | 145 | **1.96×** | 97 | **38 / lh 42, weight 400** | `p1_t02_y2560.png` (y 3418..3556) |
| Body / one-line lede | 70 | 74 | 1.00× | 49 | **19 / lh 32, weight 400** | `p1_t02_y2560.png` (y 3686..3880) |
| Option row (h3) | 66 | 69 | 0.93× | 46 | **18 / lh 29, weight 400** | `p1_t03_y3840.png` (y 4212..4278) |
| Eyebrow / caption | 40 (caps) | 56 | 0.76× | 37 | **14 / lh 20, weight 700, +0.06em, uppercase** | `p1_t02_y2560.png` (y 3254..3294) |
| Page h1 | — | — | — | — | **56–64 / lh 1.05** (ours; the reference has **no** h1) | `p1_t00_y0.png` (page opens on a tool card, no hero headline) |

Why the two columns: the capture is a narrow viewport, so a literal proportional transfer yields a
97px card title at 1440. Keep the **ratios** (title 1.96×, option 0.93×, eyebrow 0.76×, body
line-height 1.68×) and anchor the absolute ladder on a 19px lede. Measured line-height: lede ink
tops 124 capture px apart (y 3686 → 3810) = **1.68×** the derived font size.

Weights and tracking (observed, not guessed):
- Titles and lede are **regular weight** — no bold headline anywhere. Hierarchy is size + value only.
- Eyebrow is **bold, uppercase, positively tracked** (measured advance ≈0.68em/char ⇒ ≈ +0.06em).
- Title tracking ≈ neutral to **-0.01em**; lede tracking neutral.
- Title colour peak `#A0A0A0`; lede peak `#7C817C` (≈`#7C7C7C`); option ink ≈`#8A8A8A`; eyebrow and
  tick glyphs are the only saturated ink on the page (`#E9562E`, measured; 0.71% of all pixels are
  saturated — that is the whole accent budget).

## 5. Section header pattern

From `p1_t06_y7680.png` (crop `crop_sec_head.png`, y 7360..8260) and the repeated cluster at
y 11563..12368 on `p1_full.png`:

`eyebrow (category)` → `title` → `one line of purpose` → `full-width rule` → `options`.
Same sizes and same gaps as the card caption in §2; the rule is 1px `#454543`.

- The rule is **anchored to the text column and bleeds to the card's end edge**: measured from
  x≈248 (left inset 130) to x=2040 (the card's right edge) — asymmetric by design.
  @1440: starts 87px from the inline-start edge, ends flush with the inline-end edge.
- No numbered sections, no icons before the eyebrow, no coloured bars: `p1_t00`–`p1_t06`.

## 6. Calm-rhythm rules (the alternation)

- One media-led card per tool; the caption order never varies inside a card (`p1_t02`, `p1_t06`).
- Every 3–4 cards, a **quiet statement card**: flat, borderless, near-page-black, one sentence plus
  one App-Store-style button (visible in `sheet_p1.png`, middle column; not measured numerically —
  treat as qualitative, do not invent numbers for it).
- Section boundary spacing: last option ink → next media top on the following card = 732 capture px
  (y 12368 rule → 13100 media) = **488 @1440** when a statement card intervenes; without one it is
  the 52px card gap + 129px card foot + 67px media top padding = **248 @1440**.
- No background bands, no gradients, no shadows on cards; only two surface values exist on the page
  (`#101010` / `#181818`).

## 7. Motion (from the demo frame sets)

Sources: `_reference/tangerine-demo-frames.zip` (21 frames, 1280×720, inspected `f_001`, `f_014`)
and `_reference/tangerine-shift-convert-frames.zip` (80 frames, 540×960). No timing metadata is
embedded, so **character** is what we adopt; the durations below are our tokens.

Observed: elements move by **position + opacity only**; a target lifts/scales as it becomes active
while its label appears; the camera does a slow, continuous push rather than cuts; nothing bounces,
nothing spins for decoration, no parallax.

| Token | Value | Applies to |
| --- | --- | --- |
| `--dw-motion-fast` | 160ms `cubic-bezier(.22,.61,.36,1)` | hover/focus lift, tick reveal, rule draw |
| `--dw-motion-base` | 240ms `cubic-bezier(.22,.61,.36,1)` | button/pill state, option row fade |
| `--dw-motion-enter` | 420ms `cubic-bezier(.16,.84,.44,1)` | card/section enter: translateY 16px + opacity 0→1 |
| `--dw-motion-stagger` | 70ms, max 6 items | grid children on first paint |
| `--dw-motion-media` | 8s linear, `scale(1.0→1.03)` | hero/feature media slow push while in view |
| reduced motion | all → 120ms opacity only, no transform | `prefers-reduced-motion` |

Travel distances stay small (≤16px; hover `translateY(-2px)`); easing is ease-out with a soft
settle, never a spring overshoot >2%.

## 8. Colour and accent application

| Role | Dark (reference-measured) | Dark (ours) | Light (ours) |
| --- | --- | --- | --- |
| Page | `#101010` | `#0F0D12` | `#FFFFFF` |
| Card / surface | `#181818` | `#171319` | `#F7F4FA` |
| Hairline | `#454543` | `#332C39` | `#E4DCEC` |
| Title ink | `#A0A0A0` | `#EDE7F3` | `#1B1420` |
| Body ink | `#7C7C7C` | `#B9AEC4` | `#4A4152` |
| Accent (eyebrow, ticks) | `#E9562E` | **`#B48AE0`** | **`#8224D4`** |
| Accent (fills, large graphics) | — | `#8224D4` | `#8224D4` |

Contrast check (computed, not sampled): `#8224D4` on `#181818` ≈ **2.8:1** — too low for 14px
eyebrow text; `#B48AE0` on `#181818` ≈ **6.5:1** — passes AA. Therefore: **lavender for accent text
in dark mode, deep purple for accent text in light mode**; deep purple only for fills/graphics on
dark. Never put the accent on the title or the body.

## 9. Derived adaptation for our page (not in the reference — decide, then measure us)

The reference is 1-up at every captured width; DropWheel has 26 tools, so:

| Layout | Columns | Card width @1440 | Gap | Use |
| --- | --- | --- | --- | --- |
| Feature (reference-faithful) | 1 | 1280 | 52 | first 3 tools |
| Tool grid (derived) | 2 | 628 | 24 | the long tail |
| Index chips (derived, from the mobile index `p0_t00`) | 3 | 410 | 16 | category index at top |

Keep the card *proportion* identical across both: media height = 89% of card width (~1156 at 1280,
~560 at 628), caption inset 87px scaled to ~6.8% of card width (87 → 43 in the 2-up grid).

## 10. RTL — every rule that flips

| Rule | LTR | RTL |
| --- | --- | --- |
| Card grid / column order | first column at left | mirrored; DOM order unchanged, use `dir` + logical props |
| Media inset | 65px inline-start | same value, `padding-inline-start` ⇒ appears right |
| Caption indent | 87px from inline-start | `padding-inline-start` — appears from the right |
| Rule anchor | starts at text column, bleeds to **end** edge | mirrors: starts at text column, bleeds to the **left** |
| Title / lede alignment | left | right (`text-align: start`) |
| Eyebrow | uppercase + `letter-spacing: .06em` | **no uppercase, tracking → 0**; add `line-height +0.05` (Arabic ascenders/descenders) |
| Tick column | at inline-start of each option, glyph leans right | ticks at inline-start (right); provide a **mirrored** tick SVG or use a non-directional mark |
| Body line-height | 1.68 | **1.75** (Arabic needs taller line boxes; same optical density) |
| Numbers, filenames, timings, `0:00.00` | — | LTR islands: `dir="ltr"` + `unicode-bidi: isolate` |
| Arrows / chevrons in CTA | point right→left for "back" | mirror all horizontal glyphs |
| Motion travel | `translateX(-16px)` reveals from left | flip sign (`translateX(+16px)`) or animate `translate-inline` only |
| Shadows / glows | — | no horizontal offset anywhere, so no flip needed (keep centred) |
| Hairline border | 1px all sides | unchanged |

## 11. What not to copy (the line we do not cross)

- **No** reference brand name, wordmark, logo mark, icon set, product names, taglines, sentences,
  screenshots, device mockups, wallpaper, or the wheel graphic from the demo clip.
- **No** reference colour identity — their accent is a red-orange; ours is `#8224D4`/`#B48AE0`.
- **No** transcription of their copy pattern at the sentence level: our one-liners and option lines
  are written fresh from `src/` behaviour, never paraphrased from the reference's visible text.
- **No** pixel-cloned layout: no matching their exact card count, section order, or the specific
  media/statement alternation cadence; we take the *proportions and anatomy* only.
- **No** App-Store badge artwork, Apple/Mac marks or macOS window chrome in our captures.
- **No** third-party font files lifted from the reference render; we use our existing stacks.
