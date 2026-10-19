# NOTES — update-apply (2.2.0)

Requests for files owned by other agents. Nothing outside `src/FileAction.App/Updates/**` and
`scripts/test-update-apply.ps1` was edited.

## Requests from update-apply

### 1. `src/FileAction.App/App.cs` (lead) — wire the helper and `--updated`

The helper cannot work until these two call sites exist:

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
`--updated <version>` (space form); the `=` form is supported here too because `docs/auto-update-spec.md`
writes `--updated=<x.y.z>`. Do not handle `--updated` in `TryRun`: it must keep returning false for normal
startup.

### 2. `src/FileAction.Core/Strings/en.json` + `ar.json` (strings agent) — three keys

The announcement uses exactly the shared keys from `AGENT_RULES.md`; they are not in the tables yet, so
`LocaleUsageTests.Every_key_used_in_source_exists_in_both_tables` will stay red until they land:

| key | English | Arabic suggestion |
|---|---|---|
| `update.updated.title` | `DropWheel updated` | `تم تحديث DropWheel` |
| `update.updated.body` | `DropWheel was updated to {0}.` | `تم تحديث DropWheel إلى الإصدار ⁦{0}⁩.` |
| `update.updated.whatsNew` | `What's new` | `ما الجديد` |

The Arabic body wraps `{0}` in U+2066…U+2069 as the rules require. `AnnounceUpdated` also uses the
existing `action.close` key for its second button.

### 3. `.github/workflows/ci.yml` (lead) — one step

The script is CI-only. Add it to the `build-test` job after **Build** (the Release build output is enough;
the script publishes a framework-dependent folder itself if `bin\Release\net10.0-windows\DropWheel.exe` is
missing):

```yaml
      - name: Apply-update helper (portable)
        shell: pwsh
        run: '& ./scripts/test-update-apply.ps1'
```

It needs `windows-latest`; it starts the built `DropWheel.exe` in `--apply-update` mode (a windowless
process) and checks the success, corrupt-zip and rollback cases. Never run it on the owner's PC.

### 4. Note on the helper copy (for review, not a request)

The portable build is a **folder** (apphost + DLLs), not a single-file exe, so a lone `DropWheel.exe`
copy in `%TEMP%` cannot start. `PortableUpdater` therefore copies the running exe (as required) **and**
the app's runtime files, while excluding user data (`settings.json`, `portable.txt`, `log.txt`),
`licenses\` and `tools\`. That is what frees the install folder's exe lock without shipping a broken
helper.

## What update-apply did

- Replaced the `UpdateApplier` stub; added `PortableUpdater`, `SetupUpdater` and `UpdatedWindow` under
  `src/FileAction.App/Updates/`.
- Added `scripts/test-update-apply.ps1` (three CI cases).
- No changes to `App.cs`, `TrayIcon.cs`, `SettingsWindow.cs`, `UpdateBanner.cs`, `ci.yml` or the string tables.
