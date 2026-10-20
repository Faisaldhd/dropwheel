# Website captures — import procedure and capture request

The download site (`docs/site`) shows the product with **real renders of the shipped UI**. This page
covers how a capture reaches `docs/site/assets`, the file-naming contract, what is still missing, and
what to ask Team A for.

**These are not mock-ups and not a screen recording.** Every image and clip is produced by the app's
own `DropWheel.exe --self-test <dir>`, which poses the live WPF visual tree at sampled animation times
and renders each frame with `RenderTargetBitmap` offscreen (see
`src/FileAction.App/UI/Motion/MotionClips.cs`). CI has no GPU-backed interactive desktop, so
deterministic offscreen frames are the honest way to capture the shipped UI. Nobody draws over,
retouches or re-creates a capture by hand.

> Captures are produced by **Team A/Team B's existing CI jobs**. This lane never builds or runs the
> app, locally or in Actions: no `dotnet build`/`publish`, no installer, no `DropWheel.exe` on a
> workstation. The import script only copies files that a run already produced.

## 1. Where the captures come from

The app writes captures in the same layout for every caller:

```
<motion-dir>/
  wheel-dark.webp                 wheel-light.webp
  wheel-dark-poster.png           wheel-light-poster.png
  tool-<id>-dark.webp             tool-<id>-light.webp
  tool-<id>-dark-poster.png       tool-<id>-light-poster.png
self-test.txt                     (render log; optional but expected)
```

`<id>` is the registry id without the `tools.` prefix and with `.` mapped to `-` (today every id is a
single word, so `tools.audiotrim` → `audiotrim`). Posters arrive as **PNG** from `MotionClips`; WebP
posters are accepted too if a producer ever emits them.

Downloadable CI artifacts (`.github/workflows/ci.yml`):

| artifact | produced by | contains | use for the site |
| --- | --- | --- | --- |
| `self-contained-self-test` | `build-test` → "Self-contained UI self-test (shipped publish)" (`release/self-test/**`) | motion clips, posters, `self-test.txt` | **preferred** — this is the only artifact with the `.webp` clips |
| `screenshots` | `screenshots` job (`docs/screenshots/**/*.png` + `self-test.txt`) | PNG posters and window shots, **no WebP clips** (the upload path filters `*.png`) | not importable on its own (no clips); useful to read which tools rendered |
| `shipped-self-test` | `package` job | `self-test.txt` only | diagnosis |

A run only has these artifacts when its `screenshots` job ran (manual dispatch or push to `main`);
the job needs a desktop session, so it is skipped for pull requests and agent branches.

## 2. Download an artifact

```powershell
gh run list --repo Faisaldhd/dropwheel --workflow ci.yml --branch main --limit 5
gh run download <run-id> --repo Faisaldhd/dropwheel -n self-contained-self-test -D _local\tmp\captures
```

`gh` extracts the artifact's common root, so the files land as `_local\tmp\captures\motion\...` plus
`self-test.txt`. The script also finds a `motion` directory at any depth (for example a raw
`release/self-test/motion` tree copied from a runner), so either shape works. `_local/tmp` is
scratch; never commit it.

## 3. Import

```powershell
pwsh -File scripts/site/import-captures.ps1 -ArtifactDir _local\tmp\captures
```

Options:

- `-ArtifactDir <dir>` (required) — downloaded artifact root.
- `-AssetsRoot <dir>` — destination, default `<repo>/docs/site/assets`. Point it at a scratch copy
  for a dry run without touching the tracked site assets.

What it does:

1. Validates every candidate: non-zero length, `RIFF….WEBP` magic for clips, PNG signature for
   `.png` posters. Any bad file aborts the whole import before anything is copied.
2. Refuses to run when the artifact has no `wheel-<theme>.webp`, so a truncated download cannot
   half-update the site.
