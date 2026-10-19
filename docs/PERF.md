# DropWheel performance budgets (2.4.0)

2.4.0 adds tools. Each tool is a window with its own preview pipeline, so three things are budgeted
explicitly and enforced in CI on every pull request:

1. **Package size** — the installer and the portable zip may not grow by **6 MiB or more** over v2.3.0.
2. **Dependencies and payload** — no new NuGet package except ZXing.Net (QR), and no OCR/ML model
   (`*.onnx`, `*.tflite`, `*.model`, `*.pb`, `*.bin` over 1 MiB, `tessdata`) or on-demand tool binary
   (ffmpeg/pandoc) may ship.
3. **Runtime** — tool registration stays lazy, cold start may not grow by more than **5 %**, idle memory
   by more than **+15 MB**, and each tool window must return to its baseline working set after 20
   open/close cycles with no leaked ffmpeg process and no leftover temp file.

All numbers below are measured by `.github/workflows/ci.yml` on `windows-latest`; a self-test exit code
of 1 fails the build.

## v2.3.0 baselines

| metric | v2.3.0 baseline | source |
| --- | ---: | --- |
| installer (`DropWheel-Setup.exe`) | 62,967,948 bytes (60.05 MiB) | v2.3.0 GitHub release asset |
| portable zip (`DropWheel-2.3.0-portable-win-x64.zip`) | 89,266,161 bytes (85.13 MiB) | v2.3.0 GitHub release asset |
| cold start (process start → laid-out wheel) | **TBD** ms | v2.3.0 code (main `4a4a39f`) with this self-test, CI run **TBD** |
| idle working set after launch | **TBD** MB | same run |

The package baselines are hard-coded in the `env:` block at the top of `.github/workflows/ci.yml`
(`BASELINE_SETUP_BYTES`, `BASELINE_PORTABLE_BYTES`, `SIZE_BUDGET_BYTES = 6 MiB`). The runtime baselines
are the `LaunchBudget` constants in `src/FileAction.App/SelfTest.cs`; each carries a comment naming the
v2.3.0 measurement. **Never raise a baseline to make a regression pass** — re-baseline only when a new
release is measured and this document is updated in the same commit.

## Measured on this branch (2.4.0)

CI run **TBD** (`feat/2.4.0-perf`):

| metric | v2.3.0 | current | delta | budget |
| --- | ---: | ---: | ---: | ---: |
| installer | 60.05 MiB | **TBD** | **TBD** | +6 MiB |
| portable zip | 85.13 MiB | **TBD** | **TBD** | +6 MiB |
| cold start | **TBD** ms | **TBD** ms | **TBD** | +5 % |
| idle memory | **TBD** MB | **TBD** MB | **TBD** | +15 MB |

### Per-tool open/close (20 cycles after one warm-up)

| tool | opens | baseline MB | final MB | delta | result |
| --- | ---: | ---: | ---: | ---: | --- |
| `tools.crop` | 20 | **TBD** | **TBD** | **TBD** | **TBD** |

The tool loop is data-driven from `ToolRegistry.All`: once the 2.4.0 tool branches merge, every new tool
is exercised automatically. A tool that grows the working set by more than 8 MB, leaves `ffmpeg.exe`
running, or leaves a file in the redirected `TEMP`/`TMP` folder fails the self-test. Tools that need a
format the loop does not create yet fail with an explicit message asking for the format to be added to
`SelfTest.CreateToolSamples` — coverage cannot silently shrink.

## How each gate is enforced

### 1. Package size (`.github/workflows/ci.yml`)

- **`Size report`** (package job, main/tags): runs after the installer and portable zip are built,
  prints the v2.3.0 baseline, the current size and the delta for both packages to the job summary, and
  throws when a delta is `>= 6 MiB`.
- **`Size budget (PR check)`** (build-test job, `pull_request` only): builds the same installer and
  portable zip from the PR's self-contained publish, prints the same table to the pull-request job
  summary, and fails the PR check on the same `>= 6 MiB` rule. `Inno Setup` is only installed when
  `ISCC.exe` is absent, so a Chocolatey outage cannot fake a failure.

### 2. Dependencies and payload (`build-test` job)

- **`Dependency allowlist (ship set + ZXing.Net only)`** parses
  `src/FileAction.Core/FileAction.Core.csproj` and `src/FileAction.App/FileAction.App.csproj` and fails
  on any `PackageReference` outside the reviewed set:
  `ClosedXML`, `ExcelDataReader`, `Magick.NET-Q8-AnyCPU`, `PdfPig`, `PDFsharp`, `PDFtoImage`,
  `SharpCompress`, `System.Security.Cryptography.Pkcs`, plus `ZXing.Net` (QR). The referenced table is
  written to the job summary.
