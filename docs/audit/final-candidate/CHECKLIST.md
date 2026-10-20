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
| 3 | **26-tool reachability** — all 26 registered ids are dispatchable | `shipped-self-test` artifact → `ok tools/crop (26 registered)`; the `ui-coverage` report lists **26 distinct `tool:tools.<id>` ids**, each in `en` **and** `ar`; source count `ToolRegistry.Register(...)` = 26 (26 files under `src/FileAction.App/UI/Tools/`); the wheel wiring is `WheelController.BuildOperations()` (`src/FileAction.App/Wheel/WheelController.cs:88-116`). **Note, so nobody cites a test that does not exist:** there is **no `ToolDispatchTests`** class anywhere in the tree (`git grep ToolDispatch` → 0 hits at `bc204c4`). The dispatchable-in-both-cultures proof is the 26 ids in the `ui-coverage` artifact, not a unit-test row | `tools` | ☐ |
| 4 | **en+ar RTL coverage** — `ui-coverage` artifact, and a FAIL row really fails the job | `ui-coverage` artifact → `ui-coverage.txt`: `surfaces: 114 rows, pass <N>, skip 0, fail <M>` and `keys: 1094 English / 1094 Arabic`; **the failure-fails-the-job proof is run `39377633125`** (`audit/selftest`, `f3ff39e`): `Build + test` = failure, step *Self-contained UI self-test (shipped publish)* = failure, log `self-contained --self-test failed with 1` after `FAILED: … UI coverage (en+ar) failed for 1 surface(s)` — that run failed on `tool:tools.audiobleep (ar)` *"Arabic text forced LTR"* | `uicoverage` | ☐ |
| 5 | **No raw string keys in the shipped build** — four language/theme passes, 1094 keys each, zero uppercase `FAILED:` | `shipped-self-test` artifact → `self-test.txt`: `ok en/localization: 1094 keys (English)`, `ok en-light/localization: 1094 keys (English)`, `ok ar/localization: 1094 keys (Arabic)`, `ok ar-light/localization: 1094 keys (Arabic)`, `ok settings.json file load/save`, and **zero case-sensitive `FAILED:` lines** (the case-insensitive match `skipped ocr (failed: …)` is a skip, not a failure) | `rawkeys` | ☐ |
| 6 | **Lock badge/icons present** — frames committed, embedded and exercised by the packaged build | tree: `src/FileAction.App/Assets/Lock/*.ico` = **7 frames** (`locked-document`, `locked-folder`, `locked-badge`, `locked-archive`, `locked-image`, `locked-other`, `locked-video`), embedded via `src/FileAction.App/FileAction.App.csproj:66` (`LogicalName=FileAction.App.Lock.*`), loaded by `LockIcons.Asset` (`src/FileAction.App/Shell/Protection/LockIcons.cs:20-22,30,39-42`, frame sizes 16/24/32/48/64/256); committed previews `docs/screenshots/lock-badge/` = **8 PNGs + README**; packaged build: the self-test log must contain `ok tools/lock` **and** no `LockIcons`/`ShellIcon`/`SHCreateItemFromParsingName` exception — run `39380479876` is the counter-example (it crashed there) | `lockicons` | ☐ |

**Claim freeze still applies** (`docs/audit/RELEASE-AUDIT.md` §2.2): a PASS on gate 4 means the gate is
green, not that every Arabic string is perfect — quote the report's own `pass`/`fail` counts. If gate 4
ran on another branch because the `ui-coverage` step is not on `main` yet, the record must say which
branch and run, and the result counts as **PASS with caveat**, never as a release-commit PASS.

## Current state (last checked: `main` @ `bc204c4`, 2026-10-20 09:5x +03:00)

| # | state | why |
|---|---|---|
| 1 | **BLOCKED** | the `package` job has not completed on `main` since the 2.4.0 bump, so there is no smoke log at 2.4.0 |
| 2 | **BLOCKED** | source half is PASS (`<Version>2.4.0</Version>`, `AppVersion "2.4.0"`); no `DropWheel-Setup-2.4.0` artifact exists, so the packaged half is unproven |
| 3 | **BLOCKED** | needs the `shipped-self-test` artifact from that same 2.4.0 run; the 26 ids are demonstrable from `ui-coverage` already |
| 4 | **PASS with caveat** | `113/114` at `f3ff39e`; the gate is red on one Arabic string and the `ui-coverage` step is **not** on `main` yet (`git show origin/main:.github/workflows/ci.yml` → no `ui-coverage`) |
| 5 | **PASS at 2.3.0 only** | run `39375412602` shows 1094 keys ×4 and zero `FAILED:`; must be re-read on the 2.4.0 run |
| 6 | **BLOCKED / partly PASS** | tree side PASS (7 embedded `.ico`, 8 committed PNGs); packaged side is the failing assertion on `main` right now |

**Why nothing above is dispatched:** a normal push to `main` already runs the `package` job
(`.github/workflows/ci.yml:151`). The blocker is `main` itself — run `39380479876` fails the shipped
self-test in `LockIcons.PreviewForPath` → `ShellIcon.SHCreateItemFromParsingName`
(`System.ArgumentException: Value does not fall within the expected range`), so `Package` is skipped.
