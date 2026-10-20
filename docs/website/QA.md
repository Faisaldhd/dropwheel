# Website QA — DropWheel 2.4.0 product page

**Agent:** `site-qa` · **Lane:** site · **Branch:** `site/qa`
**Tested commit:** `b561b2c` (`origin/feat/2.4.0-website`, merge of the site/capture, site/design,
site/content and site/ci lanes; fast-forwarded from the assigned base `7009be9`)
**Date:** 2026-10-20 09:20 Riyadh (Asia/Riyadh) · **Deadline:** 10:20 Riyadh
**Scope:** static, offline gate over `docs/site` plus independent claim checks and GitHub Actions
evidence. No browser was opened locally, no app, installer or build was run, and `docs/site/**`
was not modified.

> The page moved during the review (7009be9 → 7531907 → b561b2c). All numbers below are for
> **b561b2c** and are reproducible from the committed script on this worktree. If the branch moves
> again, re-run the gate; do not reuse these numbers for a different commit.

---

## 1. Bottom line

**NO-GO for publishing now.**

- The static gate is **28 PASS / 2 FAIL** of 30 checks; both FAILs are real page defects with
  file:line below.
- **Site CI is red on b561b2c** (`run 39379283584`): Size budget PASS, Page screenshots PASS,
  **Link check FAIL**, **Lighthouse FAIL** (SEO 0.92 < 0.95).
- **There is no `v2.4.0` release or tag**, and the shipped version is still `2.3.0`. The page is a
  staged 2.3.0-download / 2.4.0-preview page; publishing it now would advertise 2.4.0 while the
  installer is 2.3.0. The repository's own instruction is not to publish Pages until the signed
  2.4.0 release is verified.
- The gate passes 30/30 on a scratch copy with only the two page fixes applied (broken `.sig` URL
  and hero-hash wrapping), so the FAILs are page defects, not gate noise.

**Conditional GO:** after the two page fixes, a green Site CI (including a valid `robots.txt`), and
a signed `v2.4.0` release with every version fact replaced, the page can be reconsidered. See §8.

---

## 2. Gate verdict table

Command for every row unless stated otherwise:
`python scripts/site/qa/qa_check.py --site docs/site` (exit 1 while any FAIL remains).

