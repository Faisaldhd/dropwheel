# REVIEW.md — written by Claude (reviewer). DeepSeek: read this first, every session.

## Round 4 — checked on GitHub at 1ad52ec

**Process:** commit d1d1f5e overwrote this file and deleted my Round 3. Never edit, restore or `git checkout` REVIEW.md. If you have to restore other files from git, name them one by one. Round 3 is re-added below.

Accepted:
- **R-18, R-19, R-21, R-14/R-17, R-12.**
- **R-2:** setup 19.71 MB, SDK.NET gone.
- **R-16, R-22.**
- Fixtures, with the read-only attribute restored.
- **R-23:** the self-contained portable zip.
- String tables: en 263 / ar 263 keys, identical ✓.

| ID | Sev | Finding | Required fix |
|---|---|---|---|
| R-24/R-25 | **P0** | The release job can never run. `on: push` only lists `branches: [main]`, so pushing a `v*` tag does not trigger the workflow. | Add `tags: ['v*']` under `on.push`. The package job must also run on tags. |
| R-26 | **P0** | The release job has no checkout, so `gh release create` has no repo context and will fail. | Pass `--repo ${{ github.repository }}` (or set `GH_REPO`). |
| R-27 | P1 | There is no `SHA256SUMS.txt`. The owner's README tells him to check the hash before running anything. | Generate `SHA256SUMS.txt` with `Get-FileHash` over every asset and attach it to the release. |
| R-20 | **P0** | Not started: there is no `--no-hooks` and no policy key anywhere in `src/`. This blocks the first release. | The portable build must have no global hooks by default. Open the wheel from the tray, a hotkey (RegisterHotKey) and Explorer "Send to". Hooks only when the user turns them on. No automatic downloads. Add the policy keys and SECURITY.md. |

**Do not tag a release until R-20, R-25, R-26 and R-27 are done and CI is green.** The first release, v2.1.0, must already be company-safe.

**NEXT:** R-25 → R-26 → R-27 → R-20 → tag v2.1.0 → R-15 → R-5 → UPDATE_PROMPT → R-13.

**Folder rule (owner):** everything stays in `Downloads\DropWheel_1`. Never touch Desktop or Documents.

## Round 2 — verified on GitHub at e9053ae

Closed and verified:
- **R-1:** violet build, REA shows 9 icon images.
- **R-3, R-4.**
- **R-6:** personal path removed. My own old row quoted it; that text is gone now.
- **R-7:** packaging removed.
- **R-8:** credit fixed in README, About and notices.
- **R-9:** LICENSE, THIRD-PARTY-NOTICES and 16 licence texts now ship in `{app}`, with a Start-menu shortcut and a tray item. Good work.

