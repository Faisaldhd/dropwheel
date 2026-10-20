# SEO, social cards and crawler files — docs/site

Lane: **site** · Owner: **site-seo** · Branch: `site/seo` (based on `feat/2.4.0-website`).
Scope: everything below lives in `docs/site/` except this note. The lead owns
`index.html`, `styles.css` and `app.js`; this file gives the exact snippets to paste.

---

## 1. `<head>` changes for `docs/site/index.html`

### 1.1 Replace lines 15–27 (the current Open Graph / Twitter block)

The block as shipped points `og:image` and `twitter:image` at
`assets/icon/dropwheel-256.png` (a 256×256 square) and declares
`twitter:card = summary`. Both are replaced below; every other tag in the block
(`og:site_name`, `og:url`, `og:locale`, `og:locale:alternate`) is unchanged so the
page stays consistent with `app.js`.

Delete lines 15–27 and paste this in their place (after the
`apple-touch-icon` line, line 14):

```html
  <link rel="manifest" href="site.webmanifest">
  <meta property="og:type" content="website">
  <meta property="og:site_name" content="DropWheel">
  <meta property="og:url" content="https://faisaldhd.github.io/dropwheel/">
  <meta property="og:title" content="DropWheel — 26 file tools at your cursor">
  <meta property="og:description" content="Convert, edit, compress, protect and lock files from one radial wheel. Processed on your PC. Nothing is uploaded.">
  <meta property="og:image" content="https://faisaldhd.github.io/dropwheel/assets/social/og-en.png">
  <meta property="og:image:type" content="image/png">
  <meta property="og:image:width" content="1200">
  <meta property="og:image:height" content="630">
  <meta property="og:image:alt" content="DropWheel social card: the headline 26 file tools at your cursor, the Free and open source · MIT badge, the site URL, and the shipped Convert wheel screenshot.">
  <meta property="og:locale" content="en_US">
  <meta property="og:locale:alternate" content="ar_AR">
  <meta name="twitter:card" content="summary_large_image">
  <meta name="twitter:title" content="DropWheel — 26 file tools at your cursor">
  <meta name="twitter:description" content="Convert, edit, compress, protect and lock files from one radial wheel. Processed on your PC. Nothing is uploaded.">
  <meta name="twitter:image" content="https://faisaldhd.github.io/dropwheel/assets/social/og-en.png">
  <meta name="twitter:image:alt" content="DropWheel social card: the headline 26 file tools at your cursor, the Free and open source · MIT badge, the site URL, and the shipped Convert wheel screenshot.">
```

Notes

* `og:image` must stay absolute: the page allows no external requests but share
  crawlers fetch the card themselves, and the card file is same-origin.
* The page is **one URL with an in-page language switch**, so crawlers always get
  the English card. `og-ar.png` exists for manual use (Arabic posts, press) and is
  not referenced from the head — a second `og:image` would be ignored.
* `twitter:image:alt` is new; the current page never had one.

### 1.2 Add the JSON-LD block before `</head>`

Paste immediately before the closing `</head>` (currently line 31, after the
`app.js` script tag). No version number, no rating and no review count is declared,
because the site does not publish one.

```html
  <script type="application/ld+json">
  {
    "@context": "https://schema.org",
    "@type": "SoftwareApplication",
    "name": "DropWheel",
    "description": "Windows 10/11 file tools on one radial wheel: convert, edit, compress, protect and lock. Everything runs locally on the PC.",
    "url": "https://faisaldhd.github.io/dropwheel/",
    "image": "https://faisaldhd.github.io/dropwheel/assets/social/og-en.png",
    "applicationCategory": "UtilityApplication",
    "operatingSystem": "Windows 10, Windows 11",
    "inLanguage": ["en", "ar"],
    "isAccessibleForFree": true,
    "license": "https://github.com/Faisaldhd/dropwheel/blob/main/LICENSE",
    "downloadUrl": "https://github.com/Faisaldhd/dropwheel/releases/latest/download/DropWheel-Setup.exe",
    "sameAs": ["https://github.com/Faisaldhd/dropwheel"],
    "offers": {
      "@type": "Offer",
      "price": "0",
      "priceCurrency": "USD",
      "availability": "https://schema.org/InStock"
    }
  }
  </script>
```

Fields map to the shipped page as follows: `name` → `index.html:114`;
`applicationCategory` → the tool set at `index.html:250-665`;
`operatingSystem` → `index.html:111` and `1014`; `license` → the MIT line at
`index.html:1056`; `downloadUrl` → the download button at `index.html:97` and `121`;
`sameAs` → `index.html:1053`.

### 1.3 CSP (`index.html:11`) — one conditional line

The current policy is `script-src 'self'`, with no `'unsafe-inline'` and no hash.
Inline `<script type="application/ld+json">` is a data block, and browsers differ
on whether `script-src` applies to it. If a browser or a CSP auditor reports the
JSON-LD block as refused, add this hash to the `script-src` directive:

