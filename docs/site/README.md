# DropWheel product page

Static, single-page product site for **DropWheel** — served from `docs/site` by GitHub Actions
Pages at <https://faisaldhd.github.io/dropwheel/>.

The page is deliberately dependency-free: no framework, no build step, no external fonts, CSS,
scripts, images or analytics. Everything is served from this folder, and a Content-Security-Policy
blocks every outbound connection (`connect-src 'none'`). Language (EN / العربية) and theme
(dark / light) are remembered in `localStorage`; no cookies are set.

## Files

| Path | Purpose |
|---|---|
| `index.html` | The whole page. English and Arabic live in one DOM as paired `[data-lang]` spans; `html[lang]` / `html[dir]` control which half is shown. |
| `assets/styles.css` | Purple identity, dark/light tokens, responsive and RTL rules, reduced-motion rules. |
| `assets/boot.js` | Synchronous head script: applies the stored theme and language before first paint. |
| `assets/app.js` | Theme/language switches, tool filters, motion clips, copy-hash button, sticky-header state. No fetching. |
| `assets/motion/` | Wheel clip and poster, dark and light. Rendered from the shipped app by `--self-test`. |
| `assets/tools/` | Poster + short clip for the five tool windows that have real captures: crop, editphotos, annotate, redact, addbackground. |
| `assets/lock/` | Document and folder badge previews from `docs/screenshots/lock-badge` (dark/light). |
| `assets/icon/` | Favicon, header mark and Open Graph image. |
| `.nojekyll` | Serve the files as-is. |

> `tools/` is ignored by the repository's `.gitignore` (it is meant for downloaded helper
> binaries). Site captures must be staged explicitly:
>
> ```powershell
> git add -f docs/site/assets/tools
> ```

## Release state

The page is authored for **2.4.0**, but the 2.4.0 release does not exist yet. Until it does:

- the hero CTA points at the stable, already-verified alias
  `https://github.com/Faisaldhd/dropwheel/releases/latest/download/DropWheel-Setup.exe`;
- the facts under the CTA show the **last verified release** (currently 2.3.0, 60.05 MB,
  SHA-256 `7381cbd5…1d7156ce`) and link to that version's `SHA256SUMS.txt`;
- a slim release bar and the hero note say 2.4.0 is in final verification;
- the verify section uses 2.3.0 filenames, sizes and the full hash.

**Do not publish Pages until the signed 2.4.0 release is verified.** The publish checklist is in
[`docs/audit/SITE-AUDIT.md`](../audit/SITE-AUDIT.md).

### Updating for a release

Replace every version-specific fact in `index.html`:

1. `.release-bar` — update or remove the 2.4.0 verification bar.
2. `.hero-facts` — `v2.3.0`, `60.05 MB`, the short hash and `SHA256SUMS.txt` link.
3. `.hero-hash` — short SHA-256 and the versioned checksum link.
4. `.release-note` — remove it once the release is live.
5. `#verify` — the visible `Get-FileHash` / `verify-signature.ps1` cards, the versioned
   `SHA256SUMS.txt` / `.sig` links, and the collapsed **For IT · full SHA-256** block: update its
   full 64-character hash, and the installer/portable sizes and file names in the release card.
6. `.release-card` — version, button label, file names and sizes.
7. `.version-badge` in `#whats-new` — remove or replace “In final verification”.
8. Run the checks in the audit and deploy through the normal `Pages` workflow (push to `main`).

## Captures

All screenshots on the page are renders of the shipped UI:

- wheel clip and poster: `docs/screenshots/motion/wheel-{dark,light}.webp` and `*-poster.png`;
- tools: `docs/screenshots/motion/tool-{crop,editphotos,annotate,redact,addbackground}-{dark,light}*.png/webp`;
- lock badges: `docs/screenshots/lock-badge/{document,folder}-{dark,white}-256.png`.

The old `docs/screenshots/en/` PNGs show the pre-2.4.0 orange FileAction UI; they were removed from
the site and must not be reintroduced. Twenty-one of the twenty-six registered tool windows still
need captures; the audit lists them. Re-render with `scripts\screenshots.cmd` and add the new files
to `assets/tools/`, then wire them into the matching `.tool-card`.

## Local preview

```powershell
python -m http.server 8123 --bind 127.0.0.1 --directory docs/site
# http://127.0.0.1:8123/
```

No build. Edit the files and reload.

## Quality gates

Measured on the authored copy (see the audit for the full method):

- first load: **363,059 bytes** of same-origin files (document, CSS, JS, hero clip, icons; the CI size gate in scripts/site/size_budget.py is authoritative); under the
  500 KB budget;
- zero external requests, zero console errors, zero missing alt text, zero duplicate IDs;
- no horizontal overflow at 390 px;
- theme and language survive reload; tool filters, motion clips and copy button work;
- reduced motion uses posters and hides motion controls.

Lighthouse is not part of this folder yet; it must run in CI (desktop and mobile, ≥ 95) before the
Pages publish. `pages.yml` only uploads and deploys — it does not run Lighthouse.
