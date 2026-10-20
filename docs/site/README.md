# DropWheel download page

Static, single-page site for **DropWheel 2.4.0**, published with GitHub Pages.

**Live URL:** <https://faisaldhd.github.io/dropwheel/>

English and Arabic on one page with an `EN / العربية` toggle. The choice is stored in
`localStorage` (`dropwheel.download.lang`). No build step, no framework, no external
fonts, CSS, JS, CDNs or analytics — every asset is served from this folder.

## Contents

| File | Purpose |
|---|---|
| `index.html` | The whole page (English and Arabic blocks). |
| `assets/styles.css` | Dark violet theme (`#8224D4`, `#16121F`, `#1D1729`), logical CSS for RTL. |
| `assets/app.js` | Language toggle + runtime SHA-256 lookup. |
| `assets/dropwheel-256.png` | App icon, copied from `branding/DropWheel-256.png` (byte-identical). |
| `assets/settings.png`, `assets/wheel-actions.png`, `assets/jobs.png` | Screenshots, copied from `docs/screenshots/en/`. |
| `.nojekyll` | Serve the files as-is (no Jekyll processing). |

## Publishing

The page is served from this folder (`docs/site`). GitHub’s “Deploy from a branch” source
only accepts `/` or `/docs`, so deployments use GitHub Actions:
`actions/upload-pages-artifact` with `path: docs/site`, then `actions/deploy-pages`.
Enable **Settings → Pages → Source: GitHub Actions** for `https://faisaldhd.github.io/dropwheel/`.

## Updating the version

1. Change `2.4.0` in **both** language blocks of `index.html` (the `Version` / `الإصدار` line) and in
   the `<meta name="description">` line, and point the two release-notes links at the new notes file.
   The page intentionally links the **stable aliases** (`DropWheel-Setup.exe`, `SHA256SUMS.txt`) rather
   than a versioned asset name, so the download button keeps working for the next release.
2. The SHA-256 is **not** stored in the page. It is fetched at runtime from
   `https://github.com/Faisaldhd/dropwheel/releases/latest/download/SHA256SUMS.txt` and the
   `DropWheel-Setup*.exe` line is displayed. Nothing to update after a release.
3. If the release asset naming changes, update `SUMS_URL` and the `SETUP_LINE` pattern at the
   top of `assets/app.js`. If the fetch fails (CORS, offline, release not published yet), the
   page shows a link to `SHA256SUMS.txt` and never a guessed hash.
4. Each release must publish the setup twice: `DropWheel-Setup-<version>.exe` and the stable
   `DropWheel-Setup.exe` alias used by the download button.

> NOTE: GitHub release downloads redirect to `release-assets.githubusercontent.com`, which does
> not send `Access-Control-Allow-Origin`. The `fetch` is therefore expected to fall back to the
> `SHA256SUMS.txt` link on GitHub Pages. That is the intended safe behaviour: the hash is never
> guessed.

## Updating the screenshots

Replace the three PNGs in `assets/` and keep the `width`/`height` attributes in `index.html`
in sync with the new pixel sizes. Update the English **and** Arabic alt text/captions.

> NOTE: the three screenshots were copied from `docs/screenshots/en/` and show the violet 2.2.x
> Settings, wheel and Jobs windows — they predate the 2.4.0 tool windows and the **Animations** card.
> Refresh them with the CI screenshots job (or `scripts/screenshots.cmd` on a desktop session) and keep
> the English and Arabic alt text in sync; tracked in [`docs/BACKLOG.md`](../BACKLOG.md).
