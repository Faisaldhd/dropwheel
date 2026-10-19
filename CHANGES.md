# DropWheel 2.1.0 — CHANGES
## 2.1.0

Company-safe release.

- **Keyboard and mouse monitoring is now OFF by default.** With it off, DropWheel installs no global hooks
  at all; the wheel opens from the tray menu, the Explorer context menu or by dropping files. Turn it on in
  Settings > Company mode (it applies immediately), or pass `--no-hooks`.
- **Helper-tool downloads are now OFF by default.** DropWheel only uses FFmpeg/Pandoc found on PATH, installed
  by winget, or chosen by the user. Tool errors now name the tool, the folder and the OS error instead of
  blaming the output folder.
- **IT policy:** `HKLM\SOFTWARE\Policies\DropWheel\DisableHooks=1` and `DisableDownloads=1` lock the two
  switches machine-wide.
- Portable zip (self-contained, no admin, no registry) and a tag-triggered GitHub release.
- New `SECURITY.md` and `--diagnose` report for security reviews; low-integrity start is detected and refused
  with an explanation.
- Crash report file + fallback log locations, Arabic and English UI, brand icons.


DropWheel is the independent continuation of the MIT-licensed **FileAction** project. This is the first release where
nothing points back upstream: no update checks, no GitHub links, no automatic merges.

## Removed

- **Auto-update, completely**: `Updates.cs`, `Updater.cs`, the startup check, the tray item, the notifications, the
  `--updated` argument and its installer run entry, the "check for updates" setting, and the matching tests.
- **Every link to the original project**: README sections (Releases, GitHub Actions, self-update), `.github/workflows`,
  and the `origin` git remote. `grep -i github src/` → 0 hits; the only network calls left are the on-demand FFmpeg
  (gyan.dev) and Pandoc (api.github.com, the publisher's release API) downloads, both SHA-256 verified.
- **Czech UI**: `README.cs.md`, the Czech screenshots, the installer's Czech language and the Czech half of every string.
  `Loc.T()` now always returns English. An Arabic UI can be added the same way later.
- **Windows Forms**: the tray icon is now `Shell_NotifyIcon` + a WPF `ContextMenu`, so `UseWindowsForms` is gone and
  ~19 MB of `System.Windows.Forms*.dll` no longer ship.

## Rebranded

- User-visible identity only: product, exe, windows, tray, installer and README are **DropWheel**. Internal
  project/namespace names stay FileAction.* by owner rule (renaming them adds risk without user value).
- Paths: `%APPDATA%\DropWheel` (settings), `%LOCALAPPDATA%\DropWheel` (tools), `Downloads\DropWheel` (inbox).
  A one-time migration moves the old `FileAction` folders and the old autostart value on first start.
- Registry: the autostart value, the Explorer context-menu verb, the single-instance mutex and pipe names, the AUMID.
- New icon and name everywhere (windows, tray, About, installer, Start menu); the installer uses a new `AppId`
  (`{1AB9CDBE-C50D-484B-96ED-5D575D0806D2}`) so it can never collide with the original app.
- Licence: MIT text kept verbatim with `Copyright (c) 2026 Metamorfolog` (required) plus `Copyright (c) 2026 DropWheel`.

## Added since 1.4.0

- **Drop anywhere**: releasing the wheel over another folder saves the result there; over the source folder it stays
  beside the original.
- **Previews**: an image thumbnail in the image tool dialogs, a five-frame FFmpeg filmstrip plus duration in Trim and
  Speed.
- **PDF → XLSX (tables only)**, with a clear "No tables found in this PDF." failure instead of an empty workbook.
- **Violet Glass theme**: violet/lavender accent, new dark and light palettes, and the new brand icon.

## Sizes

| | Size | Files |
|---|---|---|
| Publish folder | 219.3 MB | 293 |
| `DropWheel-Setup-2.0.0.exe` | **63.9 MB** (66,994,074 bytes) | — |

`DebugType=none`, and `createdump.exe`, `Microsoft.DiaSymReader.Native.amd64.dll` and the duplicate `mscordaccore.dll`
are stripped from the release folder.

## Verification

- `dotnet test tests\FileAction.Tests`: **50 passed, 0 failed**.
- REA `inspect_managed_artifact` on `release\DropWheel\DropWheel.dll`:
  assembly **DropWheel 2.0.0.0**, .NET 10, 161 types / 968 methods,
  SHA-256 `ed378ca82490e60b939612029bbf0e522fc9cca1abd4c8c0f9c97f81b6c29c45`.
- REA `inspect_pe_resources` on `DropWheel-Setup-2.0.0.exe`: PE32, 66,967,800 bytes,
  SHA-256 `B7B9095A51AF2AC3802176D2D0DDCEE2E873768C7E885DD2A40C6008DC59AAB1`,
  `RT_GROUP_ICON` with nine images (16, 20, 24, 32, 40, 48, 64, 128, 256 px);
  version resource: ProductName/CompanyName **DropWheel**, ProductVersion **2.0.0**.

## Not done / known limits

- The simplified 16 px tray icon variant and the light/dark tray switching are not implemented yet; the tray uses the
  full application icon.
- `--self-test` screenshots for `docs\screenshots\` were not re-generated in this environment.
- Acrylic/mica on the Settings window (Windows 11) is not implemented.
- PDF → XLSX depends on a LibreOffice build that can import PDF tables; the LibreOffice on the build machine cannot, so
  the operation fails cleanly there.

## Brand update (Violet solid, no glass)

- Accent is now #8224D4 with #B48AE0 outlines; the accent gradient is a solid fill everywhere (selected slice,
  active tab, progress bars), so no glass/frosted look remains.
- Dark palette slices #241A35, page #181220; glow reduced to BlurRadius 14 (0.45 dark / 0.3 light); dark is the
  default theme.
- Tray icons: new flat 8-slice wheel, DropWheel-tray-dark.ico / DropWheel-tray-light.ico, chosen at startup from
  HKCU\...\Themes\Personalize\SystemUsesLightTheme (live switching not implemented yet).
- Brand icon (randing\DropWheel.ico, 16-256) is embedded in the exe and the installer.
