# DropWheel 2.2.0 — CHANGES

## 2.2.0

### English

- **Signed auto-update.** DropWheel checks GitHub once a day (60 seconds after start, then every 24 hours) and
  when you click **Check for updates**. Nothing downloads until you click **Update**. Every download is checked
  twice: SHA-256 (the GitHub asset digest and `SHA256SUMS.txt`) and an ECDSA P-256 signature against the public
  key built into the app. If either check fails the files are deleted and DropWheel says **“Update could not be
  verified”** — an unverified file is never run.
- **Setup no longer needs .NET.** The installer is self-contained: no runtime detection, no download, no
  administrator permission for the default install. Choose **Install for me only** (no admin) or **Install for
  all users**. The wizard speaks English and Arabic (right-to-left).
- **Two setup tasks, both on by default:** *Start with Windows* and *Watch mouse and keyboard*. A Start-menu
  shortcut is always created; the desktop shortcut is optional.
- **Upgrades keep your choices.** The same settings file is reused, the app is closed during the update, and
  the uninstaller removes the startup entry, the Explorer menu, the Send-to shortcut and the program files —
  but keeps your settings unless you tick **Remove my settings**.
- **Settings reorganized into six tabs:** General · Wheels · Quality · Integration · Updates · About. Every
  switch appears exactly once.
- **Update UI:** a tray item, a Windows notification, and a violet banner in Settings with **Update now** and
  **What's new**.
- **Download page** at <https://faisaldhd.github.io/dropwheel/> in English and Arabic, with the current version
  and SHA-256.

### العربية

- **تحديث تلقائي موقَّع.** يتحقّق DropWheel من GitHub مرة كل يوم (بعد 60 ثانية من التشغيل ثم كل 24 ساعة) وعند
  الضغط على **التحقّق من التحديثات**. لا يُنزَّل أي شيء قبل أن تضغط **تحديث الآن**. ويُفحص كل تنزيل مرتين:
  بصمة SHA-256 (حقل digest في GitHub وملف `SHA256SUMS.txt`) وتوقيع ECDSA P-256 بالمفتاح العام المضمَّن في
  التطبيق. إذا فشل أي فحص تُحذف الملفات ويظهر **«تعذّر التحقق من التحديث»** — ولا يُشغَّل أي ملف غير موثَّق أبدًا.
- **المثبِّت لم يعد يحتاج .NET.** أصبح مستقلًّا بالكامل: لا كشف عن وقت التشغيل ولا تنزيل ولا صلاحيات مدير
  للتثبيت الافتراضي. اختر **التثبيت لي وحدي** (دون مدير) أو **التثبيت لكل المستخدمين**. المعالج بالعربية
  والإنجليزية (من اليمين إلى اليسار).
- **مهمتان في المثبِّت، مُفعَّلتان افتراضيًّا:** *البدء مع Windows* و*مراقبة الفأرة ولوحة المفاتيح*. ويُنشأ
  اختصار في قائمة ابدأ دائمًا، واختصار سطح المكتب اختياري.
- **الترقية تحافظ على اختياراتك.** يُعاد استخدام ملف الإعدادات نفسه، ويُغلق التطبيق أثناء التحديث، ويزيل
  برنامج الإزالة إدخال بدء التشغيل وقائمة Explorer واختصار «إرسال إلى» وملفات البرنامج — لكنه يحفظ إعداداتك
  إلا إذا فعّلت **إزالة إعداداتي**.
- **الإعدادات في ستة تبويبات:** عام · العجلات · الجودة · التكامل · التحديثات · حول. وكل مفتاح يظهر مرة واحدة فقط.
- **واجهة التحديث:** عنصر في علبة النظام وإشعار من Windows وشريط بنفسجي في الإعدادات مع **تحديث الآن**
  و**ما الجديد**.
- **صفحة تنزيل** على <https://faisaldhd.github.io/dropwheel/> بالعربية والإنجليزية، تعرض الإصدار الحالي
  وبصمة SHA-256.

## 2.1.1

- **Taskbar icon (R-29):** the Settings and Wheel editor windows forced a 16 px `BitmapImage` as their
  window icon, so the taskbar showed a 16 px image in a 24-32 px slot. Those overrides are gone; WPF now
  uses the executable's icon, which carries all nine frames (16-256 px), so Windows picks the right size
  at 100 % and 150 % scaling.
- **Dark title bar (R-30):** the title bar of the Settings and Wheel editor windows is now dark when the
  dark theme is active, via `DwmSetWindowAttribute(DWMWA_USE_IMMERSIVE_DARK_MODE)`, and it follows a live
  theme switch without a restart.
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
