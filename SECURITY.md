# Security notes for reviewers (DropWheel)

DropWheel converts files locally. It has **no telemetry, no analytics and no cloud service**; files never
leave the PC.

## What it installs or changes

| Thing | When | Can be disabled by |
|---|---|---|
| Keyboard/mouse hooks (`SetWindowsHookEx`, WH_KEYBOARD_LL + WH_MOUSE_LL) | only if **Settings > Company mode > Watch mouse and keyboard** is ON | default OFF; `--no-hooks`; `HKLM\SOFTWARE\Policies\DropWheel\DisableHooks=1` |
| Helper downloads (FFmpeg, Pandoc) | only if **Settings > Company mode > Download helper tools** is ON | default OFF; `HKLM\SOFTWARE\Policies\DropWheel\DisableDownloads=1` |
| `%APPDATA%\DropWheel\` (settings.json, log.txt) | always | falls back to `%LOCALAPPDATA%`, `%TEMP%`, then next to the exe |
| `%LOCALAPPDATA%\DropWheel\tools\` | only when downloads are enabled | falls back to `%LOCALAPPDATA%\Programs\DropWheel\tools`, then TEMP |
| Explorer context menu entry, autostart, Start-menu shortcut | first run of the installed build | Settings; the portable zip never registers anything |

The portable zip registers nothing, needs no administrator rights and can be deleted by removing the folder.
When `portable.txt` sits next to `DropWheel.exe`, settings stay in that folder.

## Network endpoints

Only the two tool downloads: `www.gyan.dev` (FFmpeg) and `api.github.com` / `github.com` (Pandoc). Both are
checked against the publisher's SHA-256 before use. With downloads OFF (default) the app makes no network
requests at all.

## Silent deployment

`DropWheel-Setup-x.y.z.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS`. The installer is
per-user (no admin) and installs the .NET 10 Desktop Runtime only when it is missing (that step needs UAC).

## Reporting a problem

Run `DropWheel.exe --diagnose` and send the text file it opens. It lists the integrity level, folder
permissions, hook installation result and the tools found.