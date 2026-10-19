# DropWheel

[![CI](https://github.com/Faisaldhd/dropwheel/actions/workflows/ci.yml/badge.svg)](https://github.com/Faisaldhd/dropwheel/actions/workflows/ci.yml)

**Click-free file conversion for Windows.** Grab a file, tap a key while dragging, and drop it on a format in a radial wheel. Everything runs locally; your files never leave your PC.

Downloads are built by GitHub Actions (`.github/workflows/ci.yml`): every push builds the app, runs the test suite, and produces the installer, the portable zip and a `--diagnose` report as artifacts. Nothing is built or run on the owner's machine.

DropWheel is based on [FileAction](https://github.com/Metamorfolog/file_action) by Metamorfolog (MIT). It is an independent Windows take on the idea and is not affiliated with Tangerine.

## How it works

1. Grab a file: in File Explorer, on the desktop, an image or link in your browser, an e-mail attachment, a file inside a ZIP folder…
2. While dragging, **tap** a key:

   | Key | Wheel | What it offers |
   |---|---|---|
   | **Shift** | Convert | JPG, PNG, WebP, MP4, MP3, DOCX, PDF, XLSX… depending on the file type |
   | **Ctrl** | Tools | compress, shrink, rotate, trim, merge, split, archive… |
   | **Alt** | Actions | copy to…, move to…, copy path, terminal, floating shelf, share |

3. Drop the file on a slice. The result is saved next to the original (the original is never changed). Drop it in the middle of the wheel to cancel.

The wheel stays open until you drop, so you can release the key and tap another one to switch wheels. Keys can be reassigned in Settings.

**Repeat the last action:** tap the same key again while dragging (or press **R** on an open wheel) and the wheel turns into a single slice that repeats what you did last, with the same settings (e.g. the same trim times).

**Your own wheel:** in **Settings → Customize wheels** you can star favourite slices (they always come first, in the same place) and hide the ones you never use.

**No mouse:** select files in File Explorer, or copy a file or a screenshot (**Win + Shift + S**), and press **Ctrl + Alt + Space**. Arrows pick a slice, Enter runs it, Tab switches wheels, Esc closes, 1–9 run a slice directly.
You can also open the wheel with **right-click → DropWheel…** (on Windows 11 under “Show more options”).

Progress shows up in the corner of the screen, where you can open the result, show it in its folder or cancel.

## What it converts

| Type | From | To |
|---|---|---|
| Images | JPG, PNG, WebP, HEIC, TIFF, AVIF, BMP, GIF, ICO, SVG | JPG, PNG, WebP, AVIF, TIFF, ICO, PDF (GIF → MP4/WebM) |
| Audio | MP3, M4A, AAC, WAV, FLAC, OGG, Opus, AIFF, WMA | MP3, M4A, WAV, FLAC, OGG, Opus, AIFF |
| Video | MP4, MOV, MKV, WebM, AVI, WMV, FLV, MPG, 3GP, TS | MP4, MOV, MKV, WebM, GIF, MP3 |
| PDF | PDF | DOCX, JPG, PNG (all pages, 300 DPI), PPTX, TXT, MD, HTML, EPUB |
| Documents | DOCX, DOC, ODT, RTF, MD, TXT, HTML, EPUB | DOCX, PDF, ODT, MD, HTML, TXT, EPUB, RTF |
| Slides | PPTX, PPT, ODP | PDF, PNG, JPG (all slides), PPTX, ODP |
| Spreadsheets | XLSX, XLSM, XLS, ODS, CSV, TSV | XLSX, CSV, TSV, ODS, PDF |
| Subtitles | SRT, VTT | SRT, VTT, TXT |
| Archives | ZIP, TAR, TAR.GZ, GZ, BZ2, XZ, RAR, 7z | extract, ZIP, TAR, TAR.GZ |

**Tools (Ctrl):**
- images: compress, shrink to 50 %, max 1920 px, rotate, mirror, crop to square, grayscale, strip metadata (GPS, EXIF), merge into PDF,
- video: compress (H.264), 720p, mute, trim by time, rotate, 2× speed, thumbnail frame,
- audio: compress, normalize loudness, trim, join,
- PDF: merge, split into pages, rotate, optimize,
- images and scanned PDFs: **extract text (OCR)** with the recognizer built into Windows; the text is saved as .txt and copied to the clipboard,
- anything, folders included: archive to ZIP / TAR.GZ.

Things that aren't files yet (images dragged from a browser, Outlook attachments, screenshots on the clipboard) are saved to **Downloads\DropWheel** first, and the results go next to them.

With several files selected, each one is converted separately; tools like “merge” produce a single result.

### Helper programs

DropWheel handles images, PDFs, spreadsheets, subtitles and archives on its own. Some formats need extra programs:

- **FFmpeg** (audio and video, 34 MB) and **Pandoc** (documents, 35 MB) are downloaded the first time you need them: the wheel shows their slices dimmed with “get FFmpeg”, and dropping a file there asks once, downloads the tool (checked against its published SHA-256) and runs the conversion. If you already have them installed (e.g. `winget install Gyan.FFmpeg`), DropWheel uses those,
- **LibreOffice** (DOC, PPTX, ODS, documents and spreadsheets to PDF) has to be installed separately: `winget install TheDocumentFoundation.LibreOffice`.

**Settings → Helper programs** shows what is missing and can download or install it for you. Keeping the tools out of the installer keeps it under 50 MB.

## Install

DropWheel is published on the [Releases](https://github.com/Faisaldhd/dropwheel/releases) page as a portable zip.
Nothing is installed, nothing is added to startup or to the Explorer menu, and no administrator rights are needed.

1. Download `DropWheel-<version>-portable-win-x64.zip` and `SHA256SUMS.txt` from the latest release.
2. Check the hash before running anything (PowerShell):

   ```powershell
   Get-FileHash .\DropWheel-<version>-portable-win-x64.zip -Algorithm SHA256
   ```

   The value must match the line for that file in `SHA256SUMS.txt`.
3. Unzip it anywhere (for example `%LOCALAPPDATA%\Programs\DropWheel`) and run `DropWheel.exe`.

The zip is self-contained, so no .NET install is required. It is unsigned for now, so Windows SmartScreen may
ask once ("More info" then "Run anyway"); `SECURITY.md` lists exactly what the app does on a machine.

Settings and the log stay next to `DropWheel.exe` (the zip contains a `portable.txt` marker).

**Company-safe by default:** keyboard/mouse monitoring is **off** (no global hooks at all until you turn it on in
Settings then Company mode), and DropWheel never downloads helper tools on its own - install FFmpeg or Pandoc with
winget and it finds them. Autostart and the Explorer context menu are opt-in in the same place.

English and Arabic, light and dark, following Windows. Requirements: Windows 10 (1809+) or Windows 11, x64.
## Build from source

```powershell
# .NET 10 SDK
./scripts/fetch-tools.ps1                      # downloads ffmpeg.exe and pandoc.exe into .\tools (for the tests)
$env:PATH = "$PWD\tools;$env:PATH"; dotnet test tests/FileAction.Tests
dotnet publish src/FileAction.App -c Release -r win-x64 --self-contained -o publish   # trimmed, ~165 MB unpacked
publish\DropWheel.exe
```

`DropWheel.exe --self-test <folder>` builds every window offscreen and saves PNG previews in both languages (used by CI).

## Project layout

```
src/FileAction.Core     conversions and tools (cross-platform library, also tested on Linux)
  Backends/             ImageMagick, FFmpeg, Pandoc, LibreOffice, PDF, spreadsheets, subtitles, archives
  Operations/Catalog.cs every slice on the Convert and Tools wheels
  Loc.cs                English / Czech strings
src/FileAction.Linux    the Linux app (Avalonia): drop zone, wheel, CLI, file manager integration
src/FileAction.App      the Windows app (WPF)
  Input/GlobalInput.cs  global mouse and keyboard hooks (the “drag + tap” gesture)
  Wheel/                the radial menu and when it opens
  Actions/              the Actions wheel, floating shelf, sharing
  Jobs/                 background jobs and progress cards
  Shell/                tray icon, hotkey, context menu, single instance
tests/FileAction.Tests  unit tests plus integration tests that run every operation on sample files
installer/              Inno Setup
packaging/              Linux tarball and the AUR PKGBUILD template
```

## License

[MIT](LICENSE). DropWheel uses other open-source components under their own licenses; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Differences from Tangerine

- Shift / Ctrl / Alt instead of Shift / Command / Control.
- Apple formats (Pages, Keynote, RTFD) and writing HEIC are not supported; HEIC can be opened.
- PDF → DOCX/EPUB/HTML converts the text, not the exact page layout (use PDF → PPTX or images for that).
- Tangerine's “AirDrop” action is **Share** on Windows (Nearby Sharing, Teams, Mail…).