| Check | Command | Result | Evidence (b561b2c) |
|---|---|---|---|
| EXT-1 no external subresources | `qa_check.py --site docs/site` | **PASS** | 60 resource references, 0 external. Absolute canonical/OG metadata URLs are not fetch-bearing and are not counted. |
| EXT-2 no inline event handlers | `qa_check.py --site docs/site` | **PASS** | 0 `on*=` attributes |
| EXT-3 no `<script>` without `src` | `qa_check.py --site docs/site` | **PASS** | 0 inline script blocks |
| EXT-4 no analytics/tracker identifiers | `qa_check.py --site docs/site` | **PASS** | 0 tracker identifiers in scripts/URLs/styles; visible copy saying “no analytics” is ignored |
| CSP-1 CSP meta in `<head>` | `qa_check.py --site docs/site` | **PASS** | `index.html:11`, before the first subresource |
| CSP-2 CSP restrictive | `qa_check.py --site docs/site` | **PASS** | `default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'` |
| CSP-3 `style-src 'self'` viable | `qa_check.py --site docs/site` | **PASS** | 0 `style=` attributes, 0 `<style>` blocks, 0 JS style writes |
| ID-1 unique ids | `qa_check.py --site docs/site` | **PASS** | 69 ids, 0 duplicates |
| ID-2 internal anchors resolve | `qa_check.py --site docs/site` | **PASS** | 81 `#…` href/use references, 0 missing |
| ID-3 ARIA/`for` targets resolve | `qa_check.py --site docs/site` | **PASS** | 16 `aria-labelledby`/`describedby`/`controls`/`for` references, 0 missing |
| IMG-1 alt + dimensions | `qa_check.py --site docs/site` | **PASS** | 11 `<img>`, all with `alt` (2 decorative `alt=""`), `width`, `height` |
| MEDIA-1 video/source | `qa_check.py --site docs/site` | **PASS** | 0 `<video>`; 4 `<picture><source>` with `srcset`+`type` and a dimensioned fallback `<img>` |
| HEAD-1 one h1 | `qa_check.py --site docs/site` | **PASS** | 1 `<h1>`, 68 headings |
| HEAD-2 no skipped levels | `qa_check.py --site docs/site` | **PASS** | h1→h2→h3 sequence, no skip |
| LANG-1 html lang/dir | `qa_check.py --site docs/site` | **PASS** | `lang="en" dir="ltr"` |
| LANG-2 Arabic in `lang="ar"` | `qa_check.py --site docs/site` | **PASS** | 0 Arabic text nodes outside `lang="ar"` |
| LANG-3 no mixed-language prose | `qa_check.py --site docs/site` | **PASS** | 0 Arabic in EN scopes; 0 non-technical Latin tokens in AR scopes |
| LANG-4 same sections both languages | `qa_check.py --site docs/site` | **PASS** | 9 sections, EN/AR span counts equal in every section |
| CTA-1 verified download URL | `qa_check.py --site docs/site` | **PASS** | 3 download CTAs (3 EN/3 AR), all exactly `https://github.com/Faisaldhd/dropwheel/releases/latest/download/DropWheel-Setup.exe`; 4 button CTAs (download URLs or in-page anchors) |
| LINK-1 external anchors allowlisted | `qa_check.py --site docs/site` | **PASS** | 18 external anchors: `github.com/Faisaldhd/dropwheel`, `github.com/Metamorfolog/file_action`, repo `LICENSE` path; no other hosts |
| LINK-2 external URLs well-formed | `qa_check.py --site docs/site` | **FAIL** | `docs/site/index.html:931` — `.sig` href contains literal `<!--release-version:start-->…<!--release-version:end-->` inside the attribute → HTTP 404. 17/18 well-formed. |
| LINK-3 `target=_blank` rel | `qa_check.py --site docs/site` | **PASS** | 0 `target="_blank"`; every external link already has `rel="noopener noreferrer"` |
| TXT-1 no TODO/FIXME/lorem/placeholder | `qa_check.py --site docs/site` | **PASS** | 0 placeholder-like strings in visible text/attributes; CSS comment “placeholders” is not user-visible |
| ASSET-1 referenced assets exist | `qa_check.py --site docs/site` | **PASS** | 34 unique referenced files, 783,555 B, 0 missing (31 images incl. theme/clip variants) |
| SIZE-1 first load < 500,000 B | `qa_check.py --site docs/site` | **PASS** | 266,648 B (53.3% of budget); reduced-motion variant 216,146 B |
| PHONE-1 viewport meta | `qa_check.py --site docs/site` | **PASS** | `index.html:5` `width=device-width, initial-scale=1` |
| PHONE-2 min-width ≤ 360 px | `qa_check.py --site docs/site` | **PASS** | 382 CSS rules; largest `min-width` is 62 px (key chip) |
| PHONE-3 no unconstrained 100vw | `qa_check.py --site docs/site` | **PASS** | 0 `100vw` widths |
| PHONE-4 nowrap only short/clipped | `qa_check.py --site docs/site` | **PASS** | 5 `white-space: nowrap` rules; longest visible text 31 chars, or overflow is clipped |
| PHONE-5 long tokens wrap/scroll | `qa_check.py --site docs/site` | **FAIL** | `docs/site/index.html:153` — 64-char SHA-256 token in `.hero-hash code`; `styles.css:549` has no `overflow-wrap`/`word-break`/`overflow-x`. The 390 px CI screenshot clips it at the right edge. |

The two FAILs are the only failures; a scratch copy with the two fixes applied returns
**PASS 30 / FAIL 0** from the same script.

---

## 3. Measured numbers (b561b2c)

All values are raw uncompressed bytes from the worktree, printed by `qa_check.py`.

| Metric | Value | Definition |
|---|---:|---|
| HTML (`index.html`) | **129,649 B** | document |
| CSS (`assets/styles.css`) | **50,645 B** | single stylesheet |
| JS (`boot.js` + `app.js`) | **17,525 B** | 1,244 + 16,281 |
| **First load** | **266,648 B** | document + stylesheet + both scripts + header mark + favicon + worst-case hero image (7 files) |
| First load, reduced motion | 216,146 B | same, hero poster instead of clip |
| 500 KB budget | **PASS, 53.3% used** | 266,648 / 500,000; 233,352 B headroom |
| Whole site tree | **923,927 B / 38 files** | includes lazy media, README, DESIGN-NOTES |
| Referenced local assets | 34 files / 783,555 B | every `src`, `srcset`, `data-dark/light`, `data-clip/poster-*`, stylesheet, script |
| Images | 31 files | 29 WebP + 2 PNG (11 `<img>` elements; theme/clip variants share the rest) |
| External subresource requests | **0** | absolute metadata URLs such as canonical/OG are navigational or social metadata, not fetches |
| External anchors | 18 | navigation only, allowlisted in §2 |
| Tool cards | 26 | 6 photos / 7 video / 5 audio / 2 PDF / 3 protect / 3 files; every `data-tool` maps to a source `Id` |
| Pending capture tiles | 21 | 5 of 26 tool windows have real capture assets |
| CSS rules parsed | 382 | for the phone-width checks |

