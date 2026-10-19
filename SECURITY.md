# Security notes for reviewers (DropWheel)

DropWheel converts files locally. It has **no telemetry, no analytics, no cloud service and no update
checker**; files never leave the PC. The only network requests it can make are the optional helper-tool
downloads below, and those are **off by default**.

This page describes what the shipped app and installer actually do. It was written against
`src/FileAction.App` (`AppSettings.cs`, `Shell/Policy.cs`, `Shell/ShellIntegration.cs`,
`Shell/ToolInstaller.cs`, `Diagnostics/`).

## Defaults

| Capability | Default | Turned on by | Turned off by |
|---|---|---|---|
| Global keyboard/mouse hooks (`WH_KEYBOARD_LL`, `WH_MOUSE_LL`) | **OFF** — no `SetWindowsHookEx` call at startup | Settings > Company mode > Watch mouse and keyboard | `--no-hooks`; `HKLM\SOFTWARE\Policies\DropWheel\DisableHooks=1` |
| Helper-tool downloads (FFmpeg, Pandoc) | **OFF** — no network request is made | Settings > Company mode > Download helper tools, then confirming the download | `HKLM\SOFTWARE\Policies\DropWheel\DisableDownloads=1` |
| Start with Windows | **OFF** — no Run value | Settings > Start with Windows | Removing the value in Settings |
| Explorer context menu | **OFF** — no menu keys | Settings > Explorer context menu | Removing the value in Settings |
| Telemetry / analytics / crash upload / update check | **none** | — | — |

The app writes **nothing to `HKLM`**. It only reads the two policy values above from `HKLM`.

## Files it writes

| Path | When | Notes |
|---|---|---|
| `%APPDATA%\DropWheel\settings.json` | when a setting changes and on exit | On a portable build (`portable.txt` next to `DropWheel.exe`) it is written to the exe folder instead. There is no further fallback: if the folder is read-only the save is silently skipped. |
| `log.txt` | at every start | In the first writable folder of: the settings folder, `%LOCALAPPDATA%\DropWheel`, `%TEMP%\DropWheel`, the exe folder. Startup steps, hook result and errors are appended as text. |
| `crash-<yyyyMMdd-HHmmss>.txt` | only after an unhandled exception | In the active log folder. Contains the exception, stack trace and the last 80 log lines; it does not contain converted files or their contents. |
| `diagnose-<yyyyMMdd-HHmmss>.txt` | only when `--diagnose` is run | In the active log folder, or at the path given after `--diagnose`. Opened in the default text viewer. |
| `%LOCALAPPDATA%\DropWheel\tools\ffmpeg.exe`, `pandoc.exe` | only when downloads are enabled and the user confirms | Fallback folder after a write test: `%LOCALAPPDATA%\Programs\DropWheel\tools`, then `%TEMP%\DropWheel\tools`. On a portable build downloaded tools still go to `%LOCALAPPDATA%`, not next to the exe. |
| `%TEMP%\dropwheel-*` | while a tool download runs | The downloaded archive; deleted in a `finally` block. |
| `.<name>.<id>.part.<ext>` next to the output | during a conversion | Hidden partial file; renamed to the final name on success, deleted on failure. |
| `Downloads\DropWheel\` | only for a drop that is not already a file (browser image, Outlook attachment, clipboard screenshot) | The file is saved there first and the result is written next to it. |
| result files | when a conversion runs | Next to the original file, or in the output folder chosen in Settings. The original is never modified. |

It also reads only what it needs: the files you drop or paste, the active File Explorer selection and the
clipboard when you explicitly open the wheel for the clipboard. The clipboard is not monitored continuously.

## Registry keys

By default the app writes **no registry keys at all**, portable or installed. The two optional integrations
are per-user and can be removed again from Settings:

- **Start with Windows** writes
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value name `DropWheel`, data
  `"<path>\DropWheel.exe" --background`.
- **Explorer context menu** writes
  `HKCU\Software\Classes\*\shell\DropWheel` (files) and
  `HKCU\Software\Classes\Directory\shell\DropWheel` (folders), each with the display name `DropWheel…`,
  `Icon`, `MultiSelectModel=Player`, and a `command` subkey containing
  `"<path>\DropWheel.exe" --wheel "%1"`.

`DropWheel.exe --unregister` deletes both menu roots and the Run value; the uninstaller calls it. At
startup the app re-points an already-enabled entry at the current exe if the folder was moved.

**Installer only** (the portable zip never does this): a standard per-user Inno Setup install creates the
Start-menu shortcuts `DropWheel` and `DropWheel Licenses`, and Inno's uninstall entry under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\{1AB9CDBE-C50D-484B-96ED-5D575D0806D2}_is1`.
The uninstaller removes the app, those shortcuts, `%LOCALAPPDATA%\DropWheel\tools` and the two optional
HKCU integrations. It leaves `%APPDATA%\DropWheel` (settings and log) in place; delete that folder manually
to remove every trace.

The app reads the Windows theme preference from
`HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize` and never writes it.

