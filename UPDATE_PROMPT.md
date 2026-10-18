# DropWheel — Our own update system (owner decision)

Auto-update comes back, but it points ONLY to the owner's own GitHub. It must never point to the original FileAction project.

## Hosting model
- **Source code:** a PRIVATE repo, owned by Faisal (for example `<owner>/dropwheel`).
- **Installers:** a separate PUBLIC repo used only for releases (for example `<owner>/dropwheel-releases`).
  - The app reads releases anonymously, so this repo must be public.
  - It contains no source code: only a README and the releases.
  - Never embed a GitHub token in the app.
- Put the repo name in ONE constant (`UpdateConfig.Repository = "<owner>/dropwheel-releases"`) and leave a clear TODO until the owner gives the real name.

## 1. Restore the updater from git history
The updater was removed in the 2.0.0 cleanup. Restore `Updates.cs`, `Updater.cs`, the tray item, the notification, the Settings toggle and the installer `--updated` run entry from the commit before that removal (`git log --diff-filter=D --name-only`).

Then change the following:
- Repository → `UpdateConfig.Repository` (our releases repo). Grep afterwards: no `Metamorfolog` / `file_action` anywhere except LICENSE.
- Asset names: `DropWheel-Setup-<version>.exe` and `DropWheel-Setup-<version>.exe.sig`.
- Check rules:
  - on start (delayed 60 s), then every 24 h
  - setting **"Check for updates automatically"**, default ON
  - tray item **"Check for updates now"**
  - never more than one download at a time
  - work silently when there is no internet

## 2. Security: signed updates (mandatory)
A hijacked GitHub account must not be able to push malware to users.
- Generate an ECDSA P-256 key pair once: `scripts\make-update-key.ps1`.
  - The private key stays OUTSIDE the repo, in `%USERPROFILE%\.dropwheel\update-key.pem`, and must never be committed. Print a warning to back it up.
  - The public key is embedded in the app as a constant.
- `release.cmd` must sign the installer:
  1. Compute the SHA-256 of `DropWheel-Setup-x.y.z.exe`.
  2. Sign it with ECDSA.
  3. Write the signature to `DropWheel-Setup-x.y.z.exe.sig` (base64).
- Before running a downloaded installer, the app verifies BOTH:
  - the GitHub asset `digest` (sha256), when present
  - the `.sig` signature against the embedded public key

  If either check fails: delete the file, log it, and show "Update could not be verified". Never run it.
- The installer runs with `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS` and restarts the app with `--updated`. After a restart with `--updated`, show "Updated to x.y.z" with a "What's new" link to the release page.

## 3. Publishing script
`scripts\publish-release.ps1 -Version x.y.z`:
1. Check that the csproj `<Version>` equals x.y.z and that `dotnet test` passes.
2. Build the installer and sign it (step 2).
3. Create the GitHub release with the `gh` CLI: tag `vx.y.z`, title `DropWheel x.y.z`, notes taken from the matching `CHANGES.md` section, and both assets attached. `gh auth login` is the owner's job; the script stops with a clear message if `gh` isn't logged in.
4. Print the release URL.

## 4. Version rules
Use semantic versions:
- **patch** (2.0.x): fixes
- **minor** (2.x.0): features
- **major**: big changes

The single source is the csproj `<Version>`. The installer `AppVersion` reads it. The app compares versions as numbers, never as strings.

## 5. Tests and verification
- Unit tests:
  - version comparison (2.0.10 > 2.0.9)
  - a good signature passes; a tampered installer fails; a wrong key fails
  - "no newer version" does nothing
- REA:
  - Run `inspect_managed_members` on `DropWheel.dll`. The user strings must contain our `UpdateConfig.Repository` and must NOT contain `Metamorfolog`.
  - Confirm the public key constant is present.
- Update CHANGES.md: "Auto-update (signed) from our own releases".

## 6. Owner checklist (put this at the end of README.md)
1. Create a GitHub account (or use an existing one), then two repos: `dropwheel` (private, for the source) and `dropwheel-releases` (public, for the releases).
2. Send the repo name to the developer; it goes into `UpdateConfig.Repository`.
3. Install the GitHub CLI and log in: `winget install GitHub.cli`, then `gh auth login`.
4. Run `scripts\make-update-key.ps1` ONCE and back up the key file. Without it, you can never ship an update to existing users.
5. For every release: bump `<Version>`, write the CHANGES.md section, then run `scripts\publish-release.ps1 -Version x.y.z`.
