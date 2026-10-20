# DropWheel motion clips

Short, looping motion clips for the website and release notes, rendered from the **real app UI**:

| file | shows |
| --- | --- |
| `wheel-dark.webp`, `wheel-light.webp` | the wheel opening from the cursor, its travelling highlight moving across three slices, then closing (150 ms open, 100 ms highlight, 100 ms close) |
| `tool-tools-crop-dark.webp`, `tool-tools-crop-light.webp` | the Crop tool window opening (180 ms fade + 8 px rise + 0.97 -> 1), holding, then closing (120 ms) |
| `*-poster.png` | the fully-open frame of each clip, for `<video poster>`/social previews |

Each clip is up to 480 px wide, 12.5 fps, 10-18 sampled timeline frames looping; the encoder merges
identical consecutive poses and keeps the timing (so the file may hold fewer frames than the timeline).
The self-test prints the combined size. Every registered tool is emitted automatically, so new tools
appear here after a re-render.

## How they are made

`DropWheel.exe --self-test <dir>` calls `MotionClips`, which poses the live visual tree at sampled
animation times with `WheelWindow.Pose` / `ToolWindow.Pose` (the same easing curves and durations as
the storyboards), renders each frame with `RenderTargetBitmap`, and encodes the sequence as animated
WebP with Magick.NET. They are **not mock-ups and not a screen recording**: CI has no GPU-backed
interactive desktop, so deterministic offscreen frames are the honest way to capture the shipped UI.

Re-render after a motion change:

```bat
scripts\screenshots.cmd
```

or download the `screenshots` / self-contained self-test artifact from the CI run (the files are
written under `docs/screenshots/motion/` or `release/self-test/motion/` respectively) and copy them
here. Do not hand-edit or draw over a clip.
