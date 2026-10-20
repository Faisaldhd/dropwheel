# Bug hunt - consolidated (2.4.0)

Compiler: docs lane (`audit/adocs`). Six time-boxed, read-only hunts, each on its own branch and each
written against `origin/main` `8b28758` (or the branch head named in its own header). Nothing here was
built or executed locally. **Rows are compiled, not edited:** each area section below is the hunter's own
file verbatim under a `##` heading, IDs preserved. A line marked `[compiler]` is the only content added
by this consolidation.

**P0 and P1 rows do not go to `docs/BACKLOG.md`; they go to Team A. Only P2 work is deferred.**

## Totals

| Area | File | Rows | P0 | P1 | P2 |
| --- | --- | ---: | ---: | ---: | ---: |
| State - leftover state and the jobs window | `BH-STATE.md` ($(System.Collections.Hashtable.Branch)) | 9 | 0 | 2 | 7 |
| UI - freeze, jank, memory | `BH-UI.md` ($(System.Collections.Hashtable.Branch)) | 8 | 0 | 2 | 6 |
| Crash - crashes and unhandled exceptions | `BH-CRASH.md` ($(System.Collections.Hashtable.Branch)) | 6 | 1 | 1 | 4 |
| Races - concurrency and cancellation | `BH-RACES.md` ($(System.Collections.Hashtable.Branch)) | 12 | 0 | 3 | 9 |
| Visual - rendering, RTL, DPI | `BH-VISUAL.md` ($(System.Collections.Hashtable.Branch)) | 11 | 0 | 2 | 8 (+1 unlabelled) |
| Upgrade - settings, uninstall, update path | `BH-UPGRADE.md` ($(System.Collections.Hashtable.Branch)) | 11 | 0 | 2 | 9 |
| **Total** | | **57** | **1** | **12** | **43** |


[compiler] The 12 P1 rows include `ST-05`, which the state hunt labels "P1 (conditional)": its trigger needs a jobs window that is closed, and the UI has no normal path to close it.
[compiler] Counts are read from each file''s table rows. Two P2 rows in the crash section are recorded
as **verified negatives** (no defect found); they are kept so "checked and clean" is visible, not hidden.

## State - leftover state and the jobs window

[compiler] Source: `BH-STATE.md` on `audit/bughunt-state`, verbatim below.


Area assigned to the docs lane. Read-only hunt: every row below is from reading the shipped code on
`origin/main` (`8b28758`), not from running it. Rows are `ST-nn`; severity is the hunt scale (P0 crash or
data loss, P1 wrong or lost result, P2 leftover state/cosmetic). This file is compiled into
`docs/audit/BUG-HUNT.md`; rows are kept verbatim, and a compiler footnote is marked `[compiler]`.

Scope covered: temp/`.part` files after failure or cancel, jobs-window rows that never reach a terminal
state, a row orphaned by a closed window, duplicate rows for one output, the list after restart,
helper-tool download partials, and log growth.

| ID | Severity | Symptom | Evidence | file:line | Suggested fix | Owner |
|---|---|---|---|---|---|---|
| ST-01 | P2 | A cancelled or crashed job can leave a hidden `.<name>.<id>.part.<ext>` file in the **output folder**; nothing sweeps it later, so it shows up in Explorer as a dot-file next to the result | `ProduceAsync` writes a dotted temp name and deletes it in `finally`; the `finally` does not run when the process dies (crash, logoff, forced kill) | `src/FileAction.Core/OutputPaths.cs:27,37-40` | On startup (or when a job starts in a folder), delete `.*.part.*` files older than one hour in the resolved output folders; never touch anything else | unassigned (Core) |
| ST-02 | P2 | `ffmpeg.exe.part` / `pandoc.exe.part` can be left in the tools folder: `ExtractExe` writes `<exe>.part` and moves it, but no `finally` removes the partial when the extraction throws (disk full, archive entry corrupt) or the move fails | The `.part` name is created for streamed extraction and only `File.Move` consumes it; `InstallAsync`'s `finally` deletes the downloaded **archive**, not the extraction temp | `src/FileAction.App/Shell/ToolInstaller.cs:141,148-150,97-100` | Wrap `ExtractExe` in `try/finally` with `OutputPaths.TryDelete(target + ".part")`, then retry install cleanly | unassigned (Shell) |
| ST-03 | P2 | The log grows without a cap: every session appends, nothing rotates or truncates, and `Tail()` reads the whole file into memory for a crash report | `Write` uses `File.AppendAllText` unconditionally; `Tail` calls `File.ReadAllLines` on the full file | `src/FileAction.App/Diagnostics/AppLog.cs:67-74,82-99` | Rotate at a fixed size (for example 2 MB → `app.log.1`, keep one) and make `Tail` read the last N KB instead of the whole file | unassigned (diagnostics) |
| ST-04 | P1 | Any exception thrown in the **pre-flight** part of a job escapes: `Start` is `async void`, and everything before the `try` (parameter dialog, `new JobsWindow()`, `_window.Add(...)` with `items[0].Path`, `OutputTarget.Resolve`, notifications, `ShellFolders.NoteDecision`) runs without a catch, so it reaches the dispatcher as an unhandled exception instead of a red card | `public async void Start(...)`; `iconPath: items[0].Path` indexes the list without a guard; the first `try` only starts at the `await Task.Run` | `src/FileAction.App/Jobs/JobRunner.cs:27,64,117` | Guard `items.Count == 0` at the top, and wrap the whole body in `try/catch` that shows `Notifications.Show(..., error: true)` (never rethrow from `async void`) | unassigned (jobs) |
| ST-05 | P1 (conditional) | If the jobs window is ever closed while a job is running, the card is orphaned and the **next** job crashes: `JobRunner` keeps the closed instance (`_window ??=`) and `Add` calls `Show()` on a closed `Window` | `Closed` only removes the window from `JobsWindow.OpenWindows`; `JobRunner._window` is never reset; `JobsWindow.Add` calls `Show()` when `!IsVisible` | `src/FileAction.App/Jobs/JobsWindow.cs:46-59`; `src/FileAction.App/Jobs/JobRunner.cs:60` | On `Closed`, clear the runner's reference (or make `Add` recreate the window when `!IsLoaded`), and treat `Show()` failures as a card error | unassigned (jobs) |
| ST-06 | P2 | A cancelled job leaves no terminal row: `JobCard.Cancelled()` removes the card immediately, so the user never sees "Cancelled" and cannot tell a cancelled job from one that never started | `Cancelled()` calls `_remove(this)`; only `Complete`/`Fail` leave a visible terminal state | `src/FileAction.App/Jobs/JobsWindow.cs:284-288` | Keep the card with a muted "Cancelled" status and the existing 8 s auto-close, like `Complete` does | unassigned (jobs) |
| ST-07 | P2 | The jobs list is memory-only: after a restart there is no record of what was running, and a job interrupted by a crash/restart leaves no row and no error anywhere except the log | `JobRunner._window` is an in-memory `JobsWindow`; there is no job journal or history file; only `AppLog` records failures | `src/FileAction.App/Jobs/JobRunner.cs:15,60`; `src/FileAction.App/Diagnostics/AppLog.cs:61` | Either state "progress only, not history" in the docs (cheap) or persist one line per finished job (id, title, result, paths) that the window can read on start | unassigned (jobs) |
| ST-08 | P2 | Two identical operations started at the same time on the same folder can race `OutputPaths.Unique`: both compute `<name> (2)` and the second `File.Move` throws, so one job fails with a file-exists style error although the naming rule promises `(2)`, `(3)`… | `Unique` checks `File.Exists`, then `ProduceAsync` moves later; no retry around the move | `src/FileAction.Core/OutputPaths.cs:8-16,33-35` | Retry `Unique`+`Move` a few times on `IOException` when the target appeared, instead of surfacing the race | unassigned (Core) |
| ST-09 | P2 | A download that is cancelled after `File.Create(target)` but before completion leaves the archive in `%TEMP%` only if the process dies (the `finally` handles the normal path) — but the **install folder** can still hold a stale `ffmpeg.exe` from an earlier failed install, and nothing verifies it before use | `InstallAsync` deletes the archive in `finally`; nothing validates an existing `ffmpeg.exe` / `pandoc.exe` before `ToolLocator` reports the tool present | `src/FileAction.App/Shell/ToolInstaller.cs:76-100`; `src/FileAction.Core/ToolLocator.cs` | On a tool that fails to start, delete the exe and offer one re-download; record the verified SHA-256 next to the exe | unassigned (Shell) |

