# DropWheel — Fix Prompt (REA audit results)

Context: `DropWheel_1\DropWheel` is our fork of github.com/Metamorfolog/file_action (MIT), rebranded as DropWheel.

An REA audit of `DropWheel-Setup-1.4.0.exe` and the source found the issues below.

**Owner decision:** the app must be fully decoupled from GitHub — no updates from upstream, ever.

Do ALL tasks in order. Build after each task and fix every error before moving on. Never claim something works without evidence (build log, grep output, REA output).

## 1. Remove auto-update completely
- Delete the update check and install flow:
  - `src\FileAction.Core\Updates.cs` and `src\FileAction.App\Shell\Updater.cs`
  - every call site: the startup or timer check, the tray "Update" item, update notifications, and the `--updated` argument handling.
- Remove the "check for updates" option from Settings, together with its setting key.
- In `installer\DropWheel.iss`, remove the `[Run]` line that starts `DropWheel.exe --updated`.

## 2. Remove every link to the original GitHub
- Grep the whole solution for `github.com`, `api.github.com`, `Metamorfolog` and `releases`, and remove every reference on the app side (About window, tray menu, Settings links, README "download from Releases").
- After this, the ONLY network calls left are the on-demand helper downloads:
  - ffmpeg from gyan.dev
  - pandoc

  Keep their SHA-256 verification.
- Git: run `git remote -v`, then remove the upstream remote (`git remote remove origin`). Keep the local history.
- CI: delete `.github\workflows`. They publish releases and push to the AUR under the original author's accounts.

## 3. Installer identity (it must never collide with the original DropWheel)
In `installer\DropWheel.iss`:
- Generate a NEW `AppId` GUID.
- `DefaultDirName={localappdata}\Programs\DropWheel`
- `AppPublisher=DropWheel`
- Use DropWheel paths in `[UninstallDelete]` (for example `{localappdata}\DropWheel\tools`). Keep the app able to find tools that were already downloaded, or migrate them.
- Rename the file to `installer\DropWheel.iss`, and update `build.cmd` and `release.cmd` to match.

## 4. One version number
- Define the version once, in the csproj `<Version>`, and pass that same value to the installer as `AppVersion`.
- Use **2.0.0** for this first independent release.
- Delete the stale `publish\DropWheel.*` output and the old `DropWheel-Setup-1.4.0.exe`.

## 5. Make it lighter
Today the app is ~165 MB unpacked.
- Add `<DebugType>none</DebugType>` so no `.pdb` files ship. Also make sure `createdump.exe`, `Microsoft.DiaSymReader.Native.amd64.dll` and the duplicate `mscordaccore_*.dll` are not shipped.
- Check whether WinForms is really needed. If it's only used for something small (a tray icon, a folder dialog), replace that with WPF or Win32, then remove `<UseWindowsForms>`, so `System.Windows.Forms*.dll` and `System.Windows.Forms.Design.dll` (about 20 MB) are gone.
- Review the large native libraries (`Magick.Native` 25 MB, `libSkiaSharp` 11 MB, `pdfium` 6 MB, `D3DCompiler_47_cor3` 5 MB). Keep only what is actually used. If ImageMagick and Skia overlap, keep one.
- Report the size before and after for both the publish folder and the installer.

## 6. License
- Keep the MIT `LICENSE` text with `Copyright (c) 2026 Metamorfolog`; MIT requires it.
- Add our own copyright line under it.
- Keep `THIRD-PARTY-NOTICES.md`.

## 7. Verify and report (with evidence)
1. Grep: 0 hits for `github` / `Metamorfolog` in `src\`. The only allowed exceptions are LICENSE and the notices.
2. REA on the new `DropWheel.dll`: run `inspect_managed_artifact` with detail "summary" and confirm the assembly name is DropWheel and the version is 2.0.0.
3. REA on `DropWheel.dll`: run `inspect_managed_members` and confirm that no `api.github.com` or `Metamorfolog` user string remains.
4. REA on the new `DropWheel-Setup-2.0.0.exe`: run `inspect_pe_resources` and confirm ProductName and CompanyName are DropWheel.
5. Run the existing unit tests: `dotnet test tests\FileAction.Tests` must pass.
6. Write `CHANGES_2.0.0.md` listing what was removed, what changed, the size numbers, and the REA results.