3. Copies with the contract below, then re-hashes each destination to verify the copy.
4. Prints one row per file (`added` / `replaced` / `unchanged`) and a summary count.
5. Writes `docs/site/assets/tools/manifest.json`.
6. Warns about PNG-only posters (nothing is converted), tools missing a dark or light clip, and any
   `skipped motion/...` lines found in `self-test.txt`.

It **never deletes** a file and **never converts** a format. Re-running the same artifact is a no-op:
the second run reports `Added 0, replaced 0, unchanged N`.

## 4. Naming contract

| artifact file | site file |
| --- | --- |
| `motion/wheel-<theme>.webp` | `motion/wheel-<theme>.webp` |
| `motion/wheel-<theme>-poster.(png\|webp)` | `motion/wheel-<theme>-poster.<ext>` (extension preserved) |
| `motion/tool-<id>-<theme>.webp` | `tools/<id>-<theme>-clip.webp` |
| `motion/tool-<id>-<theme>-poster.(png\|webp)` | `tools/<id>-<theme>-poster.<ext>` |

`<theme>` is `dark` or `light`. The clip name deliberately matches what `docs/site/index.html` already
loads (`data-clip-dark="assets/tools/crop-dark-clip.webp"`). Posters keep the producer's extension;
the site's existing poster slots (`assets/tools/<id>-<theme>.webp`, `assets/motion/wheel-<theme>-poster.webp`)
are **not** overwritten with a PNG. A new card should use the `-poster.png` path until a WebP poster
is published, or the site lane can convert once and commit the result (conversion is a site-asset
decision, not something this script is allowed to do silently).

## 5. Manifest

`docs/site/assets/tools/manifest.json` lists, per id, the files that exist relative to the assets root:

```json
{
  "contract": "site-tool-captures/1",
  "wheel": { "dark": ["motion/wheel-dark.webp", "motion/wheel-dark-poster.png"], "light": [] },
  "tools": {
    "crop": {
      "dark": ["tools/crop-dark-clip.webp", "tools/crop-dark-poster.png"],
      "light": ["tools/crop-light-clip.webp", "tools/crop-light-poster.png"]
    }
  }
}
```

Keys are sorted and there is no timestamp, so an unchanged import rewrites byte-identical JSON.
Consumers must treat a missing array entry as "not captured yet", not as an error.

## 6. Current inventory

26 tool windows are registered (`src/FileAction.App/UI/Tools/**`, `ToolRegistry.Register`).
Five have committed motion captures under `docs/screenshots/motion/`:

| id | registry id | poster (dark = light) |
| --- | --- | --- |
| `addbackground` | `tools.addbackground` | 423 × 648 |
| `annotate` | `tools.annotate` | 480 × 571 |
| `crop` | `tools.crop` | 480 × 508 |
| `editphotos` | `tools.editphotos` | 420 × 648 |
| `redact` | `tools.redact` | 480 × 534 |

Wheel: 442 × 442 poster, 442 px wide clip. Clips are up to 480 px wide, 12.5 fps, 10–18 sampled
frames, looping; the encoder merges identical consecutive poses.

**21 tool windows have no capture** (26 − 5):

