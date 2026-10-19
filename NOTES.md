# NOTES — 2.2.0 installer (branch `2.2.0-installer`)

Status: **done** for `installer/**` and the two scripts. Branch CI only builds and tests the app,
so the Inno Setup compile and the smoke test run in the **Package** job (see "CI steps" below).
No local build, publish, ISCC run or installer run was performed (AGENT_RULES).

## What changed

| File | Change |
| --- | --- |
| `installer/DropWheel.iss` | Rewritten for a **self-contained** win-x64 publish. No .NET detection, no download, no runtime install, no PowerShell helper. New output name, tasks page, upgrade/uninstall rules. |
| `installer/Arabic.isl` | **New, vendored**: unmodified copy of the Arabic translation that ships with Inno Setup 6.5+ (`Languages\Arabic.isl`, translator: nacer baaziz; header kept for attribution). Source file SHA-256 `D621121EBA68640CC9CB3E1E63593D9B5905C15AB6D1DCFA168DF603F52E0CAD`; UTF-8 without BOM, `LanguageID=$0401`, `RightToLeft=yes`. Git normalises its CRLF line endings on commit (Inno Setup accepts both). |
| `installer/DropWheel-ar.isl` | **New**: the five installer-only strings in Arabic (tasks group, the two task labels, the app comment, the uninstall question). Kept out of `DropWheel.iss` so the script stays pure ASCII (no BOM question). |
| `scripts/build-installer.ps1` | **New**: version from the csproj, self-contained trimmed publish, prune, licences, violet wizard BMPs, ISCC, 70 MB assert, size report. |
| `scripts/test-installer.ps1` | **New**: silent per-user install/uninstall smoke test with the assertions below. CI only; do not run it locally. |

### Installer behaviour

