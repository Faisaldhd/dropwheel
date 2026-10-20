# Website QA — DropWheel 2.4.0 product page (final candidate)

**Agent:** `site-qa` · **Lane:** site · **Branch:** `site/qa`
**Tested page head:** `beb249b` (`origin/feat/2.4.0-website`, includes the `db01f16` connect-src fix)
**Date:** 2026-10-20 09:43 Riyadh (Asia/Riyadh) · **Scope:** static offline gate over `docs/site`,
independent claim checks, and the GitHub Actions Site CI run for this exact head.
No local browser was opened, no app/installer/build was run, and `docs/site/**` was not modified
by QA.

> The page moved during review (7009be9 → b561b2c → 6711877 → beb249b). Every number below is for
> **beb249b** and is reproducible from the committed `qa_check.py` on this worktree.

---

## 1. Bottom line

**Page gate: GO. Publish gate: NO-GO.**

- **Static gate: PASS 30 / FAIL 0** on `beb249b`.
- **Site CI is green on the exact head** — run
  [39381494274](https://github.com/Faisaldhd/dropwheel/actions/runs/39381494274):
  Size budget PASS, Link check PASS, Page screenshots PASS, Lighthouse PASS
  (**100 / 100 / 100 / 100** in both runs: performance, accessibility, best-practices, SEO).
- The two earlier P1 defects are fixed and verified:
  `.sig` URL has no marker text; the hero SHA-256 wraps on two lines at 390 px.
- **Do not publish Pages yet:** there is still no signed `v2.4.0` release/tag. `gh release list`
  shows `v2.3.0` as latest, while the code/installer are now version **2.4.0** and the page still
  shows the 2.3.0 signed download + 2.3.0 hash. Publishing now would advertise a 2.4.0 release that
  does not exist. The repository's own instruction is not to publish Pages before that release.

---

## 2. Gate verdict table

Command for every static row: `python scripts/site/qa/qa_check.py --site docs/site` (exit 0 on this head).

| Check | Result | Evidence (beb249b) |
|---|---|---|
| EXT-1 no external subresources | **PASS** | 0 external resource references; 18 external anchors are navigation-only |
| EXT-2 no inline event handlers | **PASS** | 0 `on*=` attributes |
| EXT-3 no executable inline script | **PASS** | 0 executable inline scripts; 1 inert `application/ld+json` data block skipped |
| EXT-4 no analytics/tracker identifiers | **PASS** | 0 tracker identifiers in scripts/URLs/styles |
| CSP-1 CSP meta in `<head>` | **PASS** | `index.html:11`, before first subresource |
| CSP-2 CSP restrictive | **PASS** | `default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'none'; form-action 'none'`. `connect-src 'self'` accepted only under the no-outbound proof in §4; `'none'` would also pass. Wildcard/`https:`/`data:`/`unsafe-inline` still fail. |
| CSP-3 `style-src 'self'` viable | **PASS** | 0 style attributes, 0 `<style>` blocks, 0 JS style writes |
| ID-1 unique ids | **PASS** | 96 ids, 0 duplicates |
| ID-2 internal anchors resolve | **PASS** | 81 `#…` href/use references, 0 missing |
| ID-3 ARIA/`for` targets resolve | **PASS** | 21 `aria-labelledby`/`describedby`/`controls`/`for` references, 0 missing |
| IMG-1 alt + dimensions | **PASS** | 11 `<img>`, all with `alt`, `width`, `height` |
| MEDIA-1 video/source | **PASS** | 0 `<video>`; 4 `<picture><source>` with `srcset`+`type` and dimensioned fallback `<img>` |
| HEAD-1 one h1 | **PASS** | 1 `<h1>`, 68 headings |
| HEAD-2 no skipped levels | **PASS** | h1→h2→h3, no skip |
| LANG-1 html lang/dir | **PASS** | `lang="en" dir="ltr"` |
| LANG-2 Arabic in `lang="ar"` | **PASS** | 0 Arabic text nodes outside `lang="ar"` |
| LANG-3 no mixed-language prose | **PASS** | 0 Arabic in EN scopes; 0 non-technical Latin tokens in AR scopes |
| LANG-4 same sections both languages | **PASS** | 9 sections, EN/AR span counts equal |
| CTA-1 verified download URL | **PASS** | 3 download CTAs (3 EN/3 AR), all exactly the `releases/latest/download/DropWheel-Setup.exe` URL; 4 button CTAs use that URL or an in-page anchor |
| LINK-1 external anchors allowlisted | **PASS** | 18 anchors: `github.com/Faisaldhd/dropwheel`, `github.com/Metamorfolog/file_action`, repo `LICENSE` path |
| LINK-2 external URLs well-formed | **PASS** | 18/18; `.sig` link no longer contains marker text |
| LINK-3 `target=_blank` rel | **PASS** | 0 `target="_blank"`; all external links have `rel="noopener noreferrer"` |
| TXT-1 no TODO/FIXME/lorem/placeholder | **PASS** | 0 placeholder-like strings in visible text/attributes |
| ASSET-1 referenced assets exist | **PASS** | 35 unique referenced files, 730,157 B, 0 missing (31 image files + CSS + 2 JS + manifest) |
| SIZE-1 first load < 500,000 B | **PASS** | **264,110 B** (52.8% of budget); reduced-motion variant 221,196 B |
| PHONE-1 viewport meta | **PASS** | `index.html:5`, `width=device-width, initial-scale=1` |
| PHONE-2 min-width ≤ 360 px | **PASS** | 412 CSS rules; largest `min-width` is 62 px |
| PHONE-3 no unconstrained 100vw | **PASS** | 0 |
| PHONE-4 nowrap only short/clipped | **PASS** | 7 nowrap rules; longest text 31 chars or clipped |
| PHONE-5 long tokens wrap/scroll | **PASS** | hero 64-char hash has `overflow-wrap:anywhere` + `word-break:break-all`; the 390 px CI screenshot shows two clean lines |

The gate was also run against scratch copies to prove it still fails the wrong things:
`connect-src 'self' *` → FAIL; `connect-src 'self'` + injected `fetch()` → FAIL;
`connect-src 'self'` + an `https://` image → FAIL; `connect-src 'none'` → PASS.

---

## 3. Measured numbers (beb249b)

| Metric | Value | Definition |
|---|---:|---|
| HTML | **130,937 B** | document |
| CSS | **54,732 B** | `assets/styles.css` |
| JS | **18,412 B** | `boot.js` 1,244 + `app.js` 17,168 |
| **First load** | **264,110 B** | document + stylesheet + both scripts + header mark + favicon + worst-case hero image (7 files) |
| First load, reduced motion | 221,196 B | same, hero poster |
| Budget | **PASS, 52.8% used** | 264,110 / 500,000; 235,890 B headroom |
| Whole site tree | **1,275,854 B / 55 files** | includes lazy media, social images, docs |
| Referenced local assets | 35 files / 730,157 B | every `src`, `srcset`, `data-dark/light`, `data-clip/poster-*`, CSS, scripts, manifest |
| Image files referenced | 31 | 11 `<img>` elements; theme/clip/source variants share the rest |
| External subresource requests | **0** | canonical/OG metadata URLs are not fetches |
| External anchors | 18 | navigation only |
| Tool cards | 26 | 6 photos / 7 video / 5 audio / 2 PDF / 3 protect / 3 files; all `data-tool` values map to source `Id`s |
| Pending capture tiles | 21 | 5 of 26 tool windows have real captures |
| CSS rules parsed | 412 | phone-width checks |

First-load composition: `index.html` 130,937 + `styles.css` 54,732 + `boot.js` 1,244 +
`app.js` 17,168 + `dropwheel-128.webp` 3,644 + `dropwheel-32.png` 1,983 +
`wheel-light.webp` 54,402 = **264,110 B**. Site CI's broader definition
(`scripts/site/size_budget.py`) is **394,106 B (78.8%)**, also PASS.

There are 12 responsive variants on disk (2 `*-360.webp` motion posters + 10 `*-320.webp` tool
posters) that no HTML or CSS currently references. They do not affect first load; see §6.

---

## 4. CSP-2 exception: why `connect-src 'self'` is allowed here

The page changed `connect-src` from `'none'` to `'self'` in `db01f16`. Reason:
Chrome applies the document CSP to Lighthouse's own protocol fetch, so `connect-src 'none'`
made the audit unable to read `/robots.txt` and capped SEO at **0.92** (lighthouse#16597).
With `'self'`, the final Site CI run scores **100** in every category.

`qa_check.py` now accepts `connect-src` only when it is **exactly `'none'` or exactly `'self'`**.
For `'self'` it must additionally prove the page still makes no outbound request:

1. **zero** absolute `http(s)` resource references (`src`, `srcset`, `poster`,
   stylesheet/icon/manifest/preload `href`, CSS `url()`); and
2. **zero** `fetch(`, `XMLHttpRequest`, `WebSocket`, `EventSource`, `sendBeacon`, or
   `navigator.serviceWorker` uses in `index.html` and the two local scripts.

On this head both are true: `0` external resource refs and `0` API uses. Anything looser still
fails: `'self' *`, `https:`, `data:`, a wildcard, or `unsafe-inline` in `connect-src`; so does
`'self'` with an injected `fetch()` or an absolute image URL (verified against scratch copies).
The page's “no outbound network request” copy remains accurate for page-initiated traffic.

---

## 5. Independent claim verification

`docs/website/CLAIMS.md` is at base `7009be9` and was not used as authority. Claims were checked
against the shipped source, the release, and commands.

| # | Claim | Verdict | Evidence |
|---:|---|---|---|
| 1 | 26 registered tool windows | **TRUE** | 26 `ToolRegistry.Register(` calls; the page's 26 `data-tool` ids equal the source `Id` set exactly |
| 2 | Photos 6 / Video 7 / Audio 5 / PDF 2 / Protect 3 / Files 3 | **TRUE** | page `data-cat` counts = 6/7/5/2/3/3 = 26 |
| 3 | Installer 62,967,948 B = 60.05 MB | **TRUE** | `gh release view v2.3.0`: asset size 62,967,948; ÷ 1,048,576 = 60.0508 MiB |
| 4 | Portable ZIP 89,266,161 B | **TRUE** | v2.3.0 asset size 89,266,161 |
| 5 | Installer SHA-256 `7381cbd5…1d7156ce` | **TRUE** | downloaded the installer; `Get-FileHash` matches the release `SHA256SUMS.txt` and the page's short/full forms |
| 6 | ECDSA P-256 signature against the embedded key | **TRUE** | 64-byte P1363 signature; `UpdateKey.PublicKeyBase64`; Node `crypto.verify(..., dsaEncoding:'ieee-p1363')` = `true` |
| 7 | `SHA256SUMS.txt`, `.exe.sig`, `.zip.sig` assets exist | **TRUE** | `gh release view v2.3.0` lists all; the page now links the real versioned `.sig` |
| 8 | AES-256 ZIP | **TRUE** | `src/FileAction.Core/PasswordZip.cs:16`; `PasswordProtection.cs` AE-2 writer; `FUNCTION-MATRIX.md:83` |
| 9 | AES-256 PDF with permissions | **TRUE** | `src/FileAction.Core/PasswordPdf.cs:11,41` (security handler V5, PDF 2.0); `FUNCTION-MATRIX.md:90` |
| 10 | Update check 60 s after start, then daily | **TRUE** | `UpdateConfig.cs:28,31` |
| 11 | 24-character recovery key, shown once, not stored | **TRUE** | `RescuerKey.cs:7,16-17`; `FUNCTION-MATRIX.md:239` |
| 12 | No secrets written under `%LOCALAPPDATA%` | **TRUE** | `LockJournal.cs:44-45` (paths/state only); `LockRuntime.cs:26,39-45` (in-memory vault; legacy file deleted) |
| 13 | Atomic verified replacement; auto-relock; crash recovery | **TRUE** | `LockService.cs:89-90`; `LockHeaderRewriter.cs:40-50`; `LockSessions.cs:43,160,194`; `FUNCTION-MATRIX.md:236-238` |
| 14 | Sixteen photo sliders | **TRUE** | 16 `new(PhotoParameter…` entries in `PhotoEdit.cs` specs |
| 15 | Nine gradients and nine solids | **TRUE** | `AddBackground.cs` enum + 9-colour palette; `AddBackgroundView.cs` swatch loops |
| 16 | Speed 0.25×–4× | **TRUE** | `VideoControls.cs:154`; `VideoSpeedView.cs:16` presets `[0.25,0.5,1,1.5,2,4]` |
| 17 | Normalize to EBU R128 | **TRUE** | `AudioLoudness.cs:211` `loudnorm=I=…:TP=…:LRA=…`; `Catalog.cs` default filter |
| 18 | 150 ms wheel / 180 ms window motion | **TRUE** | `Motion.cs:30,51` |
| 19 | Self-contained; Windows 10 1809+/11 x64 | **TRUE** | `build.cmd:31-32`; `installer/DropWheel.iss:53-54` (`MinVersion=10.0.17763`, `x64compatible`) |
| 20 | Installer is Authenticode-unsigned | **TRUE** | `README.md:88`; `STATUS.md:143-144`; no Authenticode step in release CI |
| 21 | MIT licence | **TRUE** | `LICENSE:1-4` |
| 22 | Code/installer version is 2.4.0 | **TRUE** | `installer/DropWheel.iss:16`; `FileAction.App.csproj:24` |
| 23 | Published `v2.4.0` release / “final verification” is publishable now | **UNVERIFIED — NO RELEASE** | `gh release list` latest is `v2.3.0`; no `v2.4.0` tag. This is the publish gate, not a page typo. |

---

## 6. CI evidence and residual observations

**Run:** [39381494274](https://github.com/Faisaldhd/dropwheel/actions/runs/39381494274) on
`beb249b` — all four jobs **success**.

| Job | Result | Evidence |
|---|---|---|
| Size budget (<= 500,000 B) | **PASS** | 394,106 B (78.8%) |
| Link check | **PASS** | 0 dead external links |
| Lighthouse desktop (all ≥ 0.95) | **PASS** | both runs 100 / 100 / 100 / 100 (performance, accessibility, best-practices, SEO) |
| Page screenshots (1440/390 × EN/AR × dark/light) | **PASS** | 8 PNGs: 1440/390 × EN/AR × dark/light |

The 390 px EN dark screenshot shows the 64-char hash wrapped across two lines inside the card and
`SHA256SUMS.txt` fully visible — the earlier mobile clipping is gone.

**Residual observations, not gate failures:**

- **Skip link in mobile screenshots (P3, visual):** all four 390 px screenshots show the
  “Skip to content” pill over the release bar. CSS hides it at `inset-block-start: -100px` and only
  `:focus-visible` moves it to 12 px, so this is most likely a capture-time focus/full-page artifact;
  confirm in a real browser before publishing. It does not affect the Lighthouse 100.
- **21 pending captures (P2, content):** only 5 of 26 tool windows have real captures; the rest use
  the honest “Capture pending” tile. The pending text is no longer inside an `aria-hidden` container.
- **12 unused responsive variants (P3, media):** 2 motion `-360` posters and 10 tool `-320` posters
  exist under `docs/site/assets` but are referenced by neither HTML nor CSS. They add no delivery
  cost (lazy/unreferenced) but are dead files; remove or wire them.
- **Docs drift (P3):** `docs/site/README.md:87` still quotes a 222.4 KiB first load (measured:
  264,110 B). `docs/website/CLAIMS.md` is based on `7009be9`. Refresh both when the release lands.
- **No `v2.4.0` artifacts:** any 2.4.0-specific installer/hash/screenshot claim remains unverified
  until the release exists.

---

## 7. GO / NO-GO

**Page gate: GO.** The exact candidate `beb249b` passes all 30 static checks, Site CI is green, and
Lighthouse is 100 across all four categories. The two earlier P1s (`.sig` 404, 390 px hash clipping)
are fixed and visually confirmed.

**Publish gate: NO-GO at this head.** The page is still a 2.3.0-download / 2.4.0-preview page and
there is no signed 2.4.0 release. Publishing Pages now would put a 2.4.0 “final verification” page
live while every download is 2.3.0.

**GO for publishing requires:**

1. A signed `v2.4.0` tag/release with the installer, portable ZIP, both `.sig` files,
   `SHA256SUMS.txt`, and the stable `DropWheel-Setup.exe` alias; verify the hash and ECDSA signature.
2. Replace every version fact in `docs/site/index.html` / `README.md` for 2.4.0 (release bar, hero
   facts/hash, verify links, release card, version badge) and update `docs/website/CLAIMS.md`.
3. Re-run `python scripts/site/qa/qa_check.py --site docs/site` on the exact merge/deploy head
   (must exit 0) and keep Site CI green.
4. Confirm the mobile skip-link observation in a real browser; fix if it is not a capture artifact.
5. Optional but open: the 21 tool-window captures and the 12 unused responsive variants.

---

## 8. Reproduce

```powershell
# Static gate: expected PASS 30 / FAIL 0, first_load=264110
python scripts/site/qa/qa_check.py --site docs/site --verbose
python scripts/site/qa/qa_check.py --site docs/site --json

# Site-ci cross-checks (other lanes; not modified here)
python scripts/site/size_budget.py --root docs/site      # PASS 394,106
python scripts/site/check_links.py --root docs/site      # PASS (CI: 0 dead)

# CI evidence
gh run view 39381494274 --repo Faisaldhd/dropwheel
gh run download 39381494274 --repo Faisaldhd/dropwheel -n site-screenshots
gh run download 39381494274 --repo Faisaldhd/dropwheel -n lighthouse-report
```

The static gate is stdlib-only, makes no network calls, and exits non-zero on any FAIL. CSP-2
negative tests (looser policy, injected `fetch()`, absolute image URL) were run on scratch copies;
each returns FAIL, while `connect-src 'none'` returns PASS.

---

## 9. Limits

- No local browser, local Lighthouse, or native screen-reader pass. Visual evidence is the CI
  screenshot artifact; Lighthouse numbers are the CI artifact for `beb249b`.
- The skip-link observation is from those screenshots only; it is not reproduced from the CSS.
- No live deployment was tested (`pages.yml` deploys `docs/site` only on push to `main`).
- 2.4.0 artifacts do not exist, so 2.4.0-specific facts are unverified by definition.
- QA did not modify `docs/site/**`; only `scripts/site/qa/qa_check.py` and this report are owned by
  `site-qa`.