```
script-src 'self' 'sha256-zdBw1UkIIBA+TWHw34end3INolP+0CBSHqjw7RjFRro=';
```

The hash is over the **exact bytes between** the `<script …>` and `</script>` tags
as pasted above, including the leading newline and the two-space indentation. If
you re-indent or reformat the JSON, recompute it:

```powershell
$json = (Get-Content index.html -Raw) -split '<script type="application/ld\+json">' |
  Select-Object -Skip 1 | Select-Object -First 1
$json = ($json -split '</script>')[0]
[Convert]::ToBase64String([Security.Cryptography.SHA256]::Create().ComputeHash(
  [Text.Encoding]::UTF8.GetBytes($json)))
```

---

## 2. Files added to `docs/site/`

| File | Purpose | Content |
| --- | --- | --- |
| `robots.txt` | Allow everything, advertise the sitemap | `User-agent: *` / `Allow: /` / `Sitemap: https://faisaldhd.github.io/dropwheel/sitemap.xml` |
| `sitemap.xml` | The single indexable URL | one `<url>` with `xhtml:link` alternates `en`, `ar`, `x-default`, all pointing at `https://faisaldhd.github.io/dropwheel/` |
| `site.webmanifest` | Install/standalone metadata | `name`, `short_name` `DropWheel`, `start_url` `"."`, `display` `standalone`, `background_color` `#100B18`, `theme_color` `#8224D4`, icons 32×32 and 256×256 |
| `assets/social/og-en.png` | 1200×630 card, English | 164,769 B |
| `assets/social/og-ar.png` | 1200×630 card, Arabic RTL | 167,859 B |

Published URLs (Pages serves `docs/site` as the site root):
`https://faisaldhd.github.io/dropwheel/robots.txt`,
`…/sitemap.xml`, `…/site.webmanifest`,
`…/assets/social/og-en.png`, `…/assets/social/og-ar.png`.

Two deployment points:

* `robots.txt` and `sitemap.xml` must sit at the **site root**, not under
  `assets/`. They do.
* GitHub Pages serves `.nojekyll` already present at `docs/site/.nojekyll`, so
  `assets/social/` is served verbatim.

**Manifest icons:** the repo only ships `dropwheel-32.png` and
`dropwheel-256.png`; the manifest lists exactly those two sizes. There is no
192×192 or 512×512 asset, so this manifest makes **no** installability/purpose
claim beyond `any`. Adding 192/512 icons is a separate asset task.

**Theme colour note:** `index.html:9` declares `theme-color` `#120D1A`, while the
manifest declares `#8224D4`. Both are valid; if you want the browser chrome to
match the card, change line 9 to `#8224D4` in the same commit.

---

## 3. Consistency with the page as it stands (grep-verified)

| Fact | Source | Snippet agrees |
| --- | --- | --- |
| Title `DropWheel — 26 file tools at your cursor for Windows` | `index.html:6` | card headline uses the same 26-tool claim (shorter, no "for Windows") |
| Meta description `DropWheel puts 26 file tools…` | `index.html:7` | deliberately not reused; `og:description` keeps the existing social wording from `index.html:19`, verbatim |
| Canonical `https://faisaldhd.github.io/dropwheel/` | `index.html:12` | `og:url` identical, sitemap `<loc>` identical |
| `og:site_name` `DropWheel` | `index.html:16` | kept verbatim |
| `og:locale` `en_US` + alternate `ar_AR` | `index.html:22-23` | kept verbatim (and `app.js:189-190` rewrites them on switch) |
| Icon paths | `index.html:13-14` | unchanged; manifest uses the same `assets/icon/*.png` |
| Download URL | `index.html:97,121` | `downloadUrl` identical |
| Licence | `index.html:1056` (MIT link) | `license` points at the repo LICENSE |

## 4. JavaScript that rewrites these tags — update with the head

`app.js:183-197` (`applyMeta`) runs on every language switch:

* **`app.js:193`** sets `twitter:image:alt` to `meta.title`, i.e. a page title in
  an alt-text field. Replace with the card description.
* **`app.js:194-196`** rewrites `og:image:alt` with text describing the **old app
  icon** ("a violet segmented wheel with an arrow…"). After the head change this
  describes an image that is no longer the share image. Replace both branches with
  a description of the 1200×630 card, e.g.
  `'DropWheel social card: the headline, the Free and open source · MIT badge, the site URL, and the shipped Convert wheel screenshot.'`
* `applyMeta` never touches `og:image`, `twitter:image`, `og:image:width/height/type`
  or the manifest link, so those survive the language switch. That is fine for one
  URL; if you later serve `/ar/`, revisit it.

## 5. Social cards — provenance, fonts, byte sizes

Generated offline with the bundled interpreter (**Python 3.12.14, Pillow 12.3.0,
numpy 2.3.5**); no `pip install`, no network, no headless browser.