## Input hooks

With the default (`WatchGestures=false`) the app installs **no hooks at all**. The wheel can still be
opened with `Ctrl+Alt+Space` (`RegisterHotKey`, not a hook), from the tray menu, or from the Explorer
context menu once that opt-in is enabled.

When the user enables **Settings > Company mode > Watch mouse and keyboard**, the app installs two
low-level hooks on a dedicated thread:

- `WH_KEYBOARD_LL` — ignores injected keys and looks only at Shift/Ctrl/Alt and Esc, to notice a modifier
  tapped and released on its own while the left mouse button is dragging. It does not record keystrokes
  and does not send them anywhere. When Alt is a wheel key it may inject a dummy key so a bare Alt tap
  does not open the menu bar of the window being dragged over.
- `WH_MOUSE_LL` — tracks left/right/middle button state and the cursor position, to know whether a drag is
  in progress and where to put the wheel.

Both callbacks always call `CallNextHookEx`, and exceptions are swallowed so the system hook chain is
never broken. The `--no-hooks` command-line flag and the `DisableHooks` policy stop the hooks even if the
setting is on; the setting applies immediately, without a restart.

`--diagnose` is the one exception to “no hooks at all”: to test whether the machine permits them it
installs a `WH_KEYBOARD_LL` hook, immediately removes it, and records only the Win32 result. It does not
record input.

## Network endpoints

With downloads OFF (the default) DropWheel makes **no network requests**. When downloads are ON and the
user confirms, it uses `HttpClient` for exactly two tools:

| Tool | Endpoint | Integrity check |
|---|---|---|
| FFmpeg | `https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.7z` and the sibling `.sha256` | always: the archive's SHA-256 must equal the published `.sha256` line |
| Pandoc | `https://api.github.com/repos/jgm/pandoc/releases/latest`, then the `browser_download_url` of the `pandoc-*-windows-x86_64.zip` asset (on `github.com`, redirected to GitHub's asset host) | the `sha256:` digest from the GitHub release metadata when the API provides one; if it does not, the download proceeds without a hash check |

No other hosts are contacted: there is no telemetry endpoint, no update service, no license server, no
account login. Even with the setting on, a download only starts after an explicit user action: dropping a
file on a slice for a missing tool (then answering Yes), or clicking Download in Settings > Helper
programs. The installer is separate: if the .NET 10 Desktop Runtime is missing, Setup downloads
`https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe` from Microsoft, uses
`scripts/verify-authenticode.ps1` to check that its Authenticode signature is valid and the signer is
Microsoft Corporation, and runs it elevated.

## What it does not do

- No telemetry, analytics, crash upload, usage counters or “phone home”.
- No automatic update check, download or installation.
- No background network activity when downloads are off.
- No Windows service, driver, scheduled task, firewall rule, certificate store change or WMI provider.
- No shell extension DLL and no machine-wide registration; the context menu is a per-user registry verb.
- No administrator rights for the app or the portable zip. The only elevation is Microsoft's .NET
  runtime installer, and only when Setup needs it.
- No reading of files other than the ones the user drops, pastes or selects.

## Silent deployment

The framework-dependent installer is a standard Inno Setup package:

```bat
DropWheel-Setup-<version>.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS
```

- Per-user install into `%LOCALAPPDATA%\Programs\DropWheel`; the app itself needs no admin rights.
- Setup checks for the .NET 10 Desktop Runtime. If it is missing, it downloads Microsoft's installer,
  checks the Authenticode signature, runs it with `/install /quiet /norestart` (UAC applies to that step
  only) and continues. A silent deployment therefore needs internet access on a machine without .NET 10,
  or the runtime pre-installed.
- `/CLOSEAPPLICATIONS` is backed by `CloseApplications=force` in the script, so a running DropWheel is
  closed before files are replaced.
- Uninstall removes the app, its shortcuts and downloaded tools from its default tools folder, and calls
  `--unregister`; settings and logs in `%APPDATA%\DropWheel` are left behind.

The portable zip is the no-install alternative: no registry, no shortcuts, no services, no admin, and
settings next to the exe when `portable.txt` is present.

## Diagnostics and release verification

`DropWheel.exe --diagnose [file]` writes a text report and opens it. The report contains the app version,
Windows version, process integrity level, a write test for each candidate folder, a keyboard-hook probe
with the Win32 error, the FFmpeg/Pandoc/LibreOffice paths found, relevant environment variables and the
last 80 log lines. It is local-only; nothing is uploaded. Review it before sending: it contains local
paths, the user name and recent file names from the log.

Releases are built by GitHub Actions and published with `SHA256SUMS.txt`. Compare the downloaded asset
with that file before running it:

```powershell
Get-FileHash ".\DropWheel-<version>-portable-win-x64.zip" -Algorithm SHA256
```

The binaries are **unsigned for now** (see [docs/signing.md](docs/signing.md)), so SmartScreen may warn on
first run. A security team can allow-list the exe by hash until SignPath signing is in place.
