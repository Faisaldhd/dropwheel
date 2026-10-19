# Auto-update for the portable build (v2.2.0) — implementation spec

Owner requirement: **the user does nothing except click "Update".** This file is the agreed scope; it is not
a review file. Do not tag v2.2.0 until the owner has reviewed it.

## Behaviour

| Moment | What happens |
|---|---|
| 60 s after start, then every 24 h | `GET https://api.github.com/repos/Faisaldhd/dropwheel/releases/latest` (anonymous, `User-Agent: DropWheel`). Silent when offline or rate-limited — no dialog, log line only. |
| Newer version found | Tray balloon "DropWheel x.y.z is available" **and** a tray menu item "Update to x.y.z". Nothing is downloaded yet. |
| User clicks Update | Download `DropWheel-<version>-portable-win-x64.zip` + its `.sig`. Verify **both** the GitHub asset `digest` (sha256) and the ECDSA signature over the zip against the public key embedded in the app. On any mismatch: delete both files and show "Update could not be verified". |
| Verified | Start `DropWheel.exe --apply-update <zip> <version>`, exit the running app. |

## Settings UI (added after the first spec)

- When an update is available, a **violet banner at the top of the Settings window**:
  `Update available: x.y.z` with an **[Update now]** button and a **"What's new"** link (release page).
- A **"Check for updates"** button in Settings; when there is nothing newer it shows
  **"You're up to date"**, otherwise the banner state.
- All strings keyed in `src/FileAction.Core/Strings/en.json` + `ar.json` (Arabic mirrored, RTL-safe; version
  numbers wrapped in U+2066…U+2069 as the existing bidi helper does).

## Applying the update (`--apply-update <zip> <version>`)

- Helper process started by the running app; the app then exits.
- Waits for the old process to exit (timeout → give up and log, never half-replace).
- Unpacks into a sibling staging folder, then replaces the app files while keeping `settings.json`,
  `log.txt`, `portable.txt` and the `licenses\` folder intact.
- Restarts `DropWheel.exe --updated=<x.y.z>`; that start shows "Updated to x.y.z" with a What's new link.
- Failure at any step restores the previous files from the staging backup and reports.

## Signing

- One ECDSA P-256 key pair generated **once**, by the lead, in a session with budget to verify the result.
- Private key → `gh secret set UPDATE_SIGNING_KEY`; never in the repo, never in a workspace file, no copy left.
- Public key embedded in the app as a single constant; the private key is never needed by the app.
- CI signs every release asset: `DropWheel-Setup-<v>.exe.sig` and `DropWheel-<v>-portable-win-x64.zip.sig`,
  attached to the release alongside `SHA256SUMS.txt`.

## Tests

- version comparison (`2.2.10 > 2.2.9`, `2.2.0 > 1.9.9`), never string comparison;
- good signature → accepted; tampered zip → rejected; wrong key → rejected; missing signature → rejected;
- offline / HTTP error → no exception surfaces, no dialog, state stays "up to date";
- the Settings banner state machine: none → available → downloading → failed / up-to-date.

## Constraints that apply while building this

- **Nothing runs on the owner's laptop.** No DropWheel.exe, no installer, no `--self-test`, no tool downloads.
  Builds, tests and packaging come from GitHub Actions.
- The app is a GUI exe: commands that wait for it must use `Start-Process -Wait` with a timeout, never
  `$LASTEXITCODE`.
- Arabic and English only; every new string goes through the key tables and `LocaleUsageTests`.
- Version bump to **2.2.0** in `FileAction.App.csproj` + `CHANGES.md`; tag only after the owner's review.
