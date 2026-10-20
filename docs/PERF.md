# DropWheel performance budgets (2.4.0)

2.4.0 adds tool windows and motion. Three things are budgeted explicitly and enforced in CI:

1. **Package size** - the installer and the portable zip may not grow by **6 MiB or more** over v2.3.0.
2. **Runtime** - cold start may not grow by more than **5 %** and idle memory by more than **+15 MB**.
3. **Motion** - one animated frame must fit 60 fps (UI-thread p95 < **16.7 ms**), idle CPU after every
   surface closes must stay under **5 %**, and the motion surfaces may not retain more than **12 MB**
   after close (inside the 15 MB idle rule).

## Motion gate (2.4.0)

`DropWheel.exe --self-test` runs `MotionSelfTest` on every CI run. It drives the wheel and every
registered tool window through a deterministic pose sequence (the same easing curves and durations the
live storyboards use), once with motion ON and once with motion OFF, and records:

- **UI frame p95** - applying one animation pose plus the WPF layout pass, per 60 fps frame;
- **raster proxy p95** - the same frame rendered through `RenderTargetBitmap` (software rasterisation,
  not the GPU compositor); reported for completeness, not used as a gate;
- **CPU during** the measurement, and **idle CPU** for one second after everything is closed (a
  polling timer would show up here);
- **working set retained** after closing all measured windows.

Gate values: UI frame p95 <= 16.7 ms, idle CPU <= 5 %, retention <= 12 MB. Any breach fails the
self-test and therefore the CI job. The per-run table is written to `<self-test>/motion/report.md` and
uploaded with the self-test artifacts.

### Why the measurement is offscreen

CI's Windows runners have a desktop session but no GPU-backed interactive composition, so a
`CompositionTarget.Rendering` frame delta would mostly measure the 16.7 ms compositor tick rather than
the app's work. The UI-thread pose + layout cost is the part the app controls and the part that
regresses; the raster proxy bounds the drawing cost. The 60 fps target on a low-end laptop is a design
budget, not a claim that a datacenter runner is a laptop: render tier and processor count are recorded
with the numbers.

### Measured on this branch

The numbers below are produced by CI (`Build + test` -> self-contained `--self-test`). Re-run the
workflow after a motion change; do not hand-edit a passing number.

| surface | UI frame p95 ON (ms) | UI frame p95 OFF (ms) | raster proxy p95 ON (ms) | CPU ON (%) | p95 budget |
| --- | ---: | ---: | ---: | ---: | --- |
| wheel (424x424) | see `motion/report.md` artifact | | | | 16.7 ms |
| tool `tools.crop` | see `motion/report.md` artifact | | | | 16.7 ms |

Idle CPU after close and retained working set are printed in the same artifact and in `self-test.txt`.

## Package and runtime gates

The v2.3.0 baselines, the installer/portable size rules and the cold-start/idle-memory measurement are
owned by the performance workstream (`feat/2.4.0-perf`) and are enforced in `.github/workflows/ci.yml`.
Motion adds no dependency, no startup work beyond reading two booleans (`Animations` and
`SystemParameters.ClientAreaAnimation`), and no background timer, so it does not move those baselines.

## How each gate is enforced

- **Self-test budgets** - `MotionSelfTest` throws (exit code 1) on a frame, CPU or retention breach;
  `.github/workflows/ci.yml` already fails the job on a non-zero self-test exit code.
- **Startup / idle / size** - the perf workstream's gates in CI; re-baseline only with a measured
  release, never to make a regression pass.
- **Website assets** - `MotionClips` writes 480 px animated WebP clips and poster frames under
  `<self-test>/motion/`; the committed copies live in `docs/screenshots/motion/`. The self-test prints
  the combined size so the assets stay small.
