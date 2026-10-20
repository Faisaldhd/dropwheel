# CLAIMS — every factual claim on `docs/site/index.html`, checked

**Lane:** site / site-content · **Branch:** `site/content` · **Base:** `feat/2.4.0-website` @ `7009be9`
**Date:** 20 October 2026, 09:5x Riyadh · **Scope:** `docs/site/index.html` (EN + AR), 1,076 lines
**Rule applied:** every claim is either traced to a file or a command in this repo/its release, or it is
listed as a required fix. No claim is softened into vagueness to make it pass.

Evidence priority used: shipped code > tests/`docs/FUNCTION-MATRIX.md` (test names) > `README.md` /
`SECURITY.md` / installer script > GitHub release metadata. Where two documents disagree, the disagreement
is recorded at the end and the code wins.

---

## 0. The two answers the brief asked for

### Tool windows: **26**, not 25 — the page is right, the brief is wrong

`ToolRegistry.All` is not a hard-coded list; each tool registers itself from its own file
(`src/FileAction.App/UI/Tools/ToolRegistry.cs:11-19`). Counting the registrations is therefore the only
honest way to count the windows:

```
$ grep -rn "ToolRegistry.Register" src --include=*.cs | wc -l
26
```

The 26 ids, by group (each is an `IToolWindowSpec` with its own `Id`):

| Group | # | Ids |
|---|---:|---|
| Photos | 6 | `tools.crop`, `tools.editphotos`, `tools.annotate`, `tools.redact`, `tools.addbackground`, `tools.collage` |
| Video | 7 | `tools.videotrim`, `tools.videosplit`, `tools.videojoin`, `tools.videospeed`, `tools.videocrop`, `tools.videoframes`, `tools.videoredact` |
| Audio | 5 | `tools.audiotrim`, `tools.audiobleep`, `tools.audiochannels`, `tools.audionormalize`, `tools.audiovisualizer` |
| PDF | 2 | `tools.pdfmerge`, `tools.pdforganize` |
| Protect | 3 | `tools.protect`, `tools.unlock`, `tools.lock` |
| Files | 3 | `tools.compress`, `tools.metadata`, `tools.qr` |
| **Total** | **26** | |

Sample registration sites (one per group): `…/Audio/AudioBleepTool.cs:48`, `…/Image/CropTool.cs:46`,
`…/Video/VideoTrimTool.cs:44`, `…/Pdf/PdfMergeTool.cs:67`, `…/Password/ProtectTool.cs:51`,
`…/Qr/QrTool.cs:43`. The 26th is `tools.lock` (`…/Lock/LockTool.cs:67`), which is exactly the entry the
site audit already identified as missing from the brief: `docs/website/SITE-AUDIT.md:18-21`
(“the registry actually contains **26** (`tools.lock` was merged as the 26th), so the missing count is 21,
not 20”). **No page change needed; the brief's 25 is stale.**

### Release facts: current release is v2.3.0 and the page's numbers are exact

```
$ gh release list --repo Faisaldhd/dropwheel --limit 3
DropWheel v2.3.0   Latest   v2.3.0   2026-10-19T22:05:40Z
DropWheel v2.2.3            v2.2.3   2026-10-19T19:40:08Z
DropWheel v2.2.2            v2.2.2   2026-10-19T18:22:11Z

$ gh release view --repo Faisaldhd/dropwheel --json assets --jq '.assets[] | "\(.name)\t\(.size)"'
DropWheel-2.3.0-portable-win-x64.zip     89266161
DropWheel-2.3.0-portable-win-x64.zip.sig 88
DropWheel-2.3.0-Setup.exe                62967948
DropWheel-2.3.0-Setup.exe.sig            88
DropWheel-Setup.exe                      62967948
SHA256SUMS.txt                           484
```

* Installer **62,967,948 B** = **60.05 MiB** (62,967,948 ÷ 1,048,576 = 60.0508). The page prints
  `60.05 MB` and `62,967,948 B`; both are the same file, and “MB” here is the Windows/MiB convention.
* Portable ZIP **89,266,161 B** (85.13 MiB) — the page only prints the byte count, so no unit issue.
* `SHA256SUMS.txt` (fetched from the release asset) contains
  `7381cbd5b59249f3279b62af31091d89d3d8d994faa57fcdd08ec4331d7156ce  DropWheel-2.3.0-Setup.exe` and the
  same line for the `DropWheel-Setup.exe` alias, and that digest equals GitHub's own `digest` for both
  assets. The page's short form `7381cbd5b592…1d7156ce` and its full hash block both match.
* `DropWheel-Setup.exe` really exists as a stable alias (same digest, same size), so the header/hero CTA
  `releases/latest/download/DropWheel-Setup.exe` resolves today. There is **no**
  `DropWheel-Setup.exe.sig` asset — only the versioned pair — which is a copy bug in the Verify section
  (fix F5).
* No `v2.4.0` release or tag exists (`gh release list` above; `installer/DropWheel.iss:16` still defaults
  `AppVersion` to `2.3.0`), which is what “2.4.0 is in final verification” is resting on.

---

## 1. Verdict table

