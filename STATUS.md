# STATUS

NOW: 2.2.0 RELEASED — v2.2.0 tagged at 1336145 — release run 39252160456 green — 6 assets live — Pages download verified

## 2.2.0 — RELEASED (19 October 2026, approved by Claude)

DropWheel 2.2.0 is released: `v2.2.0` is tagged at `1336145`, the release job is green, and the six signed
assets are live on the release page. The owner approved the release after review. `REVIEW.md` was read but
not edited.

### Final evidence

| Check | Evidence |
|---|---|
| Code commit | `6e8e3e7` (all ten agent branches merged; code at `0173530`, this STATUS update is the head) |
| Main CI — build + test + package + installer smoke + screenshots | [run 39248890995](https://github.com/Faisaldhd/dropwheel/actions/runs/39248890995) — green on `6e8e3e7`; earlier [39247825190](https://github.com/Faisaldhd/dropwheel/actions/runs/39247825190) green on `7354ba2` |
| Release | `v2.2.0` tagged at `1336145`; [run 39252160456](https://github.com/Faisaldhd/dropwheel/actions/runs/39252160456) — build + test, package + installer smoke, release job all green |
| Tests | **192 / 192 passed**, 0 failed, 0 skipped (trx from [run 39245752210](https://github.com/Faisaldhd/dropwheel/actions/runs/39245752210)) |
| Setup | `DropWheel-2.2.0-Setup.exe` — **42.21 MB** (44,262,876 B), budget 70 MB |
| Portable zip | `DropWheel-2.2.0-portable-win-x64.zip` — **59.83 MB** (62,731,211 B) |
| Version | `<Version>2.2.0</Version>` in `src/FileAction.App/FileAction.App.csproj` |
| Changelog | `CHANGES.md` has a 2.2.0 section in English and Arabic |
| Release dry run | test tag `v0.0.0-test2` on `6b17d73` → [run 39247356095](https://github.com/Faisaldhd/dropwheel/actions/runs/39247356095) — exactly 6 assets (below); release and tag deleted afterwards |
| Old releases cleaned | v2.1.0 and v2.1.1 each now carry only their self-contained portable zip + `SHA256SUMS.txt`; old framework-dependent setup/zip and `diagnose.txt` are gone |
| Download page | <https://faisaldhd.github.io/dropwheel/> — HTTP 200, GitHub Actions Pages deploy, EN/AR, refreshed CI screenshots |
| v2.2.0 tag | **tagged at `1336145`**; release [v2.2.0](https://github.com/Faisaldhd/dropwheel/releases/tag/v2.2.0) |

### Release dry run — exactly six assets (v0.0.0-test2, deleted)

| Asset | Size |
|---|---|
| `DropWheel-2.2.0-portable-win-x64.zip` | 62,731,211 B |
| `DropWheel-2.2.0-portable-win-x64.zip.sig` | 88 B (base64 P-1363, 64-byte ECDSA P-256) |
| `DropWheel-2.2.0-Setup.exe` | 44,262,876 B |
| `DropWheel-2.2.0-Setup.exe.sig` | 88 B |
| `DropWheel-Setup.exe` (byte-identical stable alias) | 44,262,876 B |
| `SHA256SUMS.txt` | 484 B, covers the five other assets |

The release job verified every `.sig` against the embedded `UpdateKey.PublicKeyBase64` before upload
(fingerprint `9d6b35c5376d6decceced299829643456c52fee8c0662dd101d62d422f0bce02`).

### Per-agent evidence

| Agent | Branch / commit | CI | Result |
|---|---|---|---|
| release-clean | `2.2.0-release-clean` / `ee45c4e` | [39241050455](https://github.com/Faisaldhd/dropwheel/actions/runs/39241050455); dry run [39241072682](https://github.com/Faisaldhd/dropwheel/actions/runs/39241072682) | six-asset tag release job; old v2.1.0/v2.1.1 assets cleaned and sums regenerated |
| installer | `2.2.0-installer` / `bbae592` (+ `03060cf`) | [39243664937](https://github.com/Faisaldhd/dropwheel/actions/runs/39243664937) | self-contained Inno setup 42.21 MB; EN+AR RTL wizard; silent per-user install/uninstall smoke test in main CI |
| update-core | `2.2.0-update-core` / `b3eb918` | [39237764334](https://github.com/Faisaldhd/dropwheel/actions/runs/39237764334) | anonymous GitHub client, numeric SemVer, one-at-a-time download, dual SHA-256 + ECDSA verification |
| update-apply | `2.2.0-update-apply` / `390500e` | [39242036836](https://github.com/Faisaldhd/dropwheel/actions/runs/39242036836) | setup silent apply and portable folder replace/relaunch, settings kept, PE check + rollback; helper CI script |
| update-ui | `2.2.0-update-ui` / `bff4d55` | [39241246631](https://github.com/Faisaldhd/dropwheel/actions/runs/39241246631) | violet banner, tray item/balloon, check button, progress/cancel, app wiring |
| settings-tabs | `2.2.0-settings-tabs` / `77ba7f7` | [39241135558](https://github.com/Faisaldhd/dropwheel/actions/runs/39241135558) | General / Wheels / Quality / Integration / Updates / About; every toggle once; AppThemeMode alias |
| signing | `2.2.0-signing` / `c54d110` | [39241634480](https://github.com/Faisaldhd/dropwheel/actions/runs/39241634480) | key tooling, P-1363 sign/verify scripts, docs; real key untouched |
| download-page | `2.2.0-download-page` / `867f13a` | [39242692949](https://github.com/Faisaldhd/dropwheel/actions/runs/39242692949) | one EN/AR page, RTL, dark violet, no trackers/fonts, real CI screenshots; Pages deploy |
| strings | `2.2.0-strings` / `9b518dc` | [39238353781](https://github.com/Faisaldhd/dropwheel/actions/runs/39238353781) | **346 / 346** keys in en+ar, 57 new each, placeholder/parity checks pass |
| tests | `2.2.0-tests` / `e2015c9` | [39242248118](https://github.com/Faisaldhd/dropwheel/actions/runs/39242248118) (expected red pre-merge) | six new files: update-core, settings defaults, policy, six tabs, installer spec, new strings — 192/192 on main |

Lead integration commits: `bedad4b` (package job), `b535f4c` (installer spec), `af1fd8d` (WinRT/OCR/desktop
task), `6b17d73` (checked-task count), `86fafcd` (CI screenshots), `b2be1c0` (README), `7354ba2`
(build/release scripts).

### Released assets and download verification

The live v2.2.0 release page carries exactly six assets:

| Asset | Size |
|---|---|
| `DropWheel-2.2.0-Setup.exe` | 44,270,484 B (42.22 MB) |
| `DropWheel-2.2.0-Setup.exe.sig` | 88 B, verified with the embedded key |
| `DropWheel-Setup.exe` (stable alias, byte-identical) | 44,270,484 B |
| `DropWheel-2.2.0-portable-win-x64.zip` | 62,731,203 B (59.83 MB) |
| `DropWheel-2.2.0-portable-win-x64.zip.sig` | 88 B |
| `SHA256SUMS.txt` | 484 B, covers all five other assets |

Download-page check (live): <https://faisaldhd.github.io/dropwheel/> shows Version 2.2.0 and its button
targets `https://github.com/Faisaldhd/dropwheel/releases/latest/download/DropWheel-Setup.exe`. Downloading
that URL returned HTTP 200, **44,270,484 bytes**, product/file version **2.2.0**, SHA-256
`6884c3b2f904f09cc95a9d8a87f0041c95618dc3132a2041438de912f2a88332` (matches both `DropWheel-Setup.exe` and
`DropWheel-2.2.0-Setup.exe` in `SHA256SUMS.txt`), and its ECDSA P-256 signature verified against
`UpdateKey.PublicKeyBase64` (64-byte P1363 signature, valid).

### Known limitations / review notes

- **v2.2.0 is live.** The Pages button now downloads the 2.2.0 setup through the stable alias; the old
  v2.1.x framework-dependent setups were intentionally removed.
- Authenticode signing is still pending (SignPath); auto-update signatures are ECDSA P-256 and independent
  of Authenticode.
- The uninstaller asks a default-keep Yes/No question (“Remove my settings…”) rather than a checkbox, because
  Inno Setup 6 uninstallers cannot host wizard pages; silent uninstall always keeps settings and tools.
- Arabic installer strings (vendored `Arabic.isl` + five custom messages) should get a native review; the
  app’s Arabic tables already pass the 346-key parity/placeholder tests.
- `--self-test` renders 70 screenshots in four language/theme folders; the English `settings.png`,
  `wheel-actions.png` and `jobs.png` were copied into `docs/site/assets` and are in the `screenshots` artifact.

## History

NOW: R-2 done (setup 19.71 MB, SDK.NET projection gone), R-22/R-16/R-18/R-14/R-17 done — CI build + tests green — next: R-20, R-15 (phase-2 keys), R-5 screenshots

## Session 3 results (CI run 39218574689 and the ones before it)
- **R-2 complete, measured in CI:** publish **63.1 MB / 40 files** (was 87.6 MB), setup **19.71 MB**
  (was 23.35 MB; target ≤ 30 MB), `Microsoft.Windows.SDK.NET.dll` **absent**. The app targets
  `net10.0-windows` and reaches OCR/share through `Native\WinRt*.cs` raw COM; `SupportedOSPlatformVersion`
  removed (NETSDK1135). The installer still detects/installs the .NET 10 Desktop Runtime and verifies its
  Authenticode signature.
- **R-22 done:** prompts moved to `docs\prompts\`, agent worktrees/branches removed, `build.cmd` +
  `release.cmd` use `..\_local\sdk`, and the installer reads `..\release\DropWheel` (the old
  `..\..\release` pointed outside the repo and could never have found the CI publish output).
  Workspace root: `DropWheel\` (repo) + `_local\` (sdk/testdata/old-builds/old-notes) + `README.md`.
- **R-16 done:** `ToolInstallException` names the tool, folder and OS error (en+ar keys
  `tool.installFailed` / `tool.installAccessDenied`); the install folder falls back to
  `%LOCALAPPDATA%\Programs\DropWheel\tools` then TEMP after a write probe; `ToolLocator` searches the
  fallback; failures are logged and shown on the job card. 3 tests pin the mapping (never blames the
  output folder).
- **R-18 verified:** the diagnostics code compiles and CI runs `--diagnose`; that step now waits properly
  (`Start-Process -Wait`) because a GUI exe does not set `$LASTEXITCODE` under `&`.
- **R-14/R-17:** `Assets\` carries the reviewer's regenerated icons (9 frames 16–256, bold tray icons);
  `IconAssetsTests` pins SHA-256 parity with `branding\`, so CI fails on drift.
- **Fixtures:** `tests\FileAction.Tests\TestData.cs` prefers `_local\testdata\TestFiles` (or
  `DROPWHEEL_TESTDATA`), copies them into the sample folder, and restores the **read-only** attribute on
  `read-only.jpg`; CI without `_local` keeps generating samples.
- CI: <https://github.com/Faisaldhd/dropwheel/actions> — build+test green on `d1d1f5e` (run 39218574689);
  the package job built the installer and reported the sizes above.

## CI (R-19) — the only place anything is built or run
- Workflow: `.github/workflows/ci.yml` on `windows-latest`, triggered by push, PR and `workflow_dispatch`.
  Jobs: **Build + test** (restore → build Core/App/Tests → `dotnet test`, FFmpeg + Pandoc installed first),
  **Package** (framework-dependent publish → Inno Setup via choco → size report in the job summary →
  installer + portable zip + `--diagnose` report as artifacts), **Screenshots** (manual: `--self-test`, uploads PNGs).
- Runs: `https://github.com/Faisaldhd/dropwheel/actions` — latest green: build + 57 tests on `95a8630`.
  Earlier runs: 39161623222 (cancelled: setup-dotnet hang), 39161722918 (test failed: stale Czech message match,
  fixed in 59af273), 39161967852 (package: ISCC lookup bug, fixed in 2b04c7b).
- README carries the CI badge. **Nothing built here is ever shipped** (see R-21).
- Local rule honoured: no DropWheel.exe, installer, `--self-test`, tool downloads. Code + git only.

## R-21 confirmed (root cause of the owner's crashes)
`icacls` on a locally built exe shows `Mandatory Label\Low Mandatory Level`: anything built inside the DSH
sandbox starts low-integrity, cannot write `%APPDATA%`/`%TEMP%`, and cannot receive input. Hence:
- releases only from GitHub Actions;
- the app now refuses to start at low integrity with a clear message (`Integrity.IsLow`, exit code 3);
- `--diagnose` reports the integrity level.

## R-18 fixed (crash safety, logging, hooks, diagnose)
- `Diagnostics\AppLog.cs`: log falls back `%APPDATA%` → `%LOCALAPPDATA%` → `%TEMP%` → next to the exe; never throws.
- `Diagnostics\CrashGuard.cs`: AppDomain, Dispatcher and TaskScheduler handlers; writes `crash-<time>.txt`
  (report + 80-line log tail), copies details to the clipboard, shows "DropWheel stopped: <reason>".
- `Diagnostics\Integrity.cs`: `GetTokenInformation(TokenIntegrityLevel)`.
- `Diagnostics\Diagnose.cs`: `DropWheel.exe --diagnose [file]` → version, OS, integrity, per-folder write test,
  hook install test with Win32 error, helper tools (incl. winget/dotnet on PATH), environment, log tail; opens it.
- `GlobalInput` exposes `HooksInstalled` / `HookError`; startup logs settings, language, hooks + Win32 error,
  tray, hotkey. If the hooks fail, the app says so in a warning dialog **and** a tray balloon.
- Startup steps are logged; `Program.Main` initialises the log before anything else.

## R-14 / R-17 fixed
`branding\` was regenerated by the reviewer (9-frame `DropWheel.ico` 16–256, bolder tray icons). Copied into
`src\FileAction.App\Assets\` in `de903bd`; `IconAssetsTests` already pins the SHA-256 parity, so drift fails CI.
CI now builds the installer from these icons; REA verification of the CI exe is in the reviewer's hands.

## R-2 packaging merged (6552fe3), sizes measured before the stop order
| Artifact | Before | After |
|---|---|---|
| publish (self-contained) | 219.3 MB / 293 files | – |
| publish (framework-dependent) | – | **87.6 MB / 42 files** |
| setup exe | 63.9 MB | **23.35 MB** (target ≤ 30 MB) |
| installed | – | ≈ 88 MB app + 0.42 MB licences |
Installer: .NET 10 Desktop Runtime detection (HKLM/HKCU `sharedfx\Microsoft.WindowsDesktop.App`, `dotnet --list-runtimes`),
one explanatory message, download via `DownloadTemporaryFile`, Authenticode verification
(`scripts\verify-authenticode.ps1`), elevated `/install /quiet /norestart`, clean abort + browser fallback on failure.
`MinVersion=10.0.17763`, `SetupLogging=yes`.

## R-12 merged (988c051, 91917c1, 95a8630)
- `Loc`: key-based `T(key)` / `T(key, args)` / `Plural` / `Items` / `IsRtl`, embedded `Strings\en.json` +
  `ar.json` (**261 keys each**, parity + placeholders + non-empty + no untranslated copies enforced by 15 tests).
- Core migrated to keys (Catalog 73 sites + all backends); App keeps the legacy `T(czech, english)` overload until
  phase 2 moves its 146 sites (every literal already exists as a table value).
- UI: `UiLanguage { System, English, Arabic }` in settings with tolerant migration, live switch (no restart),
  `FlowDirection` RTL everywhere, bidi isolates for file names/numbers, DPI/rounding, `SelfTest` renders
  `en`, `en-light`, `ar`, `ar-light`.
- Arabic: MSA + Windows terminology, all six CLDR plural categories, glossary (`docs\glossary-ar.md`),
  native review list (`docs\ar-strings-review.md`), `README.ar.md`.

## Still open
- **R-2 interop** (`r2-interop` branch): raw-COM Share + OCR behind `WINDOWS10_0_19041_0_OR_GREATER`; needs the
  TFM flip (`net10.0-windows` **and** removing `SupportedOSPlatformVersion`, else NETSDK1135) — merge + CI verification pending.
- **R-15**: picker no longer offers Čeština; the literal grep for `Czech` still matches legacy identifiers and
  `Loc`'s compatibility member. Phase 2 removes them with the key migration.
- **R-16** (tool-download error mapping), **R-20** (enterprise mode: signing, no-hooks, no-downloads, portable
  zip, SECURITY.md), **R-22** (workspace tidy), R-5 README screenshots (script ready, needs an `ar` render),
  UPDATE_PROMPT (signed auto-update; **the old updater code is NOT in git history** — the repo is one squashed
  commit, so it must be written from scratch).

## Done
- **R-14 fixed in ad504ff.** `Assets\DropWheel.ico`, the two tray icons and `DropWheel.png` were the old
  grapefruit-slice artwork; they are now byte-identical copies of `branding\`. Evidence:
  - `tests\FileAction.Tests\IconAssetsTests.cs` pins the SHA-256 of every shipped icon to its brand source
    (4 parity cases) and checks the ICO frame tables (3 cases) → **57/57 tests** (was 50).
  - REA `inspect_pe_resources` on the rebuilt `DropWheel.exe` (247,296 B, sha256 2136c99b…): `RT_GROUP_ICON`
    has **4 images (48/64/128/256)** and the 256 px `RT_ICON` payload is byte-identical to the brand frame
    (`3c1c5349…e999`). The old exe had the citrus art.
  - **Question for Claude:** `branding\DropWheel.ico` has only 4 frames (48–256) while the old citrus ICO had 9
    (16–256). Windows scales 48 down for the 16/20/24/32 px slots, which is slightly softer in Explorer/taskbar.
    Do you want a 9-frame `branding\DropWheel.ico` (generated from `branding\DropWheel-1024.png`)? `branding\`
    is yours, so I did not touch it.
- **History audit + rewrite (pushed once).** The old history had **2,594 tracked cache files** (`.tmp/`, `.nuget/`,
  `.appdata/`, `.dotnet-cli/`), including LibreOffice lock files and personal paths. `.gitignore` now covers
  `.appdata/ .dotnet-cli/ .nuget/ .tmp/ artifacts/ publish*/ release/ tools/`.
  History was rebuilt as a **single clean commit** (112 files, 0 cache files, 0 secret patterns:
  `ghp_*`, `AKIA*`, `BEGIN * PRIVATE KEY` = 0 hits) and force-pushed **once** to
  `https://github.com/Faisaldhd/dropwheel` as instructed.
- **R-4 fixed**: all scratch `_*.py` helpers removed from the workspace root.
- **R-3 fixed**: `CHANGES.md` corrected — internal names stay `FileAction.*` by owner rule; only user-visible
  identity is DropWheel.
- **R-1 fixed (verified)**: rebuilt 
elease\DropWheel\DropWheel.exe (326,656 B, sha256 4201477b...) — REA
  inspect_pe_resources shows one RT_GROUP_ICON with **9 images (16,20,24,32,40,48,64,128,256)**, the new brand icon.
  Installer rebuilt: 63.9 MB, sha256 702E77F25DE467092FF198A9B3E4E17460ADFBA42EF5E7429650879AE9EB19DF.
- Old theme notes: flat dark+violet theme applied (`#8224D4` / `#B48AE0`), accent gradient replaced by solid fill,
  glow reduced to 14/0.45-0.3, dark by default, new `branding\DropWheel.ico` in the exe and installer, tray icons
  `DropWheel-tray-dark/light.ico` chosen at startup from `SystemUsesLightTheme`.

## In progress
- **R-2** size: publish is 219 MB; trimming/`Microsoft.Windows.SDK.NET.dll` review pending, target setup < 50 MB.
- **R-5** screenshots: `--self-test` produced 0 PNGs in this headless session; needs an offscreen renderer or a
  desktop run.

## Blocked / questions for Claude
- Should the 219 MB publish folder stay self-contained (no .NET install needed), or accept a framework-dependent
  build (~2 MB) that requires .NET 10 on the user's PC? Trimming alone will not reach 50 MB with WPF + Magick + Skia.

## Next
R-2 packaging → R-2 interop → R-12 (infrastructure, RTL, Arabic) → R-5 → `UPDATE_PROMPT.md` → R-13.

## This session — fresh clone, team of 4
- **Workspace:** `DropWheel\` was cloned fresh from `Faisaldhd/dropwheel` (the local folder was empty), so the
  checkout is exactly the pushed state at `5bc447c`.
- **REVIEW.md:** the pushed copy had no R-12/R-13. Both are now in `REVIEW.md` with their detail sections,
  reconstructed from the owner's brief because the newer local copy was not in the clone.
- **`git grep` for the Windows profile name: 0 hits** (R-6 follow-up). The reviewer's own row was reworded too.
- **Toolchain note (important for anyone re-running here):** `dotnet test` aborts on this PC before any test runs —
  the VSTest testhost cannot `OpenProcess` its parent (`Win32Exception 5: Access is denied`). It reproduces outside
  the sandbox (scheduled task) too, so it is machine-level, not repo-level. Compensating harness (gitignored):
  `\.tmp\test.cmd` builds Core + App + Tests and runs them with a small reflection-based xunit runner in
  `\.tmp\Runner`. **Baseline: 50 passed / 0 failed.** The owner's own machine should still use `dotnet test`.
- **NuGet:** packages are restored into `\.nuget` and `dotnet` state into `\.dotnet-cli` / `\.appdata` (all
  gitignored) because the profile folders are not writable from the sandbox.

## Legal (R-6 … R-9) — done this session
- **R-6 fixed**: `scripts/fix_changes.py` deleted; `scripts/_*` added to `.gitignore`.
  A `git grep` for the Windows profile name in this repo now returns **0 hits** (this line used to quote it;
  the reviewer's own row in `REVIEW.md` was reworded as well).
- **R-7 fixed**: `packaging/` removed (Windows-only fork).
- **R-8 fixed**: README credits "DropWheel is based on [FileAction](…/file_action) by Metamorfolog (MIT)"; the same
  sentence is appended to the help/About dialog text; `THIRD-PARTY-NOTICES.md` now says "DropWheel is MIT-licensed"
  and keeps the attribution. Tray menu has a new **Licenses** item that opens the licences folder.
- **R-9 fixed**: `licenses/` is generated from the real NuGet packages (16 files: MIT/Apache/BSD texts, SkiaSharp,
  ImageMagick notice, .NET runtime licences) plus `LICENSE-MIT-DropWheel.txt`, `THIRD-PARTY-NOTICES.md` and a
  `README.md` index. The installer now ships `LICENSE`, `THIRD-PARTY-NOTICES.md` and `licenses\*` into `{app}` and
  adds a Start-menu **DropWheel Licenses** shortcut. The stale `--updated` run entry was removed from the installer.

## This session (ddc8461, 3f38060)
- **R-10 fixed**: every `<img>` screenshot block removed from README (`img left: 0`); it will come back only with images
  from the new violet build (`scripts\screenshots.cmd`).
- **R-11 fixed**: all `dropwheel-releases` references replaced with `Faisaldhd/dropwheel` (COLLAB.md, STATUS.md,
  UPDATE_PROMPT.md). The only remaining hit is the text of R-11 inside `REVIEW.md` (reviewer-owned).
- **R-2 measured (not finished)**: framework-dependent publish
  `dotnet publish -c Release -r win-x64 --self-contained false` → **87.6 MB / 42 files** (self-contained: 219.3 MB / 293).
  Biggest remaining item: `Microsoft.Windows.SDK.NET.dll` **23.7 MB** (WinRT projection for OCR/Share), then
  Magick 23.6, Skia 10.9, pdfium 5.5 MB. Removing the SDK projection should bring the publish to ~64 MB and the setup
  to roughly 25 MB, i.e. inside the 30 MB target. Still to do: switch the csproj to framework-dependent, add the
  installer runtime check (HKLM `...\sharedfx\Microsoft.WindowsDesktop.App` / `dotnet --list-runtimes`, download
  https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe, verify Authenticode, `/install /quiet /norestart`),
  and cut the WinRT dependency. Needs a fresh session.

## Not started
- **R-12 Arabic/English** (string tables, RTL, glossary, docs/ar-strings-review.md, Arabic installer, README.ar.md).
- **R-5** `scripts\screenshots.cmd` — needs the owner on a desktop session.
- **UPDATE_PROMPT.md** (single public repo `Faisaldhd/dropwheel`).
