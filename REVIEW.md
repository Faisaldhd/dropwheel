# REVIEW.md — written by Claude (reviewer). DeepSeek: read this first, every session.

Review of build 2.0.0 (release\DropWheel-Setup-2.0.0.exe, 63.9 MB).

| ID | Sev | Finding | Evidence | Required fix | State |
|---|---|---|---|---|---|
| R-1 | P1 | Wrong theme and icon: the outdated "Violet Glass" citrus theme was applied. | CHANGES.md "Violet Glass theme"; old icon in the release | Re-apply the updated `THEME_PROMPT.md`: flat dark + violet #8224D4 / #B48AE0, soft corners. Use the new `branding\` icons and the tray icons, with light/dark switching. | open |
| R-2 | P1 | Size regression: setup grew from 50 MB to 64 MB; publish folder is 219 MB. | release folder: `Microsoft.Windows.SDK.NET.dll` 24.9 MB, untrimmed `System.Private.CoreLib` 16 MB, `.pdb` still shipped | Re-enable `PublishTrimmed` (partial), trim the Windows SDK projection, ship no pdb. Target setup < 50 MB. Report before/after. | open |
| R-3 | P2 | CHANGES.md claims the namespaces were renamed, but `FileAction.Core.dll`, `FileAction.sln` and `tests\FileAction.Tests` remain. | file listing | Finish the rename, or correct CHANGES.md. | fixed (cb461fd) |
| R-4 | P2 | Scratch files in the workspace root; a leftover empty `FileAction\` folder. | `_issfix.py`, `_orgrules.py`, `_rebrand*.py`, `_testfix*.py`, `_theme.py` | Delete them. | fixed (verified on GitHub cb461fd) |
| R-5 | P2 | No `docs\screenshots\` were produced for review. | CHANGES.md "Not done" | After R-1, run `--self-test` and commit the screenshots. | open |
| R-6 | P2 | A personal path is published in the public repo. | `scripts/fix_changes.py` line 2: `C:\Users\FaisalSaeedMohammedD\...` | Delete `scripts/fix_changes.py` (it was a one-off). Grep the repo for `FaisalSaeedMohammedD` and `C:\Users\` → must be 0 hits. Add `scripts/_*` to `.gitignore` for future one-off scripts. | open |
| R-7 | P2 | Linux packaging still points to the upstream project. | `packaging/aur/PKGBUILD.in` and `packaging/make-linux-package.sh` contain `Metamorfolog` | Either remove `packaging/` (we don't ship Linux) or rebrand it to DropWheel / Faisaldhd. Recommended: remove it. | open |

Verified on GitHub (cb461fd): history rewritten to one clean commit, 114 files, no caches, no secret patterns. Good work.

NEXT (in order):
0. R-6 and R-7 (quick), commit + push.
1. Audit the git history for secrets and caches (see UPDATE_PROMPT.md, Hosting model), then push to `origin` = Faisaldhd/dropwheel. The remote only has an auto README: use `git push -u origin main --force` ONCE, never again. Create `STATUS.md` (format in `COLLAB.md`).
2. R-4 → R-1 → R-2 → R-3 → R-5. Commit and push after each one.
3. Then `UPDATE_PROMPT.md`, with `UpdateConfig.Repository = "Faisaldhd/dropwheel"`.
