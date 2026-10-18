# STATUS

NOW: R-14 fixed (brand icons + SHA-256 guard) — R-2 (packaging + interop) and R-12 (ar/en) in progress with 4 sub-agents — last build: ok — 57/57 tests

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
