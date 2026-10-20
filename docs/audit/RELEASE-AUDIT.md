# Release / installer audit — DropWheel 2.4.0

Lane 5 of 8 (TEAM B), branch `audit/arelease`, base `origin/main` @ `f833459` (merged up to `4f23c9c`).
Target: the 2.4.0 tag, today. Everything below is **CI evidence or static reading of the tree**;
nothing in this lane built, installed, signed or ran anything on a local machine.

All CI numbers in this document come from **one** `workflow_dispatch` run on this branch,
[`39375412602`](https://github.com/Faisaldhd/dropwheel/actions/runs/39375412602) (success).

| claim | verdict | evidence |
|---|---|---|
| Per-user **HKCU only**, no HKLM write | **PASS** | `installer/DropWheel.iss:46,124`; only HKLM use is a read at `:151` |
| Stable `AppId` → 2.4.0 upgrades in place, settings kept | **PASS** | `installer/DropWheel.iss:21`; identical at `v2.1.0`, `v2.2.0`, `v2.3.0` |
| Silent install exits 0 | **PASS (CI)** | `scripts/test-installer.ps1:218-227` |
| Silent uninstall exits 0 | **PASS (CI)** | `scripts/test-installer.ps1:355-362` |
| Uninstall removes the HKCU key, shell entries and app files | **PASS (CI)** | `scripts/test-installer.ps1:366-375` |
| Uninstall needs no leftover `%APPDATA%\DropWheel` | **PASS** | `installer/DropWheel.iss:215-240`; `scripts/test-installer.ps1:344-379` |
| Version `2.4.0` consistent (csproj / .iss / site / notes) | **FAIL at audit commit** | **R-01**, **R-02**, **R-03** — the audited tree says 2.3.0 / 2.2.1, never 2.4.0 |
| **Pre-tag gate R-01a (source)**: `main` says 2.4.0 | **PASS** | `main` @ `bc204c4`: `<Version>2.4.0</Version>`, `#define AppVersion "2.4.0"` (PR #34 merged) |
| **Pre-tag gate R-01b (packaged)**: asset is `DropWheel-2.4.0-Setup.exe` | **NOT PROVEN — remaining gap** | no `main` package run has completed since the bump; `DropWheel-Setup-2.4.0` does not exist. The audited build shipped `DropWheel-2.3.0-Setup.exe` (run `39375412602`) — see §8 |
| Shipped `--self-test` translated, 300+ keys en+ar | **PASS (CI)** | run `39375412602`, artifact `shipped-self-test` |
| `--diagnose` positive key count + translated `settings.title` | **PASS (CI)** | run `39375412602`, artifact `diagnose` |
| Size budget: setup + zip may not grow ≥ 6 MiB over v2.3.0 | **PASS (CI)** | §4 below; gate added in this lane (`ci.yml:293-337`) |
| Release asset set (6 names) + `SHA256SUMS.txt` covers the other 5 | **PASS (static only)** | `ci.yml:447-654` — cannot be executed without a `v*` tag (see §5) |

---

## 1. Installer contract

### 1.1 Per-user, HKCU only, no HKLM

- `PrivilegesRequired=lowest` — `installer/DropWheel.iss:46`. The default install is per-user into
  `{autopf}\DropWheel`, i.e. `%LOCALAPPDATA%\Programs\DropWheel` (`:35-37`).
- `PrivilegesRequiredOverridesAllowed=dialog commandline` — `:47`. An **all-users** install is possible
  only when the user picks it in the dialog or passes `/ALLUSERS` explicitly; the updater re-uses the
  mode of the running install (`:42-45`), so a per-user install stays per-user.
- The only `[Registry]` entry is HKCU — `:124`:
  `Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "DropWheel"`.
- The only `HKLM` in the script is a **read** used to detect a previous install in the other hive —
  `:151` `RegKeyExists(HKCU, …) or RegKeyExists(HKLM, …)`. There is no HKLM write anywhere.
- The smoke test installs with `/CURRENTUSER` (`scripts/test-installer.ps1:222`) and checks the
  per-user uninstall entry (`:45,52-55,367`).

**Gap (R-07):** no gate asserts that an HKLM uninstall entry was *never created*; the script only
prints whichever entries exist (`:243-245`). The contract holds by construction (`lowest` privilege
means the installer cannot write HKLM), but it is implied, not asserted.

### 1.2 Stable `AppId` — in-place upgrade, settings preserved

- `AppId={{1AB9CDBE-C50D-484B-96ED-5D575D0806D2}` — `installer/DropWheel.iss:21` (the doubled brace is
  the Inno escape; the real AppId is `{1AB9CDBE-C50D-484B-96ED-5D575D0806D2}`).
- Verified unchanged in every tagged release: `v2.1.0`, `v2.2.0` and `v2.3.0` all carry the same
  `AppId` (`git show <tag>:installer/DropWheel.iss`). So 2.4.0 replaces the previous install instead
  of adding a second entry.
- Settings live outside the install folder (`%APPDATA%\DropWheel\settings.json`, `%LOCALAPPDATA%\DropWheel`),
  so they survive an upgrade; the uninstaller only deletes them on an explicit "Yes" (`:225-241`).
- Task choices are preserved: `UsePreviousTasks=yes` (`:49`) plus `checkedonce` on the two default
  tasks (`:101-102`), and `ApplyInstallDefaults` (`:159-162`) stops a **silent** upgrade from turning
  autostart/watch back on for a user who turned them off.

**Contract note:** the setup *filename* changed in 2.2.0 from `DropWheel-Setup-{#AppVersion}`
(`v2.1.0`) to `DropWheel-{#AppVersion}-Setup` (`:58`), which is what `UpdateConfig.SetupAssetName`
expects (`src/FileAction.Core/Updates/UpdateConfig.cs:22`). The AppId did **not** change with it, so
naming and upgrade identity are independent. `scripts/test-installer.ps1:207` pins the new name shape.

### 1.3 Silent install `/S` and silent uninstall `/S` both exit 0

- Install: `scripts/test-installer.ps1:218-227` runs
  `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /DIR=… /LOG=…` and asserts exit code 0.
- Uninstall: `scripts/test-installer.ps1:355-362` runs the installed `unins000.exe` with
  `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=…` and accepts exit code 0 (or 3010 = "reboot
  suggested").
- **Nuance, stated honestly:** CI exercises `/VERYSILENT`, which is the stricter silent mode, not the
  literal `/S` switch. In Inno Setup `/S` is a synonym for silent mode, so this is the same code path,
  but `/S` itself is not the switch under test. Nothing here claims a literal `/S` run.

### 1.4 Uninstall removes the HKCU key, the shell entries and the app files

`scripts/test-installer.ps1:369-375` asserts, after the silent uninstall:

| removed | asserted at | implementation |
|---|---|---|
| program folder (`{app}` incl. `DropWheel.exe`) | `:366,370` | `[Files]` `:109` |
| per-user uninstall entry `…\Uninstall\{1AB9CDBE-…}_is1` | `:367` | Inno own key |
| HKCU `Run` value `DropWheel` | `:371` | `RemoveShellIntegration` `:173-181` |
| file + folder Explorer context-menu keys | `:372-373` | `:183-191` |
| Send-to shortcut | `:374` | `:193-202` |
| Start-menu shortcut | `:375` | `[Icons]` `:113-114` |

The uninstaller deliberately does **not** run `DropWheel.exe --unregister`; it removes the shell
entries itself so cleanup also works when the exe is gone or locked (`:164-167`).

**Tray cleanup** is a process kill, not a registry entry: `[UninstallRun]` calls
`{sys}\taskkill.exe /im DropWheel.exe /f` with `RunOnceId: "StopDropWheel"` (`:134-136`), so a running
instance cannot hold `DropWheel.exe` open while the files are deleted. There is no tray residue to
remove — the icon dies with the process.

### 1.5 No leftover `%APPDATA%\DropWheel` requirement for uninstall

A silent uninstall always **keeps** the user's data (`:215-219` logs "Silent uninstall: keeping the
DropWheel settings"), and the deletion path is guarded by `RemoveUserData` (`:233-240`). `DelTree` on a
folder that does not exist is a no-op, so uninstall does not depend on the folder existing. CI proves
both directions: it seeds `%APPDATA%\DropWheel\installer-smoke-test.txt` and
`%LOCALAPPDATA%\DropWheel\tools\installer-smoke-test.txt` (`scripts/test-installer.ps1:344-349`) and
asserts they survive (`:378-379`).

### 1.6 Version consistency — **FAIL**

Target release is **2.4.0**. The tree contains no release-versioned `2.4.0` anywhere; the three
surfaces that decide what ships say otherwise:

| surface | value | where |
|---|---|---|
| csproj `<Version>` (authoritative: read by CI, names the setup) | **2.3.0** | `src/FileAction.App/FileAction.App.csproj:24` |
| installer fallback `#define AppVersion` | **2.3.0** | `installer/DropWheel.iss:16` |
| download page (EN + AR) | **2.2.1** | `docs/site/index.html:7,40,97` |
| release notes | **2.3.0**, no 2.4.0 section | `CHANGES.md:1,3` |

See **R-01**, **R-02**, **R-03**. The .iss default is overridden by
`scripts/build-installer.ps1:400` (`/DAppVersion=$version` read from the csproj at `:363`), so the
installer always follows the csproj — which makes the csproj the single line that must change.

---

## 2. CI evidence (the only execution path)

CI was dispatched **once** on the audit branch:

```
gh workflow run ci.yml --repo Faisaldhd/dropwheel --ref audit/arelease
```


| item | value |
|---|---|
| **run id** | **`39375412602`** — `workflow_dispatch` on `audit/arelease` |
| commit | `0911869` (base `f833459`) |
| conclusion | **success** — `Build + test` ✅, `Package (self-contained setup + portable zip)` ✅, `Screenshots` ✅, `GitHub release (tag)` skipped (no tag — expected) |
| artifacts | `self-contained-self-test`, `shipped-self-test`, `test-results`, `DropWheel-Setup-2.3.0`, `diagnose`, `screenshots` |

**Shipped `--self-test`** — artifact `shipped-self-test`, `self-test.txt` (6,394 B), run `39375412602`,
package job, step *Self-test the exact packaged publish*:

```
ok en/localization: 1094 keys (English)
ok en-light/localization: 1094 keys (English)
ok ar/localization: 1094 keys (Arabic)
ok ar-light/localization: 1094 keys (Arabic)
ok settings.json file load/save
ok tools/crop (26 registered)
ok motion/clips (20 clips, 833 KB, 480 px wide, 80 ms/frame)
```

- **1,094 keys in English and 1,094 in Arabic**, in all four language/theme passes — far above the
  300 minimum.
- **0 uppercase `FAILED:` lines** (case-sensitive check), 105 `ok` lines, process exit code 0.
- `ok tools/crop (26 registered)` confirms all 26 tools register; only Crop is *rendered* (§2.1).
- 37 skipped lines: 35 are motion surfaces (§2.3), 1 is `skipped explorer selection (no Explorer
  window appeared)`, 1 is `skipped ocr (failed: NullReferenceException)` — the OCR check cannot run on
  a runner without the OCR language pack; it is a skip, not a failure, and the job stayed green.

**Installed copy, installer smoke, `--diagnose`** — same run, package job, step
*Installer silent install/uninstall smoke test* (`scripts/test-installer.ps1`), verbatim verdict:

```
PASS: silent install, shell entries, silent uninstall and settings retention verified.
```

| contract point | CI line |
|---|---|
| setup name matches the updater contract | `ok: setup name matches the update contract (DropWheel-2.3.0-Setup.exe)` |
| silent per-user install exits 0 | `ok: the installer exited with 0 (got 0)` |
| app, LICENSE, notices, licences installed | `ok: DropWheel.exe is installed` / `ok: licenses\ is installed (17 files)` |
| HKCU Run value written, points at `--background` | `ok: the HKCU Run value DropWheel exists` / `ok: Run value starts DropWheel --background` |
| Start-menu shortcut yes, desktop shortcut no | `ok: the Start-menu shortcut exists` / `ok: no desktop shortcut was created` |
| installed `--self-test` exits 0, no raw keys | `ok: the installed --self-test exited with 0 (got 0)` / `ok: the installed self-test rendered no raw localization key` |
| 4 language/theme passes, ≥ 300 keys each | `ok: the installed self-test checked all four language/theme passes (4 reported)` / `ok: the installed app loaded 1094 localized keys (minimum 300)` ×4 |
| installed `--diagnose` exits 0 with both tables | `ok: the installed English table has 1094 keys (minimum 300)` / `ok: the installed Arabic table has 1094 keys (minimum 300)` |
| silent uninstall exits 0 | `ok: the uninstaller exited with 0 (got 0)` |
| uninstall removed files and the HKCU key | `ok: the program folder is gone` / `ok: the HKCU Run value is gone` |
| uninstall removed shells + Start menu | `ok: the file context-menu key is gone` / `ok: the folder context-menu key is gone` / `ok: the Send-to shortcut is gone` / `ok: the Start-menu shortcut is gone` |
| silent uninstall kept user data | `ok: a silent uninstall keeps the settings folder` / `ok: a silent uninstall keeps the downloaded helper programs` |

**`--diagnose`** — artifact `diagnose`, `diagnose.txt` (2,443 B), from the exact `release/selfcontained`
folder ISCC packages:

```
version:    2.3.0.0
integrity:  high (0x3000)
strings:    1094 keys, settings.title="DropWheel – settings"
localization: English=1094 keys, Arabic=1094 keys
```

Positive key count (1,094, not 0) and `settings.title` resolved to its **translated** value
`"DropWheel – settings"` — not the raw key. This is the exact 2.2.0 black-window regression signal,
and it is clean.

**Version evidence for R-01:** the package job printed `version = 2.3.0`, built
`DropWheel-2.3.0-Setup.exe`, named the artifact `DropWheel-Setup-2.3.0`, and `--diagnose` reports
`version: 2.3.0.0`. The audited tree therefore cannot produce a 2.4.0 release.

### 2.1 What the shipped `--self-test` actually asserts (and does not)

This matters for the release notes, so it is stated precisely.

The shipped gate (`ci.yml:188-213`) fails the job when:

1. the process exit code is non-zero (`ci.yml:200`);
2. no `self-test.txt` was written (`:201`);
3. any rendered string is a raw localization key (`:203-205`);
4. the `localization: N keys` count is missing or 0 (`:206-210`);
5. `settings.json` load/save was not exercised (`:211-213`).

The installed-copy gate (`scripts/test-installer.ps1:251-302`) additionally requires **four**
language/theme passes (`:269`) with **≥ 300 keys each** (`:271`), English **and** Arabic tables
≥ 300 keys (`:288-289`), an active count ≥ 300 (`:296`), and no raw dotted key as a value (`:300-302`).

**Coverage limit (R-04):** the string-table assertion covers the app's four language/theme renders,
but the *per-tool-window* label check builds **only `CropTool`** offscreen —
`src/FileAction.App/SelfTest.cs:465-467` constructs `new ToolWindow(CropTool.Instance, [item], null)`
and saves `tools/crop.png`. `ToolRegistry.All.Count` is only asserted to be non-zero
(`SelfTest.cs:454`) and logged (`:481`). The XML doc comment two lines above claims it "builds every
registered floating tool offscreen" (`SelfTest.cs:449`) — that comment is **wrong**.

So the shipped log supports: *the app's string tables are loaded and translated in en/ar (dark+light),
Crop's window renders translated labels, and no window rendered a raw key in those passes.*
It does **not** support "every tool window was rendered in Arabic". No release note may say that.

### 2.2 Independent UI-coverage gate (`ui-coverage`) — verified, currently red

A separate lane is building a new branch `audit/selftest` whose job is to render **every** window in
every culture and upload the PNGs plus a report as an artifact named `ui-coverage`. This lane's job is
to verify it as a **release gate**, not to write it.

The branch now exists (`audit/selftest`, head `2315f19` at the time of writing) and this lane has
verified the gate's mechanism from its **red** run, which is the harder half of the proof:

| check | result |
|---|---|
| artifact name | **`ui-coverage`** — present on run `39377633125`, 5,858,055 B, not expired |
| artifact really contains the renders | **yes** — 98 files: **90 PNGs**, plus `ui-coverage.txt`, `ui-coverage.md` and 5 sample inputs (`photo.jpg`, `clip.mp4`, `doc.pdf`, `bundle.zip`, `tone.wav`) |
| per-window / per-culture counts (report header) | `surfaces: 114 rows, pass 113, skip 0, fail 1`<br>`keys: 1094 English / 1094 Arabic` |
| does every tool window appear in both cultures? | **yes** — **52 tool rows = 26 tools × 2 cultures**, plus 26 `fit` (layout) rows, 22 dialog rows, 6 `wheel:*` rows and 2 each for `settings`, `jobs`, `editor`, `shelf` (8 surface kinds, 114 rows) |
| does the job **fail** on a non-zero self-test exit? | **yes, proven by a real red run**: run `39377633125` failed at step *Self-contained UI self-test (shipped publish)* with `self-contained --self-test failed with 1`, after the log printed `FAILED: … UI coverage (en+ar) failed for 1 surface(s)` |

**The gate is currently RED, and it is red for a real defect:** run `39377633125` failed on
`tool:tools.audiobleep (ar)` — *"Arabic text forced LTR: FFmpeg مفقود. ثبّته بالأمر: winget install
Gyan.FFmpeg"*. That is a genuine RTL bug in the Arabic string of one tool window, caught by exactly the
per-window assertion the shipped self-test never made. Later commits on `audit/selftest` (`2315f19`)
were still running when this document was written.

**Therefore the claim freeze stands** (§2.1, below). What this audit can now say — and still cannot:

- **Can say:** every window that the app registers (26 tools + settings, shelf, jobs, editor, wheel
  surfaces and the dialogs) is *rendered and string-checked* in `en` and `ar` by the `ui-coverage` gate,
  and the gate fails the build when one of them regresses. 113 of 114 surfaces passed in the last
  completed run.
- **Cannot say:** that this is green. One Arabic surface (`tools.audiobleep`) fails as of `f3ff39e`.
  Until a green `ui-coverage` run exists on `main`, no release note may claim "every window verified in
  Arabic" — the truthful statement is "113/114 surfaces pass in en+ar; one Arabic tool string is
  mid-fix".

**CLAIM FREEZE — in force until `ui-coverage` is green on `main`:** no document in this release (release
notes, `CHANGES.md`, README, download page, PR body, tag message) may claim *"every window verified in
Arabic"*, *"all 26 tool windows rendered in en+ar"*, or any equivalent, and none may cite the shipped
`--self-test` as proof of per-window coverage (§2.1: it renders only Crop; the string tables are what it
verifies, in four language/theme passes). This audit does not lift the freeze.

---

### 2.3 Motion coverage in the same run (observation only)

The motion gate (`docs/PERF.md:11-26`) is advertised as data-driven over `ToolRegistry.All`, but this
run **skipped 17 of the 26 tools**: `audiobleep, audiochannels, audionormalize, audiotrim,
audiovisualizer, collage, lock, pdfmerge, pdforganize, unlock, videocrop, videoframes, videojoin,
videoredact, videospeed, videosplit, videotrim` — 16 with *"no sample in the motion harness"* and
`tools.lock` with `TimeoutException: the tool window preview did not finish within 30 s`. The motion
numbers in `docs/PERF.md:45-52` cover 6 surfaces, not 26. This is the motion lane's call, recorded here
only so no release note claims a per-tool motion measurement that this run did not make.

## 3. Reachable tool windows in the build under audit

- **26 tools register** themselves: 26 `ToolRegistry.Register(...)` calls under
  `src/FileAction.App/UI/Tools/` (`AudioBleepTool`, `AudioChannelsTool`, `AudioNormalizeTool`,
  `AudioTrimTool`, `AudioVisualizerTool`, `CollageTool`, `CompressTool`, `AnnotateTool`, `CropTool`,
  `EditPhotosTool`, `AddBackgroundTool`, `RedactTool`, `LockTool`, `MetadataTool`, `ProtectTool`,
  `UnlockTool`, `PdfMergeTool`, `PdfOrganizeTool`, `QrTool`, `VideoCropTool`, `VideoFramesTool`,
  `VideoJoinTool`, `VideoRedactTool`, `VideoSpeedTool`, `VideoSplitTool`, `VideoTrimTool`).
- **Found at `f833459` (24/26).** That is the base of the build this audit inspected (commit `0911869`). `WheelController.BuildOperations()` listed 25 operations = 24 tool operations +
  `Ocr` (not a tool window), and `UI.Tools.Image.AnnotateOperation` (`AnnotateTool.cs:60`, id
  `tools.annotate` at `:36`) and `UI.Tools.Image.RedactOperation` (`RedactTool.cs:73`, id
  `tools.redact` at `:34`) were registered as tool windows but absent from that list. `VideoRedactOperation`
  was already present, so the unreachable redact was the image one only.
- **Fixed upstream at `4f23c9c` (26/26).** `origin/main` `4f23c9c` *"fix(2.4.0): wire Annotate and
  Redact into the Tools wheel (all 26 tools reachable)"* adds exactly the two missing operations
  (`WheelController.cs:88-116`, +2 lines). This branch merges that commit in, so the tree that will carry
  this document has **26/26 reachable**. That is a verified reading of `WheelController.cs` on
  `origin/main`, not a run of a CI build of it — see §7.
- This lane did not touch `WheelController.cs`. R-05 is closed by the UX lane, not by this one.

**Release-notes rule:** `26 tool windows` is correct for the rebased tree; a build cut from any commit
before `4f23c9c` exposes 24.

---

## 4. Size budget

Rule (`docs/PERF.md:5`): *the installer and the portable zip may not grow by **6 MiB or more** over
v2.3.0*. `docs/PERF.md:67-72` says the numeric baselines are owned by the perf workstream; there is no
v2.3.0 byte count anywhere in the repository (checked `STATUS.md`, `docs/PERF.md`, `CHANGES.md:238`).
The baseline used here is therefore taken from the **published v2.3.0 release** — the artefacts users
actually downloaded:

```
gh release view v2.3.0 --repo Faisaldhd/dropwheel --json assets
```

| asset | v2.3.0 bytes | v2.3.0 MiB | source |
|---|---:|---:|---|
| `DropWheel-2.3.0-Setup.exe` | 62,967,948 | 60.05 | v2.3.0 release page |
| `DropWheel-2.3.0-portable-win-x64.zip` | 89,266,161 | 85.13 | v2.3.0 release page |

Fail threshold = baseline + 6 MiB (6,291,456 B): **69,259,404 B** for the setup,
**95,557,617 B** for the zip.

Produced by run `39375412602` (package job). Byte counts are read from the two files the job uploaded,
not converted from the printed MB:

| asset | 2.4.0 bytes | 2.4.0 MiB | v2.3.0 bytes | delta bytes | delta MiB | delta % | verdict |
|---|---:|---:|---:|---:|---:|---:|---|
| `DropWheel-2.3.0-Setup.exe` (setup) | **63,492,861** | 60.55 | 62,967,948 | **+524,913** | **+0.50** | +0.83 | **PASS** |
| `DropWheel-2.3.0-portable-win-x64.zip` | **89,963,517** | 85.80 | 89,266,161 | **+697,356** | **+0.67** | +0.78 | **PASS** |

Verbatim from the new gate's log:

```
setup: 63,492,861 B vs v2.3.0 62,967,948 B -> 0.5 MiB (0.83 %) PASS
portable zip: 89,963,517 B vs v2.3.0 89,266,161 B -> 0.67 MiB (0.78 %) PASS
```

For reference the same job printed `publish: 196.3 MB / 297 files` and `setup: 60.55 MB`.

**Verdict: PASS with 5.50 MiB of setup headroom and 5.33 MiB of zip headroom before the 6 MiB line.**
The asset file names in this table say `2.3.0` because the audited tree is still version 2.3.0 (§1.6,
R-01); the *size* comparison is unaffected, and the same gate runs unchanged once the version is 2.4.0.

### 4.1 The gate that was missing (fixed in this lane)

Before this branch, `.github/workflows/ci.yml` only **printed** the two sizes in the "Size report"
step; nothing compared them to a baseline, so `docs/PERF.md:5` was a documented rule with no
enforcement. This branch adds one additive step, `ci.yml:293-337`
(*Package size budget (v2.3.0 baseline, +6 MiB fails)*), which:

- reads the two uploaded files by their exact names;
- compares each against the v2.3.0 byte constants above;
- fails when the delta is **≥ 6 MiB** (so exactly +6 MiB is red, matching "6 MiB or more");
- writes the delta table (bytes, delta, delta %, verdict) to the job summary.

No existing step changed; the step only runs in the `package` job (dispatch / `main` / tags), so
`build-test` on every branch is unaffected.

---

## 5. Release asset set and checksum manifest (static verification only)

The `release` job (`ci.yml:447-654`) publishes exactly six assets for a `v*` tag:

| asset | produced by |
|---|---|
| `DropWheel-<version>-Setup.exe` | staged from the package artifact, `ci.yml:516-518` |
| `DropWheel-<version>-Setup.exe.sig` | ECDSA P-256 over the file bytes, `ci.yml:548-558` |
| `DropWheel-Setup.exe` (stable alias) | byte-for-byte copy, asserted equal by SHA-256, `ci.yml:520-526` |
| `DropWheel-<version>-portable-win-x64.zip` | `ci.yml:274-285` |
| `DropWheel-<version>-portable-win-x64.zip.sig` | `ci.yml:548-558` |
| `SHA256SUMS.txt` | `ci.yml:567-588` |

Checksum-manifest logic verified by reading:

- exactly **five** staged assets must exist before the manifest (`ci.yml:570-580`);
- the manifest lists those five and never itself, `sha256sum` format
  `<64 lowercase hex><two spaces><name>`, sorted (`ci.yml:567-588`);
- the publish step re-reads the release and fails unless exactly **six** names match the expected set
  (`ci.yml:618,641`), and prints the per-asset byte table to the summary (`ci.yml:647-652`);
- the stable alias is proven byte-identical before upload (`ci.yml:521`);
- signing refuses to publish unsigned assets (`ci.yml:532`).

**What this lane did not and could not verify:** the `release` job only runs on a `v*` tag
(`ci.yml:453`), and this lane may not tag or touch signing material, so the six-asset publish, the
two `.sig` files and the `SHA256SUMS.txt` → asset match are **static-verified only**. No signature was
created, read or verified here. `SHA256SUMS.txt` *content* for 2.4.0 does not exist until a tag is cut.

---

## 6. Findings that need action

| id | sev | finding | where | owner |
|---|---|---|---|---|
| **R-01** | **Critical (blocks tag)** | `<Version>` is still **2.3.0**, so CI builds `DropWheel-2.3.0-Setup.exe` and the release job names assets after the *packaged* version (only a warning on tag mismatch). Tag `v2.4.0` would publish 2.3.0-named assets, and `UpdateConfig.SetupAssetName(2.4.0)` looks for `DropWheel-2.4.0-Setup.exe` → the updater 404s. **Fix opened as PR #34 (`audit/version`, +2 lines, csproj + .iss default), not merged at the time of writing.** | `src/FileAction.App/FileAction.App.csproj:24`; `installer/DropWheel.iss:16`; `ci.yml:483-507` | release owner — **PR #34** |
| **R-02** | High | No 2.4.0 release notes. `CHANGES.md` starts at `# DropWheel 2.3.0 — CHANGES` with `## 2.3.0` as the newest section (EN + AR). The tag's notes are `--generate-notes` from commits, not the repo changelog. | `CHANGES.md:1,3` | docs lane |
| **R-03** | High | Download page advertises **2.2.1** in both language blocks — two minor versions stale on the surface users see first. | `docs/site/index.html:7,40,97` | docs/site owner |
| **R-04** | Medium | `--self-test` claims to build every registered tool offscreen but builds only `CropTool`; the log's "N registered" is a count, not a render. Release notes must not claim "every tool window rendered in en/ar". | `src/FileAction.App/SelfTest.cs:449,454,465-467,481` | app/self-test owner |
| **R-05** | **Closed** | 2 of 26 tool windows were unreachable (`tools.annotate`, `tools.redact` registered but missing from `BuildOperations()`). **Fixed on `main` at `4f23c9c`** (+2 lines), so the merged tree is 26/26. | `src/FileAction.App/Wheel/WheelController.cs:88-116`; `AnnotateTool.cs:36,60`; `RedactTool.cs:34,73` | UX lane — **closed** |
| **R-06** | Low | `release.cmd` comments claim the build "asserts the setup is at most 70 MB" and "builds … the signatures". Neither is true: `scripts/build-installer.ps1` has no size assertion at all (the size gate is now in `ci.yml`), and signing only happens in the tag `release` job. | `release.cmd:1-7` | docs lane |
| **R-07** | Low | No gate asserts an **HKLM** uninstall entry was never created; the smoke test only prints existing entries and asserts the HKCU one disappears. Per-user-only holds by construction (`PrivilegesRequired=lowest`), not by assertion. | `scripts/test-installer.ps1:243-245,367`; `installer/DropWheel.iss:46,124,151` | release lane (this one) — not fixed, needs a script change |
| **R-08** | Low | `docs/signing-updates.md` still says Inno emits `DropWheel-Setup-<version>.exe` and that the release job must rename it; 2.2.0 changed the output name and `ci.yml` now handles both shapes. Stale guidance for the next release. | `docs/signing-updates.md:163-166`; `installer/DropWheel.iss:58`; `ci.yml:492-503` | docs lane |

### Fixed in this lane

- **Missing size gate** → `ci.yml:293-337` (see §4.1). Additive only, no behaviour change to any
  existing step. It is confined to the `package` job; the L10N lane owns the self-test/`ui-coverage`
  hunk in the `build-test` job and this branch does not touch it.

---

## 7. Could not verify — and why

| not verified | reason |
|---|---|
| Six live release assets at 2.4.0, their `.sig` files, `SHA256SUMS.txt` content | the `release` job requires a `v*` tag (`ci.yml:453`); this lane may not tag, and must not touch signing keys, certificates or the update signature. Static reading only (§5). |
| Signature validity of any asset | same rule: no access to `UPDATE_SIGNING_KEY`, and no signature work is in scope for this lane. |
| An actual `/S` (as opposed to `/VERYSILENT`) silent install/uninstall | CI's smoke test uses `/VERYSILENT` (`scripts/test-installer.ps1:219,356`); both map to Inno's silent mode, but only `/VERYSILENT` is exercised. |
| HKLM-never-written | no assertion exists (R-07); holds by `PrivilegesRequired=lowest`, not measured. |
| Real 2.4.0 upgrade-over-2.3.0 run (settings kept across a real previous install) | the smoke test installs on a clean machine; an upgrade-in-place test is not in CI. The AppId + `UsePreviousTasks` + `checkedonce` reading in §1.2 is the evidence that it works, not a run. |
| Whether the 2.4.0 installer's Arabic wizard renders correctly | requires a desktop session and a human; the `screenshots` job is framework-dependent and does not capture the installer. `installer/DropWheel-ar.isl` is a plain message file (6 custom messages, `:11-16`) over the vendored `Arabic.isl`, and `RightToLeft=yes` comes from that file — read, not rendered. |
| A **green** `ui-coverage` run on `main` | the gate exists and is verified red-correct (§2.2), but every completed run so far fails on a real Arabic RTL defect in `tools.audiobleep`. Until one is green the claim freeze in §2.2 stands. |
| Whether the size gate fails when it should (a red size run) | only its PASS path has executed. The `throw` is a two-line branch and was read, not exercised; forcing a 6 MiB regression would need an artificial bloat commit, which this lane will not push. |

---

## 8. Pre-tag gates

The tag must not be cut unless **every gate below** holds. Gate 1 is the one that decides this release.

Gate 1 has two halves: the **source** says 2.4.0 (static) and the **packaged build** carries it
(dynamic). The source half is now green; the packaged-build half is **still unproven**.

| # | gate | state |
|---|---|---|
| **1a** | **R-01 source** — `origin/main` `<Version>` and the `.iss` default | **PASS.** `main` @ `bc204c4` carries `<Version>2.4.0</Version>` (`src/FileAction.App/FileAction.App.csproj:24`) and `#define AppVersion "2.4.0"` (`installer/DropWheel.iss:16`); PR #34 merged. |
| **1b** | **R-01 packaged build** — a `package` run uploads artifact `DropWheel-Setup-2.4.0` containing `DropWheel-2.4.0-Setup.exe`, the zip carries 2.4.0, and `--diagnose` prints `version: 2.4.0.x` with `strings: 1094 keys` | **NOT PROVEN — the remaining gate gap.** No `main` run has *completed* the `package` job since the bump: run `39378492942` (`cf8a1a6`) reached the package job and was **cancelled** by concurrency before it uploaded anything, and the three runs after it (`39380442643`, `39380463329`, `39380472013`) were cancelled too. `39380479876` (`bc204c4`) was still in progress when this document was written. No `DropWheel-Setup-2.4.0` artifact exists on any run. |
| *history* | *2.3.0 observation, kept as the reason this gate exists* | *Run `39375412602` built `DropWheel-2.3.0-Setup.exe`, artifact `DropWheel-Setup-2.3.0`, `--diagnose` `version: 2.3.0.0` — the FAIL this audit raised as R-01.* |
| 2 | **R-05** — all 26 tools reachable | **PASS on `main`** (`4f23c9c`). |
| 3 | **R-02 / R-03** — 2.4.0 release notes (EN + AR) in `CHANGES.md`, download page says 2.4.0 | **FAIL at the audit commit** (`CHANGES.md:1,3`; `docs/site/index.html:7,40,97`); re-check on `main` before the tag. |
| 4 | Size budget on the release commit | **PASS** on the audited build (+0.50 MiB setup, +0.67 MiB zip); **must be re-stated by the same run that proves 1b**, because the version bump changes the asset names the gate looks up. |

**Fastest way to close 1b:** let one `main` push (or the already-queued `39380479876`) run the `package`
job to completion, then read the artifact name and the `diagnose` artifact. `--diagnose` printing
`version: 2.4.0.x` plus artifact `DropWheel-Setup-2.4.0` is the whole proof. Do **not** dispatch on
`main` for this; a normal push already triggers `package` (`ci.yml:151`). Until that artifact exists,
the tag would ship assets whose version has never been observed in a packaged build.