| Card | Pixels | Bytes | Mode |
| --- | --- | --- | --- |
| `og-en.png` | 1200 × 630 | 164,769 B (161 KB) | RGB, fully opaque |
| `og-ar.png` | 1200 × 630 | 167,859 B (164 KB) | RGB, fully opaque |

Both are well under the 250 KB budget.

**Imagery — shipped assets only, never mocked:**

* `docs/site/assets/motion/wheel-dark-poster.webp` (442×442, 12,700 B) — the real
  Convert wheel render the site already ships as the hero poster.
* `docs/site/assets/icon/dropwheel-256.png` (256×256, 52,660 B) — the shipped app
  icon, unmirrored on both cards.

No screenshot was drawn, re-coloured or composited from parts; the poster is
pasted 1:1 at 442 px with a rounded mask, shadow and 1 px rim.

**Fonts (all present on Windows, nothing downloaded):**

| Use | File | Family |
| --- | --- | --- |
| Headlines | `C:\Windows\Fonts\segoeuib.ttf` | Segoe UI Bold |
| Brand, badge | `C:\Windows\Fonts\seguisb.ttf` | Segoe UI Semibold |
| Honest line | `C:\Windows\Fonts\segoeui.ttf` | Segoe UI Regular |
| URL | `C:\Windows\Fonts\consola.ttf` | Consolas |

**Text on the cards**

* English: `DropWheel` · `26 file tools` / `at your cursor.` ·
  `Runs on your PC. Nothing is uploaded.` · `Free & open source · MIT` ·
  `https://faisaldhd.github.io/dropwheel/`
* Arabic: `DropWheel` · `ست وعشرون أداة` / `عند مؤشرك` ·
  `كل شيء يبقى على جهازك — دون رفع أي ملف.` · `مجاني ومفتوح المصدر · MIT` ·
  the same URL, left-to-right.

**Arabic shaping without libraqm.** This Pillow build has no Raqm
(`features.check("raqm") == False`), so `direction`/`language`/`features` raise
`KeyError` and the default engine would emit isolated, left-to-right Arabic.
The cards are therefore shaped manually: every letter is mapped to its
Arabic Presentation Forms-B codepoint from its joining class, `lam` + alef
ligatures are formed, and the run is emitted right-to-left as separate bidi runs
(the Latin `MIT` and the URL stay LTR).

Verification, because "trust the shaper" is not evidence:

* The same 18 words were rendered with the **Windows text engine** (GDI+
  `DrawString`, `DirectionRightToLeft`) as an oracle in scratch, then compared
  with the Pillow render at the same 40 px size.
* **18/18 words have identical ink connected-component counts** (letter bodies +
  dot clusters), i.e. the same joins and the same number of pieces.
* Ink mass within 4 % for 16/18 words; the two outliers are Segoe UI's
  presentation-form vs OpenType glyph variants (`شيء` +11 %, `كل` −6 %), not a
  shaping difference.
* Word ink widths within 9 px; binary IoU per word ranges 0.42–0.99 because GDI
  GridFit snaps stems and advances differently — the component count is the
  reliable signal, and a 2× side-by-side was inspected word by word.

**Pixel-level sanity check performed on the shipped files**

| Check | `og-en.png` | `og-ar.png` |
| --- | --- | --- |
| PIL re-open, size, mode | 1200×630 RGB | 1200×630 RGB |
| fully opaque (no alpha) | yes | yes |
| corner pixel | `#100b18` | `#100b18` |
| centre pixel | `#1e1130` | `#1e1130` |
| poster region vs source poster, Pearson r | **0.9966** | **0.9963** |
| ink pixels in headline box | 21,080 | 11,965 |
| ink pixels in honest-line box | 1,986 | 1,925 |
| ink pixels in licence-pill box | 1,327 | 1,385 |
| ink pixels in URL box | 1,645 | 1,637 |
| distinct colours | 9,973 | 9,326 |

The 0.996 correlation proves the shipped poster pixels are what is on the card,
and the per-region ink counts prove each required string actually landed inside
its box rather than off-canvas.

## 6. Validation run

* `sitemap.xml` — parsed with `xml.etree.ElementTree` (well-formed; namespaces
  `sitemap` + `xhtml` resolve).
* `site.webmanifest` — parsed with `json` (13 keys, 2 icons, every icon path
  exists in `docs/site/assets/icon/`).
* `robots.txt` — ASCII, one `Sitemap:` directive, no `Disallow`.
* Cards — PIL re-open, dimensions, opacity, poster correlation, ink boxes (above).
* No tags, no PR, no force-push, no analytics added.

## 7. What was not done

* No browser render and no local server: proof of the head tags belongs to the
  GitHub Actions run and to a share-card debugger after deploy. That is also why
  the CSP note in §1.3 is stated as conditional.
* No 192/512 manifest icons exist in the repo to reference.
* `og-ar.png` is not wired into the head (single-URL page, crawler would ignore a
  second image); it is ready for manual/off-site use.
