# Architecture review — DropWheel 2.4.0 as shipped on `main`

Reviewed revision: **`8b28758`** (*Merge pull request #43 from Faisaldhd/audit/arelease*), branch
`audit/arch`, lane `docs/audit/**`. Read-only: no source edit, no local build/exe, no refactor PR before
2.4.0. Second reader: security lane.

**RELEASE-BLOCKER: none found.** The two areas the brief flags as highest risk — partial outputs on
failure (area 3) and Lock/Protect in-place rewriting (area 4) — are the two best-defended parts of the
codebase. Findings below are CI-reliability and hardening items, not shipping hazards. Negatives are
recorded as findings too (`no finding`), because they are the evidence for that sentence.

Method: static reading plus the shipped test suite and CI configuration. Every claim cites `file:line`.
Where I could not confirm a runtime context (e.g. which thread calls a blocking call) the finding says so
and is graded BACKLOG rather than asserted as a defect.

| # | Severity | Area | Finding | Owner |
| ---: | --- | --- | --- | --- |
| 1 | FIX-NOW-SMALL | CI | every branch runs CI twice (push + pull_request, different concurrency groups) | release/CI |
| 2 | FIX-NOW-SMALL | Test isolation | `Theme` global mutated by parallel tests → the flake seen twice | test/QA |
| 3 | FIX-NOW-SMALL | Threading | `JobRunner.Start` is `async void`; preflight failures can crash the process | app |
| 4 | FIX-NOW-SMALL | Startup | `GlobalInput.Start()` blocks startup thread on an unbounded `Wait()` | shell |
| 5 | BACKLOG | Threading | `async void` UI handlers without try/catch (`UpdateBanner`, `AddBackgroundView`, `LockCli`) | app/UI |
| 6 | BACKLOG | Registry parity | the guard is one-directional: an operation with no tool is not detected | UX/app |
| 7 | BACKLOG | Duplication | two chip implementations and two card paddings in the shared control kit | UX |
| 8 | BACKLOG | Dead/duplicate code | duplicate `tools.ocr` operation id across App and Linux projects | platform |
| 9 | BACKLOG | Threading | `OcrOperation` blocks on a convert task (`GetAwaiter().GetResult()`) — thread unconfirmed | app |
| 10 | no finding | Layering | `FileAction.Core` has no WPF/Shell dependency | — |
| 11 | no finding | Partial outputs | temp-then-move + `finally` delete is the house pattern, and it is tested | — |
| 12 | no finding | Lock/Protect | target-adjacent staging, verify-then-move, journal recovery, no cross-volume move | — |
| 13 | no finding | Startup/memory | 26 registrations build no UI; windows and views are constructed on demand | — |
| 14 | no finding | Strings/theming/DPI | embedded en/ar with enforced parity; DPI read per window | — |

---

## 1. FIX-NOW-SMALL — CI runs twice for every branch (push **and** `pull_request`)

`.github/workflows/ci.yml:3–4` triggers `push` on `branches: ['**']`; `:8` adds `pull_request`.
`concurrency` is `group: ci-${{ github.ref }}` (`:14`), and `github.ref` is `refs/heads/<branch>` for a
push but `refs/pull/<n>/merge` for a PR, so `cancel-in-progress: true` (`:16`) cannot collapse the pair:
both full jobs run to completion.

Observed on this lane: push run `39377304805` and PR run `39377310570` for the same commit `e3c0554`,
and again `39378656678` + `39378661406` for `4ade597`. Each run is ~13 minutes including the shipped
self-test, so every lane doubles its runner cost and doubles its exposure to the flake in finding 2.

**Minimal fix:** use `group: ci-${{ github.event.pull_request.head.ref || github.ref_name }}` so a PR
and its branch push share one group, or drop `push` for branches that always have a PR
(`push: branches: [main]`). No behaviour change for `main`/tags.

## 2. FIX-NOW-SMALL — `Theme` is process-global mutable state shared by parallel tests

`Theme.Override` (settable) + `Theme.Apply/Refresh` mutate static `_palette`/`IsDark`
(`UI/Theme.cs`), and the suite partitions only language-switching tests
(`TestSetup.cs` `LanguageSwitching` collection). At least three test classes mutate the palette
(`RedactOutputTests.cs:188 Theme_switch_changes_the_palette_the_tool_uses`, plus the PDF/video theme
tests), so two of them can interleave.

Observed twice on this train: `RedactOutputTests.Theme_switch_changes_the_palette_the_tool_uses` failed
with `Assert.NotEqual(darkText, ColorOf(Theme.Text))` on `4ade597` while 990 other tests passed, and the
same test was reported on another lane. It is not a product defect: the failing assertion compares two
colours read after `Theme.Apply`, and the diff under test never touches `Theme.Text/Card/Muted`.

**Minimal fix:** add a `[Collection("ThemeState")]` (DisableParallelization) and put every test that
writes `Theme.Override`/`Apply` in it — the same pattern the repo already uses for language.

## 3. FIX-NOW-SMALL — `JobRunner.Start` is `async void`

`Jobs/JobRunner.cs:24,27`: both overloads are `public async void`. The long-running body is wrapped in
`try/catch` (`:118–131`), but everything before it is not: the tool-download path (`:32–37`), the
`AppAction` path (`:39–51` catches, ok), and `new ParameterDialog(...).ShowDialog()` (`:57`).
An exception there leaves `async void` unobserved on the dispatcher, which by default terminates the
process — i.e. a crash while a job card may still be running.

**Minimal fix:** make the method `async Task StartAsync(...)` and call it as
`_ = StartAsync(...)` from the callers, or wrap the whole body in one `try/catch` that routes to
`Notifications.Show`. One file, no API consumers outside the app.

## 4. FIX-NOW-SMALL — startup blocks on an unbounded `Wait()`

`Input/GlobalInput.cs:100` calls `ready.Wait()` on the thread calling `Start()` (startup) with no
timeout. The worker sets the two low-level hooks and signals (`:84–96`); if `SetWindowsHookEx` stalls
(a locked-down desktop, AV interference), startup hangs with no window and no message.

**Minimal fix:** `if (!ready.Wait(TimeSpan.FromSeconds(2))) throw new InvalidOperationException(T("error.globalInput.failed"))`
— the same exception the failure path already raises, so the UI already handles it.

## 5. BACKLOG — `async void` UI handlers with no catch

`UI/UpdateBanner.cs:304` (`CheckNow`), `:349` (`DownloadNow`), `UI/Tools/Image/Frame/AddBackgroundView.cs:595`
(`PickImage`), `UI/Tools/Lock/LockCli.cs:94` (`Open`). This is normal WPF event-handler style, but an
exception thrown after the first `await` (network, file picker, shell) is unobserved.

**Minimal fix:** wrap each body in `try { … } catch (Exception ex) { Notifications.Show(…, ex.Message, error: true); }`.

## 6. BACKLOG — the tool↔wheel guard is one-directional

`tests/FileAction.Tests/ToolDispatchTests.cs` proves every registered `IToolWindowSpec.Id` has an
operation with the same id that implements `IInteractiveTool`, and that operation ids are unique. It does
**not** prove the reverse: an operation constructed in `WheelController.BuildOperations()`
(`Wheel/WheelController.cs:88–125`) whose id has no registered tool would be shown on the wheel and do
nothing when clicked. It also does not check `WheelKind`, so a tool operation declared on the wrong wheel
would be listed but misplaced.

The registration path that can still fail silently today: add `XOperation` to `BuildOperations()` and
forget `ToolRegistry.Register` (or the reverse order), or register a tool whose `Applies` never matches —
the guard is green, the window is unreachable.

**Minimal fix:** extend the test with the inverse assertion
(`BuildOperations().Where(o => o is IInteractiveTool)` ids ⊆ `ToolRegistry.All` ids) and a
`WheelKind.Tools` check for each tool operation. Two lines of LINQ in the existing test.

## 7. BACKLOG — duplicated control builders in the shared kit

`UI/Controls.cs:90–133` (`Segmented`) and `UI/Tools/ToolControls.cs:142–193` (`Chips`) implement the same
pill row with different metrics (`Padding 12,5,12,6` vs `11,5,11,6`; label `12.5` vs `12`; pill margin
none vs `0,0,4,0`). Cards differ the same way: `Controls.Card` pads `18,14,18,14` (`:83`),
`ToolControls.Card` pads `12,10,12,10` (`:54`).

**Minimal fix:** delete `Segmented` and route its two call sites through `Chips`, or make one of them a
thin wrapper. This is the mechanical cause of the typography/spacing drift reported in
`docs/audit/UX-AUDIT.md` F-16.

## 8. BACKLOG — duplicate `tools.ocr` operation id in the solution

`src/FileAction.App/Ocr/OcrOperation.cs:29` and `src/FileAction.Linux/TesseractOcr.cs:15` both declare
`: base("tools.ocr", WheelKind.Tools, …)`. Only the App one is shipped on Windows (the Linux project is
not in the Windows build), so there is no runtime duplicate today, but two assemblies in one solution
owning one wheel id will collide the first time a cross-platform build includes both.
The Linux file also uses the obsolete two-argument `T(czech, english)` overload (`Loc.T(string,string)`),
which is a migration leftover.

**Minimal fix:** give the Linux build a distinct id (`tools.ocr.linux`) or move the shared OCR operation
into Core behind an interface. Track only; no 2.4.0 change.

## 9. BACKLOG — `OcrOperation` blocks on an async convert

`src/FileAction.App/Ocr/OcrOperation.cs:110` calls
`ImageBackend.ConvertAsync(...).GetAwaiter().GetResult()`. If this runs on the UI thread it freezes the
window for the duration of the conversion; if it runs inside the job's `Task.Run`
(`JobRunner.cs:118`) it is harmless. The operation also does this inside an OCR pipeline that is already
async, so the sync-over-async is avoidable either way.

**Minimal fix:** `await ImageBackend.ConvertAsync(...)` and make the enclosing method async (it is
already on an async path); confirm the caller's thread before treating this as more than BACKLOG.

## 10. no finding — Layering `Core` ↔ `App` ↔ Shell is clean

`src/FileAction.Core/FileAction.Core.csproj` targets plain `net10.0` with no WPF/Shell reference, and a
content scan of every `src/FileAction.Core/**/*.cs` for `System.Windows`, `System.Drawing`,
`FileAction.App` and `FileAction.Shell` returns **zero** hits. The one App-shaped thing in Core is
`ToolSpecLayout`/`MediaSize` (plain records used to communicate window geometry), which is data, not a
dependency. `UI` depends on Core; Shell depends on Core; nothing depends back upward.
The one place App reaches into a sibling's internals is `WheelController` constructing
`UI.Tools.<Group>.<Op>Operation` (`Wheel/WheelController.cs:88–125`) — deliberate, and the subject of
finding 6.

## 11. no finding — partial outputs are handled by construction, not by discipline

`Core/OutputPaths.cs:20–48` is the house pattern: `ProduceAsync` writes `.<name>.<8hex>.part.<ext>`,
verifies the file exists, moves it to a unique final name, and deletes the temp in a `finally`
(`TryDelete` swallows `IOException`/`UnauthorizedAccessException`). `Dpi`-independent and used by the
media backends (`OfficeBackend.cs:82`, `PdfBackend.cs:170`, `VideoToolRunner.cs:111` all move from a
temp path to `final`). Two cancellation tests assert the property directly:
`tests/FileAction.Tests/AudioFunctionTests.cs:213` (`Cancel_mid_run_leaves_no_partial_output`) and
`LockSafetyTests.cs:1231` (`Cancel_mid_run_leaves_the_source_and_no_output`). A crash between move and
notification can leave a complete file, never a truncated one.

## 12. no finding — Lock/Protect staging is same-volume and verified before it replaces anything

`Core/Lock/LockPaths.cs:29–35`: the work file is `<target>.dwenc.tmp` **next to the final name**, and the
folder-restore staging path is likewise inside the destination directory — so every `File.Move`
(`LockService.cs:213`, `:672`) is same-volume and cannot hit the cross-volume `IOException`.
The container is verified before the move (`LockService.cs:212 VerifyContainer(...)`) and the move never
overwrites (`:213` comment: fail closed). Recovery is journal-driven (`Core/Lock/LockJournal.cs`, atomic
`temp + File.Move` at `:76`, `:412`) with staging under `%TEMP%/DropWheel/restore` only for the recovery
path (`LockGuard.cs:29`), and the plaintext is removed only after the verified container is in place
(`LockService.cs:673–676`). Shell side effects are confined to `LockIconCache`/`ShellIcon`, which write
their own file via temp+move (`LockIconCache.cs:144–146`, `ShellIcon.cs:68`) rather than touching the
registry directly.

## 13. no finding — startup does not build UI

The 26 tools register through `[ModuleInitializer]` with a singleton spec (`XTool.Instance = new()`), and
`ToolRegistry.Register` only appends to a list (`UI/Tools/ToolRegistry.cs:15–19`). Views are built in
`IToolWindowSpec.Create(...)`, which is only called from `ToolWindow`'s constructor. `Theme`'s statics are
frozen brushes/fonts plus one registry read (`UI/Theme.cs` `ReadSystemDark`). `WheelController`'s
`BuildOperations()` constructs stateless `Operation` objects (their constructors only call `base(...)` with
strings — verified across all 26). Nothing eagerly rasterises, no window is created before a user action.

## 14. no finding — strings, theming and DPI are structurally sound

`en.json`/`ar.json` are 1095 keys each, embedded as fixed logical names
(`FileAction.Core.csproj` `EmbeddedResource`), loaded once, with parity enforced by
`LocalizationTests` (key-set equality, no empty values) and by the append-only gate added on
`audit/l10n`. DPI is read per window (`ToolWindow.cs:421 GetDpiForWindow`) and converted into DIP rather
than assumed 96; no fixed pixel size is used for the card (the sizer works in DIP and clamps to the work
area). Theming is one frozen palette record per mode (`Theme.cs`), which is exactly why finding 2's
global mutable override is the only shared-state hazard.

---

## What I would do before the tag

Nothing here blocks 2.4.0. If exactly one item is taken before the tag, take **finding 2** (serialize
`Theme` mutating tests): it is six lines in `TestSetup.cs`/attributes and it removes the only known
source of red CI on an otherwise green tree, which is what the release gate actually depends on.
Findings 1, 3 and 4 are small, single-file fixes suitable for 2.4.1; 5–9 are backlog.

*Static review of `8b28758`; no executable was run by this lane. Where a thread context could not be
confirmed the finding is graded BACKLOG and says so.*