First-load composition: `index.html` 129,649 + `styles.css` 50,645 + `boot.js` 1,244 +
`app.js` 16,281 + `dropwheel-128.webp` 3,644 + `dropwheel-32.png` 1,983 +
`wheel-light.webp` 63,202 = **266,648 B**. The browser selects one hero candidate; the dark
variant (`wheel-dark.webp`, 57,108 B) gives 260,554 B.

Cross-check with the site-ci definition (`scripts/site/size_budget.py --root docs/site`), which also
counts the apple-touch-icon and all hero candidates: **402,768 B (80.6% of 500,000)** — still PASS.

> Line endings: the worktree files are CRLF; these byte counts include CRLF. The Site CI size job
> measured the same 129,649 B document, so the commit/blob preserves them.

---

## 4. Top blockers, ranked

| # | Severity | Finding | Evidence | Required action |
|---:|---|---|---|---|
| B1 | **P0 — publish gate** | No signed `v2.4.0` release/tag exists; version is still 2.3.0. The page says 2.4.0 is “in final verification” while every download resolves to 2.3.0. | `gh release list`: latest `v2.3.0`; `git ls-remote --tags origin 'refs/tags/v2.4*'`: empty; `installer/DropWheel.iss:16` `AppVersion "2.3.0"`; `src/FileAction.App/FileAction.App.csproj:24` `<Version>2.3.0</Version>` | Hold Pages. Publish only after the signed v2.4.0 release exists and every version fact in `docs/site/index.html` is replaced (release bar, hero facts/hash, verify links, release card, version badge, README). |
| B2 | **P1 — merge blocker** | The `.sig` download href contains HTML comment markers *inside the attribute value*, so the requested URL is `…/DropWheel-<!--release-version:start-->2.3.0<!--release-version:end-->-Setup.exe.sig` and returns HTTP 404. | `docs/site/index.html:931`; `python scripts/site/check_links.py --root docs/site` → 1 DEAD (HTTP 404); Site CI run 39379283584 link-check job FAIL | Remove the markers from the attribute (leave them only in text nodes) or hardcode the version inside the href. Re-run the link check; it must be 0 dead. The real asset `DropWheel-2.3.0-Setup.exe.sig` exists in the release. |
| B3 | **P1 — mobile / visual** | The hero shows the full 64-char SHA-256 in a monospace `<code>` with no wrapping. At 390 px it overflows the viewport and is clipped by `body { overflow-x: hidden }`; the adjacent `SHA256SUMS.txt` link is clipped too. | `docs/site/index.html:153`; `docs/site/assets/styles.css:549`; PHONE-5 FAIL; CI screenshot `home-390-dark.png` (run 39379283584 artifact `site-screenshots`) shows the hash and link cut at the right edge | Restore the short hash (`7381cbd5b592…1d7156ce`, as the README describes) or add `overflow-wrap:anywhere; word-break:break-all; max-width:100%` to `.hero-hash code` and re-check the 390 px screenshot. |
| B4 | **P1 — CI gate** | Site CI is red: Lighthouse SEO **0.92** < 0.95 because `robots.txt` is invalid/missing; the workflow requires all four categories ≥ 0.95. | Run 39379283584 Lighthouse job FAIL; `lighthouse-report` artifact, both runs SEO 0.92 (performance 99/100, accessibility 97, best-practices 100); `docs/site/robots.txt` absent; audit id `robots-txt` | Add a valid `docs/site/robots.txt` (e.g. `User-agent: *` / `Allow: /`) or otherwise make the static-root audit valid; re-run Site CI green. Do not lower the gate. |
| B5 | **P2 — accessibility/localisation** | 21 “Capture pending” notes are inside `.tool-media[aria-hidden="true"]`, so screen readers never hear the explanation. 26 `<ul class="tool-options" aria-label="Key options">` labels are English-only in the Arabic UI. Lighthouse also reports `label-content-name-mismatch` on the footer brand link and one contrast failure (accessibility still 97). | `docs/site/index.html:274` and 20 more pending tiles; `index.html:281` and 25 more `Key options` lists; Lighthouse a11y audits `label-content-name-mismatch`, `color-contrast` | Move the pending note out of the `aria-hidden` container or expose it via `aria-describedby`; localise the “Key options” label with `data-label-en/ar`; align the footer brand’s aria-label with its visible “DropWheel” text. |
| B6 | **P2 — content completeness** | Only 5 of 26 tool windows have real captures; 21 use the honest “Capture pending” tile. There are no Arabic or light-theme CI screenshots (the workflow captures dark EN only). This is a product/content gap, not a mock-up. | 21 `tool-pending` blocks; 5 real tool capture pairs; CI screenshots: `home-1440-dark.png`, `home-390-dark.png` only | Add the remaining app self-test captures before calling the page “all windows”. Add AR/light screenshot scenarios to Site CI if those states are in scope. |
| B7 | **P3 — documentation drift** | Older docs quote stale sizes (SITE-AUDIT: 105,307 B HTML / 43,783 B CSS / 222.4 KiB first load; current 129,649 / 50,645 / 266,648). `docs/website/CLAIMS.md` states its base is 7009be9 and its line numbers predate the current page. `theme-color` `#120D1A` differs from CSS `--bg` `#100b18`. Arabic `.tool-kicker`/`.abstract-label` etc. still get `letter-spacing: 0.03em` despite the audit’s “zero letter-spacing” statement. | `docs/audit/SITE-AUDIT.md`; `docs/site/README.md:87-92`; `docs/website/CLAIMS.md:3`; `index.html:9` vs `styles.css:9`; `styles.css:1481-1500` | Refresh the stale numbers/line references; align theme-color with `--bg`; remove residual Arabic letter-spacing if the zero-spacing rule is intentional. |

