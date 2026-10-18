# REVIEW.md — written by Claude (reviewer). DeepSeek: read this first, every session.

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
| R-11 | P2 | STATUS.md "Next" still says `Faisaldhd/dropwheel-releases`. That is outdated: we use ONE public repo. | `UpdateConfig.Repository = "Faisaldhd/dropwheel"` (see `UPDATE_PROMPT.md` and `COLLAB.md`). | open |
| R-12 | P0 | English only in practice: `Loc.T(czech, english)` discards the Czech text at runtime, the strings are hard-coded in 250+ call sites, and there is no RTL or Arabic at all. The owner and his users read Arabic. | Arabic + English: string tables, `Loc.T("key")`, live language switch, RTL layout, Arabic plurals, glossary, docs. Details below. | open |
| R-13 | P2 | The source repository is private and has no open-source housekeeping. | Publish the source publicly with the standard community files and CI. Details below. | open |
| R-14 | P1 | The built exe and installer still carry the OLD icon: `src\FileAction.App\Assets\*.ico` were the pre-brand artwork (violet grapefruit slice) while `branding\` already holds the new flat wheel-and-arrow. | Copy `branding\*.ico` (and the 256 px PNG) over `Assets\`, add a SHA-256 guard so a stale copy fails the build, rebuild. | fixed in ad504ff |

### R-2 details (framework-dependent)
- Publish `--self-contained false` for win-x64, and keep `DebugType=none`.
- **Installer:**
  - Detect the .NET 10 Desktop Runtime (x64): check `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App`, or run `dotnet --list-runtimes`.
  - If it is missing, show one clear page ("DropWheel needs the free Microsoft .NET 10 Desktop Runtime, about 55 MB, installed once"), then download the official installer from Microsoft (`https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe`), verify its Authenticode signature, and run it with `/install /quiet /norestart`.
  - On any failure, fall back to opening the download page.
- **Keep the native libraries the app really uses** (Magick, Skia, pdfium). Remove `Microsoft.Windows.SDK.NET.dll` if the WinRT surface (OCR/Share) can be reached through a narrower target framework, e.g. `net10.0-windows10.0.19041.0` with `<WindowsSdkPackageVersion>` and trimming of the projection, or through `[ComImport]` interop.
- **Report** the publish folder size and setup size before and after. Target: setup ≤ 30 MB.
- **Test** on a clean machine or VM without .NET: the installer must install the runtime and the app must start. If you can't test that, write the exact manual steps in STATUS.md for the owner.

### R-12 details (Arabic + English)
- **Tables first.** Every user-visible string is an entry in `src\FileAction.App\Strings\en.json` and
  `Strings\ar.json`. Code calls `Loc.T("key")`; placeholders use `{0}`, `{1}`… and `Loc.T("key", args)`.
  No two-argument `T(czech, english)` call survives in `src\FileAction.App` or `src\FileAction.Core`.
- **Tested.** A unit test asserts that `en.json` and `ar.json` have exactly the same keys and, per key, exactly
  the same placeholder set, and that no value is empty. A second test asserts that the app has no missing key
  at startup for the default wheel.
- **Arabic quality.** Natural MSA, not word-for-word. Correct plural forms (zero / one / two / few / many / other),
  not just singular/plural. A glossary in `docs\glossary-ar.md` fixes recurring terms; `docs\ar-strings-review.md`
  lists the strings that a native reviewer should confirm. `README.ar.md` mirrors the English README.
- **Bidi and numbers.** File names, sizes, percentages, times and formats are wrapped in Unicode isolates
  (U+2066…U+2069) so they stay readable inside RTL sentences. Technical values keep Western digits.
- **Live switch.** Setting → Language: System / العربية / English. Switching re-renders the wheel, tray menu,
  jobs window and all open dialogs without restart, and persists the choice.
- **RTL.** `FlowDirection` follows the language on every window; the wheel, shelf and job list mirror; alignment
  and padding are direction-aware.
- **DPI.** Check 100 %, 150 % and 200 % scaling after the layout changes.

### R-13 details (open source)
- Source repository public, same single repo `Faisaldhd/dropwheel` (releases stay public too).
- `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`, `.github\ISSUE_TEMPLATE\*` and a PR template.
- GitHub Actions CI on `windows-latest`: restore, build `FileAction.App` + `FileAction.Core` + tests, run tests.
- README gets the CI badge and a "build from source" section; MIT licence and the FileAction attribution stay.
- Zero personal paths, usernames or tokens in the tree (`git grep` clean).

## NEXT (in order)
1. R-2 packaging: framework-dependent publish, installer runtime detect/download/verify/install, size report.
2. R-2 interop: drop `Microsoft.Windows.SDK.NET.dll` (OCR/Share through `[ComImport]` or a narrower target).
3. R-12: Arabic + English (infrastructure, then RTL, then text and glossary).
4. R-5: screenshots script, both languages.
5. `UPDATE_PROMPT.md` (signed auto-update from `Faisaldhd/dropwheel` releases).
6. R-13: open-source housekeeping.
