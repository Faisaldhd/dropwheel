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

## Requests from update-ui

### 1. `src/FileAction.App/UI/SettingsWindow.cs` (owner: settings-tabs) — pass the saving callback

When the Updates tab embeds the banner, pass the window's saving action:

```csharp
UpdateBanner.Create(SettingsWindow.Updates, _settings, Save)
```

`Save` writes `settings.json` and calls the app's `OnSettingsChanged`, which starts/stops the 24 h timer.
Passing `_changed` directly would only update memory until the next save/exit.

### 2. `src/FileAction.App/SelfTest.cs` (not owned by update-ui) — optional banner screenshots

The self-test renders `settings.png` with `SettingsWindow.Updates == null`, so it covers only the disabled
"updates are not available" banner. To capture the violet states, set `SettingsWindow.Updates` to a fake
`IUpdateService` whose `Available` is an `UpdateInfo` for e.g. `9.9.9` (release page
`https://github.com/Faisaldhd/dropwheel/releases/tag/v9.9.9`) before constructing `SettingsWindow`, render
`updates-available.png`, then restore `SettingsWindow.Updates = null`. A `Checking`/`Downloading` capture needs
the fake to raise `StateChanged`/report progress; do not do network work in the self-test.
