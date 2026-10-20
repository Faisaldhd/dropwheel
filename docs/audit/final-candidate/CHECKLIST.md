# Final-candidate verification — DropWheel 2.4.0

Pre-staged so it can run **the minute `main` is green**. Nine minutes of `gh` queries, no local build,
no exe, no installer, no signing.

- **Runner:** `pwsh ./docs/audit/final-candidate/verify.ps1`
- **Contract:** read-only. It calls `gh`, reads files already in the tree, downloads CI artifacts into
  `_local/tmp/finalcheck` (git-ignored) and deletes them unless `-KeepArtifacts`. It never starts a
  process from the repository: its own first action is a self-scan that refuses to run if the script
  text contains an execution primitive (see `Assert-ReadOnly`).
- **Exit codes:** `0` every gate PASS · `1` a gate FAILED · `2` **not yet provable** — no run carries
  the 2.4.0 artifacts (this is the current state while `main` is red).

## How to run it

```powershell
# auto-pick the newest green main run that carries DropWheel-Setup-2.4.0
pwsh ./docs/audit/final-candidate/verify.ps1

# pin a specific run, keep the downloaded artifacts for the record
pwsh ./docs/audit/final-candidate/verify.ps1 -RunId <run> -KeepArtifacts

# rehearse against another branch (e.g. while the ui-coverage step is not on main)
pwsh ./docs/audit/final-candidate/verify.ps1 -Branch audit/selftest
```