---

## 5. Independent claim verification

`docs/website/CLAIMS.md` was **not** used as authority (its stated base, `7009be9`, is older than
the tested page). Each row below was checked against the shipped source, the release, or a command.

| # | Claim | Verdict | Evidence |
|---:|---|---|---|
| 1 | 26 registered tool windows | **TRUE** | 26 `ToolRegistry.Register(` calls; page `data-tool` set equals the 26 `public string Id => "tools.…"` values exactly (no extras, no missing) |
| 2 | Photos 6 / Video 7 / Audio 5 / PDF 2 / Protect 3 / Files 3 | **TRUE** | page `data-cat` counts are 6/7/5/2/3/3 = 26; source ids match the page |
| 3 | Installer 62,967,948 B = 60.05 MB | **TRUE** | `gh release view v2.3.0` asset size 62,967,948; ÷ 1,048,576 = 60.0508 MiB (Windows “MB” convention) |
| 4 | Portable ZIP 89,266,161 B | **TRUE** | `gh release view v2.3.0` asset size 89,266,161 |
| 5 | Installer SHA-256 `7381cbd5…1d7156ce` | **TRUE** | downloaded the v2.3.0 installer: `Get-FileHash` = `7381cbd5b59249f3279b62af31091d89d3d8d994faa57fcdd08ec4331d7156ce`; matches the release `SHA256SUMS.txt` and the page’s short/full forms |
| 6 | ECDSA P-256 signature checked against the embedded key | **TRUE** | 88-byte `.sig`, 64-byte P1363 signature; `UpdateKey.PublicKeyBase64` (`UpdateKey.cs:10-11`); Node `crypto.verify(..., {dsaEncoding:'ieee-p1363'})` = `true` |
| 7 | `SHA256SUMS.txt`, `.exe.sig`, `.zip.sig` exist in the release | **TRUE** | `gh release view v2.3.0` lists all three (88/88/484 B) plus the `DropWheel-Setup.exe` alias |
| 8 | AES-256 ZIP | **TRUE** | `src/FileAction.Core/PasswordZip.cs:16` (WinZip AES-256 AE-2); `PasswordProtection.cs:320` (“never a weaker method”); `FUNCTION-MATRIX.md:83` |
| 9 | AES-256 PDF with print/copy permissions | **TRUE** | `src/FileAction.Core/PasswordPdf.cs:11,41` (security handler V5, PDF 2.0); `FUNCTION-MATRIX.md:90` |
| 10 | Update check 60 s after start, then once a day | **TRUE** | `src/FileAction.Core/Updates/UpdateConfig.cs:28,31` (`FromSeconds(60)`, `FromHours(24)`) |
| 11 | Recovery key 24 characters, shown once, never stored | **TRUE** | `src/FileAction.Core/Lock/RescuerKey.cs:7,16-17` (24 chars, six groups of four); `FUNCTION-MATRIX.md:239` |
| 12 | No secrets written under `%LOCALAPPDATA%` | **TRUE (current build)** | `src/FileAction.Core/Lock/LockJournal.cs:39-40` (journal records paths/state only); `LockRuntime.cs:38-45` keeps secrets in memory and deletes the legacy DPAPI vault |
| 13 | Atomic, verified replacement; auto-relock; crash recovery | **TRUE** | `LockService.cs:86`; `LockHeaderRewriter.cs:40-50`; `LockSessions.cs:43,118,159`; `LockJournal.cs:189-202`; `FUNCTION-MATRIX.md:236-238` |
| 14 | Sixteen photo sliders | **TRUE** | `src/FileAction.Core/PhotoEdit.cs:110-128` — 16 `PhotoParameterSpec` entries |
| 15 | Nine gradients and nine solids | **TRUE** | `src/FileAction.Core/AddBackground.cs:7-18` (9 enum values), `:95-109` (9 colours); `AddBackgroundView.cs:198-216` |
| 16 | Speed 0.25×–4×, audio pitch kept | **TRUE** | `src/FileAction.App/UI/Tools/Video/VideoControls.cs:154`; `VideoSpeedView.cs:9,16` presets `[0.25,0.5,1,1.5,2,4]` |
| 17 | Normalize targets EBU R128 | **TRUE** | `src/FileAction.Core/AudioLoudness.cs:7-8,211` (`loudnorm=I=…:TP=…:LRA=…`); `Catalog.cs:86` (`loudnorm=I=-16:TP=-1.5:LRA=11`) |
| 18 | 150 ms wheel / 180 ms window motion | **TRUE** | `src/FileAction.App/UI/Motion/Motion.cs:30` (`WheelOpenMs = 150`), `:51` (`WindowOpenMs = 180`) |
| 19 | Self-contained installer, no .NET install; Windows 10 1809+/11 x64 | **TRUE** | `build.cmd:31-32` (`--self-contained true -r win-x64`); `installer/DropWheel.iss:3-5,109`; `:53-55` (`MinVersion=10.0.17763`, `x64compatible`) |
| 20 | Installer is not Authenticode-signed yet | **TRUE** | `README.md:88`; `STATUS.md:143-144`; release workflow signs the update `.sig` (ECDSA P-256) but has no Authenticode step |
| 21 | “The only network request is the update check” | **FIXED in b561b2c — now accurate** | Old wording was an overclaim: the app can download FFmpeg/Pandoc when the user opts in (`ToolInstaller.cs:15-24,71,105`). Current page `index.html:777` lists the update check, optional downloads (off by default), and a dropped file/link; FAQ `:985` matches. |
| 22 | “2.4.0 is in final verification” | **UNVERIFIED / NO RELEASE** | No `v2.4.0` release or tag; `installer/DropWheel.iss:16` and `FileAction.App.csproj:24` are 2.3.0. This is the publishing gate, not a page typo. |
| 23 | MIT licence | **TRUE** | `LICENSE:1-4` (“MIT License”, 2026 Metamorfolog/DropWheel) |

