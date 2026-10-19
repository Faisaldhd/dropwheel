# NOTES — cross-agent requests

## Requests from download-page

1. **`DropWheel-Setup.exe` alias is missing from the release (blocks the download button).**
   `docs/site/index.html` points the main button at
   `https://github.com/Faisaldhd/dropwheel/releases/latest/download/DropWheel-Setup.exe`.
   `.github/workflows/ci.yml` (release job) currently publishes only
   `DropWheel-Setup-<version>.exe`, so that URL will 404 until the release also carries an
   unversioned alias.
   **Requested change (`.github/workflows/ci.yml`, release job, CI owner):** before
   `gh release create`, copy the versioned setup to `DropWheel-Setup.exe` (same bytes) and
   attach it, e.g. `Copy-Item (Get-Item 'release/DropWheel-Setup-*.exe') 'release/DropWheel-Setup.exe'`,
   then include it in `$files` and relax the `$files.Count -ne 3` check. If the alias is listed
   in `SHA256SUMS.txt` too, the page will prefer that line; it also works with only the
   versioned line because the bytes are identical. Alternative: change the button in
   `docs/site/index.html` to a versioned URL — but then the page needs an edit every release.

2. **GitHub Pages cannot serve `docs/site` from a branch (Pages is not deployable yet).**
   Pages’ “Deploy from a branch” source only offers `/` or `/docs`, not `docs/site`. The page is
   dependency-free, so it just needs a Pages workflow in `.github/workflows/` (CI owner):
   `actions/configure-pages` → `actions/upload-pages-artifact` with `path: docs/site` →
   `actions/deploy-pages`, triggered on `main` (or `workflow_dispatch`) with
   `permissions: pages: write, id-token: write`. Then enable **Settings → Pages → Source:
   GitHub Actions** for `https://faisaldhd.github.io/dropwheel/`. No custom build step is needed.

3. **The three screenshots are stale (content review needed).**
   `docs/screenshots/en/settings.png`, `wheel-actions.png` and `jobs.png` are the only real PNGs
   that exist, so `docs/site/assets/` uses them as instructed — but they show the old orange
   FileAction UI, the title “FileAction” and a Czech (“Čeština”) option, not the 2.2.0 violet
   build. Replace the three files (keeping the same names) when R-5 screenshots exist; the HTML
   dimensions and both language alt texts must be updated with them.

4. **CORS makes the inline SHA-256 fall back to the link (expected, but worth a decision).**
   GitHub redirects `.../releases/latest/download/SHA256SUMS.txt` to
   `release-assets.githubusercontent.com`, and that response carries no
   `Access-Control-Allow-Origin` (verified 2026-10-19: 302 → 302 → 200 with no ACAO, even with
   an `Origin` header). A browser `fetch` from `faisaldhd.github.io` will therefore be blocked,
   so the page will normally show the required “Open SHA256SUMS.txt” link rather than the hash.
   That matches the brief (“if the fetch fails … show a link”), but if the lead wants the hash
   visible inline, the GitHub REST API (`api.github.com` sends CORS and exposes
   `assets[].digest`) or a same-origin copy of `SHA256SUMS.txt` would be needed — both are
   outside this agent’s owned files.

5. **Current `latest` release (v2.1.1) has no setup asset.** Its assets are only
   `DropWheel-2.1.1-portable-win-x64.zip` and `SHA256SUMS.txt`, and that
   `SHA256SUMS.txt` lists only the zip. The page’s download button and checksum therefore
   cannot be end-to-end checked until **v2.2.0** is released with the setup exe (and the
   unversioned alias from item 1). Nothing to change in the page for this.
