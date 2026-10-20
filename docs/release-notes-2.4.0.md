# DropWheel 2.4.0 — release notes

**The release that makes the Tools wheel a set of real instruments.** 26 floating tool windows, one
motion language, and password protection for your files — all local, all purple, English and Arabic.

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
- **Light and fast, and measured.** The `--self-test` motion gate fails CI above 16.7 ms UI frame p95,
  5 % idle CPU or 12 MB retained; the recorded run on the merge head is 0.02–0.07 ms p95, 0.39 % idle
  and 0.0 MB retained ([PERF.md](PERF.md), CI run `39351795353`).

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
[docs/TOOLS.md](TOOLS.md).

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

## Install or upgrade

Download from the [releases page](https://github.com/Faisaldhd/dropwheel/releases) or the
[download page](https://faisaldhd.github.io/dropwheel/): `DropWheel-2.4.0-portable-win-x64.zip`
(self-contained, no admin, no registry) or `DropWheel-2.4.0-Setup.exe` (per-user without administrator
rights, or all users). `SHA256SUMS.txt` and an ECDSA P-256 `.sig` accompany each binary. The setup is
self-contained, so no .NET install is needed.

The binaries are Authenticode-unsigned for now, so Windows SmartScreen may ask once (**More info** →
**Run anyway**). Auto-updates are separately signed and verified against the public key built into the
app before anything runs.

## Known limits

Every gap names its owner, so a limit is never mistaken for something forgotten.

- **Lock is Beta.** The container, Rescuer envelope, auto-relock and crash recovery are covered by tests,
  but the format has not been signed off. **Owner: owner sign-off, a release gate.**
- **Redact image has no face detection** in this build; manual areas and Windows OCR text detection do
  ship. **Owner: none assigned** (`docs/BACKLOG.md`).
- **QR Code is read-only** — it decodes a code, it does not create one. **Owner: none**, not planned for
  2.4.x.
- Video and audio tools need **FFmpeg**; without it their slices show the helper-tool prompt (by design —
  the tools stay out of the package).
- The **per-window bilingual rendering gate** (every tool window in English and Arabic at 100/150/200 %
  DPI) is **`audit/selftest`, PR #37, in flight**. What is enforced today: both string tables are
  complete and identical in structure (1,094 keys each), with parity enforced by `LocalizationTests` in
  CI, and the self-test renders the shipped build in English and Arabic, light and dark.
- **Light-mode contrast and the video timer** are **`audit/amotion` / `audit/adesign`, in flight**.
- Deferred, non-blocking work is tracked in [BACKLOG.md](BACKLOG.md).