- **`No OCR/ML model payload`** (build-test) and **`Shipped payload guard`** (package job) scan the
  exact folder ISCC packages and the portable zip copies (`release/selfcontained`) for `*.onnx`,
  `*.tflite`, `*.model`, `*.pb`, any `*.bin` over 1 MB, a `tessdata` directory, and `ffmpeg.exe` /
  `ffprobe.exe` / `pandoc.exe`. Optional tools must stay on-demand downloads into
  `%LOCALAPPDATA%\DropWheel\tools`.

### 3. Lazy loading

- **`Lazy tool registration guard`** (CI, source): every `[ModuleInitializer]` body must be exactly
  `ToolRegistry.Register(new XTool())` or `ToolRegistry.Register(XTool.Instance)`. Any other statement
  (bitmap creation, Magick/PDFsharp work, `new ToolWindow`, constructing a view) fails the build.
- **Self-test registration proof**: `--self-test` records `ToolRegistry.All` at its earliest managed
  point, before any UI is built, and fails if a renderer assembly (`Magick.NET`, `PdfSharp`,
  `PDFtoImage`, `SkiaSharp`, `ClosedXML`, `ExcelDataReader`, `UglyToad.PdfPig`, `SharpCompress`,
  `SixLabors`, `DocumentFormat.OpenXml`, `ZXing`) is already loaded. A registrar that did heavy work
  would have loaded its renderer by then.
- **Self-test wheel proof**: the first wheel is laid out offscreen; the self-test fails if laying it
  out loads any renderer assembly beyond the ones the operation catalog already references (the
  catalog's image-edit operations legitimately mention `MagickImage`; that is v2.3.0 behaviour and is
  logged as a note, not a tool-view load).
- **Contract**: a tool class is a plain singleton (`static readonly XTool Instance = new()`), its
  `[ModuleInitializer]` only calls `ToolRegistry.Register`, and its heavy view (`CropView`, …) is
  constructed only inside `Create(items, targets)`. Registration must not construct a `ToolWindow`.

### 4. Self-test runtime budget (`src/FileAction.App/SelfTest.cs`)

`DropWheel.exe --self-test` now measures, on the shipped publish:

1. **Cold start** — `Process.StartTime` to the first laid-out `WheelWindow` (the wheel is measured
   before any PNG preview is rendered). Fails when `actual > baseline * 1.05`.
2. **Idle working set** — after the dispatcher reaches `ApplicationIdle` and a forced full GC, right
   after launch. Fails when `actual > baseline + 15 MB`.
3. **Per-tool open/close** — one warm-up cycle (which pays the one-time renderer/native load and
   renders the tool's preview), then 20 create → prepare → show offscreen → close cycles. Working set
   is sampled after a forced GC on every cycle; the final reading must stay within **+8 MB** of the
   post-warm-up baseline. `TEMP`/`TMP` are redirected to a private folder for the loop, and the folder
   must be empty afterwards. New `ffmpeg` PIDs are a failure.
4. **Lazy registration** — the checks described above.

All actuals are printed as `perf …` lines in `self-test.txt`, the build log, and (build-test) the
pull-request job summary.

## Known limitations / notes for the tool PRs

- **Only the tools merged on main are covered today.** At the time of this branch main contains
  `tools.crop` only; the loop is driven by `ToolRegistry.All`, so each tool branch that merges is
  measured automatically and must add its format to `CreateToolSamples` if it is not listed yet.
- **Tool windows have no generic `Closed` hook in the framework.** `ToolWindow`/`IToolWindowSpec` are
  shared files owned by the tool-framework PR, so this branch does not change them. The current tool
  (`tools.crop`) holds no timer, stream, `MediaElement`, ffmpeg process or temp file; its only native
  resource is the Magick image, which is disposed in `Load`, and the decoded `BitmapSource` is frozen
  and collected with the view. Tools that own a `MediaElement`, timer or ffmpeg process must release it
  in their own view/file on close; the per-tool loop fails a tool that keeps memory, a process or a
  temp file alive.
- **Cold-start noise**: the baseline is a full process start on a fresh CI runner. The gate allows 5 %
  growth; the recorded value is rounded up to the next millisecond and only re-baselined with a new
  v2.3.0-equivalent measurement, never with a slower build.
