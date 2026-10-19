# NOTES

## Requests from settings-tabs

### 1. Strings (owner: strings agent / lead) — CI stays red until merged

`src/FileAction.App/UI/SettingsWindow.cs` now uses the 2.2.0 tab keys from `AGENT_RULES.md`. They do not
exist in `src/FileAction.Core/Strings/en.json` / `ar.json` on this branch yet, so
`LocaleUsageTests.Every_key_used_in_source_exists_in_both_tables` fails **by design** until the strings
branch is merged (the lead asked for exactly these call sites, with no local keys and no workaround).

Keys referenced directly (must exist in both tables):

- tabs: `settings.tab.general`, `settings.tab.wheels`, `settings.tab.quality`,
  `settings.tab.integration`, `settings.tab.updates`, `settings.tab.about`
- watch: `settings.general.watch.title`, `settings.general.watch.detail`,
  `settings.general.watch.policy`
- theme: `settings.general.theme.title`, `settings.general.theme.detail`, `settings.theme.system`,
  `settings.theme.dark`, `settings.theme.light`
- gestures: `settings.general.gestures.title`, `settings.general.gestures.detail`
- updates: `settings.updates.title`, `settings.updates.detail`
- about: `settings.about.tagline`, `settings.about.version`, `settings.about.licenses`,
  `settings.about.source`, `settings.about.website`, `settings.about.credits`

Existing keys reused (no change requested): `settings.language.*`, `settings.gesture.*`,
`settings.wheels.customize.*`, `settings.keyboard.*`, `settings.results.*`, `settings.quality.*`,
`settings.company.*`, `settings.tools.*`, `settings.footer`, `language.english`, `language.arabic`,
`settings.header.tagline` (no longer used by this window).

Two of the new keys are called with one format argument, so please keep a `{0}` where the value belongs:

- `settings.about.version` is called as `T("settings.about.version", version)` (version = `2.2.0`).
  English should read like `Version {0}`; if the value has no placeholder the window appends the
  version itself, so the number still shows.
- `settings.about.website` is called as
  `T("settings.about.website", "https://github.com/Faisaldhd/dropwheel")`. Plain text or `{0}` both work.

`settings.updates.automatic.*` and `settings.updates.check*` are **not** used by this window: the update
banner owns the automatic-check switch and every check/download state (per the spec: no second toggle).

### 2. App.cs (not mine): assign the update service

`SettingsWindow.Updates` (the static property on main) has to be set by `App.cs` once the update service
exists, e.g. `SettingsWindow.Updates = _updates;`. Until then the Updates tab shows
`settings.updates.detail` (the specified fallback) and no banner. `UpdateBanner.Create` is called with the
service, this window's `AppSettings` and the window's `Save` callback.

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