Additional structural checks not in the table: all 26 page `data-tool` values match the source
`Id` set exactly; 26 cards = 26 registrations; the downloaded installer hash and `.sig` both verify
(no installer was executed).

---

## 6. GitHub Actions evidence (visual proof)

Workflow: `.github/workflows/site-ci.yml` (new on b561b2c). Run:
<https://github.com/Faisaldhd/dropwheel/actions/runs/39379283584> on `b561b2c`.

| Job | Result | Evidence |
|---|---|---|
| Size budget (<= 500,000 B) | **PASS** | `scripts/site/size_budget.py` → 402,768 B (80.6%) |
| Link check | **FAIL** | 1 dead external link: `…/DropWheel-<!--release-version:start-->2.3.0<!--release-version:end-->-Setup.exe.sig` → HTTP 404 (`index.html:931`) |
| Lighthouse desktop (all ≥ 0.95) | **FAIL** | two runs: performance 99/100, accessibility 97, best-practices 100, **SEO 92** (audit `robots-txt`: “robots.txt is not valid”) |
| Page screenshots (1440 / 390) | **PASS** | artifact `site-screenshots`: `home-1440-dark.png`, `home-390-dark.png` |

The 390 px full-page screenshot is 390×27,521 px. Its hero shows the full SHA-256 running off the
right edge and `SHA256SUMS.t` clipped — visual confirmation of B3. The 1440 px shot is the desktop
reference. The workflow captures dark English only; Arabic/light are not represented.

