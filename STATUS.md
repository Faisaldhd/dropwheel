# STATUS

NOW: R-4/R-3 done, R-1 rebuilt (violet 2.0.0) — R-2/R-5 open — last build: ok — last commit: see git log

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
- **R-1 fixed**: flat dark+violet theme applied (`#8224D4` / `#B48AE0`), accent gradient replaced by solid fill,
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
R-2 → R-5 → `UPDATE_PROMPT.md` (UpdateConfig.Repository = Faisaldhd/dropwheel-releases).