`EN` quotes `docs/site/index.html`; `AR` quotes the same block's `data-lang="ar"` sibling. Line numbers are
`index.html` lines.

| # | Claim (EN · AR) | Verdict | Evidence (path:line / command) | Required fix |
|---:|---|---|---|---|
| 1 | “26 file tools at your cursor” (title L6, description L7, og L18, twitter L25) · “26 أداة عائمة” L254 | **PROVEN** | 26 registrations, §0; `docs/website/SITE-AUDIT.md:18-21` | none |
| 2 | Filter counts All 26 / Photos 6 / Video 7 / Audio 5 / PDF 2 / Protect 3 / Files 3 (L264-270) | **PROVEN** | Group table in §0 (6+7+5+2+3+3=26); `SITE-AUDIT.md:92` | none |
| 3 | “Twenty-six tools, one gesture — Six photo tools, seven video, five audio, PDF pages, protection and file utilities” L832-833 · “ست وعشرون أداة…” | **PROVEN** | Same 26; counts per `IToolWindowSpec.Id` prefixes (`tools.video*` 7, audio 5, PDF 2, protect 3, files 3) | none |
| 4 | “Windows 10/11 · 64-bit · Free and open source” L111 · “ويندوز 10/11 · 64 بت · مجاني ومفتوح المصدر” | **PROVEN** | `README.md:96` (“Windows 10 (1809+) or Windows 11, x64”); `installer/DropWheel.iss:54-55` (`x64compatible`) | F7 (add 1809+ for precision) |
| 5 | “Free and open source” + “MIT licence” L111, L810, L1056, L1070, L1022 · “ترخيص MIT” | **PROVEN** | `LICENSE:1-4` (MIT, “Copyright (c) 2026 Metamorfolog / Copyright (c) 2026 DropWheel”); `README.md:136` | none |
| 6 | “v2.3.0 verified” L139-140 · “v2.3.0 موثَّق” | **PROVEN** | v2.3.0 is the published release with `SHA256SUMS.txt` + `.sig` (§0) | F6 (“verified”→“signed”; “verified” implies a check the page does not name) |
| 7 | “60.05 MB installer” L144-145, L935 · “المثبّت 60.05 ميجابايت” | **PROVEN** | 62,967,948 B ÷ 1,048,576 = 60.05 MiB (§0) | none (unit is MiB; byte count is printed next to it at L941) |
| 8 | Hero short hash `7381cbd5b592…1d7156ce` L151 | **PROVEN** | `SHA256SUMS.txt` line for `DropWheel-Setup.exe` = `7381cbd5…1d7156ce` (§0) | none |
| 9 | Full hash `7381cbd5b59249f3279b62af31091d89d3d8d994faa57fcdd08ec4331d7156ce` L966 | **PROVEN** | Same `SHA256SUMS.txt` line; asset `digest` from `gh release view` (§0) | none |
| 10 | Installer `62,967,948 B` L941 | **PROVEN** | `gh release view` asset `size` = 62967948 (§0) | none |
| 11 | Portable ZIP `89,266,161 B` L945 | **PROVEN** | `gh release view` asset `size` = 89266161 (§0) | none |
| 12 | Asset names `DropWheel-2.3.0-Setup.exe`, `DropWheel-2.3.0-portable-win-x64.zip`, `SHA256SUMS.txt`, `.sig` L941-949 | **PROVEN** | `gh release view` asset names (§0) | none |
| 13 | CTA `releases/latest/download/DropWheel-Setup.exe` L97, L121, L933 | **PROVEN** | Alias asset exists in v2.3.0 with the same digest/size (§0) | none |
| 14 | “Every release publishes SHA256SUMS.txt and a detached .sig” L909 · “ينشر كل إصدار…” | **PROVEN** | v2.3.0, v2.2.3 and v2.2.0 each publish `SHA256SUMS.txt` + `*.exe.sig` + `*.zip.sig` (`gh release view v2.2.3/v2.2.0 --json assets`) | none |
| 15 | `pwsh ./scripts/verify-signature.ps1 .\DropWheel-2.3.0-Setup.exe`; “Exit code 0 means the ECDSA P-256 signature matches the key compiled into DropWheel. Any other code: do not run the file.” L922-923 | **PROVEN** | `scripts/verify-signature.ps1:4-19` (verifies `<file>.sig`, ECDSA P-256/SHA-256, key from `src/FileAction.Core/Updates/UpdateKey.cs`, “Exit code 0 = valid… 1 = …do not run”) | none |
| 16 | “The installer is not Authenticode-signed yet, so SmartScreen may warn” L973, L990 | **PROVEN** | `README.md:88` (“The binaries are Authenticode-unsigned for now, so Windows SmartScreen may ask once”); `scripts/verify-authenticode.ps1` exists as the separate check | none |
| 17 | “Downloads stay on the last signed release (2.3.0) until 2.4.0 is published” L61-62, L162-163 | **PROVEN** | `gh release list`: v2.3.0 is `Latest`, no 2.4.0 (§0); `/releases/latest/download/` therefore serves 2.3.0 | none |
| 18 | “Version 2.4.0 … In final verification — ships with the signed release” L822-825 · “قيد التحقق النهائي” | **PROVEN** | No v2.4.0 release/tag (§0); 2.4.0 is the in-flight train: `docs/FUNCTION-MATRIX.md:4` (“the 2.4.0 tools”), `docs/MOTION.md:1`, `docs/PERF.md:1`; `installer/DropWheel.iss:16` still 2.3.0 | none |
| 19 | “AES-256 for a password-protected ZIP” L589, L686, L868 | **PROVEN** | `docs/FUNCTION-MATRIX.md:83` (“`AesZipWriter.Create` writes an AE-2 WinZip AES-256 entry… PASS”); `:93` (“ZIP key size 256”); `src/FileAction.Core/PasswordZip.cs` | none |
| 20 | “an AES-256 PDF with print and copy permissions” L589, L686 | **PROVEN** | `docs/FUNCTION-MATRIX.md:90` (`/AESV3`, round-trip PASS); `src/FileAction.App/UI/Tools/Password/ProtectTool.cs:366-368` (`AllowPrinting`, `AllowCopying`); `src/FileAction.Core/PasswordPdf.cs:51-53` (`PermitPrint`, `PermitExtractContent`) | none |
| 21 | “AES-256 in a format other apps already open” L684 | **PROVEN** | WinZip AE-2 ZIP + PDF AES-256 V5 are the standard formats other tools read (`FUNCTION-MATRIX.md:83,90`; `PasswordPdf.cs:11` “security handler V5, PDF 2.0 … nothing is faked or downgraded”) | none |
| 22 | “a ZIP keeps file names visible; the PDF path is the one with permissions” L690 | **PROVEN** | `docs/FUNCTION-MATRIX.md:93` (“ZIP names are not encrypted (the note is shown) … PDF owner permissions are supported”) | none |
| 23 | “Password strength, confirmation and a reveal toggle; nothing is logged.” L687 | **PROVEN** | `docs/FUNCTION-MATRIX.md:91-92` (strength meter, match gate, eye reset); `:95` (“No `AppLog`/`Notifications` call receives a password”, P-15) | none |
| 24 | “Unlock makes a new unprotected copy; the protected file is never overwritten.” L688, L603 | **PROVEN** | `docs/FUNCTION-MATRIX.md:88` (P-8 unlock writes a new ZIP, no partial file); `:85` (P-5 never overwrites); `:89` (plain ZIP not rewritten) | none |
| 25 | “Password recovery is not attempted.” L603 · “لا تُجرَّب استعادة كلمة المرور” | **PROVEN** | No recovery/brute-force path in `src/FileAction.App/UI/Tools/Password/**` or `src/FileAction.Core/Password*.cs` (`grep -rn "recover\|brute" …` → 0 hits); `FUNCTION-MATRIX.md:91` (unlock only validates a supplied password) | none |
| 26 | Lock marked **Beta** (L616, L696, L874) | **PROVEN** | `src/FileAction.App/UI/Tools/Lock/LockTool.cs:19` (“labelled Beta until the owner signs it off”), `:166` (`T("tool.lock.beta")` pill); `src/FileAction.Core/Strings/en.json:944` `"tool.lock.beta": "Beta"`, `ar.json:944` “تجريبي”; `FUNCTION-MATRIX.md:219` “Lock — `tools.lock` (Beta)” | none |
| 27 | “Recovery key … 24 characters, shown once, never stored.” L702, L875 · “24 حرفًا…” | **PROVEN** | `src/FileAction.Core/Lock/RescuerKey.cs:6-17` (“24 characters in six groups of four … 120 bits … shown to the user exactly once”; `CharacterCount = 24`); “never stored”: `FUNCTION-MATRIX.md:239` (`No_secret_is_written_under_the_app_data_folder`) and `LockRuntime.cs:14` (secrets live only in memory) | none |
| 28 | “Rescuer password — one password for every protected file” + “three ways in” L700-703, L617 | **PROVEN** | `FUNCTION-MATRIX.md:222` (“a three-slot password/recovery envelope”); `src/FileAction.App/Shell/Protection/LockRuntime.cs:71-96` (`RequireRescuer`, `RescuerSecrets(key, password)`), `LockSettingsWindow.cs:14-15` | none |
| 29 | Container extensions `.dwenc` / `.dwdir` L875 | **PROVEN** | `docs/FUNCTION-MATRIX.md:221` (“one self-contained `.dwenc` / `.dwdir`”); `LockContainerTests` rows L228-230 | none |
| 30 | “Atomic, verified replacement; auto-relock when the app closes; crash recovery.” L705, L875 | **PROVEN** | `FUNCTION-MATRIX.md:236` (L-9 header-rewrite atomicity, “a verified replace is required”), `:238` (L-11 auto-lock rules), `:237` (L-10 crash recovery re-encrypts the journal) | none |
| 31 | “No secrets written under %LOCALAPPDATA%; per-user associations only.” L706 | **PROVEN** | `FUNCTION-MATRIX.md:239` (L-12 “No password/key under `%LOCALAPPDATA%\DropWheel`”; L-13 “HKCU-only registry”, `Associations_are_per_user_and_removable`) | none |
| 32 | “Locked files and folders keep their identity and gain the two-tone badge in Explorer … from the shipped icon set, over real document and folder glyphs.” L713-714, L719-729 | **PROVEN** | `FUNCTION-MATRIX.md:240` (L-13: `Theme.LockBadge` #8224D4 / highlight #B48AE0, two-tone padlock bottom-left, 7 icons × 6 sizes, folder `desktop.ini` badge); source renders exist at `docs/screenshots/lock-badge/*.png` (8 files) | none |
| 33 | “the download is checked by SHA-256 and an ECDSA P-256 signature” L803, L923, L994 | **PROVEN** | `src/FileAction.Core/Updates/UpdateKey.cs:4` (“ECDSA P-256, SubjectPublicKeyInfo”); `UpdateContracts.cs:68-69`; `UpdateVerifier.cs:7`; `README.md:94` | none |
| 34 | “checks GitHub 60 seconds after start and then once a day” L803, L994 · “بعد 60 ثانية… ثم مرة يوميًا” | **PROVEN** | `src/FileAction.Core/Updates/UpdateConfig.cs:28` `FirstCheckDelay = TimeSpan.FromSeconds(60)`; `:31` `CheckInterval = TimeSpan.FromHours(24)`; `README.md:94` (“one minute after start and then once a day”) | none |
| 35 | “Nothing downloads until you press Update” L803, L994 | **PROVEN** | `README.md:94` (“nothing downloads until you click **Update now**”); the check only lists the release (`UpdateService.cs`) | none |
| 36 | Failure message “Update could not be verified” L994 | **PROVEN** | `README.md:94` (same string, “a failure deletes the files and says…”); `UpdateService.cs:241-251` deletes on digest/sums/sig failure | none |
| 37 | “The only network request is the update check you control.” L795 · “الطلب الشبكي الوحيد هو فحص التحديث الذي تتحكّم به.” | **WRONG** | Three outbound paths exist: (a) updates `src/FileAction.Core/Updates/UpdateService.cs:21`, (b) helper downloads `src/FileAction.App/Shell/ToolInstaller.cs:24,153-155` (FFmpeg/Pandoc), (c) a URL dropped on the wheel `src/FileAction.App/Wheel/DropReader.cs:25,233-237` (`HttpGetAsync`). The check is also **on by default**, not user-controlled: `src/FileAction.App/AppSettings.cs:82` `CheckForUpdates { get; set; } = true`. The page itself contradicts the sentence at L784 (fetches FFmpeg/Pandoc “only when you allow downloads”). | **F1** |
| 38 | “Processed on your PC. Nothing is uploaded.” L157-158, L794 · “تُعالَج على جهازك. لا يُرفَع أي شيء.” | **PROVEN** | No upload call anywhere in `src` (`grep -rn "HttpClient"` → only the three download paths in #37, all `Get*`); `SECURITY.md:3-5` (“no telemetry, no analytics, no cloud service … files never leave the PC”); `README.md:5` | none (but see #37: the *request* count is what is wrong, not the upload claim) |
| 39 | “No account, no cloud conversion, no telemetry” L795, L1002 | **PROVEN** | `SECURITY.md:19` (row “Telemetry / analytics / crash upload / update check — **none**”); no telemetry/analytics hits in `src` (`grep -rn "telemetry\|analytics"` → 1 comment at `QrTool.cs:9` stating there is none); no sign-in code | none |
| 40 | “No for the default install… **Install for me only** → `%LOCALAPPDATA%\Programs\DropWheel`; **Install for all users** needs administrator rights → `%ProgramFiles%\DropWheel`.” L997-998 | **PROVEN** | `README.md:84` (same two options, same paths, “no administrator rights” / “administrator”); `installer/DropWheel.iss:35-37` (`{autopf}\DropWheel`), `:46-47` (`PrivilegesRequired=lowest`, overridable by dialog) | none |
| 41 | “The installer is self-contained, so no .NET install.” L1014 | **PROVEN** | `README.md:88` (“Both the zip and the setup are self-contained: they include the .NET runtime, so no .NET install is required”); `installer/DropWheel.iss:3` (“a SELF-CONTAINED win-x64 build: the publish already contains .NET”) | none |
| 42 | “English and Arabic with a full right-to-left layout, including the installer.” L1018 | **PROVEN** | `installer/DropWheel.iss:79-86` (`[Languages]` english + `arabic` with `Arabic.isl,DropWheel-ar.isl`); `installer/Arabic.isl` (29,275 B), `installer/DropWheel-ar.isl`; `README.md:96` | none |
| 43 | “FFmpeg and Pandoc are optional… finds them on PATH, or fetches them only when you allow downloads; company-safe defaults keep that off.” L784-785, L1014 | **PROVEN** | `README.md:67` (downloads gated by “Settings → Company mode”, off by default, else PATH), `:92` (“helper-tool downloads are **off**”); `SECURITY.md:16` (“OFF — no network request is made”), `:31` | none |
| 44 | “This page has no trackers… The Content-Security-Policy blocks every outbound connection from the page.” L806-807 | **PROVEN** (wording) | `index.html:11` CSP (`default-src 'self'; connect-src 'none'; font-src 'none'; object-src 'none'`); no external URL in `assets/app.js`, `assets/boot.js`, `assets/styles.css` (`grep -n "https\?://\|@import"` → 0); `SITE-AUDIT.md:82` (“External requests **0**”) | **F8** (links are still clickable: say “network request”, not “connection”) |
| 45 | “DropWheel is based on FileAction by Metamorfolog and is not affiliated with Tangerine.” L1022, L1065, L1071 · “مبني على FileAction من Metamorfolog وغير مرتبط بـTangerine” | **PROVEN** | `README.md:9` (verbatim), `README.ar.md:11`; `LICENSE:3-4` (“Copyright (c) 2026 Metamorfolog”); `THIRD-PARTY-NOTICES.md:3` (“based on FileAction by Metamorfolog (MIT)”) | **F10** (Arabic bidi: `بـTangerine` needs isolation) |
| 46 | “150 ms for the wheel, 180 ms for windows, and reduced motion honoured from Windows or the app” L882 | **PROVEN** | `docs/MOTION.md:17` (`WheelOpenMs` 150 ms), `:25` (`WindowOpenMs` 180 ms), `:86-93` (both Windows `ClientAreaAnimation` and app `AppSettings.Animations`) | none |
| 47 | “No timers and no idle work.” L882 · “بلا مؤقتات وبلا عمل خامل.” | **WRONG** | Overbroad: a `DispatcherTimer` runs in `src/FileAction.App/Shell/Protection/RescuerWindows.cs:681` (`TimeSpan.FromSeconds(60)`), and the update scheduler runs when `CheckForUpdates` is true (`src/FileAction.App/App.cs:203`). The true, narrower fact is stated in `MOTION.md:9-11` (“Nothing is animated on a timer… no animation work while idle”) and `:115-116`. | **F4** (scope it to animation) |
| 48 | “Budgets for package size, cold start and idle memory; a motion gate checks UI frame p95, idle CPU and retention on every run.” L889 | **PROVEN** | `docs/PERF.md:3-9` (the three budgets and the three motion gates: p95 < 16.7 ms, idle CPU < 5 %, retention ≤ 12 MB), `:24-26` (“Any breach fails the self-test and therefore the CI job”), `:76-77` | none |
| 49 | “Sixteen sliders for light, colour and grain” L308 | **PROVEN** | `docs/FUNCTION-MATRIX.md:64` (E-7 “all 16 sliders non-zero”), `:58-59` (neutral = byte copy) | none |
| 50 | “nine gradients, nine solids or your own image” L365 | **PROVEN** | `docs/FUNCTION-MATRIX.md:30` (“9 gradient presets… and 9 distinct solid colours”); `:24` (image background cover-fit) | none |
| 51 | “1080², 1920×1080 or 1080×1920” L379 | **PROVEN** | `docs/FUNCTION-MATRIX.md:40-42` (C-1…C-3 output presets PASS) | none |
| 52 | “0.25× to 4× with the audio pitch kept. A silent clip is fine.” L435 | **PROVEN** | `src/FileAction.Core/VideoEdit.cs:75-76` (`MinSpeed = 0.25`, `MaxSpeed = 4.0`), `:183-186` (atempo chain, “pitch is kept”), `:330-356` (audio filter only when audio exists); `VideoToolRunner.cs:72` (“a silent clip simply has no audio filter”) | **F19** (Arabic “وارتفاعها” is redundant/incorrect) |
| 53 | “Solid, blur or pixelate over the areas you draw — with automatic text detection.” L346 | **PROVEN** | `src/FileAction.App/UI/Tools/Image/RedactTool.cs:16` (“Manual areas plus the Windows OCR text detection … are the shipped, offline … feature set”); `UI/Tools/Image/RedactTextDetector.cs` (16,918 B) | none |
| 54 | Format card **Photos** `JPG PNG WEBP AVIF TIFF ICO PDF` L750 | **PROVEN** | `README.md:41` “Images … To: JPG, PNG, WebP, AVIF, TIFF, ICO, PDF” | none |
| 55 | Format card **Video** `MP4 MOV MKV WEBM GIF MP3` L754 | **PROVEN** | `README.md:43` “Video … To: MP4, MOV, MKV, WebM, GIF, MP3” | none |
| 56 | Format card **Audio** `MP3 M4A WAV FLAC OGG OPUS AIFF` L758 | **PROVEN** | `README.md:42` “Audio … To: MP3, M4A, WAV, FLAC, OGG, Opus, AIFF” | none |
| 57 | Format card **Documents** `DOCX PDF ODT MD HTML TXT EPUB RTF` L762 | **PROVEN** | `README.md:45` “Documents … To: DOCX, PDF, ODT, MD, HTML, TXT, EPUB, RTF” | none |
| 58 | Format card **Slides** `PDF PNG JPG PPTX ODP` L766 | **PROVEN** | `README.md:46` “Slides … To: PDF, PNG, JPG, PPTX, ODP” | none |
| 59 | Format card **Sheets** `XLSX CSV TSV ODS PDF` L770 | **PROVEN** | `README.md:47` “Spreadsheets … To: XLSX, CSV, TSV, ODS, PDF”; `src/FileAction.Core/Formats.cs:42` `SpreadsheetOutputs` | none |
| 60 | Format card **From PDF** `JPG PNG PPTX XLSX TXT MD HTML EPUB DOCX` L774 | **PROVEN** | `README.md:44` (PDF → DOCX, JPG, PNG, PPTX, TXT, MD, HTML, EPUB) plus XLSX: `src/FileAction.Core/Operations/Catalog.cs:231-232` (`pdf.to.xlsx`, `PdfBackend.ToXlsxAsync`), `src/FileAction.Core/Backends/PdfBackend.cs:159-164` | none (note: PDF→XLSX needs LibreOffice, `PdfBackend.cs:164`; not claimed either way) |
| 61 | Format card **Archives** `ZIP TAR TAR.GZ` L778 | **PROVEN** | `README.md:49` “Archives … To: extract, ZIP, TAR, TAR.GZ” | none |
| 62 | “The wheel opens at the pointer while you drag. **Keep the key down** and tap another to switch wheels” L193 · “أبقِ المفتاح مضغوطًا واضغط آخر…” | **WRONG** | `README.md:26`: “The wheel stays open until you drop, so you can **release the key** and tap another one to switch wheels.” Holding the key is not required and the sentence teaches a wrong gesture. | **F3** |
| 63 | “Drop in the middle to cancel.” L242 | **PROVEN** | `README.md:24` (verbatim) | none |
| 64 | “Tap the same key again to repeat the last action.” L243 | **PROVEN** | `README.md:28` (“tap the same key again while dragging … repeats what you did last”) | none |
| 65 | “Keys are reassignable in Settings.” L244 | **PROVEN** | `README.md:26` (“Keys can be reassigned in Settings”) | none |
| 66 | “No mouse: Ctrl + Alt + Space, arrows, Enter.” L245 | **PROVEN** | `README.md:32` (Ctrl+Alt+Space, arrows, Enter, Tab, Esc, 1-9) | **F30** (Arabic “وEnter” bidi join) |
| 67 | “Each window sizes itself to the media: portrait stays tall, landscape stays wide, and the controls never shrink.” L258 | **PROVEN** | `CHANGES.md:17-25` (adaptive tool windows, 95 %/90 % clamp, “on a small screen the preview shrinks, never the controls”); `FUNCTION-MATRIX.md:48` (C-9), `:62` (E-5) | **F16** (Arabic verb form) |
| 68 | “Captures are app self-test renders; the set grows tool by tool.” L258 | **PROVEN** | `SITE-AUDIT.md:18-21` (5 of 26 captured; 21 use a decorative tile, “never a mock-up”), `:97-98` (captures are the real `--self-test` renders) | **F17** (Arabic wording) |
| 69 | “Rendered from the shipped app. No mock-ups.” L180-181 | **PROVEN** (hero only) | `SITE-AUDIT.md:97` (“The tool captures are the real `--self-test` renders”); the caption sits on the wheel hero | **F29** (Arabic “نماذج” is the standard word for mock-up) |
| 70 | Photos cards: Crop “apply at the original pixel size. The source is never rewritten” (L289); Edit “byte-for-byte copy” (L308); Annotate “Undo, reorder … flat copy” (L327); Collage “spacing, rounded cells and a background” (L379) | **PROVEN** | `src/FileAction.App/UI/Tools/Image/CropTool.cs:19-20` (“W/H boxes in original pixels … the original is only read”); `FUNCTION-MATRIX.md:58` (E-1), `:40-46` (C-1…C-7); `AnnotateView.cs:209-214` (undo, bring-front/send-back), `AnnotationSurface.cs:221` (undo) | **F26** (Arabic Crop title must be “قصّ”, not “اقتصاص”) |
| 71 | Video cards: Trim “stream-copy when the codecs allow it — with a re-encode fallback” (L393); Split “equal parts or exact ranges” (L407); Join “first clip sets the size and frame rate; the rest are scaled and padded” (L421); Crop “output dimensions stay even” (L449); Save frames “full-resolution PNGs into a new folder” (L463); Redact video “time range … solid colour or blur” (L477) | **PROVEN** | `src/FileAction.Core/VideoEdit.cs:250` (“a fast stream copy or a frame-accurate re-encode”), `:241-242` (even dimensions); `VideoSplitJoin.cs:60` (stream copy), `:278-279` (first clip size/rate, others scaled + padded); `VideoSplitTool.cs:17-18` (equal parts / ranges into a new folder); `VideoFramesTool.cs:13`; `VideoRedactTool.cs:15-16` | **F18, F26** (Arabic wording/title only) |
| 72 | Audio cards: Trim “automatic silent-end trimming and playback of the selection” (L491); Bleep “hear the tone before applying” (L505); Channels “mono or stereo … level each side … solo” (L519); Normalize “EBU R128 … analysis pass that predicts … two-pass apply” (L533); Visualizer “waveform or vector scope, on black or a still, in three shapes” (L547) | **PROVEN** | `AudioTrimTool.cs:14` (“trim silent ends automatically” + playback), `:209` (play button); `AudioBleepTool.cs:15` (“previews the tone that will replace each section”); `AudioChannelsTool.cs:12-14,138-148` (mono/stereo, two sliders, solo); `AudioNormalizeTool.cs:14-15` (EBU R128, analysis predicts, two-pass); `AudioVisualizerTool.cs:129-142` (Waveform/Vector scope, None/Image background) + `en.json:884-886` (Landscape/Portrait/Square) | **F20, F22** (Arabic framing/terms) |
| 73 | PDF cards: Merge “page rotation and text are preserved” (L561); Organize “make every page the same width” (L575) | **PROVEN** | `FUNCTION-MATRIX.md:189` (M-4 rotation), `:188` (M-3 text), `:213` (O-8 same width), `:191` (M-6 arrows) | **F21** (Arabic verb) |
| 74 | Files cards: Compress “target size or percentage; the source is untouched” (L631); Edit metadata “EXIF, IPTC, XMP, PDF info and chapters — or clear camera and location in one action” (L645); Read QR “offline. Only http and https links can be opened.” (L659) | **PROVEN** | `FUNCTION-MATRIX.md:112-114` (C-5 target/percentage, C-4 source untouched), `:130` (M-2 sections), `:133` (M-5 Remove All clears camera/GPS), `:159` (Q-7 non-web schemes refused), `:167` (Q-15 no network); `QrTool.cs:9` | none |
| 75 | Protect “1 file, folder or several files” (L590); Unlock “1 ZIP or PDF” (L604); Lock “1 file or folder” (L618) | **PROVEN** | `FUNCTION-MATRIX.md:82` (P-2 selection rules, verbatim), Lock `Applies` in `LockTool.cs` | none |
| 76 | “Grab a file, tap a key, drop it on what you need.” L116 | **PROVEN** (copy) | Gesture documented at `README.md:13-24`; note the gesture is **off on a fresh install** (`README.md:13`, `SECURITY.md:15`) and the page never says so | **F31** (say “the tool you need”; see also note N3) |
| 77 | Footer “File tools that come to your cursor. Built for Windows, running entirely on your PC.” L1037 | **PROVEN** | `README.md:5`; #38 | none |
| 78 | Footer “No cookies · no analytics · no external requests” L1038 | **PROVEN** (page scope) | `index.html:11` CSP + #44 | **F9** (Arabic calque “ملفات ارتباط”; and “third-party” is the precise word) |

**Counts:** 78 rows — **PROVEN 75, UNPROVEN 0, WRONG 3** (rows #37, #47, #62). Required fixes are
F1–F31 in `COPY-FIXES.md`; F5 is an instruction bug rather than a claim (see N7), and F6/F7 are precision
edits on claims that are already true.

---

## 2. Worst finding

**#37 — “The only network request is the update check you control.”** It is false in two independent ways:
the app makes two other kinds of outbound request (optional FFmpeg/Pandoc downloads, and a file/link
dropped from a browser — `ToolInstaller.cs:24`, `DropReader.cs:25,233`), and the update check is *on by
default* (`AppSettings.cs:82`), so it is not something the user “controls” unless they find the switch.
It is also the one false claim on the page that is contradicted **by the page itself** two sections
earlier (L784 says DropWheel “fetches them only when you allow downloads”), and by the paragraph above it
(“no cloud conversion” is fine; the request count is not). Fix F1/F2 replaces it with the exact set.
Severity: high — it is a privacy/security promise, which is the one class of claim a reviewer will test.

Close second: **#62** — the page teaches the wrong gesture (“keep the key down”), contradicting
`README.md:26`.

---

## 3. Notes, and document conflicts found while verifying

**N1 — `SECURITY.md` is stale and contradicts `README.md` + the code.** `SECURITY.md:3-5` says the app has
“no update checker” and that “the only network requests it can make are the optional helper-tool
downloads”; `SECURITY.md:19` lists “update check — none”. But `README.md:94`, `src/FileAction.Core/Updates/**`
and the shipped v2.3.0 release all implement the signed update check. `SECURITY.md` is not mine to edit;
the website lane should not cite it for the update claim until the security lane fixes it. The claims table
therefore cites code + `README.md` for update behaviour, and `SECURITY.md` only for what it still gets right
(no telemetry, helper downloads off by default, file writes).

**N2 — `README.md:44` omits PDF→XLSX**, while `Catalog.cs:231-232` ships it. The page's “From PDF” card is
therefore *more* correct than the README. No page change; flag to whoever owns the README.

**N3 — The drag-and-tap gesture is off on a fresh install** (`README.md:13`, `SECURITY.md:15`, and the
installer's unchecked default task). The page describes the gesture as if it works out of the box. This is
an omission, not a false statement, so it is not a separate verdict row; if the lead wants full honesty in
the hero, add one clause (F33 offers a version that does not lengthen the hero much). Recommend fixing at
least in the FAQ.

**N4 — “Rendered from the shipped app. No mock-ups.”** is true of the hero (a `--self-test` render) and,
per `SITE-AUDIT.md:97-99`, of the 5 tool captures and the 4 lock-badge images. The other 21 tool cards use a
deliberate decorative tile (category + index), which the section note at L258 already discloses
(“Captures are app self-test renders; the set grows tool by tool”). Keep both; do not extend the hero
caption to the whole grid.

**N5 — “verified” vs “signed”.** The page uses three words for the same fact: “last signed release”
(L61), “v2.3.0 verified” (L139), “Latest verified release” (L930). Only “signed” is proven by an artifact
(the `.sig`). F6 aligns the other two. Likewise “Update could not be verified” is a real UI string
(`README.md:94`) and must stay verbatim.

**N6 — The 60.05 MB unit.** 62,967,948 B is 60.05 **MiB**, which Windows Explorer and GitHub both display
as “MB”. The byte count is printed next to it (L941), so a reader can check. No fix; do not “correct” it to
62.97 MB, which would then contradict the byte count.

**N7 — The two Verify commands name different files (fix F5).** Step 1 hashes `.\DropWheel-Setup.exe`
(L917) and the hash block is labelled `DropWheel-Setup.exe` (L960), while step 2 passes
`.\DropWheel-2.3.0-Setup.exe` (L922). Both files exist with the same digest, so neither hash command is
wrong — but the alias has **no** `DropWheel-Setup.exe.sig` asset (release asset list, §0), so a reader who
only downloaded the alias gets “missing .sig” from `verify-signature.ps1` and a non-zero exit. F5 makes the
section self-consistent on the versioned name, which is the one that has both `.sig` and a `SHA256SUMS.txt`
line.

---

## 4. Arabic review (detail in `COPY-FIXES.md`)

Checked against `docs/glossary-ar.md`. Verdict: the Arabic is mostly strong, idiomatic MSA, uses Western
digits and `«»`/`،`/`؛` as the glossary requires, and does **not** translate product names. Problems found:

| Kind | Where | Example | Fix |
|---|---|---|---|
| Grammar | L1002 | “تُعالَج **كل شيء** محليًا” (verb agrees with nothing) | F2 → “تُعالَج كل الملفات محليًا” |
| Grammar | L259, L833 | “كل نافذة **تكيّف** حجمها” (needs the passive/transitive form) | F16 → “تُكيّف” |
| Grammar | L1006 | “المجلد الذي **تُفلت عليه الملف**” | F15 → “تُسقط عليه الملف” |
| Gender | L1010 | “و«حماية» … **هو** المسار الناضج” | F12 → “هي المسار الناضج” |
| Meaning | L1010 | “الصيغة **مُصدَّرة**” for “the format is versioned” (مُصدَّرة = exported/issued) | F11 → “تحمل رقم إصدار” |
| Meaning | L547 | “بثلاثة **اتجاهات**” for three frame shapes | F20 → “بثلاثة أُطُر: أفقي ورأسي ومربّع” |
| Meaning | L421 | “وتُقاس البقية عليه” drops “and padded” | F18 → “وتُحجَّم بقية المقاطع لتلائمه وتُضاف إليها حواف سوداء” |
| Redundant/wrong | L435 | “مع الحفاظ على طبقة الصوت **وارتفاعها**” (pitch is طبقة الصوت) | F19 → drop “وارتفاعها” |
| Inconsistent term | L288, L448 | Crop titled “اقتصاص”, but the app says “قصّ الصورة” / “قصّ الفيديو” (`ar.json:99,919`); Trim is “اقتصاص” (`ar.json:76,900`) | F26 → “قصّ” for the two Crop cards |
| Calque | L1038 | “ملفات ارتباط” vs the page's own “ملفات تعريف الارتباط” (L807) | F9 |
| Calque | L990 | “إدراج الملف بقائمته البيضاء” | F25 → “في قائمة السماح” |
| Machine-sounding | L888, L889 | “مقيس في CI”, “وحدة المعالجة” | F23, F24 → “قياس داخل CI”, “استهلاك المعالج” |
| Machine-sounding | L853 | “صوت حتى أدق تفاصيل الموجة” | F27 → “الصوت حتى أدقّ تفاصيل الموجة” |
| Bidi (LRM/RLM class) | L1022, L1071 | “غير مرتبط **بـTangerine**” — the Arabic preposition is glued to a Latin word with no isolate, so the renderer reorders it; the glossary (`glossary-ar.md:80-81`) requires U+2066…U+2069 (or `<bdi>`) around Latin values inside Arabic | F10 → “لا صلة له بمشروع Tangerine” (no glued preposition left to reorder) |
| Bidi | L245 | “والأسهم **وEnter**” — the waw is glued to Latin | F30 → “ومفتاح Enter” |
| Wrong UI quote | L989 | “يقول ويندوز «حماية Windows»” — this is not the Arabic SmartScreen string | F13 → «قام Windows بحماية جهازك» |
| Terse/awkward | L998 | “لا للتثبيت الافتراضي.” | F14 → “لا، ليس في التثبيت الافتراضي.” |
| Wording | L181 | “بلا تصميمات وهمية” for “No mock-ups” | F29 → “بلا نماذج وهمية” |

No LRM/RLM characters (U+200E/U+200F) were found in the file, which is correct: the page uses `<bdi
dir="ltr">` isolation for versions, hashes, paths and key names (equivalent to the glossary's isolate
requirement). The only isolation gaps are the two `بـTangerine` strings and `وEnter` above.
