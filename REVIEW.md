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

### R-2 details (framework-dependent)
- Publish `--self-contained false` for win-x64, and keep `DebugType=none`.
- **Installer:**
  - Detect the .NET 10 Desktop Runtime (x64): check `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App`, or run `dotnet --list-runtimes`.
  - If it is missing, show one clear page ("DropWheel needs the free Microsoft .NET 10 Desktop Runtime, about 55 MB, installed once"), then download the official installer from Microsoft (`https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe`), verify its Authenticode signature, and run it with `/install /quiet /norestart`.
  - On any failure, fall back to opening the download page.
- **Keep the native libraries the app really uses** (Magick, Skia, pdfium). Remove `Microsoft.Windows.SDK.NET.dll` if the WinRT surface (OCR/Share) can be reached through a narrower target framework, e.g. `net10.0-windows10.0.19041.0` with `<WindowsSdkPackageVersion>` and trimming of the projection, or through `[ComImport]` interop.
- **Report** the publish folder size and setup size before and after. Target: setup ≤ 30 MB.
- **Test** on a clean machine or VM without .NET: the installer must install the runtime and the app must start. If you can't test that, write the exact manual steps in STATUS.md for the owner.

## NEXT (in order)
1. R-10: remove the old screenshots from README now. Commit + push.
2. R-11: fix the repository name in STATUS.md / any config.
3. R-2 (framework-dependent) as above. Commit + push.
4. R-5: add the script. Mark it "needs owner" in STATUS.md.
5. `UPDATE_PROMPT.md` (signed auto-update from `Faisaldhd/dropwheel` releases).
