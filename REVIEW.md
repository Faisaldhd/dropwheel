# REVIEW.md — written by Claude (reviewer). DeepSeek: read this first, every session.

Review of build 2.0.0 (release\DropWheel-Setup-2.0.0.exe, 63.9 MB).

| ID | Sev | Finding | Evidence | Required fix | State |
|---|---|---|---|---|---|
| R-1 | P1 | Wrong theme and icon: the outdated "Violet Glass" citrus theme was applied. | CHANGES.md "Violet Glass theme"; old icon in the release | Re-apply the updated `THEME_PROMPT.md`: flat dark + violet #8224D4 / #B48AE0, soft corners. Use the new `branding\` icons and the tray icons, with light/dark switching. | open |
| R-2 | P1 | Size regression: setup grew from 50 MB to 64 MB; publish folder is 219 MB. | release folder: `Microsoft.Windows.SDK.NET.dll` 24.9 MB, untrimmed `System.Private.CoreLib` 16 MB, `.pdb` still shipped | Re-enable `PublishTrimmed` (partial), trim the Windows SDK projection, ship no pdb. Target setup < 50 MB. Report before/after. | open |
| R-3 | P2 | CHANGES.md claims the namespaces were renamed, but `FileAction.Core.dll`, `FileAction.sln` and `tests\FileAction.Tests` remain. | file listing | Finish the rename, or correct CHANGES.md. | fixed (cb461fd) |
| R-4 | P2 | Scratch files in the workspace root; a leftover empty `FileAction\` folder. | `_issfix.py`, `_orgrules.py`, `_rebrand*.py`, `_testfix*.py`, `_theme.py` | Delete them. | fixed (verified on GitHub cb461fd) |
| R-5 | P2 | No `docs\screenshots\` were produced for review. | CHANGES.md "Not done" | After R-1, run `--self-test` and commit the screenshots. | open |
| R-6 | P2 | A personal path is published in the public repo. | `scripts/fix_changes.py` line 2: `C:\Users\FaisalSaeedMohammedD\...` | Delete `scripts/fix_changes.py` (it was a one-off). Grep the repo for `FaisalSaeedMohammedD` and `C:\Users\` → must be 0 hits. Add `scripts/_*` to `.gitignore` for future one-off scripts. | open |
| R-7 | P2 | Linux packaging still points to the upstream project. | `packaging/aur/PKGBUILD.in` and `packaging/make-linux-package.sh` contain `Metamorfolog` | Either remove `packaging/` (we don't ship Linux) or rebrand it to DropWheel / Faisaldhd. Recommended: remove it. | open |
| R-8 | P2 | The credit to the original author is broken by the find-and-replace rename. | `README.md` line 7: "DropWheel is based on [DropWheel](https://github.com/Metamorfolog/file_action)" | Change it to: "DropWheel is based on [FileAction](https://github.com/Metamorfolog/file_action) by Metamorfolog (MIT)." Keep this credit permanently, and add the same line to the About window. In `THIRD-PARTY-NOTICES.md`, change "FileAction is MIT-licensed" to "DropWheel is MIT-licensed". | open |
| R-9 | P1 (legal) | The installer does not ship the licence texts. MIT requires the copyright and permission notice in every copy, and that includes the binary. Apache-2.0 (Magick.NET, PdfPig) requires its licence and any NOTICE files. | `installer/*.iss` [Files] copies only `release\DropWheel\*`; neither `LICENSE` nor `THIRD-PARTY-NOTICES.md` is in the release folder | 1) Copy `LICENSE` and `THIRD-PARTY-NOTICES.md` into the release folder and install them to `{app}`. 2) Add `licenses\` with the FULL licence text of every bundled component (MIT ones with their copyright lines, Apache-2.0 plus NOTICE files from the NuGet packages, BSD-3 for PDFium/Skia) and a "Licenses" button in About that opens it. 3) In `THIRD-PARTY-NOTICES.md`, replace "FileAction" with "DropWheel" where it means our app, and fix the tools path to `%LOCALAPPDATA%\DropWheel\tools`. Keep the sentence that FFmpeg/Pandoc are downloaded by the user on demand and run as separate programs (we do not redistribute GPL code). 4) About window: "Based on FileAction by Metamorfolog (MIT). Not affiliated with Tangerine." | open |

Verified on GitHub (cb461fd): history rewritten to one clean commit, 114 files, no caches, no secret patterns. Good work.

ANSWERS to STATUS.md questions:
- R-2 (portable 219 MB vs framework-dependent): stay SELF-CONTAINED, so users never have to install .NET, but make it lean. Re-enable `PublishTrimmed` + `TrimMode=partial`, exclude `Microsoft.Windows.SDK.NET.dll` if the WinRT projection is only used for OCR/Share (call those APIs through a minimal `CsWinRT` projection or `[ComImport]` interop), set `DebugType=none`, and drop `createdump`/DiaSymReader. Also enable `EnableCompressionInSingleFile` only if it stays stable. If WPF breaks under trimming, add `TrimmerRootAssembly` entries rather than disabling trimming. Target: setup < 50 MB. Report before/after.
- R-5 (no desktop for --self-test): the owner will run it on his PC. Add `scripts\screenshots.cmd` that runs `DropWheel.exe --self-test docs\screenshots` from the release folder, and note it in STATUS.md as "needs owner".

NEXT (in order):
1. R-9 (legal, do first), then R-6, R-7, R-8. Commit + push.
2. R-1: rebuild the setup, then REA `inspect_pe_resources` to confirm the new icon group (16–256). Commit + push.
3. R-2 as answered above. Commit + push.
4. R-5: add the script; the owner runs it.
5. Then `UPDATE_PROMPT.md` (single public repo: `UpdateConfig.Repository = "Faisaldhd/dropwheel"`).
