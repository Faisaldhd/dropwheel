# Lock padlock asset

`generate.ps1` deterministically regenerates the purple Lock padlock (16/24/32/48/64/256 PNG frames).
It needs only Windows PowerShell 5.1 (System.Drawing), no packages and no network.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\lock-icons\generate.ps1
```

Outputs:

- `src/FileAction.App/Assets/Lock/padlock.ico` — transparent, six PNG frames.
- `src/FileAction.App/Assets/Lock/padlock-256.png` — design source for docs/previews.

Design: #8224D4 body, #B48AE0 highlight band, white keyhole, bottom-left when composited, about 43% of
the icon height, crisp at every size. No yellow folder or document illustration is shipped: the app
composes `SHCreateItemFromParsingName` / `IShellItemImageFactory` system icons with this padlock.