### Method and limits

- Read: `Jobs/JobRunner.cs`, `Jobs/JobsWindow.cs`, `Core/OutputPaths.cs`, `Shell/ToolInstaller.cs`,
  `Diagnostics/AppLog.cs`, plus grep over the tool runners for `TempDirectory`/`TryDelete` patterns.
- Not verified by execution (no build/run rule). "Conditional" marks a row whose crash needs an event
  the UI has no normal path to (a closed jobs window).
- Duplicate rows for one output: **none found** — one `Add` per started operation, and `Unique` prevents
  overwriting; the only related defect is the ST-08 race.
- Helper-tool partials: the downloaded **archive** is cleaned in `finally` (`ToolInstaller.cs:97-100`);
  only the extraction `.part` and a stale already-installed exe are open (ST-02, ST-09).

## UI - freeze, jank, memory

[compiler] Source: `BH-UI.md` on `audit/bughunt-ui`, verbatim below.


Lane: TEAM B motion/UI, time-boxed hunt (code reading + CI artifacts only — no build, no exe, no
`--self-test` locally). Evidence is `file:line` on `origin/main` `8b28758` plus the CI logs named per row.
One row per bug; severity P0/P1/P2.

**P0: none found.** Two P1s, both UI-thread blocking on the open/hot path: an uncached shell icon lookup
on the wheel (every open and every mode switch) and the input-hook startup wait with no timeout. Five P2s
cover per-open job-card icon/theme work, the per-open operation-list rebuild, the per-open theme refresh,
clipboard/OCR sync-over-async, and open/close working-set churn. The motion numbers are *not* a jank
source: `MotionSelfTest` measures UI-frame p95 **0.02-0.04 ms** against the 16.7 ms budget and idle CPU
**0.00 %** (run 39376236166, `motion/report.md`).

| ID | Sev | What happens | Repro / CI evidence | file:line | Suggested fix | Owner team |
| --- | --- | --- | --- | --- | --- | --- |
| UI-01 | P1 | Wheel open **and every mode switch** calls `FileIcons.Get`, which does a synchronous `SHGetFileInfo` + `CreateBitmapSourceFromHIcon` + `DestroyIcon` with **no cache**. A cold/slow shell (network path, icon-cache miss, Explorer busy) blocks the UI thread that is about to start the 150 ms open / 18 ms slice stagger — visible hitch exactly on the product's core gesture. The same call also runs per shelf item and per job card (UI-08). | Code: `WheelWindow.BuildHub` calls `Shell.FileIcons.Get`; `Build()` runs from the constructor and from every `CommitView` (mode switch). No cache field exists in `FileIcons`. CI smoke covers opening each wheel (`SelfTest.RenderAll` wheel-* rows) but does not measure wall time, so this is static evidence. | `src/FileAction.App/Wheel/WheelWindow.cs:929` (call), `:167-168` (ctor build), `:341-350` (mode switch `CommitView`); `src/FileAction.App/Shell/FileIcons.cs:17-34` | Cache the frozen `ImageSource` per (extension or path, large/small) in a bounded `ConcurrentDictionary`; the hub only needs the type icon, so pass a path that does not exist and let the existing `SHGFI_USEFILEATTRIBUTES` branch compute it once. Optionally prefetch on a worker and set `_icon.Source` when it arrives. | Wheel/UI (Team A) |
| UI-02 | P2 | Every wheel open and mode switch rebuilds the operation list on the UI thread: `Catalog.For` runs `Where(o => o.Wheel == wheel && o.AppliesTo(items) && o.IsAvailable(tools, items)).ToList()` over all operations (26 registered tools + built-ins) and allocates a fresh list. `ToolLocator` is cached, so this is allocation/term-evaluation per open, not disk I/O — small today, but it grows with the tool count and happens inside the open animation. | Code; `_coreOperations` is built once (`WheelController.cs:21,82`) — that part is fine — but the filtering is per open. | `src/FileAction.App/Wheel/WheelWindow.cs:517-521` (`LoadOperations`), `src/FileAction.Core/Operations/Catalog.cs:27` | Cache the filtered list per (wheel, media signature) and invalidate on settings/tool-install change; at minimum reuse a `List` buffer and avoid `Where(...).ToList()` in the open path. | Wheel (Team A) |
| UI-03 | P2 | Every tool window, Settings, the wheel editor, `ParameterDialog`, the colour dialog and each job card call `Theme.Refresh()` on the UI thread. That reads HKCU (`AppsUseLightTheme`) **and** re-applies the dark title bar to every open window (a `EnumWindows`-style pass + DWM call per window), so the cost grows with how many surfaces are already open — paid right where a 180 ms open animation starts. | Code: `Theme.Refresh()` → `Use(ReadSystemDark())` → `DarkTitleBar.ApplyToOpenWindows()`; registry read at `Theme.cs:154-158`. Call sites per row. | `src/FileAction.App/UI/Theme.cs:145-158`; `src/FileAction.App/UI/Tools/ToolWindow.cs:88`; `UI/SettingsWindow.cs:56`; `UI/WheelEditorWindow.cs:44`; `UI/ParameterDialog.cs:23`; `UI/Tools/Image/Frame/AddBackgroundColorDialog.cs:49`; `Jobs/JobsWindow.cs:55` | Refresh the theme once per user/settings change (the `SystemParameters`/`SystemEvents` path already exists) and let `ToolWindow` apply the title bar only to itself; cache the dark flag until a preference-changed event. | UI/theme |
| UI-04 | P1 | `GlobalInput`'s constructor starts the hook thread and then blocks the caller on `ready.Wait()` **with no timeout**. If `SetWindowsHookEx` stalls or the hook thread dies before `ready.Set()`, startup hangs before any UI exists (the app shows nothing, and the tray/wheel never appears). The failure path that would report `HookError` is never reached. | Code; `ready.Set()` happens only after both hooks are attempted (`GlobalInput.cs:85`), and a throw on that thread would skip it. Called during startup from `Program/App` construction. | `src/FileAction.App/Input/GlobalInput.cs:100` (wait), `:85` (`ready.Set`) | `ready.Wait(TimeSpan.FromSeconds(2))`; on timeout dispose the thread and take the existing `HookError` warning path (balloon + dialog) instead of hanging. | Shell/input (Team A) |
| UI-05 | P2 | Copying paths retries clipboard contention with `Thread.Sleep(50)` up to five times **on the UI thread**, so a busy clipboard (Office/Explorer holding it) freezes the acting surface for up to 250 ms with no feedback. | Code: retry loop inside the action invoked from the wheel/menu. | `src/FileAction.App/Actions/FileActions.cs:107-119` | Make the action `async` and `await Task.Delay(50)` (the method already returns `Task`), or move clipboard retries to a dispatcher timer. | Actions |
| UI-06 | P2 | OCR's `PagesOf` blocks on an async image conversion: `ImageBackend.ConvertAsync(...).GetAwaiter().GetResult()` inside a synchronous helper. When OCR is reached from a UI-context operation, the blocked await can freeze the UI for the whole conversion (large HEIC/AVIF can be seconds), with no progress until it finishes. | Code: sync-over-async inside the OCR pipeline; the operation interface is already async. | `src/FileAction.App/Ocr/OcrOperation.cs:110` | Make `PagesOf` `async Task<IReadOnlyList<string>>` and `await` the conversion; the caller already awaits `ExecuteAsync`. | OCR |
| UI-07 | P2 | Repeated open/close churns the working set; the largest positive deltas over 20 cycles are **collage +7.7 MB** and **editphotos +3.0 MB** (others: redact −3.9, addbackground −5.0, audiovisualizer −6.6, compress −5.7, audiochannels +1.0, crop +1.2); the loop's tolerance is +16 MB and the motion suite retains **0.0 MB** (no leak in motion itself). Each open decodes full images for the preview; the churn is bounded but the two image-heavy tools keep the most after close. | CI `self-test.txt`, run **39380479876** (`main`): `perf tools.collage: 20 open/close, working set 228.7 -> 236.4 MB (delta +7.7 MB, peak 236.4 MB, tolerance +16.0 MB)`; `perf tools.editphotos: ... 235.9 -> 238.9 MB (delta +3.0 MB)`; same block for all 26 tools. (That run later failed at the lock-icon step, after the perf loop completed; the perf lines are valid.) | `src/FileAction.App/UI/Tools/Collage/CollageView.cs` (preview decode), `UI/Tools/Image/EditPhotosView.cs` (`_renderGate`/preview); `SelfTest.cs` perf loop | Dispose preview `BitmapSource`/`MagickImage` deterministically on view close and keep a bounded thumbnail cache keyed by path+mtime+size so a reopen does not re-decode the full image. | Tools/perf |
| UI-08 | P2 | Each job card is created with `Theme.Refresh()` **and** `FileIcons.Get` on the UI thread, so the registry read + shell icon call land exactly while conversions are running and the user is watching the cards. | Code: `JobsWindow.Add` → `Theme.Refresh()` then `new JobCard(...)` → `FileIcons.Get(iconPath)`. | `src/FileAction.App/Jobs/JobsWindow.cs:55` (theme), `:125` (icon) | Reuse the icon cache from UI-01 and refresh the theme once per batch instead of per card. | Jobs/UI |

