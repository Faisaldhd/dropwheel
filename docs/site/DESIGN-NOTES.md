# Design notes — what we take from the owner's reference

Lane: site-design (TEAM C). Source: the owner's reference PDF, rendered to tiles in scratch and
measured; see `docs/website/DESIGN-SPEC.md` for every number and its tile citation.
Inspiration only — none of the reference's name, logo, imagery or sentences go into DropWheel.

- **Rhythm is a stack of posters, not a grid of widgets.** Every tool gets one full-width card
  (~89% of the viewport) with a large media area and a text block, and the only thing between cards
  is a border-to-border gap (~52px at 1440) plus the card's own generous bottom padding. The page
  reads slowly and confidently. We adopt this for the 3–4 hero tools, then compress to a 2-up grid
  for the long tail so our 26 tools stay scannable.
- **One card = one tool, and the card is the unit of composition.** Card fill sits one step above the
  page (`#181818` on `#101010`), a 1px hairline border at low contrast, a large corner radius (~52px
  at 1440), no drop shadow at all. Depth comes from the value step and the radius, never from blur.
  We keep exactly this recipe with our own surfaces (`--surface-1` on `--bg`).
- **The screenshot is framed by contrast, not by chrome.** A light app window on a near-black card,
  inset ~65px from the card edge, its own rounded corners, no device bezel, no tilt, no glow, no
  backdrop blur. That single move is what makes the capture look premium. We reproduce it with our
  own app captures, and we never add a laptop mockup or a fake browser bar.
- **Caption anatomy is fixed and short: eyebrow → title → one line → rule → options.** Category in
  small caps with the brand accent, title in large *regular* weight, exactly one sentence of purpose,
  a hairline that anchors to the text column and bleeds to the card's end edge, then 2–3 ticked
  options that name capabilities rather than features. We copy the anatomy, not the words.
- **Hierarchy comes from size and value, not weight.** Titles are not bold; the accent is spent only
  on the eyebrow and the tick glyphs; everything else is a neutral grey ladder. For DropWheel this
  maps cleanly onto purple `#8224D4` for the eyebrow in light mode and lavender `#B48AE0` in dark
  mode (the deep purple fails small-text contrast on `#181818`; the lavender passes).
- **One spacing family, ink-to-ink.** Vertical gaps are consistent multiples (~52 / 65 / 87 / 129 px
  at 1440) and the biggest gap on the page is deliberately the media-to-caption one — the pause is
  the design. We keep that budget and spend our "loud" space there instead of on dividers or colour.
- **Alternation, not decoration.** The long page alternates a media-led card with a quiet statement
  card (flat, borderless, one sentence + one CTA) every few tools. That break is the only rhythm
  device; there are no coloured bands, no icon rows, no alternating background tints. We adopt one
  quiet CTA card per group (e.g. "everything runs on your PC" + download button).
- **Dark and light are a value ladder, not a hue change.** The reference only shows dark, so we
  define both themes as the same ladder (page / surface / hairline / muted / body / title) and let
  the accent do the only chroma. Light mode inverts elevation, not the accent hue; the purple stays
  `#8224D4` and gains a lavender tint only for fills.
- **Type is re-anchored, not scaled.** The capture is a narrow viewport, so a literal proportional
  transfer would give a 97px card title at 1440. We keep the measured *ratios* (title 2.0× body,
  options 0.95×, eyebrow 0.76×, body line-height 1.68) and anchor the absolute size on a 19px lede.
  Same feel, sane page.
- **RTL mirrors the composition, not just the text.** The media inset, the caption indent, the rule's
  anchor edge, the tick column and every horizontal travel in motion flip to the inline-start side;
  the eyebrow loses its uppercase tracking (Arabic has no caps) and gains line-height instead;
  numbers, filenames and timings stay LTR islands. Motion stays ease-out and short — the reference
  animates position and opacity only, with a soft settle and no bounce, which we encode as our
  existing duration/easing tokens.
