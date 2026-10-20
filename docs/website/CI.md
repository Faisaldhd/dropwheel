# Site CI and deploy gates

This document covers the static download page in `docs/site/` and the GitHub
Actions that measure and publish it.

- Page owner: website lead. The CI lane does **not** edit `docs/site/**`.
- Workflows: `.github/workflows/site-ci.yml`, `.github/workflows/pages.yml`.
- Gate scripts: `scripts/site/size_budget.py`, `scripts/site/check_links.py`,
  `scripts/site/stamp_release.py`.

If a gate fails because of page content, report the exact finding to the lead.
Never lower the Lighthouse threshold, widen the size budget or delete a link
check to make a run green.

## When Site CI runs

`Site CI` (`.github/workflows/site-ci.yml`) runs on:

| Event | Scope |
| --- | --- |
| `pull_request` | changes under `docs/site/**`, `scripts/site/**`, `docs/website/CI.md` or the workflow itself |
| `push` | any branch matching `site/**` or the `feat/2.4.0-website` branch |
| `workflow_dispatch` | manual re-run from the Actions tab |

The workflow has three independent gates plus a screenshot job. Each job is
shown separately in the run, so a failure names the gate directly.

## Gate 1: Lighthouse CI (desktop, every category >= 0.95)

Runner: `treosh/lighthouse-ci-action@v12`, Lighthouse CI against an explicit
static server (`python3 -m http.server 8080 --directory docs/site --bind
127.0.0.1`) with the **desktop preset** and two runs per category. The job
first proves `/robots.txt` is served as HTTP 200 `text/plain` from the origin
root.

Two page/harness facts matter for the robots audit:

- LHCI's built-in `staticDistDir` server did not serve `/robots.txt`, which made
  Lighthouse report `robots-txt = 0` and SEO 0.92, so the workflow now runs an
  explicit server.
- Chrome's `Network.loadNetworkResource` (used by Lighthouse's protocol
  fetcher) enforces the document CSP. The page therefore uses
  `connect-src 'self'`: `connect-src 'none'` blocked `/robots.txt` with a CSP
  violation even though curl got 200. `'self'` still blocks every third-party
  or outbound connection.

Assertions are hard errors, not warnings:

| Category | Minimum score |
| --- | --- |
| performance | 0.95 |
| accessibility | 0.95 |
| best-practices | 0.95 |
| SEO | 0.95 |

A score below any threshold fails the job. The gate is intentionally a floor:
a regression from a previous 1.00 to 0.96 still fails once it drops under 0.95,
and a change that drops a previously passing category below the floor is
reported with the failing audit in the Lighthouse report.

The report is uploaded as the `lighthouse-report` artifact (HTML and JSON under
`.lighthouseci/`). Open the run, download the artifact, and open the `.html`
file for the audit list and the `.json` file for machine-readable scores.

Local equivalent (requires Node and a Chromium install; copy the
`lighthouserc.json` written by the workflow step):

```bash
npx @lhci/cli@0.15.1 autorun --config=lighthouserc.json
```

## Gate 2: initial-document size budget (<= 500,000 bytes)

Script: `scripts/site/size_budget.py` (Python standard library only).

```bash
python scripts/site/size_budget.py --root docs/site
```

The budget counts **uncompressed file bytes** that the browser can request
before first paint:

1. `docs/site/index.html` itself.
2. Every local `rel=stylesheet`, `rel=icon`, `rel=apple-touch-icon`,
   `rel=preload` / `rel=modulepreload` link in `<head>`.
3. Every local `<script src>` in `<head>` (`assets/boot.js`,
   `assets/app.js`).
4. Every image candidate in the site header (the brand mark) and in the hero
   `<section class="hero">`: all `<source srcset>` candidates plus the
   fallback `<img>` (`assets/motion/wheel-*.webp`).
5. Any image carrying `fetchpriority="high"` anywhere in the document.

Duplicate paths are counted once. Compression (gzip/brotli) is not assumed.
The gate fails when the total is **greater than** 500,000 bytes.

Below-the-fold assets are deliberately excluded:

- images with `loading="lazy"` (tool screenshots, previews, lock illustrations);
- `.webp` clip files that are only fetched when a tool card is played;
- runtime theme variants that are not part of the hero `<picture>`;
- anything outside `docs/site/index.html`'s initial document (for example
  `docs/site/README.md`).

At the time of writing (commit `042a71d`, including feat `db01f16`,
2026-10-20) the measured payload is **390,552 bytes**: 129,854 HTML + 52,785
CSS + 16,680 app JS + 1,208 boot JS + 1,983 favicon + 52,660 apple-touch-icon +
3,644 brand mark + 131,738 hero candidates. That is 78.1% of the 500,000 byte
budget, with 109,448 bytes of headroom. The script prints the per-file table and
the total on every run and writes the same table to the job summary.