The earlier push run `39379279087` was cancelled by the workflow concurrency group; the
`pull_request` run above is the authoritative one for b561b2c. A dispatched run `39379289429`
showed the same red jobs. Artifacts: `lighthouse-report`, `site-screenshots`.

**No live deployment** was performed. `.github/workflows/pages.yml` deploys `docs/site` only on
push to `main`; `main` is at `cf8a1a6` and does not contain this page work.

---

## 7. Reproduce

```powershell
# static gate (expected: exit 1, PASS 28 / FAIL 2, first_load=266648)
python scripts/site/qa/qa_check.py --site docs/site --verbose
python scripts/site/qa/qa_check.py --site docs/site --json

# site-ci cross-checks (other lanes; not modified here)
python scripts/site/size_budget.py --root docs/site      # PASS 402,768
python scripts/site/check_links.py --root docs/site      # FAIL 1 dead (HTTP 404, .sig)

# release evidence (read-only; no asset executed)
gh release list --repo Faisaldhd/dropwheel --limit 3
gh release view v2.3.0 --repo Faisaldhd/dropwheel --json assets
gh release download v2.3.0 --repo Faisaldhd/dropwheel --pattern DropWheel-2.3.0-Setup.exe
# SHA-256 + ECDSA P-256 verification: installer hash 7381cbd5…1d7156ce;
# Node crypto.verify(..., {dsaEncoding:'ieee-p1363'}) against UpdateKey.PublicKeyBase64 = true

# CI evidence
gh run view 39379283584 --repo Faisaldhd/dropwheel --log-failed
gh run download 39379283584 --repo Faisaldhd/dropwheel -n site-screenshots
```

Gate definition details: `qa_check.py` is stdlib-only, makes no network calls, parses HTML/CSS
with source line numbers, and exits non-zero on any FAIL. It was also run against a scratch copy
with only B2/B3 fixed: **PASS 30 / FAIL 0**, proving the gate does not over-fail.

---

## 8. GO / NO-GO

**NO-GO at b561b2c.** The page is not publishable behind the 2.4.0 release gate for four
independent reasons: no signed 2.4.0 release, a broken `.sig` download confirmed by both the static
gate and the online link checker, mobile clipping of the full hash confirmed by the CI screenshot,
and a red Site CI (Lighthouse SEO 92 due to `robots.txt`).

**GO conditions (all required):**

1. Fix `docs/site/index.html:931`: remove the release-marker comments from the `.sig` `href`
   (the URL must end in `DropWheel-2.3.0-Setup.exe.sig` or the correct 2.4.0 name); link check 0 dead.
2. Fix the hero hash overflow: restore the short hash or add wrapping/max-width to
   `.hero-hash code`; PHONE-5 PASS and the 390 px CI screenshot shows no clipping.
3. Add a valid `docs/site/robots.txt` (or an equivalent fix) so Lighthouse SEO ≥ 0.95; Site CI all
   jobs green.
4. Ship the signed `v2.4.0` release (assets, `.sig`, `SHA256SUMS.txt`, stable alias) and replace
   every version fact in `index.html` / README, or keep the page explicitly pre-release and do
   not publish Pages.
5. Re-run `python scripts/site/qa/qa_check.py --site docs/site`; it must exit 0.
6. Optional before calling the page complete: the 21 remaining capture tiles, Arabic/light CI
   screenshots, localised “Key options” labels, and the footer brand label fix.

---

## 9. Limits of this QA

- No local browser, no local Lighthouse: the only visual evidence is the GitHub Actions
  screenshots; Lighthouse numbers are from the CI artifact. No native screen-reader pass (NVDA),
  200% zoom, High Contrast, or live-deploy check was performed.
- The static gate cannot prove runtime behaviour (theme/language persistence, filters, clipboard,
  focus order); those remain covered only by whatever CI exercises.
- No 2.4.0 artifacts exist, so any 2.4.0-specific asset/hash/version claim is unverified by
  definition; only the 2.3.0 facts are proven.
- The gate's 500 KB definition (one hero candidate, no apple-touch-icon) and the site-ci definition
  (all hero candidates plus apple-touch-icon) both pass; they are not the same number and should
  not be compared directly.
- This review did not modify `docs/site/**`. Only `scripts/site/qa/qa_check.py` and this file are
  authored by `site-qa`.