### Checked and found fine (no row)

- **Tool-window open does not decode the image on the UI thread.** Views start their load on a worker
  (`CompressView._probe = Task.Run(Probe)`, `CompressView.cs:64`; `ActivateAsync` only awaits it,
  `CompressView.cs:90-94`), and `ToolWindow` shows immediately then calls `PrepareAsync` from `Loaded`
  (`ToolWindow.cs:106-110`). The remaining open cost is visual-tree construction plus UI-03/UI-08 above.
- **The wheel's operation catalog is not rebuilt per open**: `_coreOperations` is a field initialised
  once and refreshed only on demand (`WheelController.cs:21,82`).
- **Motion is not a frame-time source**: p95 0.02-0.04 ms, raster proxy is software-only and not a gate,
  idle CPU 0.00 % (run 39376236166; `docs/audit/MOTION-AUDIT.md` §5).
- **`PreviewPane` UI marshalling is correct**: `Dispatcher.InvokeAsync` only when
  `CheckAccess()` is false (`PreviewPane.cs:287-296`), frames decode on a worker (`:298-300`).
- **`ToolLocator` caches tool resolution** (`ToolLocator.cs:9,65`), so per-open availability checks are
  dictionary hits, not PATH scans.

### Method / limits

Time-boxed: greps for blocking primitives (`File.ReadAll*`, `.Result`, `.Wait(`, `Thread.Sleep`,
`Dispatcher.Invoke`, `GetAwaiter().GetResult`), then the wheel/tool-window open paths and the self-test
perf loop. Not exhaustive: XAML does not exist; async void and third-party callbacks (Magick/FFmpeg
decoders entering the UI thread through events) were not traced. Everything here is static reading of
`8b28758` plus CI logs; nothing was executed locally.

## Crash - crashes and unhandled exceptions

[compiler] Source: `BH-CRASH.md` on `audit/bughunt-crash`, verbatim below.


Time-boxed hunt in the security lane, using the 72-case hostile-input suite as the map and code reading for the
gaps. **No src edits, no builds, no exe runs.** Evidence is the merged tree (`origin/main` @ `8b28758`), CI logs,
and the owner's sample pack read in place.

Severity: **P0** = kills a release gate or the app on a normal path; **P1** = user-visible crash/failed window
on a hostile but realistic input; **P2** = latent/unobserved or caught-late.

| ID | Sev | Trigger | Repro / CI evidence | file:line | Suggested fix | Owner |
| --- | --- | --- | --- | --- | --- | --- |
| **CR-01** | **P0** | Any exception on the UI thread inside a surface the shipped `--self-test` builds. The self-test host creates `new Application` **without** the app's `DispatcherUnhandledException` handler, so one bad window takes the whole gate down instead of failing that surface. | **Proven:** run [39380479876](https://github.com/Faisaldhd/dropwheel/actions/runs/39380479876), head `bc204c4` — `FAILED: System.ArgumentException` in `LockIcons.Compose` → `self-contained --self-test failed with 1`; the job never reached the unit `Test` step, so no test result existed for that SHA. The *instance* was fenced at `8b28758` (`LockIcons.cs:47-52`), **the class is not**: there is still no handler in the self-test host. | `SelfTest.cs:28-34` (host), contrast `App.cs:100` and `App.cs:480-485` (`e.Handled = true`) | Register the same guarded handler (or `CrashGuard`) in the self-test host and convert a caught exception into a `FAILED <surface>` line + non-zero exit, so one surface cannot erase the whole run. | App shell / selftest lane |
| **CR-02** | **P1** | A corrupt or truncated PDF opened in **Organize Pages**, or any `pdf.to.*` conversion. PDFium failures are not wrapped, unlike every PDFsharp path. | Source read + the owner's `pack/hostile/truncated.pdf` (1,562 B, `%PDF-1.3` prefix) and a 0-byte PDF. `PdfBackend.PageCount` throws the raw decoder exception; the tool awaits it on the UI thread (`PdfOrganizeTool.cs:215`, caught at `:217-221` → localized status) but the **conversion** path has no such catch, so the job card / redact-style previews show the raw decoder text instead of `error.pdf.cannotOpen`. | `src/FileAction.Core/Backends/PdfBackend.cs:13-17` (`PageCount`), `:22-25` (`RenderAsync`); the wrapped model is at `:255-264` (`Open`) | Wrap `PDFtoImage.Conversion.GetPageCount` in `PageCount`/`RenderAsync` as `ConversionException(T("error.pdf.cannotOpen", name, message))`. | Core (unassigned) |
| **CR-03** | P2 | Open Organize Pages on a corrupt PDF, then close the window before activation. `_pageCount` is started in the constructor and awaited only on activation. | Source read; no crash observed (the task is caught at the await). The fault is simply unobserved and the window dies. | `PdfOrganizeTool.cs:110`, `:215-221` | Start the probe lazily in `ActivateAsync`, or observe it in a `finally`/`ContinueWith` that clears the field. | UI (unassigned) |
| **CR-04** | P2 | Missing, renamed or crash-on-start `ffmpeg`/`pandoc` **mid-run** (helper deleted while a job is queued). | **Verified negative — no crash.** Start failures are wrapped as `error.process.cannotStart` and non-zero exits as `error.process.exitCode`. | `src/FileAction.Core/ProcessRunner.cs:69-71`, `:93-99` | none needed; recorded as a checked surface | — |
| **CR-05** | P2 | Fire-and-forget render on a hostile image in Redact. | **Verified negative — no crash.** The discarded task is fully guarded (outer `try` `:717` and inner `try` around the codec call `:731-738`), so the unobserved-task risk is closed by construction. | `RedactView.cs:712`, `:715-747` | none needed | — |
| **CR-06** | P2 | Six `async void` handlers if any of them is ever invoked by a host without the app's global handler (see CR-01). | Source read: `JobRunner.Start` (×2), `LockCli.Open`, `AddBackgroundView.PickImage`, `UpdateBanner.CheckNow`/`DownloadNow`. In the shipped app every one of them lands on `App.OnUnhandled`, which sets `e.Handled = true`, so the user gets a toast, not a crash. | `JobRunner.cs:24,27`; `LockCli.cs:94`; `AddBackgroundView.cs:595`; `UpdateBanner.cs:304,349` | not a defect in the app; only CR-01's host is unprotected | — |

### Not reproduced, and why

* **No crash was found in the image/archive/lock families** on the pack's hostile shapes: the 72-case suite
  already asserts “throws, no partial output, source unchanged” for 0-byte, 1-byte, text-as-PNG, truncated
  PNG/JPEG, empty/corrupt PDF, empty ZIP and zip-slip entries, and the lock/zip paths return typed
  `PasswordProtectionException`/`ConversionException`.
