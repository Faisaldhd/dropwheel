# Notes — update-ui

## Requests from update-ui

### 1. `src/FileAction.Core/Strings/en.json` + `ar.json` — one missing key

Add `settings.updates.unavailable`:

- en: `Updates are not available in this build.`
- ar: `التحديثات غير متوفرة في هذا الإصدار.`

Used by `UpdateBanner` when `IUpdateService` is null (the self-test and a build without the updater). The lead
approved the key and sent it to the strings agent; `LocaleUsageTests` fails red on this branch until both tables
carry it.

### 2. `src/FileAction.App/UI/SettingsWindow.cs` (owner: settings-tabs) — pass the saving callback

When the Updates tab embeds the banner, pass the window's saving action:

```csharp
UpdateBanner.Create(SettingsWindow.Updates, _settings, Save)
```

`Save` writes `settings.json` and calls the app's `OnSettingsChanged`, which starts/stops the 24 h timer. Passing
`_changed` directly would only update memory until the next save/exit.

### 3. `src/FileAction.App/SelfTest.cs` (not owned by update-ui) — optional banner screenshots

The self-test renders `settings.png` with `SettingsWindow.Updates == null`, so it covers only the disabled
"updates are not available" banner. To capture the violet states, set `SettingsWindow.Updates` to a fake
`IUpdateService` whose `Available` is an `UpdateInfo` for e.g. `9.9.9` (release page
`https://github.com/Faisaldhd/dropwheel/releases/tag/v9.9.9`) before constructing `SettingsWindow`, render
`updates-available.png`, then restore `SettingsWindow.Updates = null`. A `Checking`/`Downloading` capture needs the
fake to raise `StateChanged`/report progress; do not do network work in the self-test.
