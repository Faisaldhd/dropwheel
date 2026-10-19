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

## Requests from update-apply

Nothing outside `src/FileAction.App/Updates/**` and `scripts/test-update-apply.ps1` was edited.

### 1. `src/FileAction.App/App.cs` (update-ui / lead) — wire the helper and `--updated`

Required for the helper to work at all:

```csharp
// Program.Main, immediately after AppLog.Initialize() and BEFORE --self-test / the single-instance check:
if (UpdateApplier.TryRun(args, out var updateExit))
    return updateExit;
```

`--apply-update` must run before `SingleInstance.TryAcquire()`: the old app is still running when the
helper starts, so it must never be treated as a second instance. `AppLog.Initialize()` must stay before
it so both helper hops leave a log.

```csharp
// App.OnStartup — after LanguageManager.Apply(...) and Theme.Apply(...), on the first-instance start:
var updatedIndex = Array.IndexOf(args, "--updated");
if (updatedIndex >= 0 && updatedIndex + 1 < args.Length)
    UpdateApplier.AnnounceUpdated(args[updatedIndex + 1]);
else if (args.FirstOrDefault(a => a.StartsWith("--updated=", StringComparison.Ordinal)) is { } updatedFlag)
    UpdateApplier.AnnounceUpdated(updatedFlag["--updated=".Length..]);
```

`using FileAction.App.Updates;` is needed in both `Program` and `App`. The helper relaunches with
`--updated <version>` (space form); the `=` form is supported too because `docs/auto-update-spec.md`
writes `--updated=<x.y.z>`. Do not handle `--updated` in `TryRun`: it must keep returning false for
normal startup.

### 2. Strings — done on main

`update.updated.title`, `update.updated.body` and `update.updated.whatsNew` are in both tables on main
(`7cc3e30`). Merging main removes the `LocaleUsageTests` failure the first branch CI run showed
(`update.updated.title` / `.whatsNew` missing). No further string work is needed from update-apply.

### 3. `.github/workflows/ci.yml` (lead; routed to release-clean)

The script is CI-only and needs `windows-latest`. Exact step for the job that has a Release build:

```yaml
      - name: Apply-update helper (portable)
        shell: pwsh
        run: '& ./scripts/test-update-apply.ps1'
```

It starts the built `DropWheel.exe` in `--apply-update` mode (a windowless process) and checks the
success, corrupt-zip and rollback cases. If the Release build output is missing, the script publishes a
framework-dependent folder itself with `dotnet publish`. Never run it on the owner's PC.

### 4. Note on the helper copy (for review, not a request)

The portable build is a **folder** (apphost + DLLs), not a single-file exe, so a lone `DropWheel.exe`
copy in `%TEMP%` cannot start. `PortableUpdater` therefore copies the running exe (as required) **and**
the app's runtime files, while excluding user data (`settings.json`, `portable.txt`, `log.txt`),
`licenses\` and `tools\`. That is what frees the install folder's exe lock without shipping a broken
helper.

## What update-apply did

- Replaced the `UpdateApplier` stub; added `PortableUpdater`, `SetupUpdater` and `UpdatedWindow` under
  `src/FileAction.App/Updates/`.
- Added `scripts/test-update-apply.ps1` (success, corrupt-zip and true-rollback cases).
- No changes to `App.cs`, `TrayIcon.cs`, `SettingsWindow.cs`, `UpdateBanner.cs`, `ci.yml` or the string tables.