* **H-03…H-06** (output-name clamp, batch message text, PDFium wrapping, `PackAsync` partial) are **not**
  re-listed except CR-02, where H-05 gains a new consequence: a crash-dialog/toast surface on a hostile PDF
  rather than merely an untidy message.
* **Cannot verify headlessly:** whether a faulted UI surface leaves the app usable after the toast (needs a
  desktop), and whether the `LockIcons` fallback is visually acceptable.

### Limits

Static reading + CI logs only. Line numbers are `8b28758`. This is a crash-surface map, not a proof of absence:
the two negatives (CR-04, CR-05) are the only places I read the full guard chain.

## Races - concurrency and cancellation

[compiler] Source: `BH-RACES.md` on `audit/bughunt-races`, verbatim below.


Lane 7 (drag-and-drop) side-hunt, branch `audit/bughunt-races`, base `origin/main` @ `8b28758`.
Method: code reading only. No build, no exe, no local test run; CI evidence is cited from the runs
named per row. Every row states explicitly whether the race can leave a **partial or corrupt output**,
because that is where data loss lives.

Owners: **DL** = desktop/UI (wheel + tool windows), **JOB** = jobs/runner, **TOOL-x** = the named tool's
owner, **TEST** = test-infrastructure owner.

| ID | Sev | Race | Repro / CI evidence | file:line | Suggested fix | Owner |
| --- | --- | --- | --- | --- | --- | --- |
| RC-01 | P1 | **Closing a tool window while its Apply runs does not cancel the write.** `OnClosing` only plays the 120 ms fade and `OnClosed` only releases motion clocks; nothing cancels the view's `CancellationTokenSource`, so the `Apply` task keeps running against a closed window. For the four writers that write the final path directly (Crop, Edit Photos, Annotate, Redact) a close during the write leaves a **partial/corrupt file** at the final name. Writers that use `OutputPaths.ProduceAsync` are safe (temp + `finally` delete). | Deterministic by construction: `_cts`/`_lifetime` are only cancelled by the view's own Cancel/Reset, never from `ToolWindow`. Same defect class as matrix F-06 (partial writes). No CI failure yet because the window-open tests never close mid-Apply. | `src/FileAction.App/UI/Tools/ToolWindow.cs:262-279` (OnClosing), `:280-287` (OnClosed); direct writers `src/FileAction.App/UI/Tools/Image/CropTool.cs:446-464`, `.../Image/EditPhotoRenderer.cs:58-74`, `.../Image/AnnotateRunner.cs:75-91`, `.../Image/RedactRunner.cs:53-68`; safe pattern `src/FileAction.Core/OutputPaths.cs:24-41` | Give `ToolWindow` the view's cancellation source (or expose `Func<CancellationToken>` from `ToolContent`) and cancel it in `OnClosing` *before* the fade; route the four direct writers through `OutputPaths.ProduceAsync`. **Partial output: YES until both parts are fixed.** | DL + TOOL-crop/editphotos/annotate/redact |
| RC-02 | P1 | **`Theme.Override` is process-global mutable state read by every `Theme.*` brush.** Tests and the self-test write it from worker threads while other windows build on the UI thread, so a window can render with the wrong palette or a palette assertion can read the other test's value. Known instance: `RedactOutputTests` sets the override while the collection-serialized language tests run. | Already red-adjacent: theme assertions are only stable because the language tests are serialized; the audio suite shows how parallel-state flakes look (`AudioFunctionTests.Cancel_mid_run` red in CI run 39378176711). Reproduce by running `RedactOutputTests` + `LocalizationTests` in parallel with `Theme.Override` written from both. | Write sites `tests/FileAction.Tests/RedactOutputTests.cs:188`; `src/FileAction.App/UI/Motion/MotionClips.cs:56,76,91,120`; `src/FileAction.App/SelfTest.cs:71,88`; `src/FileAction.App/SelfTestUiCoverage.cs:120,259`; reader `src/FileAction.App/UI/Theme.cs:130,135,145` (`Override`, `Use`, `Refresh` → every brush) | Mark both the theme-writing test classes and the language tests `[Collection("ThemeState")]` (exactly the fix asked for), and in the product hold `Theme.Override` behind a lock or `AsyncLocal<bool?>` so a worker cannot change the palette under the UI thread. **Partial output: NO** (wrong colours only). | TEST + DL |
| RC-03 | P2 | **Double-click / double-drop dispatches twice.** `Run(index)` calls `Finish()` and then `Dispatch`; `Finish` is idempotent (`_finished = true`) but `Run` is not guarded, so a second mouse-up (double click) on the same slice, or a drop arriving after a click choice, runs `Dispatch` a second time. Two interactive tools can open on the same items, or two jobs can start on the same file. | Plausible on a fast double click on a tool slice; nothing in `Run`/`Dispatch` checks `_finished` or a `_dispatched` flag. Not covered by any test (wheel interaction needs a desktop session). | `src/FileAction.App/Wheel/WheelWindow.cs:655-672` (`Run`, `Dispatch`), `:394-398` (`Finish`, `_finished`) | Set a `_dispatched` flag inside `Run` and return early when set; alternatively check `_finished` at the top of `Run`. **Partial output: NO for `ProduceAsync` writers (unique names, second result `(2)`); for Lock/Protect a second run can fail mid-batch after the first removed the plaintext (matrix F-12), which is a partial *batch*, not a corrupt file.** | DL |
| RC-04 | P2 | **`AppSettings.SaveTo` uses a fixed temp name and swallows the failure.** Two saves at once (interactive `Dispatch` now saves on every open, a settings dialog save, a job's `Remember`) both write `settings.json.tmp` and both `File.Move` it; the loser gets an `IOException` that the `catch` discards, so one settings update is silently lost. A crash between write and move leaves a `.tmp` (target stays intact). | Code-read; the F-03 fix adds a `settings.Save()` on every interactive open, so the save rate goes up. `SaveTo` is UI-thread-only today, which is the only reason this has not been seen. | `src/FileAction.App/AppSettings.cs:193-212` (fixed `.tmp`, `catch (IOException or UnauthorizedAccessException)` swallows) | Serialize saves (single lock/queue) and use a unique temp name per save (`Path.GetRandomFileName()`), then `File.Move(overwrite:true)`; log the swallowed failure instead of dropping it. **Partial output: NO** (settings loss only; target file is moved atomically). | DL/JOB |
| RC-05 | P2 | **`OutputPaths.Unique` is a check-then-use race across concurrent jobs.** Two jobs (double dispatch, or two drops in flight) can compute the same final name; the first `File.Move` wins and the second throws because `File.Move` does not overwrite - the job fails after doing all the work. | Deterministic under a two-job test: `ProduceAsync` picks `Unique(dir, base, ext)` then moves. No corruption because the move refuses to overwrite, but the second result is lost. | `src/FileAction.Core/OutputPaths.cs:8-16` (`Unique`), `:24-41` (`ProduceAsync`), `src/FileAction.Core/Lock/LockService.cs:199` (`File.Move(temp, target)` "never overwrites") | Create the final name atomically (`File.Move` with `overwrite:false` retry loop on `IOException`, or open `FileMode.CreateNew` at the final path and stream into it). **Partial output: NO** (the temp is deleted in `finally`; the failure is a lost result, not a corrupt file). | JOB |
| RC-06 | P2 | **`AudioFunctionTests.Cancel_mid_run_leaves_no_partial_output` is timing-based.** The test cancels after a delay and asserts `OperationCanceledException`; if FFmpeg exits 0 first, no exception is thrown and the test fails. The product path is correct (on cancel `AudioRunner` pauses 400 ms so Windows releases the file, then rethrows, and `ProduceAsync` deletes the temp in `finally`). This is the flake that reddened CI run 39378176711 (`Assert.ThrowsAny() Failure: No exception was thrown`). | CI: run 39378176711 (PR) and 393782… family, `AudioFunctionTests.cs:235`; totals 1010/1011 passed with only this fact failing. | `tests/FileAction.Tests/AudioFunctionTests.cs:235`; `src/FileAction.App/UI/Tools/Audio/AudioRunner.cs:168-191` (400 ms pause then rethrow) | Cancel before the process can finish: pre-cancelled token, or cancel on the first progress report and await a task-completion signal; assert "no output files" unconditionally and treat cancellation as one allowed outcome. **Partial output: NO** (the 400 ms pause exists precisely to let the temp be deleted). | TEST + TOOL-audio |
| RC-07 | P2 | **`JobRunner.Start` is `async void` and owns a lazily created window.** Two dispatches interleave on the UI thread; a second `Start` while the first awaits `Task.Run` re-enters `_window.Add` (fine) but `Remember`/`settings.Save()` and the `OutputTarget` decision run before the await, so two jobs on the same source can pick the same destination folder and race `Unique` (RC-05). Exceptions in the `async void` tail surface on the dispatcher, not to the caller. | Code-read; the seeded double-dispatch path (RC-03) is the trigger. | `src/FileAction.App/Jobs/JobRunner.cs:27-132` (`async void Start`, `_window ??=`, `Remember`, `OutputTarget.Resolve`) | Make `Start` return `Task` and await it from the dispatcher entry point (or log the task), and add the `_dispatched` guard of RC-03. **Partial output: NO** (only duplicate work / lost second result through RC-05). | JOB |
| RC-08 | P2 | **The diagnostics decision log is appended from job threads.** `ShellFolders.NoteDecision` writes one line per drop from whichever thread runs the job; two simultaneous drops interleave or lose a line. Diagnostics only, never user data. | Code-read; `NoteDecision` is called inside `JobRunner.Start` before the `Task.Run`. | `src/FileAction.App/Shell/ShellFolders.cs:55` (`NoteDecision`), caller `JobRunner.cs:100-104` | Serialize with a static lock or a single writer queue. **Partial output: NO** (diagnostic lines only). | JOB |
| RC-09 | P1 | **`Theme.Override` + `MotionClips` during self-test screenshot runs can render a window in the wrong theme, and the shipped screenshot job is skipped on PRs** - so a theme regression can ship unnoticed. | `docs/screenshots` regeneration only runs on a main push/`workflow_dispatch` (`.github/workflows/ci.yml` screenshots job condition); the self-test sets `Theme.Override` while rendering every window. | `src/FileAction.App/SelfTest.cs:71,88`; `src/FileAction.App/UI/Motion/MotionClips.cs:56-120`; `src/FileAction.App/SelfTestUiCoverage.cs:120,259` | Reset the override in a `finally` around the render loop and assert the palette per window (the self-test already renders every window - it can assert `Theme.IsDark` matches the requested mode). **Partial output: NO** (visual artifact). | DL |

### Second pass (same scope, more code reading)

| ID | Sev | Race | Repro / CI evidence | file:line | Suggested fix | Owner |
| --- | --- | --- | --- | --- | --- | --- |
| RC-10 | P2 | **A slow URL drop dispatches after the user cancelled.** `OnDrop` schedules `Dispatcher.BeginInvoke(async …)` that awaits the network download (`content.Prepare`), then calls `Dispatch`. Escape, closing the wheel or starting another drag does not cancel that continuation, so a drop the user abandoned still opens a tool or starts a job, and two quick link drops dispatch independently on stale item lists. | Code-read: the only cancellation is the `try/catch` around `Prepare`; nothing checks `IsFinished`/a lifetime token before `Dispatch`. No test (needs a real drop). | `src/FileAction.App/Wheel/WheelWindow.cs:556-580` (`OnDrop`, the `BeginInvoke` + `await content.Prepare()` + `Dispatch`), `:394-398` (`Finish`, `_finished`) | Capture the window's closed state in a `CancellationTokenSource` owned by the wheel, cancel it in `Finish`, and check it after the download before dispatching. **Partial output: NO** (a stale dispatch produces a normal, complete output). | DL |
| RC-11 | P2 | **A failed or cancelled URL download leaves a partial file in `Downloads\DropWheel`.** `Download` writes straight to the final target with `File.Create(target)` and `CopyToAsync`; there is no `.part` file, no unique final name and no cleanup in a `finally`, and `OnDrop` only shows `error.download.failed`. | Code-read; the failure path is reachable by any dropped link whose server dies mid-transfer. No test. | `src/FileAction.App/Wheel/DropReader.cs:223-239` (`Download`, `File.Create(target)`), caller `WheelWindow.cs:565-575` | Download to `target + ".part"` and `File.Move` on success, deleting the temp on failure/cancel (the `OutputPaths.ProduceAsync` pattern); keep `Unique` for the final name. **Partial output: YES - but only the downloaded copy, never the user's original file.** | DL |
| RC-12 | P2 | **Closing the Jobs window orphans running jobs.** The per-job `CancellationTokenSource` lives in `JobRunner.Start`'s local `using var cts` and is only reachable through the card's Cancel button; `JobsWindow` has no `OnClosing` hook, so closing it removes the only Cancel affordance while the job keeps running (and `card.Report`/`Complete` then post to a closed window). | Code-read: no `OnClosing`/`OnClosed` in `JobsWindow`; `Cancelled()` is a card method. Same family as the question "closing a tool window while its job runs - who owns the token". | `src/FileAction.App/Jobs/JobsWindow.cs:18-90` (no close hook, `_cards`), `src/FileAction.App/Jobs/JobRunner.cs:61-73` (`using var cts`, `card` closure), `:117-127` (`card.Complete`/`Cancelled`) | Cancel or hide-and-keep the cards on close (or refuse to close while a card is running and say so); keep the token in a field so cancellation survives the window. **Partial output: NO** (`ProduceAsync` writers still delete their temp; the user just loses the Cancel button). | JOB |

#### Ruled out after reading (no row, recorded so the hunt is auditable)

- `GlobalInput` raises every hook event through `_dispatcher.BeginInvoke` (`src/FileAction.App/Input/GlobalInput.cs:152,171,209,221`), so wheel windows are always created on the UI thread - no cross-thread WPF race.
- `Loc` keeps immutable `Lazy<IReadOnlyDictionary<…>>` tables and `Current` flips a `Language` field (`src/FileAction.Core/Loc.cs:51-52,63-77`); a job running during a language switch reads either the old or the new table, never a torn one (mixed-language text at worst, no crash).
- `Theme.Override` is still RC-02/RC-09: the declaration is `src/FileAction.App/UI/Theme.cs:130` and `Refresh` reads it at `:145`.
### Totals and the one-line P0/P1 hand-off

- Rows: **12** - P0 **0**, P1 **3** (RC-01, RC-02, RC-09), P2 **9** (RC-03..RC-08, RC-10..RC-12).
- Only **RC-01** can leave a partial/corrupt *user* file; **RC-11** can leave a partial *downloaded copy*; RC-01 is the same defect as matrix F-06 and should be
  fixed by routing the four image writers through `OutputPaths.ProduceAsync` plus cancelling on window
  close. RC-02/RC-09 are state/visual races. RC-03..RC-08 are duplicate-work, lost-settings or
  lost-second-result races with no corrupt output.
- **P1 one-liner:** `RC-01 closing a tool window mid-Apply leaves a partial/corrupt output in Crop/Edit Photos/Annotate/Redact (no cancel on close); RC-02 global Theme.Override races the language tests; RC-09 theme regressions can ship because screenshots are skipped on PRs.`


## Visual - rendering, RTL, DPI

[compiler] Source: `BH-VISUAL.md` on `audit/bughunt-visual`, verbatim below.


Lane: TEAM B bug hunt · Branch `audit/bughunt-visual` · Evidence: `ui-coverage` artifact from
run **39379321230** (`32757e9`, self-test exit 0, 114/114 surfaces, 90 PNGs: 44 en + 44 ar + 2 controls),
re-downloaded and inspected at 100 % DPI, dark theme. Time-boxed audit: **no src/build/string edits**.

What the gate asserts vs. what it cannot see: the coverage pass checks **raw keys, empty titles, Arabic
root FlowDirection, no Arabic text forced LTR, and size fit**. It does **not** assert overlap, clipping,
truncation, icon mirroring, physical left/right alignment, mixed-direction runs inside a label, or any
DPI above 150 %. Those blind spots are the rows below.

**Totals: 0 P0 · 2 P1 · 8 P2 · 1 P3.**

> **Claim integrity (VI-01):** until the post-tag fix lands, the honest claim is
> **“112/114 surfaces genuinely en+ar; 2 surfaces (wheel, parameter dialog) are English renders in the
> ar pass.”** The gate's Arabic coverage for those two surfaces is **void** (English text trivially passes
> a raw-key/empty-label assertion, so “114/114” is really **112 genuine + 2 vacuous**), and the two
> committed `ar/` PNGs are **false evidence** that must not be cited as proof of Arabic rendering.
>
| ID | P | symptom | evidence | file:line | suggested fix | owner |
|---|---|---|---|---|---|---|
| VI-01 | **P1** | Arabic `ui-coverage` **wheel** and **parameter dialog** screenshots are actually **English** ("Copy/Move/Float", "PDF document", "Start/End", "e.g. 0:30…", "empty = until the end") while their buttons are Arabic. Two distinct consequences: **(a) the gate's Arabic coverage for those two surfaces is void** — English text trivially passes a raw-key/empty-label assertion, so “114/114 en+ar” is really **112 genuine + 2 vacuous**; **(b) the two committed `ar/` PNGs are false evidence** — nobody may cite them as proof of Arabic rendering. This is an evidence/claim defect, not a product defect: the app relocalizes live. | artifact `ar/wheel-actions.png`, `ar/wheel-image-convert.png` (subtitle "document"), `ar/parameter-dialog.png`; compare `ar/settings.png` (correct Arabic). Gate report still says ar PASS because the assertion only looks for Arabic-in-LTR, not for missing Arabic. | `src/FileAction.App/SelfTestUiCoverage.cs:76-78` builds `WheelController.BuildOperations()` / `FileActions.Build` **once, before** the en/ar loop at `:81`; `Operation.Label/Subtitle/Parameters` are fixed at construction. Product path is correct: `App.cs:233-240` calls `_controller.Relocalize()` on language change. | Build core/actions **inside** each culture iteration (or call a relocalize helper) before rendering the wheel and ParameterDialog; add an assertion that the ar wheel contains Arabic on every slice and the ar parameter dialog localizes Start/End; re-render those two pairs. | audit/selftest (B) — **status: post-tag** |
| VI-02 | **P1** | At **200 % DPI** the protection dialogs are taller than the work area and their bottom actions fall off-screen: LockSettings **520×660**, RescuerSetup **460×560**, LockWarning **520×540** DIP vs **540 DIP** usable height at 1920×1080@200 %. On 1280×800@200 % (400 DIP) also ChangePassword **460×470**, RescuerSession **450×390**. No scroll, no clamp; the custom close X stays reachable but "إغلاق"/"التالي"/"تشفير" do not. This is precisely the risk the gate's uncovered **200 % dimension** left unnamed: the fit matrix stops at 150 %, so no CI check would ever have caught it. | `LockUi.Configure` assigns `window.Width = width; window.Height = height` with **no** work-area clamp (unlike `UiSetup.FitToWorkArea`, used by Settings); values from the call sites listed in the "DPI table" below. Confirmed by code + the 100 % screenshots; 200 % is the gate's named-uncovered dimension. | `src/FileAction.App/Shell/Protection/LockUi.cs:258-263` | Clamp to `SystemParameters.WorkArea` (reuse `UiSetup.FitToWorkArea(this, width, height, …)`) and put the body in a `ScrollViewer`; add 200 % to the fit matrix for dialogs. | Team A (Lock/UI) — **status: post-tag** |
| VI-03 | P2 | **Toggle switches never mirror.** In Arabic the knob still travels left→right (off at left, on at right), so the "on" side is the trailing edge instead of the leading edge. | `ar/settings.png`: "مراقبة الماوس…" off knob is left, "الحركات" on knob is right; en is identical. Physical-coordinate animation, no `IsRtl` branch. | `src/FileAction.App/UI/Controls.cs:138` (knob `HorizontalAlignment.Left`), `:156` (`var x = on ? 23.0 : 3.0`) | Mirror the animation for RTL: `x = on ? (rtl ? 3 : 23) : (rtl ? 23 : 3)`, or set the track's `FlowDirection` and anchor logically. | UX/UI |
| VI-04 | P2 | **Jobs progress bar fills from the left in Arabic** (running card's purple bar is anchored at x=0 of the track). The rest of the card is correctly RTL. | `ar/jobs.png` running card: purple segment starts at the left edge, dark track continues to the right. WPF does not mirror an explicitly left-anchored child. | `src/FileAction.App/Jobs/JobsWindow.cs:101` (`_fill = new Border { HorizontalAlignment = Left, Width = 0 }`), `:146` (`_fill.Width = _bar.ActualWidth * _value`) | Anchor the fill on the logical start: for RTL use `HorizontalAlignment.Right`/`FlowDirection.RightToLeft` on `_bar` (verify with screenshot), or compute margin from the right. | UX/UI + jobs |
| VI-05 | P2 | **Mixed run splits the output file name in Arabic audio bleep.** The rendered name reads like `wav… مُصغّر tone` — `.wav` detaches from `tone` because the Arabic suffix is placed **inside** an LTR isolate. | `ar/tool-tools-audiobleep.png`, bottom-centre output name (crop kept in the lane scratch); compare `en/...` where it reads `tone … .wav` cleanly. | `src/FileAction.App/UI/Tools/Audio/AudioBleepTool.cs:403` `Bidi.Value($"{_item.BaseName} {T("tool.audiobleep.suffix")}.{format}")` — isolates the whole mixed string, not just the Latin parts | Isolate only the Latin parts: `Bidi.Value(BaseName) + " " + T(suffix) + "." + format`, or compose separate `Run`s (same pattern in audio-channels/visualizer if they add an Arabic suffix). | Audio tools |
| VI-06 | P2 | **Audio bleep lane captions sit on the far left** in Arabic ("صافرة" / "الصوت" at the left margin, outside the lane), while the window and every other label are RTL. The lane grid is LTR for its time axis, but the captions inherited it. | `ar/tool-tools-audiobleep.png`: both lane titles at x≈80 while lane surfaces start at x≈118; en is identical by design. | `src/FileAction.App/UI/Tools/Audio/AudioBleepTool.cs:169-173` (whole `Grid` `FlowDirection.LeftToRight`), `:198,:201` (`Place(bleepTitle,0,0)` / `audioTitle,2,0`) | Keep the time axis LTR only for the waveform/time rows; place captions in a separate RTL column (or set the title blocks' `FlowDirection` **and** put the title column at the leading edge for RTL). | Audio tools |
| VI-07 | P2 | **Wheel has a fixed 442×442 DIP size and no work-area clamp.** At 200 % on a 1280×800 panel (400 DIP of work height) the bottom of the disc is off-screen; there is no scroll and no scale-down. | `WheelWindow.cs:39` `Size = 2*(196+9+16) = 442`; `:99-100` `Width = Size; Height = Size`; `:109` `UiSetup.Configure(this, animate:false)` — configuration path does not call `FitToWorkArea`. 200 % screenshot is the gate's uncovered dimension. | `src/FileAction.App/Wheel/WheelWindow.cs:39,99-100,109` | `UiSetup.FitToWorkArea(this, Size, Size)` plus a `LayoutTransform`/canvas scale when the work area is smaller than the disc. | Wheel/UI |
| VI-08 | P2 | **Tool windows at 200 % compress below their design minimum.** At 1080p@200 % the sizer caps height at `0.90 × 540 = 486` DIP; the default chrome is 270 DIP, leaving **216 DIP** of preview against a declared `PreviewMinHeight` of **320**. Tools with fixed internal rows (AudioBleep two lanes + two sliders, VideoTrim timeline + transport, PDF organise list, Metadata list) will overlap/clip; no 200 % screenshot exists. | `ToolSizes.cs:15-23` (Default: ChromeHeight 270, PreviewMin 320/320, Default 350×450); `ToolWindowSizer.cs:49-54,92-94` (hard clamp, min not enforced after clamping). | `src/FileAction.App/UI/Tools/ToolSizes.cs:15-23`, `src/FileAction.Core/ToolWindowSizer.cs:49-54,92-94` | Add 1080p@200 % to the fit matrix (report-only first), then give each tool a `ScrollViewer` fallback or lower the chrome height at high DPI. | Tools/UI |
| VI-09 | P2 | **Settings sliders are left-anchored in Arabic.** The accent fill and thumb use physical `HorizontalAlignment.Left` plus a left margin, so in Arabic the fill grows left→right and the thumb starts at the left end; the numeric label is right-aligned. Not visible on the General tab screenshot, but the same `Controls.Slider` builds Quality/Integration sliders. | `src/FileAction.App/UI/Controls.cs:179` (fill `HorizontalAlignment.Left`), `:187` (thumb `Left`), `:200` (`thumb.Margin = Thickness(t*(width-18),0,0,0)`); VI-04 proves WPF does not physically mirror this. | `src/FileAction.App/UI/Controls.cs:175-235` | Anchor fill/thumb on the logical start (`FlowDirection.RightToLeft` on the `surface` when RTL, or right margin `(width-18)-t*(width-18)`), and align the value label to the logical trailing edge. | UX/UI |
| VI-10 | P2 | **Mixed-language Metadata window in Arabic.** Section headers are Arabic ("الصورة", "بيانات ExifIFD") but the field labels are English (`format`, `width`, `height`, `size`, `Make`, `Model`, `Latitude`, `Longitude`) and the values are unisolated Latin runs; also the subtitle reads `PNG · عدد الحقول: 8` with the Latin token mid-sentence. | `ar/tool-tools-metadata.png`; search label is correctly Arabic ("البحث في البيانات الوصفية"). Whether these are EXIF tag identifiers (data) or UI labels needs a product decision. | `src/FileAction.App/UI/Tools/Meta/MetadataView.cs:134-139` localizes only the search label; field names originate from `MetadataModel` (data model, not from `en/ar.json`) | Decide data-vs-UI: if UI, add keys for the synthetic fields (`format/width/height/size`) and keep real EXIF tags Latin/isolated; isolate each Latin value with `Bidi.Value`. | Metadata |
| VI-11 | P3 | **Video-trim transport row edge asymmetry.** In Arabic the right-hand "البداية" label is flush against the window edge (no 18 DIP chrome margin), while the left-hand group has margin; at 150 % it will touch/clip. The chevron order is also visually asymmetric between the two time groups. | `ar/tool-tools-videotrim.png` bottom row, right edge (crop: label ends ≈2 px from the border); en comparison shows symmetric margins. | `src/FileAction.App/UI/Tools/Video/VideoTrimView.cs:120-123` (frame back/forward buttons) and the transport `DockPanel`/`StackPanel` around them | Use the same margin on both ends and mirror the group order; verify at 150 %. | Video tools |

### 200 % DPI risk table (the uncovered dimension)

DIP work area = physical pixels ÷ scale. A 200 % panel therefore has half the DIP height:

| Physical panel @200 % | DIP work area | Fits? |
|---|---:|---|
| 1920×1080 | 960×540 | Wheel 442 ✔ (VI-07 only at 1280×800); tool height cap 486 → preview 216 < min 320 (VI-08); LockSettings 660 ✘, RescuerSetup 560 ✘, LockWarning 540 ✘, ChangePassword 470 ✔, Session 390 ✔ |
| 1280×800 | 640×400 | Wheel 442 ✘ (clipped); tool cap 360 → preview 90 DIP (severe); LockSettings 660 ✘, RescuerSetup 560 ✘, LockWarning 540 ✘, ChangePassword 470 ✘, Session 390 ✘ borderline |

Settings itself is safe: `UiSetup.FitToWorkArea(this, 680, 820, 520, 360)` plus a `ScrollViewer`
(`SettingsWindow.cs:63,157`). The dialogs above are the gap.

### Gate blind spots (why these escaped)

1. Assertions never inspect pixels: overlap, clipping, ellipsis and icon mirroring are invisible.
2. `ar` PASS only proves "no Arabic text is forced LTR" — it cannot prove "Arabic text exists", which is
   exactly how VI-01 (English wheel in the ar folder) passed.
3. Fit is asserted at 100 %/150 % only; fixed-size dialogs are checked at 1280×800@100 % only.
4. WPF mirrors **panel order**, not physical `HorizontalAlignment`/margins — VI-03/04/07/09 are all
   physical-coordinate bugs that an RTL `FlowDirection` cannot fix.
5. No light-theme Arabic screenshots were reviewed in this pass (the artifact contains ar-light only for
   the Settings/wheel family via `RenderAll`; the new `ui-coverage` set is dark-only). Light/dark token
   contrast is therefore **not asserted** here and needs the existing screenshot job.

### What was checked and is clean

- Arabic Settings (General): tab order, text, gesture chips and mirrored card rows are correct.
- Wheel hub/tabs RTL, slice captions RTL except format identifiers (by design), selected slice state.
- Shelf: Arabic title, RTL close, Arabic hint; file names correctly isolated.
- Lock warning: Arabic body wraps without clipping at 100 %, checkbox indicator on the right, buttons
  mirrored (cancel right, encrypt left).
- Parameter dialog layout is correct in English; only its Arabic localization is stale (VI-01).

### Method / limits

Artifact images inspected (en + ar): settings, shelf, jobs, wheel ×3, parameter dialog, 10 protection
dialogs, audio bleep, metadata, video trim; other tools reviewed by code and the shared `ToolWindow`
chrome. No binaries were run locally; no 200 % screenshot exists in the artifact, so DPI rows are
code-derived from the declared sizes above. Suggested fixes are one-file, minimal; this lane made no
source edits.

## Upgrade - settings, uninstall, update path

[compiler] Source: `BH-UPGRADE.md` on `audit/bughunt-upgrade`, verbatim below.


Lane 5 (release/installer). Scope: **clean-profile first run, upgrade in place over 2.3.0, settings
migration, output-folder edge cases, repeat-last after restart, uninstall that keeps user data, and what
is left behind on disk**. Method: code reading plus the installer smoke log this lane already verified
(`scripts/test-installer.ps1`, run `39375412602`). No build, no exe, no installer run, no dispatch, no
`src` edits.

Baseline: `origin/main` `8b28758`.

**Totals: 0 P0, 2 P1, 9 P2.**

| ID | sev | scenario | evidence | file:line | suggested fix | owner |
|---|---|---|---|---|---|---|
| **UP-01** | **P1** | **A corrupt or unreadable `settings.json` is silently replaced by defaults and then overwritten — the user's settings are destroyed with no backup.** Happens on upgrade if a 2.3.0 file cannot be parsed (truncated by a crash/AV lock, hand-edited key, enum stored as an unknown string), and on a permissions problem. The exception is swallowed with the comment "Broken settings file: start with defaults", `new AppSettings()` is returned, and the next `Save()` writes defaults over the original file. | The load path catches and returns defaults; the save path writes unconditionally over the same path with no `.bak` and no notification. | `src/FileAction.App/AppSettings.cs:152-156` (catch → defaults), `:196-211` (`SaveTo` overwrites `FilePath`) | Before returning defaults, copy the unreadable file to `settings.bad-<yyyyMMdd-HHmmss>.json` in the same folder and set a flag; show a one-time toast/log line "settings could not be read, a backup was kept". Never delete the only copy. | app/self-test owner |
| **UP-02** | P2 | A failed settings save is invisible and can leave `settings.json.tmp` behind forever. `SaveTo` writes the temp file and then moves it; if the move fails (file locked by AV/backup, disk full) the catch swallows it and the `.tmp` is never deleted. The user sees their change in the UI and loses it on restart. | `File.WriteAllText(temp, …)` then `File.Move(temp, path, overwrite: true)`, both inside a catch that ignores `IOException`/`UnauthorizedAccessException`; no `finally` for the temp path. | `src/FileAction.App/AppSettings.cs:203-210` | Wrap the temp in `try/finally` and delete it on failure; log one warning per session so a read-only profile is visible instead of silently losing settings. | app owner |
| **UP-03** | **P1** | **Uninstalling while a conversion is running orphans the helper process.** The uninstaller kills only `DropWheel.exe`, with no process-tree switch and no mention of `ffmpeg`, so an in-flight `ffmpeg` keeps running after the app and its folder are gone — it holds the output file and can block the uninstall of the tools cache. The app kills its children only on a *cooperative* cancellation; a hard kill or a crash orphans them too. | `Parameters: "/im DropWheel.exe /f"` — no `/t`, no helper match. `Process.Kill(entireProcessTree: true)` exists but only on the `OperationCanceledException` path, i.e. only while the app itself is alive and cancelling. | `installer/DropWheel.iss:136`; `src/FileAction.Core/ProcessRunner.cs:77-91` | Two-line fix: add `/t` to the uninstaller's `taskkill` and a second `taskkill /im ffmpeg.exe /f`. Structurally better: assign each child to a Job Object with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` at spawn so a crash cannot orphan it. | release lane (taskkill) + core owner (job object) |
| **UP-04** | P2 | **"Repeat last" forgets the parameters the user chose.** `LastParameters` is written on every run but never read on the Windows path, so repeating an operation that had parameters (quality, target format, split points) re-runs it with defaults. The Linux controller reads them, which shows the intended behaviour. | Written in `JobRunner`; the only reader in the tree is the Linux controller. | `src/FileAction.App/Jobs/JobRunner.cs:208-209` (write); `src/FileAction.Linux/Controller.cs:70` (read, Linux only); no Windows read | Pass `_settings.LastParameters` into `RunOperation` for the repeat path, as Linux does. | app owner |
| **UP-05** | P2 | Repeat-last is a **silent no-op** when the recorded operation no longer applies or its tool is unavailable (e.g. the user removed FFmpeg between sessions, or dropped a file type the recorded operation rejects). The user presses the key, nothing happens, no message. | `LastFor` returns `null` when `AppliesTo`/`IsAvailable` fail, and `RunRepeat` guards with `if (LastFor(repeatItems) is { } last)` — no `else`. | `src/FileAction.App/Wheel/WheelController.cs:156-161, 277-281` | `else` branch: toast `toast.repeatUnavailable` ("last action is not available for these files") and fall back to the normal wheel. | app owner |
| **UP-06** | P2 | **Per-conversion scratch folders under `%TEMP%\DropWheel\<guid>` are never swept.** If the app crashes or is killed mid-conversion, that folder survives indefinitely. The two sibling folders `%TEMP%\DropWheel\lo-profile` and `%TEMP%\DropWheel\restore` are intentionally long-lived and are never pruned either, so the cache grows with every LibreOffice run. | `TempDirectory()` creates `%TEMP%\DropWheel\<guid>`; nothing enumerates and deletes stale children at startup. | `src/FileAction.Core/OutputPaths.cs:43-48`; `src/FileAction.Core/Backends/OfficeBackend.cs:39`; `src/FileAction.Core/Lock/LockGuard.cs:29` | At startup, delete `%TEMP%\DropWheel\<guid>` folders older than 24 h (the guid shape only, never `restore`), and recreate `lo-profile` when its size exceeds a cap. | core owner |
| **UP-07** | P2 | A locked partial file can be left in the **user's own output folder**. `ProduceAsync` writes `.<name>.<8hex>.part.<ext>` next to the destination and deletes it in `finally`, but `TryDelete` swallows `IOException`/`UnauthorizedAccessException` — precisely the case where a killed `ffmpeg` still holds the handle (UP-03). The user is left with a hidden file nobody explains. | Hidden temp name is created in the destination directory; deletion failure is silent. | `src/FileAction.Core/OutputPaths.cs:24-41, 50-61` | Retry the delete once after a short delay; if it still fails, rename to `<name>.partial-<hex>.<ext>` and log it, so the file is discoverable and clearly not a result. | core owner |
| **UP-08** | P2 | **The configured output folder is trusted, never probed.** Empty/whitespace is handled correctly (⇒ next to the source), but a folder that no longer exists, a disconnected network share, or a read-only/OneDrive-synced path is only discovered when the job runs, and then appears as a generic conversion failure for that job. Also note the drop-target folder wins over the setting (`chosen.Directory ?? settings.OutputDirectory`), so a user who configured an output folder can still see results land next to the source depending on how they dropped. | Empty ⇒ `null` is explicit; there is no existence/write probe or canonicalisation anywhere; the precedence is drop-folder first. | `src/FileAction.App/AppSettings.cs:84-85`; `src/FileAction.App/Jobs/JobRunner.cs:106-115`; `src/FileAction.App/UI/SettingsWindow.cs:302-321, 690` | Probe the configured folder when it is chosen (create + touch a temp file, like the tools folder fallback does) and mark it invalid in the UI; document the precedence in the Settings tooltip. | app owner |
| **UP-09** | P2 | **A mass/silent uninstall keeps the settings *and* the downloaded helper cache, with no switch to remove them.** Silent uninstall always keeps `%APPDATA%\DropWheel` and `%LOCALAPPDATA%\DropWheel\tools`; the Keep/Remove question is suppressed by design. On a corporate fleet that leaves FFmpeg/Pandoc/LibreOffice downloads (hundreds of MB) behind with no supported way to say "remove everything". | `UninstallSilent` → log "keeping the settings" and skip the delete; CI asserts the retention as a feature. | `installer/DropWheel.iss:210-241`; `scripts/test-installer.ps1:344-349, 378-379` | Add an optional `/REMOVEDATA` (or `/PURGEDATA`) command-line switch that forces `RemoveUserData := True` for management tools, and mention it in `docs/signing.md`-style install docs. | release lane |
| **UP-10** | P2 | **Upgrading over 2.3.0 never removes files the old version left behind.** `[Files]` copies the new publish with `ignoreversion` and nothing deletes files that existed in 2.3.0 but are gone in 2.4.0 (renamed/removed tool DLLs), so `{app}` accumulates stale binaries across upgrades and "reinstall to fix it" does not actually reset the folder. | The installer only adds/overwrites; there is no `[InstallDelete]` entry and no uninstall-then-install step. | `installer/DropWheel.iss:105-109` | Add an `[InstallDelete]` for `{app}\*.dll` / `{app}\*.exe` before `[Files]` (the publish is self-contained, so nothing else legitimately lives there), or have `build-installer.ps1` emit an explicit old-file list. | release lane |
| UP-11 | P2 | **The log is unbounded and records full file paths on every drop.** `drop folder decision: source=<full path> cursor=<full path> remembered=<full path>` is written at INFO for every job, into an append-only `log.txt` that is never rotated or capped. On a company laptop that is a growing plaintext record of document names and directory structure — leftover state that survives uninstall unless the user opts in to removing `%APPDATA%`. | The decision line includes three absolute paths; the log opens in `FileMode.Append` and nothing anywhere checks its size or age. | `src/FileAction.App/Shell/ShellFolders.cs:55-59`; `src/FileAction.App/Jobs/JobRunner.cs:100-104`; `src/FileAction.App/Diagnostics/AppLog.cs:33-55` | Log the file name only (not the full directory) for the drop decision, and rotate `log.txt` at ~5 MB to `log.1.txt` keeping one generation. | app owner |

### Notes on what I could **not** establish

- **No P0 found.** Nothing in the first-run, upgrade or uninstall path destroys data or bricks an
  install. The closest is **UP-01** (settings loss on a corrupt file), which is a silent data loss but
  scoped to the settings file and not to user documents.
- **`%LOCALAPPDATA%\DropWheel` contents were not exercised live** beyond what CI proves: the smoke test
  only creates a marker file there (`installer-smoke-test.txt`) and asserts it survives. What the tool
  downloader actually writes (cache layout, partial downloads) was read, not observed — this lane may
  not download tools or run the app.
- **Settings migration from 2.3.0 defaults was checked only as code**: unknown/new keys are ignored by
  `System.Text.Json` source-gen (so a 2.3.0 file loads into 2.4.0 with new members at their defaults),
  and `MigrateLanguage` rewrites only `Language`. I did not run a real 2.3.0 file through a 2.4.0 build.
- **The uninstall-leaves-data behaviour is verified in CI** (run `39375412602`): `ok: a silent uninstall
  keeps the settings folder` and `ok: a silent uninstall keeps the downloaded helper programs`. That is
  the intended behaviour; UP-09 is about the missing opt-out, not about that assertion being wrong.
- **The first-run path itself** (`FirstRunDone`, the welcome surface) was read, not run; the clean-machine
  assumption is CI-proven ("no DropWheel install, settings, Run value or shell entries" before install).