Numbers above are the bytes in the Git tree (LF), which is what GitHub Pages
serves and what CI measures. A Windows working tree with `core.autocrlf=true`
reports about 3.5 KB more because CRLF adds one byte per line; the CI number is
authoritative for the budget.

## Gate 3: link check

Script: `scripts/site/check_links.py` (Python standard library only).

```bash
# CI (online): internal + external links
python scripts/site/check_links.py --root docs/site --timeout 20 --retries 2

# Local runs with no network: skip external HTTP entirely
python scripts/site/check_links.py --root docs/site --offline
```

Internal checks always run:

- every local `href`, `src`, `srcset` and `poster` resolves to an existing file
  under `docs/site`;
- every `#fragment` resolves to an `id` in the target HTML document (this
  includes the inline SVG `<symbol id="i-…">` references).

External checks:

- every `http(s)` URL is requested with `HEAD`, falling back to a ranged `GET`
  when a server rejects `HEAD`;
- redirects are followed, so GitHub's release-asset redirects to
  `objects.githubusercontent.com` are tolerated;
- **404 and 410 fail the gate**;
- 401, 403, 429 and 5xx are reported as warnings, not failures, because rate
  limits and transient upstream errors are not evidence of a dead link.
  `--strict` turns those warnings into failures when a release check needs it;
- network errors after retries are warnings in normal mode.

The script prints counts for internal links, checked fragments, unique external
URLs and warnings. Current page: **102 internal links, 81 fragments, 19
external references (12 unique URLs), 0 dead**. The link gate is run online in
CI; `--offline` exists only for local work and is never used by the workflow.

Release marker tokens are stripped from a URL before it is resolved
(`<!--release-version:start-->…<!--release-version:end-->` and the other four
marker kinds), because the deploy stamp rewrites or removes them before the
page is served. This keeps a template href such as
`…/DropWheel-<!--release-version:start-->2.3.0<!--release-version:end-->-Setup.exe.sig`
from being mistaken for a literal 404. The script reports how many URLs needed
that normalization.

## Screenshots

The `screenshots` job starts `python3 -m http.server` over `docs/site`, installs
Playwright Chromium and captures full-page PNGs for four states at two widths:

| Width | States |
| --- | --- |
| `1440 x 900` | EN dark, EN light, AR dark, AR light |
| `390 x 844` | EN dark, EN light, AR dark, AR light |

Files are named `home-1440-en-dark.png`, `home-1440-ar-light.png`, and so on.
Before each capture the script forces `scroll-behavior: auto` (the page sets
`smooth`, which otherwise makes rapid programmatic jumps animate and never
reach the bottom), walks the document in ~800 px steps with a 60 ms pause,
checks that the scroll actually reached the bottom, asserts that zero
`[data-reveal]` nodes remain hidden, waits for pending images, scrolls back to
the top, blurs the active element and parks the pointer at (0,0) so the skip
link is not captured in its focus-visible state, waits 300 ms and only then
screenshots. At 390 px it hard-fails the step when
`document.scrollingElement.scrollWidth > window.innerWidth + 1`, which catches
horizontal overflow in either direction or language.

They are uploaded as the `site-screenshots` artifact for visual review by the
lead. Screenshots are evidence, not a score gate; Lighthouse remains the
performance/accessibility gate.

## Artifacts

From the Actions run page, download:

| Artifact | Contents |
| --- | --- |
| `lighthouse-report` | `.lighthouseci/*.html` and `*.json` reports, including category scores |
| `site-screenshots` | eight full-page PNGs (`home-{1440,390}-{en,ar}-{dark,light}.png`) |

CLI equivalents:

```bash
gh run list --branch site/ci
gh run watch <run-id>
gh run download <run-id> -n lighthouse-report -D .lighthouse
gh run download <run-id> -n site-screenshots -D screenshots
```

## Deploy gate (`pages.yml`)

Publishing is **manual only**. There is no `push` trigger, so an ordinary commit
to `main` or to a `site/**` branch can never publish the page. (A `v*` tag
trigger was deliberately left out: a tag can exist before its release assets are
uploaded, and a tag push should not publish a half-verified release.)

### Release gate

Before anything is uploaded, the deploy job:

1. Resolves the newest stable release (`releases/latest` excludes drafts and
   pre-releases) or the tag supplied in `release_tag`. Drafts and pre-releases
   are refused.
2. Requires a `DropWheel-Setup.exe` asset in that release and reads its exact
   size from the GitHub API.
3. Downloads that release's `SHA256SUMS.txt` and requires a valid
   `DropWheel-Setup.exe` line. If the installer is absent from the release or
   from the sums file, the job fails and nothing is deployed.
