# Lock badge assets

`generate.ps1` deterministically regenerates the purple Lock badge artwork used by the 2.4.0 Lock tool.
It needs only Windows PowerShell 5.1 (System.Drawing), no packages and no network.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\lock-icons\generate.ps1
```

Outputs:

- `src/FileAction.App/Assets/Lock/locked-{document,image,video,archive,other,folder,badge}.ico`
  with PNG frames at 16, 24, 32, 48, 64 and 256 px; each size is drawn at that size (no downscaled
  256 px frame).
- `docs/screenshots/lock-badge/{document,folder}-{white,dark}-{48,256}.png` as design evidence.

Design rules (owner mockup, 2026-10-20): two-tone purple padlock in the **bottom-left** corner, body
split vertically (#A012D8 left / #6A0DA8 right), lavender highlight, thick round shackle, white
keyhole, about 40-45% of the icon height, no blur/drop shadow. The base stays recognisable: yellow
Windows folder, document page, or the image/video/archive/other type glyph.

The assets are embedded in the app (`FileAction.App.Lock.*`); `LockIcons` never draws a scaled copy at
runtime. The `.dwenc` DefaultIcon is the document variant because Windows allows one DefaultIcon per
extension; the original-file-type icon is composed from the shell icon for the tool window/preview.