The script declares `#Requires -Version 5.1`, so it runs under both PowerShell 7 (what CI uses,
`shell: pwsh`) and Windows PowerShell 5.1:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\docs\audit\final-candidate\verify.ps1
```

**Execution policy.** On a machine locked to `AllSigned` by Group Policy, the per-process
`-ExecutionPolicy Bypass` is overridden and the script will not start — that is the machine, not the
script, and the fix is to run it where policy allows scripts (the CI runner does) or to use the `gh`
commands in the table below by hand. Every gate is expressed as a plain `gh`/artifact read for exactly
that reason; the script is a convenience, never the only way to check a gate.

**What was validated here, and what was not.** This lane could not execute the script on the owner's
laptop (Group-Policy `AllSigned`, and only Windows PowerShell 5.1 is installed — no `pwsh`). It was
validated by: a full PowerShell parser pass (0 syntax errors), a simulation of its own read-only guard
(exactly one invocation in the file, `gh`; no execution primitive), and live checks of the three `gh`
API shapes it relies on (`contents?ref=<sha>` base64 decode → returned `<Version>2.4.0</Version>`;
`runs/<id>/artifacts?per_page=100` → 6 artifact names; `run view --job --log` → the package log). Its
behaviour on a green 2.4.0 run is therefore unproven end-to-end — treat the first real run as a smoke
test of the script too, and read its per-gate evidence rather than its exit code alone.

`-RunId` skips discovery. Without it, the script walks the newest **completed, successful** runs on the
branch and picks the first one that owns an artifact named `DropWheel-Setup-2.4.0`. If none does, it
prints exactly which runs it looked at and exits `2` — it does **not** guess and does **not** dispatch.

## Gates

Fill the PASS/FAIL cell in the run record below (the script prints the same rows). Every row names the
precise artifact or log line, so a PASS is never a matter of opinion.

| # | Gate | Exact evidence to read | Check id | PASS/FAIL |
|---|---|---|---|---|
| 1 | **Installer clean-profile smoke** — silent install exit 0, silent uninstall exit 0, HKCU `Run` value + Start-menu + Send-to + both context-menu keys gone, program folder gone, settings retained, **and no HKLM uninstall entry** | `gh run view <run> --job <package-job> --log`, lines from `scripts/test-installer.ps1`: `ok: the installer exited with 0`, `ok: the uninstaller exited with 0`, `ok: the HKCU Run value is gone`, `ok: the Start-menu shortcut is gone`, `ok: the Send-to shortcut is gone`, `ok: the file context-menu key is gone`, `ok: the folder context-menu key is gone`, `ok: the program folder is gone`, `ok: a silent uninstall keeps the settings folder`, and the verdict `PASS: silent install, shell entries, silent uninstall and settings retention verified.` HKLM: the smoke test prints `uninstall entry: <key>` only for keys that exist (`scripts/test-installer.ps1:243-245`), so **PASS requires that no printed entry starts with `HKLM:`** | `smoke` | ☐ |
| 2 | **2.4.0 everywhere** — csproj, installer define, asset names, packaged diagnose, About string, README, CHANGES | `gh api repos/Faisaldhd/dropwheel/contents/src/FileAction.App/FileAction.App.csproj?ref=<sha>` → `<Version>2.4.0</Version>`; the same for `installer/DropWheel.iss` → `#define AppVersion "2.4.0"`; artifact `DropWheel-Setup-2.4.0` contains **`DropWheel-2.4.0-Setup.exe`** and **`DropWheel-2.4.0-portable-win-x64.zip`**; `diagnose` artifact → `version:    2.4.0.`; About window: `settings.about.version` present in **both** `src/FileAction.Core/Strings/en.json` and `ar.json` and fed by `Assembly.GetName().Version` (`src/FileAction.App/UI/SettingsWindow.cs:477-483`) — the diagnostic `version:` and the About line come from the same assembly version, so the packaged `2.4.0.x` proves the About line renders `2.4.0`; `README.md` mentions `2.4.0`; `CHANGES.md` has a `## 2.4.0` section | `version` | ☐ |
| 3 | **26-tool reachability** — all 26 registered ids are dispatchable | `shipped-self-test` artifact → `ok tools/crop (26 registered)`; **`tests/FileAction.Tests/ToolDispatchTests.cs` → `EveryRegisteredToolWindowIsReachableFromTheWheel`** (iterates `ToolRegistry.All`, requires a dispatchable `IInteractiveTool` operation with a matching id) and `WheelOperationIdsAreUnique` (duplicate ids fail), both `Passed` in `test-results/*.trx` — this test **fails CI when a registered tool is unwired** (it is why #42/#31 exist); the `ui-coverage` report lists **26 distinct `tool:tools.<id>` ids**, each in `en` **and** `ar`; source count `ToolRegistry.Register(...)` = 26; wiring is `WheelController.BuildOperations()` (`src/FileAction.App/Wheel/WheelController.cs:88-116`) | `tools` | ☐ |
| 4 | **en+ar RTL coverage** — `ui-coverage` artifact, and a FAIL row really fails the job | **This is a release-commit gate: the step is on `main`** (`.github/workflows/ci.yml:250-270` — asserts the report exists, contains no `| FAIL |` row, has ≥ 52 tool/culture rows and an explicit "Not covered by this run" section, then uploads `ui-coverage/**`). Read from that run's `ui-coverage` artifact: `ui-coverage.txt` → `surfaces: 114 rows, pass 114, skip 0, fail 0` and `keys: 1094 English / 1094 Arabic`; `ui-coverage.md` → no `| FAIL |` rows, 52 tool/culture rows; **90 PNGs**. Current green proof: run **`39379321230`** (main lineage `32757e9`) — `114 surfaces · 0 failure(s)`, and the audiobleep Arabic leak is fixed in code (`AudioBleepView.BuildLanes` pins the lane grid LTR). **Gate-strength proof that a FAIL row fails the job:** run `39377633125` went red at *Self-contained UI self-test (shipped publish)* with `self-contained --self-test failed with 1` after `FAILED: … UI coverage (en+ar) failed for 1 surface(s)`. **Remaining honest caveat: 200 % DPI is not covered** — the report's own section says fixed-size dialogs are only size-checked at 1280x800 @100 % and that 150 %/200 % is asserted for tool windows only | `uicoverage` | ☐ |
| 5 | **No raw string keys in the shipped build** — four language/theme passes, 1094 keys each, zero uppercase `FAILED:` | `shipped-self-test` artifact → `self-test.txt`: `ok en/localization: 1094 keys (English)`, `ok en-light/localization: 1094 keys (English)`, `ok ar/localization: 1094 keys (Arabic)`, `ok ar-light/localization: 1094 keys (Arabic)`, `ok settings.json file load/save`, and **zero case-sensitive `FAILED:` lines** (the case-insensitive match `skipped ocr (failed: …)` is a skip, not a failure) | `rawkeys` | ☐ |
| 6 | **Lock badge/icons present** — frames committed, embedded and exercised by the packaged build | tree: `src/FileAction.App/Assets/Lock/*.ico` = **7 frames** (`locked-document`, `locked-folder`, `locked-badge`, `locked-archive`, `locked-image`, `locked-other`, `locked-video`), embedded via `src/FileAction.App/FileAction.App.csproj:66` (`LogicalName=FileAction.App.Lock.*`), loaded by `LockIcons.Asset` (`src/FileAction.App/Shell/Protection/LockIcons.cs:20-22,30,39-42`, frame sizes 16/24/32/48/64/256); committed previews `docs/screenshots/lock-badge/` = **8 PNGs + README**; packaged build: the self-test log must contain `ok tools/lock` **and** no `LockIcons`/`ShellIcon`/`SHCreateItemFromParsingName` exception — run `39380479876` is the counter-example (it crashed there) | `lockicons` | ☐ |

**What a PASS on gate 4 does and does not mean.** It means the coverage gate is green on the release
commit: every window the app registers was rendered and string-checked in `en` and `ar`, no `| FAIL |`
row exists, and the job would have gone red if one did. It does **not** mean RTL is perfect — quote the
report's own `pass`/`fail` counts, and keep the two limits the report itself names: **200 % DPI is not
covered**, and native `MessageBox` surfaces plus `UpdatedWindow` are not rasterised. If the
`ui-coverage` artifact ever has to come from a non-release run, the record must say which run and the
result is **PASS with caveat**, never a release-commit PASS — `verify.ps1` prints that caveat itself
whenever the artifact's run id differs from the release run id.

## Current state (last checked: `main` @ `8b28758`, 2026-10-20 ~10:2x +03:00)

`main` has moved past the red commit: the `ShellIcon`/`LockIcons` crash was fixed by #42, the version
bump (#34) and the dispatch test (#31) are merged, and the `ui-coverage` step plus its upload are now
part of `main`'s CI (#30/#37). What is still missing is only the **packaged 2.4.0 artifact**, because
no `main` run has finished the `package` job since the bump.

| # | state | evidence |
|---|---|---|
| 1 | **BLOCKED — waiting on the 2.4.0 package run** | the `package` job must complete on a `main` commit that carries the 2.4.0 bump; then its smoke log is readable |
| 2 | **source PASS, packaged BLOCKED** | `main` @ `8b28758`: `<Version>2.4.0</Version>` + `#define AppVersion "2.4.0"`; no `DropWheel-Setup-2.4.0` artifact has existed yet |
| 3 | **PASS (test + tree), packaged self-test pending** | `ToolDispatchTests.EveryRegisteredToolWindowIsReachableFromTheWheel` and `WheelOperationIdsAreUnique` exist and run in CI; 26 ids in both cultures already shown by `ui-coverage` |
| 4 | **PASS — 114/114, 0 fail** | run `39379321230`: `surfaces: 114 rows, pass 114, skip 0, fail 0`, `keys: 1094 / 1094`, 90 PNGs, no `| FAIL |` row; caveat: 200 % DPI not covered (report's own section) |
| 5 | **PASS at 2.3.0, needs the 2.4.0 re-read** | run `39375412602`: 1094 keys × four language/theme passes, zero uppercase `FAILED:` |
| 6 | **tree PASS, packaged pending** | 7 embedded `.ico` (document/folder/badge present) + 8 committed PNG previews; the packaged assertion is `ok tools/lock` with no `LockIcons` exception in the 2.4.0 self-test log |

**What is deliberately not claimed:** the 2.4.0 setup/zip names, the 2.4.0 `--diagnose` line and the
installer smoke at 2.4.0. They are the first four things `verify.ps1` prints once a green `main` run
owns the artifact, and until then they are **unproven**, not "probably fine".

**Why nothing is dispatched:** a normal push to `main` already runs the `package` job
(`.github/workflows/ci.yml:151`). This lane never dispatches on `main` and never builds locally.