4. Runs `stamp_release.py` against a **copy** of `docs/site` in `build/site`.
   The repository copy is never modified; a `git diff --exit-code -- docs/site`
   step proves it.

### Marker contract in `docs/site/index.html`

`stamp_release.py` replaces the **contents** of every marker pair below (the
markers themselves stay). Version, MiB, bytes and SHA-256 may repeat — the
page uses them in the release bar, hero facts, For IT commands, release card,
download card and installer filenames — and every occurrence is replaced.
At commit `042a71d` the page has 8 version, 4 MiB, 2 SHA-256, 1 bytes and 1
status pair; the status pair must appear exactly once. There is no note marker
to stamp: the top status strip is the only status surface.

| Marker pair | Written value |
| --- | --- |
| `<!--release-version:start-->…<!--release-version:end-->` | version without a leading `v`, e.g. `2.4.0` |
| `<!--release-mb:start-->…<!--release-mb:end-->` | `bytes / 1048576`, two decimals, e.g. `60.05` |
| `<!--release-bytes:start-->…<!--release-bytes:end-->` | exact decimal bytes with separators, e.g. `62,967,948` |
| `<!--release-sha256:start-->…<!--release-sha256:end-->` | full lowercase 64-hex SHA-256 of `DropWheel-Setup.exe` |
| `<!--release-status:start-->…<!--release-status:end-->` | one bilingual status strip (`release-bar__tag` + `release-bar__text`, English and Arabic inline) |

Every `releases/download/v<old-version>/` URL is rewritten to the deployed
version, including the `DropWheel-<old-version>-…` asset filename segment.
Stable `releases/latest/download/DropWheel-Setup.exe` links are untouched.
No inline script or style is added, so the page's CSP is unchanged.

The script fails closed (exit 2, no output written) if **any** marker pair is
missing or unbalanced, if the asset is missing from `SHA256SUMS.txt`, or if the
marker regions overlap. Missing `release-bytes` markers are a hard failure; the
page must carry all five marker kinds before a deploy.

### Dry run (required before the first stamping deploy)

```bash
# use the SHA256SUMS.txt downloaded from the release
python scripts/site/stamp_release.py \
  --index docs/site/index.html \
  --sums /path/to/SHA256SUMS.txt \
  --version v2.4.0 \
  --asset-size 62967948 \
  --dry-run
```

`--dry-run` validates the markers and sums, prints the version/bytes/SHA-256 it
would write, and modifies nothing. Without `--dry-run`, pass either
`--output build/site` (deploy artifact; copies the whole site first) or
`--in-place` (maintainer use only). The deploy workflow always uses
`--output build/site`.

### Trigger a deploy after release verification

Prerequisites:

1. The signed `2.4.0` release is the newest stable release and contains
   `DropWheel-Setup.exe` plus `SHA256SUMS.txt`.
2. The page with all five marker kinds is on `main` (or on the branch selected
   in the workflow's "Use workflow from" picker).
3. The Site CI run for that page is green (Lighthouse >= 0.95, size budget,
   link check).

Then run:

```bash
gh workflow run pages.yml --ref main -f confirm=publish -f release_tag=latest
gh run list --workflow=pages.yml
gh run watch <run-id>
```

Or: Actions → **Pages** → **Run workflow** → branch `main` → confirm `publish`
→ release tag `latest` (or an explicit `v2.4.0`).

The job fails before upload if the confirmation is not exactly `publish`, the
release is a draft/pre-release, `DropWheel-Setup.exe` is missing from the
release or from `SHA256SUMS.txt`, any marker is missing, or a stale
`releases/download/v<other-version>/` link remains. A successful run deploys
`build/site` to the `github-pages` environment and prints the page URL.

## Troubleshooting

| Symptom | Meaning | Action |
| --- | --- | --- |
| Lighthouse category < 0.95 | page regression | report the failing audit from `lighthouse-report` to the lead; do not lower the threshold |
| SEO 0.92 with `robots-txt` score 0 | `/robots.txt` missing, or CSP `connect-src` blocks the protocol fetch | page owner adds a valid `robots.txt` and keeps `connect-src 'self'` (Chrome's fetcher enforces CSP) |
| `size_budget.py` exits 1 | initial payload > 500,000 B | identify the added file in the per-file table; report to the lead |
| `check_links.py` DEAD 404/410 | broken internal path or external URL | report the exact `file:line` to the lead; do not delete the check |
| stamp exits 2 "marker(s) missing" | page lacks one of the five marker pairs | page owner adds the marker; deploy stays blocked |
| stamp exits 2 "missing from SHA256SUMS.txt" | release has no `DropWheel-Setup.exe` | finish the release assets/checksums, then re-run |
| Pages job refuses | `confirm` was not `publish` | re-dispatch with `confirm=publish` |