| artifact id | registry id | source | sample the window needs | requested |
| --- | --- | --- | --- | --- |
| `audiobleep` | `tools.audiobleep` | `Audio/AudioBleepTool.cs` | short audio file | dark + light clip, poster |
| `audiochannels` | `tools.audiochannels` | `Audio/AudioChannelsTool.cs` | stereo audio file | dark + light clip, poster |
| `audionormalize` | `tools.audionormalize` | `Audio/AudioNormalizeTool.cs` | quiet audio file | dark + light clip, poster |
| `audiotrim` | `tools.audiotrim` | `Audio/AudioTrimTool.cs` | audio file | dark + light clip, poster |
| `audiovisualizer` | `tools.audiovisualizer` | `Audio/AudioVisualizerTool.cs` | audio file | dark + light clip, poster |
| `collage` | `tools.collage` | `Collage/CollageTool.cs` | 2+ images | dark + light clip, poster |
| `compress` | `tools.compress` | `Compress/CompressTool.cs` | any compressible file | dark + light clip, poster |
| `lock` | `tools.lock` | `Lock/LockTool.cs` | a file to wrap | dark + light clip, poster |
| `metadata` | `tools.metadata` | `Meta/MetadataTool.cs` | any media file | dark + light clip, poster |
| `pdfmerge` | `tools.pdfmerge` | `Pdf/PdfMergeTool.cs` | 2 PDFs | dark + light clip, poster |
| `pdforganize` | `tools.pdforganize` | `Pdf/PdfOrganizeTool.cs` | 2+ page PDF | dark + light clip, poster |
| `protect` | `tools.protect` | `Password/ProtectTool.cs` | any file | dark + light clip, poster |
| `qr` | `tools.qr` | `Qr/QrTool.cs` | one image or text | dark + light clip, poster |
| `unlock` | `tools.unlock` | `Password/UnlockTool.cs` | any file | dark + light clip, poster |
| `videocrop` | `tools.videocrop` | `Video/VideoCropTool.cs` | short video | dark + light clip, poster |
| `videoframes` | `tools.videoframes` | `Video/VideoFramesTool.cs` | short video | dark + light clip, poster |
| `videojoin` | `tools.videojoin` | `Video/VideoJoinTool.cs` | 2+ short videos | dark + light clip, poster |
| `videoredact` | `tools.videoredact` | `Video/VideoRedactTool.cs` | short video | dark + light clip, poster |
| `videospeed` | `tools.videospeed` | `Video/VideoSpeedTool.cs` | short video | dark + light clip, poster |
| `videosplit` | `tools.videosplit` | `Video/VideoSplitTool.cs` | short video | dark + light clip, poster |
| `videotrim` | `tools.videotrim` | `Video/VideoTrimTool.cs` | short video | dark + light clip, poster |

Requested pixel sizes, identical rules for all 21: **width 480 px** (the encoder clamps wider windows),
**height follows the window aspect** — do not pad or crop; the five captured tools came out
508–648 px tall at 480 wide, and 420–480 wide when the window itself is narrower. Posters are the
fully open frame at the same size as the clip frames. Dark and light must both be present; a one-theme
capture is reported as incomplete by the import script.

## 7. Why the 21 are missing, and what Team A needs to change

`MotionClips.Run` renders one sample fixture — a 640 × 420 PNG (`motion-sample.png`) — and skips any
tool whose `Applies([item])` rejects it, logging `skipped motion/<id> (no sample)` in `self-test.txt`.
A single PNG cannot satisfy the audio, video, PDF, archive and password tools, so the motion pass
cannot emit their clips. That is app behaviour in `src/FileAction.App/UI/Motion/MotionClips.cs`, not
an import problem, and it is Team A's call to change it.

To produce the missing 21, Team A needs to give `MotionClips` one sample per media class (and two
items for the multi-file tools) and let the existing loop render every registered spec. Two details
worth checking in the run log first:

- `collage`, `qr` and `compress` already accept a PNG, yet no clip is committed for them. Their
  absence is likely an older render or a `PrepareAsync` failure caught by the per-tool `catch`; the
  log line names the exception.
- Any tool that throws while preparing is logged and skipped, so a run can succeed overall with
  fewer than 26 clips. Ask for `self-test.txt` together with the artifact, and read the
  `skipped motion/` lines rather than trusting the artifact's file count.

Until those clips exist, the site must not invent them: keep the 5 real captures and describe the
other tools in text. When an artifact lands, run section 3 and commit only the resulting files.

## 8. Rules

- Do not run, build, publish or install the app to make a capture; do not run the self-test on a
  workstation. Import only.
- Do not hand-edit, retouch, upscale or re-draw a capture, and do not substitute a screenshot of a
  mock-up.
- Do not delete an existing asset during an import; the script adds and replaces only.
- `docs/site/**` belongs to the site lane: this procedure is run by whoever owns that change, on a
  branch that is allowed to touch it.
