# DropWheel 2.4.0 — release notes

**The release that makes the Tools wheel a set of real instruments.** 26 floating tool windows, one
motion language, and password protection for your files — all local, all purple, English and Arabic.

## What changed since v2.3.0

Six pull requests were merged into `main` after the v2.3.0 tag. Each row names the merge commit and the
evidence that the change is in this build.

| PR | Merge | What it changed | Evidence |
|---|---|---|---|
| #39 | `3ba599cc` | The bilingual website (`docs/site`) rebuilt for 2.4.0 — English and Arabic, release markers stamped at deploy, real UI captures, social cards/robots/sitemap, the redrawn brand icon with an icon CI gate, and site CI gates (page weight, links, Lighthouse) | 72 commits; `docs/site/index.html`, `scripts/site/*`, `.github/workflows/site-ci.yml`, `.github/workflows/icon-ci.yml` |
| #55 | `8a5d7804` | The six separate bug hunts consolidated into `docs/audit/BUG-HUNT.md`, with RC-01 and UP-01 acceptance tests specified for the fix lanes | `docs/audit/BUG-HUNT.md`, `docs/audit/BUG-HUNT-RACES.md` |
| #45 | `e0c8ca05` | Audit fixes group 1 — F-02 the PDF tools open their own window again, F-03 "repeat last action" remembers interactive tools, F-04 Video Split/Join honour the drop folder, F-07 a zero-page PDF fails with a message instead of an empty folder | `PdfToolOperations.cs:35,60`; `WheelController.cs:256-268`; `VideoSplitTool.cs:137`, `VideoJoinTool.cs:114`; `PdfBackend.cs:27,65` (`error.pdf.noPages`) |
| #57 | `8e903be6` | RC-01, first part: a tool window can no longer close while its Apply is still writing, so the write always has an owner until it ends | `ToolWindow.cs:265-272`; `ToolCloseDuringApplyTests` |
| #48 | `2dcb59c4` | Lock: a relock is refused when the unlocked file or folder changed since the last save (the good container is kept), and a file is deleted before it is zeroed so a failed delete can never re-encrypt zeros over the container | `LockService.cs:282,288,897-898` |
| #58 | `7c6587e6` | UP-01: an unreadable or corrupt `settings.json` is copied to `settings.bad-<timestamp>.json` before the app falls back to defaults, and the user is told once — settings are never silently destroyed | `AppSettings.cs:63-72,160`; `edfacaa0` (tests) |

## Highlights

- **26 tool windows.** Choosing a tool on the Tools wheel (Ctrl) now opens a card over the file instead
  of starting a background job. You see the photo, the waveform, the timeline or the pages, you set what
  you want, and only then does DropWheel write a new file. 25 of the 26 windows are new in this release;
  Crop arrived in 2.3.0.
- **Each window fits its media.** It opens at its default size, reads the file in the background and
  animates to the media's shape — tall for a portrait photo, wide for a clip — clamped to the screen
  under the cursor. Small media is never blown up past 200 %.