- Self-contained publish, so Setup has **no runtime check, no download and no elevation for a runtime**.
  `DefaultDirName={autopf}\DropWheel`, `PrivilegesRequired=lowest`,
  `PrivilegesRequiredOverridesAllowed=dialog commandline` (the dialog is kept; `/CURRENTUSER` and
  `/ALLUSERS` are accepted explicitly — Inno documents `dialog` as implying `commandline`, the value
  is listed anyway so the updater's switches can never regress),
  `WizardStyle=modern`, `MinVersion=10.0.17763`, `ArchitecturesAllowed=x64compatible`,
  `Compression=lzma2/ultra64`, `SolidCompression=yes`, `CloseApplications=yes`,
  `RestartApplications=no`, `SetupLogging=yes`, `OutputBaseFilename=DropWheel-{#AppVersion}-Setup`.
- `AppId` unchanged (`{{1AB9CDBE-C50D-484B-96ED-5D575D0806D2}`).
- Languages: English (`compiler:Default.isl`) and Arabic (`installer\Arabic.isl` + `DropWheel-ar.isl`, RTL).
- Tasks page: **Start with Windows** (HKCU `Run\DropWheel` = `"<app>\DropWheel.exe" --background`)
  and **Watch mouse and keyboard** (`DropWheel.exe --set-watch`, hidden) are **checked on a first
  install**; **desktop shortcut** is unchecked. Start-menu shortcut always. "Launch DropWheel" on
  the finish page (`skipifsilent`).
- Upgrades: same AppId, `UsePreviousTasks=yes` (choices are restored, not reset), settings kept.
  **`ApplyInstallDefaults`**: a *silent* upgrade (`/VERYSILENT`, i.e. the updater) does **not**
  re-apply the two defaults to an existing install, so it cannot re-enable autostart/watch that the
  user turned off in Settings. On a first install and in interactive installs the checkboxes decide.
- Uninstall removes: the HKCU Run value, both Explorer context-menu keys
  (`HKCU\Software\Classes\*\shell\DropWheel`, `...\Directory\shell\DropWheel`), the Send-to shortcut
  (`%APPDATA%\Microsoft\Windows\SendTo\DropWheel.lnk`), the Start-menu/desktop shortcuts and the
  program files. It kills a running `DropWheel.exe` first.
- Settings (`%APPDATA%\DropWheel` and `%LOCALAPPDATA%\DropWheel`, the latter includes the downloaded
  `tools\`) are kept: silent uninstalls always keep them, an interactive uninstall asks one Yes/No
  question (default: keep) and only a Yes deletes both folders. There is no unconditional
  `[UninstallDelete]` entry: "keep my settings" means keep the helper programs too.
- `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART` works; the updater's extra `/CLOSEAPPLICATIONS` is the
  same behaviour as `CloseApplications=yes`.

### `scripts/build-installer.ps1`

1. Reads `<Version>` from `src/FileAction.App/FileAction.App.csproj` (fails on a missing/invalid value).
2. Publishes `-c Release -r win-x64 --self-contained true -p:PublishTrimmed=true -p:TrimMode=partial -o release/selfcontained`.
3. Removes `*.pdb` and `createdump.exe`, copies `LICENSE`, `THIRD-PARTY-NOTICES.md` and `licenses\`
   next to `DropWheel.exe`, and fails if any of them is missing.
4. Generates `release\wizard\wizard-large.bmp` (164x314) and `wizard-small.bmp` (55x55) from
   `branding\DropWheel-1024.png` with System.Drawing (violet gradient + halo + logo). If
   System.Drawing or the logo is unavailable it warns and lets Inno use its own images.
5. Finds `ISCC.exe` (Program Files, Program Files (x86), `%LOCALAPPDATA%\Programs`, Chocolatey),
   requires Inno Setup **6.3+** (`x64compatible`), and runs `ISCC /DAppVersion=<ver>`.
6. Asserts `release\DropWheel-<ver>-Setup.exe` exists and is **<= 70 MB** (parameter `-MaxSetupMB`),
   prints publish size/file count and setup size, and adds a size table to `$GITHUB_STEP_SUMMARY`.

## CI steps (exact)

In `.github/workflows/ci.yml`, **package** job, keep `Install Inno Setup` and replace the old
`Publish (framework-dependent)`, `Strip debug symbols` and `Build installer` steps with:

```yaml
      - name: Build the installer (self-contained, trimmed)
        shell: pwsh
        run: ./scripts/build-installer.ps1

      - name: Installer smoke test (silent install + uninstall)
        shell: pwsh
        run: ./scripts/test-installer.ps1
```

Then these existing steps must follow the new paths/name (all in the same job unless noted):

1. **Size report**: publish is now `release/selfcontained` and the setup is
   `release/DropWheel-$version-Setup.exe` (the script already prints/summarises both; this step can
   simply be deleted).
2. **Diagnose report**: `release/DropWheel/DropWheel.exe` no longer exists — use
   `release/selfcontained/DropWheel.exe`. This is the only runtime check of the *trimmed* build, so
   consider dropping `continue-on-error: true` for it.
3. **SHA256SUMS**: the setup glob is now `DropWheel-*-Setup.exe` (was `DropWheel-Setup-*.exe`).
4. **Upload setup**: `release/DropWheel-*-Setup.exe` (and `release/DropWheel-Setup.exe` when the
   stable alias below is added).
5. **Release job**: same glob change for the setup asset (`DropWheel-*-Setup.exe`).
6. **Stable alias for the website** (requested by update-core): after the build step add
   `Copy-Item "release/DropWheel-$version-Setup.exe" "release/DropWheel-Setup.exe"` and include it in
   `SHA256SUMS.txt`, the upload and the release (that makes 3 binaries + `SHA256SUMS.txt`).
7. The **Portable zip** step is unchanged (it still publishes untrimmed, self-contained, to
   `portable/DropWheel`) — say so if you want parity with the trimmed installer publish.

## Requests from installer

- **`.github/workflows/ci.yml`** — apply the six edits above. I did not edit it.
- **`README.md`** (lines ~74/76) and **`README.ar.md`** — the setup asset is
  `DropWheel-<version>-Setup.exe`, not `DropWheel-Setup-<version>.exe`
  (`UpdateConfig.SetupAssetName` already expects the new name).
- **`build.cmd` / `release.cmd`** — stale: they publish framework-dependent to `release\DropWheel`,
  install/check the .NET runtime and print `DropWheel-Setup-<ver>.exe`. Either delete them or point
  them at `scripts\build-installer.ps1`; they are not in my ownership.
- **`CHANGES.md`** — the 2.2.0 section needs a line for "setup is now self-contained (no .NET
  install), <= 70 MB"; sizes are only known after the first Package run.
- **update-ui** (`--set-watch`) — contract the installer relies on: read settings, set
  `WatchGestures = true`, `Save()`, exit 0. It must be handled **before** the
  `SingleInstance.TryAcquire()` forwarding block in `Program.Main` (like `--unregister`), so it also
  works when DropWheel is already running. The `[Run]` entry uses `runhidden nowait`, so a switch
  that starts the UI instead of exiting would leave a stray window after installation.
- **signing owner** — `docs\signing.md` still describes signing `DropWheel-Setup-<ver>.exe`; the asset
  name changed (the signing step must pick up `DropWheel-<ver>-Setup.exe` / `DropWheel-Setup.exe`).
- **`scripts/verify-authenticode.ps1`** — no longer used by the installer (it only existed for the
  .NET runtime download check). Its owner can delete it.

## Limits and things to know before merging

- **Inno Setup 6.3+ is required** (`ArchitecturesAllowed=x64compatible`). CI installs the Chocolatey
  package, which is current 6.7.x; `build-installer.ps1` fails with a clear message below 6.3.
- **Arabic.isl provenance**: copied from the Inno Setup 6.5+ distribution (same file the compiler
  ships); it is not modified. All 296 messages of the current `Default.isl` are present (verified).
  The five DropWheel strings in `DropWheel-ar.isl` are new and should get a native review (installer
  wording only: "Additional tasks:", "Start with Windows", "Watch mouse and keyboard", the shortcut
  comment and the uninstall question).
- **No custom uninstall page**: the Inno Setup 6 uninstaller cannot add wizard pages
  (`CreateInputOptionPage` is Setup-only), and `CreateCustomForm` changed its signature in 6.6.0
  (width/height are constructor parameters now), so a form-based checkbox would be written against
  one compiler version only and cannot be compiled locally. "Remove my settings" is therefore a
  single Yes/No confirmation (default: keep) instead of a checkbox page. The required semantics are
  preserved: silent uninstall always keeps the settings (and the helper programs), interactive
  uninstall asks and defaults to keeping them.
- **Win-x64 publish is trimmed**: `PublishTrimmed` + `TrimMode=partial` with WPF is not supported by
  the .NET SDK (the csproj suppresses `NETSDK1196` with `_SuppressWpfTrimError`). Settings JSON is
  source-generated (`SettingsJson`), which is trim-safe, but the Package job's `--diagnose` run is
  the first real check of the trimmed app. If it fails, drop `-p:PublishTrimmed=true` in
  `build-installer.ps1` (one line; the setup will grow past 70 MB, so report before doing that).
- **Wizard images**: exactly 164x314 and 55x55 as specified. Inno Setup 6.6+ displays them in a
  202x386 / 58x58 area and scales them (aspect kept), so they are slightly enlarged but not distorted.
- **Branch CI does not exercise the installer**: the `Package` job runs only on `main`/tags. The
  first compile of the new `.iss` and the first smoke-test run therefore happen in that job, which is
  also the only place the setup size and the smoke-test PASS/FAIL exist (`build-installer.ps1` prints
  the size table and `test-installer.ps1` prints PASS/FAIL with the installer log tail on failure).
  Run the Package job once via `workflow_dispatch` after merging (or temporarily allow it on the
  branch) to get those two numbers before tagging.
- **Uninstall scope**: the Run value, context-menu keys and Send-to shortcut are per-user (HKCU) of
  the installing user, as the app creates them. An all-users install cleans the installing user's
  HKCU entries (a different user's entries are not touched, by design).
- **`--set-watch` is a cross-agent dependency**: with this branch alone the switch does not exist yet,
  so an install would start the app instead of applying the setting (the installer still completes;
  `nowait`). Merge `2.2.0-update-ui` (or whatever implements it) with or before this branch.

## Local verification performed (no build, no installer run)

- `DropWheel.iss`: pure ASCII (0 bytes > 0x7F), reviewed against the Inno Setup 6.7 docs for every
  directive used; `Arabic.isl` and `DropWheel-ar.isl` verified UTF-8 without BOM.
- Both PowerShell scripts: 0 parser errors (`System.Management.Automation.Language.Parser`).
- Wizard image code executed locally against `branding\DropWheel-1024.png`: produced 164x314 and
  55x55 24-bit BMPs (valid `BM` header) and the result was inspected visually (violet gradient, logo,
  halo) — the images are generated at build time, not committed.
- No `dotnet publish`, no `ISCC`, no setup, no uninstaller and no `DropWheel.exe` was run locally.
