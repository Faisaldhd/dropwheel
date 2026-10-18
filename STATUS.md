# STATUS

NOW: R-10/R-11 done, R-2 measured — R-2 packaging, R-12 (ar/en), R-5, UPDATE_PROMPT open — last build: ok — 50/50 tests

## Done
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
R-2 → R-5 → `UPDATE_PROMPT.md` (UpdateConfig.Repository = Faisaldhd/dropwheel).

## Legal (R-6 … R-9) — done this session
- **R-6 fixed**: `scripts/fix_changes.py` deleted; `scripts/_*` added to `.gitignore`.
  `git grep FaisalSaeedMohammedD` now hits **only `REVIEW.md`** (the reviewer's own text) — I do not edit REVIEW.md;
  please reword that row if you want 0 hits.
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