| ID | Sev | Finding | Required fix | State |
|---|---|---|---|---|
| R-10 | P1 | README still embeds the ORIGINAL app's screenshots (orange wheel, "FileAction" title, Czech option). Not addressed yet. | Remove the screenshot block from README now (lines 9-18). Re-add it only with new images from the violet build (see R-5). | open |
| R-2 | P1 | Size: publish 219 MB / setup 63.9 MB. | **Decision (answers your question):** go FRAMEWORK-DEPENDENT. The owner wants a light app. Details below. | open |
| R-5 | P2 | No screenshots of the new build. | Add `scripts\screenshots.cmd` (runs `DropWheel.exe --self-test docs\screenshots\en` from the release folder). The owner will run it on his PC and send you the PNGs. | open |
| R-12 | P1 | Owner requirement: the UI must be in exactly two languages, **Arabic and English**, both polished. | Full spec below. | open |
| R-13 | P3 | Owner decision: DropWheel stays FREE and OPEN SOURCE under MIT. Prepare the repo for contributors. | See R-13 details. | open |
| R-14 | P1 | The built app STILL uses the old citrus icon. `branding/DropWheel.ico` is the new wheel icon, but the build takes `src/FileAction.App/Assets/DropWheel.ico`, which is the old one (md5 63dc21d4… vs branding fc312ac1…). The tray icons in Assets also differ from `branding/`. | Copy `branding/DropWheel.ico`, `DropWheel-256.png` → `Assets/DropWheel.png`, and both tray icons from `branding/` into `src/FileAction.App/Assets/` (overwrite). Add a build check that fails if the Assets icons differ from `branding/` (compare SHA-256). Rebuild, then extract the largest icon from DropWheel.exe and confirm it shows the wheel with the arrow, not the fruit. | open |
| R-17 | P1 | Owner: "the icon is way too small". At 16–48 px the detailed artwork shrinks to a tiny smudge. In addition, the progress card shows an EMPTY purple square instead of an icon (see the "Downloading Pandoc" card). | NEW icons are in `branding/` (Claude regenerated them): `DropWheel.ico` now uses a bold, simplified wheel that fills the frame at 16/20/24/32/40/48 px and the full artwork at 64/128/256; `DropWheel-tray-dark.ico` / `-light.ico` are bolder and fill the full canvas. Copy all three into Assets (this is part of R-14). For the progress card: when the job is a tool download (no source file), show the DropWheel icon or a download glyph, never an empty box. Check the window title-bar, taskbar, Alt+Tab, tray and Start-menu icons at 100% and 150% scaling. | open |
| R-18 | P0 | On the owner's PC (corporate CrowdStrike Falcon), the portable app opened Settings, then **disappeared silently**. The drag+key gesture never worked, and `%APPDATA%\DropWheel` was never created, so there is no log and no way to diagnose. App.cs uses ShutdownMode.OnExplicitShutdown, so closing Settings should not exit: this is a crash or a kill. | 1) Crash safety: catch AppDomain.UnhandledException, DispatcherUnhandledException and TaskScheduler.UnobservedTaskException; write `crash-<time>.txt`; show a dialog "DropWheel stopped: <reason>" with Copy details. 2) Log fallback: if `%APPDATA%\DropWheel` can't be created, log to `%LOCALAPPDATA%\DropWheel`, then `%TEMP%`, then next to the exe. Log startup steps: settings loaded, tray created, mouse hook installed (Win32 error code), keyboard hook installed, hotkey registered. 3) If SetWindowsHookEx fails or no hook callback arrives within 5 s of a real drag, show a tray balloon: "DropWheel can't watch the mouse/keyboard on this PC (security software may block it). Use Ctrl+Alt+Space or right-click → DropWheel." 4) Add `DropWheel.exe --diagnose`: prints the environment (paths writable?, hooks OK?, tools found?) to a text file and opens it. The owner will run it on his PC. | open |
| R-19 | P0 | **Owner constraint:** the only PC is a company laptop with corporate security (CrowdStrike). From now on, NOTHING built from this repo may be RUN on it: no DropWheel.exe, no installer, no --self-test, no tool downloads. All building, testing and screenshots move to GitHub Actions (free Windows runners; the repo is public). | Add `.github/workflows/ci.yml` on `windows-latest`, triggered on push and workflow_dispatch: 1) set up .NET 10; 2) `dotnet build` (0 warnings) and `dotnet test`; 3) publish (framework-dependent once R-2 lands); 4) build the Inno installer (install Inno via choco); 5) run `DropWheel.exe --self-test shots` (en+ar, dark+light) and `--diagnose`; 6) upload artifacts: installer, portable zip, screenshots, diagnose report, test results. Add a status badge to README. Fetch FFmpeg/Pandoc in CI for the integration tests. Keep secrets out of the workflow (none are needed yet). Locally on this PC: edit code and run git only. If you need a build check, push and read the CI result (`gh run watch`, `gh run view --log-failed`). Report CI links in STATUS.md. | open |
| R-20 | P1 | **Enterprise-friendly mode.** The owner wants DropWheel to be easy for a corporate security team to evaluate and approve. | 1) **Code signing:** apply to SignPath Foundation (free for OSS); sign DropWheel.exe, all DropWheel*.dll and the installer in CI (R-19). Document the steps in `docs/signing.md`; the owner does the application. Until it's approved, the CI pipeline keeps an unsigned build. 2) **No-hook mode:** setting "Keyboard & mouse monitoring": On (default) / Off. With Off, NO SetWindowsHookEx is installed at all; the wheel opens via Explorer right-click → DropWheel…, via a small always-on-top drop zone window (drag files onto it), and via the tray menu → "Open wheel for clipboard/selection". Also support `DropWheel.exe --no-hooks` and a policy override `HKLM\SOFTWARE\Policies\DropWheel\DisableHooks=1` (and `DisableDownloads=1`) so IT can enforce it. 3) **No downloads:** setting "Download helper tools automatically" (default On); when Off or disabled by policy, only use tools found on PATH, from winget, or set by the user. Never download executables. 4) **Portable zip** from CI: no installer, no registry, no autostart, no context menu unless the user enables it; settings stored next to the exe when a `portable.txt` marker exists. 5) **SECURITY.md for reviewers:** exactly what DropWheel does on a machine: hooks used and why, files and registry keys written, network endpoints (only the tool downloads, and only when enabled), no telemetry, how to deploy silently (`/VERYSILENT`), and the policy keys above. | open |
| R-21 | P0 (root cause found) | **The "blocked" behaviour was NOT CrowdStrike.** Every file you create in this workspace carries **`Mandatory Label\Low Mandatory Level`** (inherited; verified with `icacls` on `release\DropWheel\DropWheel.exe`), because your Harness sandbox runs at Low integrity. Executables with that label start as LOW-integrity processes, which cannot write to `%TEMP%`, `%APPDATA%` or `%LOCALAPPDATA%` and cannot receive input from medium-integrity windows. That explains the installer's "Access denied" in Temp, the missing settings folder, the failed Pandoc download, the dead gesture hooks and the silent exit. | 1) Never ship or hand over binaries built in this workspace. Releases come ONLY from GitHub Actions (R-19); the owner downloads them from GitHub. 2) The app must detect when it runs at Low integrity (`GetTokenInformation(TokenIntegrityLevel)`): show a clear message ("DropWheel was started with low integrity, so it can't watch the keyboard or save files. Copy it to a normal folder or reinstall."), log it, and exit cleanly. 3) Add this to the `--diagnose` report (R-18). 4) Correct STATUS.md and SECURITY.md wherever they blame CrowdStrike. | open |
| R-22 | P2 | The workspace feels scattered (owner). | See R-22 details: one clean tree, prompts in docs/prompts, local-only stuff in `_local\`, junk deleted. | open |
| R-15 | P2 | Owner tested the portable build on his PC: it runs and the violet theme is correct. But Settings → Language still shows **Čeština** (CHANGES.md claims Czech was removed), and the header uses the old citrus icon (R-14). | Remove Čeština from the language picker now; R-12 replaces it with: System / العربية / English. Grep for "cs", "Čeština" and "Czech" in src/ and installer/ → 0 hits, except third-party licence files. | open |
| R-16 | P1 | The owner's first real conversion failed. The toast "Downloading Pandoc 35 MB" ended with "Cannot write to the target folder. Choose another output folder in Settings." That message is WRONG: the failure is in the helper download into `%LOCALAPPDATA%\DropWheel\tools`, not in the output folder, so the user is sent to fix the wrong setting. Likely cause on this PC: the corporate EDR (CrowdStrike Falcon) blocks unsigned processes from writing .exe files there. | 1) Separate the errors: tool download/install failures must say which tool, the exact path and the OS error (e.g. "Couldn't save Pandoc to C:\…\tools (Access denied)"), plus actions: Retry · Open folder · Use my own Pandoc… (a file picker to point to an existing pandoc.exe/ffmpeg.exe) · Copy details. 2) Before downloading, test write access to the tools folder; if it's denied, fall back to `%LOCALAPPDATA%\Programs\DropWheel\tools`, then to a user-chosen folder. 3) Detect tools already on PATH or installed by winget (Gyan.FFmpeg, JohnMacFarlane.Pandoc) BEFORE offering a download. 4) Log the full exception with the path to `%APPDATA%\DropWheel\log.txt`. Add tests for the error-message mapping. | open |
| R-11 | P2 | STATUS.md "Next" still says `Faisaldhd/dropwheel-releases`. That is outdated: we use ONE public repo. | `UpdateConfig.Repository = "Faisaldhd/dropwheel"` (see `UPDATE_PROMPT.md` and `COLLAB.md`). | open |

### R-2 details (framework-dependent)
- Publish `--self-contained false` for win-x64, and keep `DebugType=none`.
- **Installer:**
  - Detect the .NET 10 Desktop Runtime (x64): check `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App`, or run `dotnet --list-runtimes`.
  - If it is missing, show one clear page ("DropWheel needs the free Microsoft .NET 10 Desktop Runtime, about 55 MB, installed once"), then download the official installer from Microsoft (`https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe`), verify its Authenticode signature, and run it with `/install /quiet /norestart`.
  - On any failure, fall back to opening the download page.
- **Keep the native libraries the app really uses** (Magick, Skia, pdfium). Remove `Microsoft.Windows.SDK.NET.dll` if the WinRT surface (OCR/Share) can be reached through a narrower target framework, e.g. `net10.0-windows10.0.19041.0` with `<WindowsSdkPackageVersion>` and trimming of the projection, or through `[ComImport]` interop.
- **Report** the publish folder size and setup size before and after. Target: setup ≤ 30 MB.
- **Test** on a clean machine or VM without .NET: the installer must install the runtime and the app must start. If you can't test that, write the exact manual steps in STATUS.md for the owner.

### R-12 details (Arabic + English, polished)
1. **Infrastructure**
   - All user-visible text goes through one localization layer (`Loc`), with string tables in `src/FileAction.Core/Strings/en.json` and `ar.json`, and the same keys in both. Zero hard-coded UI strings: grep for string literals in WPF code and fix every hit.
   - Add a unit test that fails if any key is missing in either language, or if any `{0}` placeholder doesn't match between the two.
2. **Language setting:** Settings → Language with **System / العربية / English**. "System" picks Arabic when the Windows display language is Arabic, otherwise English. Switching takes effect immediately, without a restart.
3. **Right-to-left (Arabic)**
   - Set `FlowDirection=RightToLeft` on every window, dialog, progress card, shelf and tray menu.
   - Mirror the layouts: close buttons, icons before text, progress direction.
   - In the wheel: slice labels and the centre text are RTL. The wheel geometry is NOT mirrored, but the slice order goes clockwise from the top in both languages.
   - Keep these LTR inside Arabic text: file names, paths, format names (MP4, PDF), numbers, sizes (MB), times. Use Unicode isolation (U+2066..U+2069) so they don't jump around.
4. **Typography:** use a font with good Arabic glyphs. Segoe UI renders Arabic well on Windows 10/11; check that the bold and semibold weights render. Arabic text must never be clipped: increase line height where needed and test at 100%, 150% and 200% DPI.
5. **Arabic quality (very important)**
   - Use natural Modern Standard Arabic, short and clear like a native app. No word-for-word translation.
   - Keep a glossary (`docs/glossary-ar.md`) and use it consistently, for example:
     - Convert = تحويل · Compress = ضغط · Trim = قص · Merge = دمج · Split = تقسيم
     - Rotate = تدوير · Copy = نسخ · Move = نقل · Copy path = نسخ المسار · Terminal = الطرفية
     - Float = تثبيت عائم · Share = مشاركة · Settings = الإعدادات · Licenses = التراخيص
     - "Saved next to the original" = حُفظ بجانب الملف الأصلي
   - Keep format names in Latin letters (MP4, JPG, PDF).
   - Use correct Arabic plural and dual forms for counts (ملف / ملفان / ٣ ملفات / ١١ ملفًا). Implement a small Arabic plural helper. Numerals: Western digits (0-9) by default.
   - Write the Arabic error messages properly; they must not read like machine translation.
6. **Installer:** Inno Setup with exactly two languages, Arabic and English. Use `Arabic.isl`; if it isn't bundled with your Inno version, take it from the official Inno Setup translations. Make the installer wizard RTL in Arabic. Remove Czech completely.
7. **README:** English `README.md`, plus `README.ar.md` with a short guide in Arabic.
8. **Review loop:** when done, write `docs/ar-strings-review.md` listing every Arabic string next to its English source. Claude will review the Arabic and send corrections in REVIEW.md.
9. **Self-test:** `--self-test` renders every window in BOTH languages (en/ar) and both themes, so the owner can check the RTL layout from the screenshots.

### R-13 details (open-source readiness; do LAST, after UPDATE_PROMPT)
- Keep `LICENSE` as MIT (Metamorfolog line + our line). State "Free and open source (MIT)" in README.md, README.ar.md and the About text.
- Add `CONTRIBUTING.md` in English and Arabic: how to build (`build.cmd`), the test command, code style, the rule "one feature per PR with tests", how to add or translate strings (en.json/ar.json + glossary), and that all contributions are under MIT.
- Add `.github/ISSUE_TEMPLATE/bug_report.yml` (Windows version, DropWheel version, steps, expected/actual, `%APPDATA%\DropWheel` log attached) and `feature_request.yml`, plus `.github/pull_request_template.md`.
- Add a `CODE_OF_CONDUCT.md` (Contributor Covenant 2.1).
- Add `SECURITY.md`: how to report vulnerabilities privately (GitHub private vulnerability reporting), plus a short note on signed updates.
- Turn on GitHub Discussions only if the owner agrees (ask in STATUS.md).
- Optional, ask the owner first in STATUS.md: prepare a short summary of generic fixes and the Arabic translation that could be offered upstream to Metamorfolog/file_action as a goodwill contribution. Do NOT open PRs upstream without the owner's approval.

### R-22 details (workspace tidy-up; owner: "it feels scattered")
**Two copies exist.** Your active workspace is `C:\Users\…\Downloads\DropWheel_1` (your new session cloned the repo there). `Desktop\DropWheel_1` is an OLDER copy: Claude had been writing REVIEW.md there, which is why you only got R-14 to R-22 through chat. From now on the Downloads copy is the ONLY one. Claude writes REVIEW.md into `Downloads\DropWheel_1\DropWheel\`.
- Copy `Desktop\DropWheel_1\TestData` and `Desktop\DropWheel_1\tools` (dotnet-sdk.zip, Inno, 7zr) into `Downloads\DropWheel_1\_local\` if you need them; nothing else on the Desktop copy is needed (everything is on GitHub). Then tell the owner in STATUS.md: "Desktop\DropWheel_1 can be deleted."
- When the R-19/R-12/R-2 worktrees (`wt-*`) are merged, remove them with `git worktree remove` so the workspace root stays clean.
Target layout. Move with `git mv` inside the repo and plain moves outside it, then delete the junk:

```
DropWheel_1\                      (Harness workspace — keep this name)
├── README.md                     short map of this folder (rewrite it to match the tree below)
├── DropWheel\                    THE git repo (pushed to GitHub) — source only
│   ├── README.md · README.ar.md · LICENSE · THIRD-PARTY-NOTICES.md · CHANGES.md
│   ├── STATUS.md · REVIEW.md · COLLAB.md       (team files stay at the root)
│   ├── src\  tests\  installer\  scripts\  licenses\  branding\
│   ├── docs\
│   │   ├── prompts\    FIX_PROMPT.md, THEME_PROMPT.md, UPDATE_PROMPT.md  (git mv; update the links in COLLAB/REVIEW)
│   │   ├── glossary-ar.md, signing.md, ar-strings-review.md …
│   │   └── screenshots\  (CI output only)
│   ├── build.cmd · release.cmd · FileAction.sln · nuget.config · .gitignore
│   └── (ignored, local only) .appdata\ .dotnet-cli\ .nuget\ .tmp\ artifacts\ tools\
├── _local\                       NOT in git, local machine stuff
│   ├── sdk\        dotnet-sdk.zip, innosetup*.exe, 7zr.exe, 7zextra* (from DropWheel_1\tools)
│   ├── testdata\   (from DropWheel_1\TestData)
│   └── old-builds\ (from DropWheel_1\release; these are Low-integrity local builds, see R-21; delete when CI works)
```

Delete:
- `DropWheel_1\FileAction\` (leftover)
- `DropWheel_1\Claude outputs\`
- `DropWheel\release\` (empty)
- `DropWheel_1\release\DropWheel-fd\` (experiment)
- `tools\pylibs\`, unless a script really needs it

Update `build.cmd` and `release.cmd` to the new SDK path (`..\_local\sdk`). Run a build and the tests after the move, so nothing breaks. One commit: "chore: tidy workspace layout".

## NEXT (in order)
0. R-22 (tidy-up, 10 minutes) and R-19 (CI on GitHub Actions, P0; stop running builds on this PC), then R-18 (crash/diagnostics, P0), R-20 (enterprise-friendly mode), R-14 (icon), R-15 (Czech leftovers) and R-16 (wrong error + tool download fallback) first: the owner is testing now.
1. R-10: remove the old screenshots from README now. Commit + push.
2. R-11: fix the repository name in STATUS.md / any config.
3. R-2 (framework-dependent) as above. Commit + push.
4. R-12 (Arabic + English).
5. R-5: add the script; it must capture both languages. Mark it "needs owner" in STATUS.md.
6. `UPDATE_PROMPT.md` (signed auto-update from `Faisaldhd/dropwheel` releases).
7. R-13 (open-source readiness).
