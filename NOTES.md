# NOTES — cross-agent requests

## Requests from update-core

1. **Setup asset name is inconsistent (blocks the setup-install update path).**
   `UpdateConfig.SetupAssetName` (shared, lead-owned) and the update-core brief name the installer
   `DropWheel-<version>-Setup.exe`, but `installer/DropWheel.iss` sets
   `OutputBaseFilename=DropWheel-Setup-{#AppVersion}`, and `.github/workflows/ci.yml`, `README*.md` and
   `docs/prompts/UPDATE_PROMPT.md` use `DropWheel-Setup-<version>.exe`. The updater looks for the exact
   `DropWheel-<version>-Setup.exe`, so an installed copy would never find the setup asset. Request to the
   installer/CI owners: rename the installer output to `DropWheel-<version>-Setup.exe`; alternatively the
   lead changes `UpdateConfig.SetupAssetName` and tells update-core. The portable name
   (`DropWheel-<version>-portable-win-x64.zip`) already matches everywhere.

2. **Release signing assets (signing agent).** For the selected asset, `UpdateService.DownloadAsync`
   downloads `<asset name>.sig` and the release asset `SHA256SUMS.txt`. The `.sig` must be the base64
   ECDSA P-256 signature over the file's SHA-256 with the `UpdateKey` private key; IEEE P1363 (r||s) and
   DER are both accepted. `SHA256SUMS.txt` must contain `<sha256hex>  <asset name>` (sha256sum format).
   CI currently creates no `.sig` assets, so every download is refused as
   "Update could not be verified" until the signing + upload step lands (this is the intended fail-closed
   behaviour).

3. **GitHub asset `digest` must be populated.** Per the 2.2.0 spec the updater requires BOTH the GitHub
   asset `digest` field (sha256) and the `SHA256SUMS.txt` entry. GitHub fills `digest` for assets uploaded
   to a release; please confirm the v2.2.0 release payload carries it for the setup exe and the portable
   zip before shipping, otherwise every update will be refused.

4. **Tests note (tests agent).** `DownloadAsync` success cannot be exercised with the production
   `UpdateKey` because its private half is a secret. Cover `UpdateVerifier` with a generated P-256 key pair
   (optional `publicKeyBase64` argument) and cover the `DownloadAsync` failure paths (bad/missing
   signature, digest or sums) plus cancellation; assert the exact message
   `UpdateService.VerificationFailure` ("Update could not be verified").

5. **Version shape (tests agent).** `UpdateInfo.Version` is normalised to the shortest exact form
   (`2.3.0`, not `2.3.0.0`) unless the tag has a non-zero revision. Tags such as `v2.3.0` therefore compare
   equal to `new Version(2, 3, 0)`; asset names still use `Version.ToString(3)` via `UpdateConfig`.

## Requests from settings-tabs

### 1. Strings — merged, nothing to do

The six-tab window uses exactly the 2.2.0 keys from `AGENT_RULES.md`. They arrived with the strings branch
(main `ce3dcdc` / `7cc3e30`), so `LocaleUsageTests` passes now. Reused keys: `settings.language.*`,
`settings.gesture.*`, `settings.wheels.customize.*`, `settings.keyboard.*`, `settings.results.*`,
`settings.quality.*`, `settings.company.*`, `settings.tools.*`, `settings.footer`, `language.english`,
`language.arabic`. `settings.updates.automatic.*` and `settings.updates.check*` are **not** used here: the
update banner owns the automatic-check switch and every check/download state (no second toggle).

Notes on two strings, for whoever edits the tables next:

- `settings.updates.detail` is shown as the description above the banner when an update service exists.
  When it does not (self-test build / no service), the tab shows `settings.updates.unavailable` instead —
  that key was added by the strings branch exactly for this state and is more accurate than the old
  "show detail instead" wording.
- `settings.about.website` is "Website" (no `{0}`). About renders it as a link to
  `https://github.com/Faisaldhd/dropwheel` and appends that URL to the label when the localized value does
  not contain a URL, so the address is visible in both languages. No table change needed.

### 2. App.cs (not mine): assign the update service

`SettingsWindow.Updates` (the static property on main) has to be set by `App.cs` once the update service
exists, e.g. `SettingsWindow.Updates = _updates;`. Until then the Updates tab shows
`settings.updates.unavailable` and no banner. `UpdateBanner.Create` is called with the service, this
window's `AppSettings` and the window's `Save` callback.

### 3. Self-test tabs (`SelfTest.cs` is not mine)

`SettingsWindow` exposes `internal static int TabCount` (= 6) and
`internal void SelectTab(int index)` (0 = General, 1 = Wheels, 2 = Quality, 3 = Integration,
4 = Updates, 5 = About). For per-tab screenshots, `SelfTest.RenderAll` must construct a **fresh**
`SettingsWindow` for each index (because `SaveWindow` detaches `window.Content`, so one window cannot be
rendered twice), call `SelectTab(i)` and then `SaveWindow`; suggested names
`settings-general.png` … `settings-about.png`. Not required for this branch.

### 4. Design note

The three gesture-key chooser cards are in **General**, under `settings.general.gestures.title/detail`
(as approved). **Wheels** shows customise, pause and the keyboard shortcut plus the
`settings.general.gestures.detail` pointer to General — no control is duplicated. The watch and
download toggles are greyed out and non-interactive when the HKLM policy locks them.

### 5. Merge-resolution warning (SettingsWindow.cs)

Inside a `Window`, WPF's `Window.ThemeMode` instance property shadows the app's `ThemeMode` enum name, so
`ThemeMode.System` gives CS0176. The file uses `using AppThemeMode = FileAction.App.ThemeMode;`. Keep that
alias when resolving future merges — do not "simplify" it back.