- **One motion language.** A single token set drives the wheel, the tool windows, Settings and dialogs;
  only transforms and opacity animate, nothing polls, and **Settings → General → Animations** (or
  Windows' own animation setting) turns all of it off.
- **Password protection.** AES-256 for a ZIP or a PDF copy, and **Remove password** to get back in.
  **Lock** (Beta) goes further: a file becomes one `.dwenc` container and a folder a `.dwdir`, in place,
  with a Rescuer password and a one-time recovery key.
- **Light and fast, and measured.** The motion gate drives the wheel plus the **11 image-family tool
  windows** (the 15 media-family tools are skipped and named in `motion/report.md`), en-LTR, runner
  theme; all 26 windows share the animated chrome code path the gate covers. In run
  `39376236166` the UI frame p95 stayed at or under **0.04 ms**, idle CPU at **0.00 %** and retention at
  **0.0 MB**, against gates of 16.7 ms, 5 % and 12 MB (`docs/PERF.md`).

## New tools

| Media | What you can do now |
|---|---|
| Images | **Crop Image**, **Annotate Image** (arrows, shapes, text, callouts, magnifier, pen), **Edit Photos** (16 sliders, byte-identical no-op save), **Add BG** (padding, corners, shadow, backdrop), **Collage** (2+ photos, three presets), **Redact image** (areas, solid/blur/pixelate, OCR text detection) |
| Video | **Trim Video**, **Crop Video**, **Change Speed** (pitch kept), **Split Video** (parts folder), **Join Videos**, **Save frames** (PNG stills), **Redact video** (areas with their own time range) |
| Audio | **Trim Audio**, **Normalize Audio** (EBU R128), **Audio Channels** (mono/stereo, volumes, solo), **Bleep Audio** (tone sections), **Audio Visualizer** (audio → MP4) |
| Documents | **Merge to PDF** (PDFs and images, in your order), **Organize Pages** (rotate, duplicate, remove, drag) |
| Any file | **Compress File** (image, audio, video, PDF; preset, target size or percentage), **Edit Metadata** (searchable tags, chapters, Remove All) |
| Protection | **Password protection**, **Remove password**, **Lock** (Beta) |
| Reading | **QR Code** — decodes offline and only ever opens `http`/`https` |

The full reference — id, English and Arabic title, inputs, output names and controls — is in
[docs/TOOLS.md](TOOLS.md). The 26 tool ids registered in `ToolRegistry` are the single list every slice
opens from.

## Also in this release

- The Tools wheel keeps its quick one-shot slices (compress, shrink, rotate, mirror, grayscale, strip
  metadata, merge into PDF, PDF split/rotate/optimize, audio join, video 2×, trim, **extract text
  (OCR)**, **archive** to ZIP / TAR.GZ). Those still run as background jobs with a progress card.
- Results are never overwritten (a second copy becomes `<name> (2).<ext>`), and they land in the folder
  you dropped on, or next to the source.
- Tool cards state their own verb on the primary button ("Create Collage", "Merge PDFs", "Save Edited
  Copy", "Encrypt"…); **Reset** restores the defaults, **Esc** closes without writing, **Enter** applies.
- Settings keeps its six tabs and gains **Animations** under General. An in-place upgrade keeps your
  settings and your chosen install mode.
- Both string tables carry **1,111 keys each** with identical key sets and placeholders, enforced by
  `LocalizationTests` in CI; the self-test renders the shipped build in English and Arabic, light and
  dark, and the independent `ui-coverage` gate rasterises every window in both languages.

## Install or upgrade

Download from the [releases page](https://github.com/Faisaldhd/dropwheel/releases) or the
[download page](https://faisaldhd.github.io/dropwheel/): `DropWheel-2.4.0-portable-win-x64.zip`
(self-contained, no admin, no registry) or `DropWheel-2.4.0-Setup.exe` (per-user without administrator
rights, or all users). `SHA256SUMS.txt` and an ECDSA P-256 `.sig` accompany each binary. The setup is
self-contained, so no .NET install is needed.

The binaries are Authenticode-unsigned for now, so Windows SmartScreen may ask once (**More info** →
**Run anyway**). Auto-updates are separately signed and verified against the public key built into the
app before anything runs.

The website publishes only after a verified release: the Pages workflow is manual, resolves the newest
stable release, refuses to stamp unless the setup is listed in that release's `SHA256SUMS.txt`, stamps
the version/size/SHA-256 into every marker, and uploads the stamped copy — never the repository file.
Until then the download page keeps pointing at the last signed release.

## Known issues

Every gap names its owner, so a limit is never mistaken for something forgotten.

- **RC-01 — partial fix shipped; the remaining partial-file-on-cancel risk is deferred to 2.4.1.**
  Shipped in 2.4.0: closing a tool window while Apply is writing is now blocked until the write
  finishes — `ToolApplyState` keeps the close deferred until the write ends (`ToolWindow.cs`, test
  `ToolCloseDuringApplyTests`, commit `27350fe`, PR #57). Still open: a partial output file can still
  remain when an operation is **cancelled or fails mid-write**, because the four writers that write the
  final name directly — Crop, Edit Photos, Annotate, Redact (e.g. `CropTool.cs:461-462`) — do not go
  through `OutputPaths.ProduceAsync`, which is what deletes the temporary file; the residual row is
  still open in `docs/audit/BUG-HUNT-RACES.md`. **Owner: release gate for 2.4.1.**
- **Cold start is over its budget with all 26 tools registered.** The measured start was **3,118 ms**
  against the **1,575 ms** gate (1,500 ms baseline + the 5 % rule, `LaunchBudget.ColdStartMs`). The gate
  itself is correct and unchanged; the measured number is tracked in [BACKLOG.md](BACKLOG.md) for
  **2.4.1**.
- **Lock is Beta.** The container, Rescuer envelope, auto-relock and crash recovery are covered by
  tests, but the format has not been signed off. **Owner: owner sign-off, a release gate.**
- **Redact image has no face detection** in this build; manual areas and Windows OCR text detection do
  ship. **Owner: none assigned** (`docs/BACKLOG.md`).
- **QR Code is read-only** — it decodes a code, it does not create one. **Owner: none**, not planned for
  2.4.x.
- Video and audio tools need **FFmpeg**; without it their slices show the helper-tool prompt (by
  design — the tools stay out of the package).
- **The `ui-coverage` gate asserts 100 % and 150 % DPI; the 200 % case is still missing** and is tracked
  in [BACKLOG.md](BACKLOG.md). What is enforced today: every window rasterised in English and Arabic
  with localized-title, raw-key, bidi and RTL-root assertions, plus string-table parity.
- Deferred, non-blocking work is tracked in [BACKLOG.md](BACKLOG.md).

## Credits

DropWheel is based on [FileAction](https://github.com/Metamorfolog/file_action) by **Metamorfolog**,
released under the MIT licence. The MIT text is kept verbatim with `Copyright (c) 2026 Metamorfolog`
plus `Copyright (c) 2026 DropWheel` in [LICENSE](LICENSE); [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)
lists the other libraries (Magick.NET, SkiaSharp, PDFium, FFmpeg, Pandoc and the .NET runtime) with their
own licences. DropWheel is an independent continuation and is not affiliated with the upstream project
or with Tangerine.
