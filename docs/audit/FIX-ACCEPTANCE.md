# Fix acceptance criteria - RC-01 and UP-01

Owner of this file: TEAM B supervisor. Purpose: give Team A's two fixers a precise, falsifiable
acceptance test, and give the independent re-testers (races lane for RC-01, release lane for UP-01) the
same wording so the verdict cannot drift. A fix is accepted only when **every** numbered criterion is
demonstrated by a named test on a green CI run; "it looks right" is not a criterion.

---

## RC-01 - closing a tool window during Apply must not corrupt output

Source finding: `docs/audit/BUG-HUNT-RACES.md` RC-01 (Crop, Edit Photos, Annotate, Redact).
Defect: the window-close path never cancels the in-flight write, and those four writers write the final
path directly, so closing the window mid-Apply can leave a partial or corrupt file at the user's output path.

A passing fix must show ALL of:
1. **Cancellation reaches the writer.** The window-close path cancels the in-flight write's cancellation
   token (`ToolWindow.OnClosing`/`OnClosed` -> the view's CTS). Fading the window or stopping motion is not
   cancellation.
2. **All four writers are atomic.** Crop, Edit Photos, Annotate and Redact emit output only through
   `OutputPaths.ProduceAsync` (`.part` -> verify -> atomic move); none of them writes the final path directly.
3. **Test A - cancel before completion.** Close mid-write, then assert: the final output path does not
   exist, **no `.part` file remains in the user's output folder**, and the source file is byte-identical
   (compare a SHA-256 before/after).
4. **Test B - cancel after completion.** A write that finished before the close leaves a valid, fully
   decodable output (open it and decode; no truncated image, correct dimensions).
5. **No regression.** The existing Crop / Edit Photos / Annotate / Redact tests stay green and output
   naming is unchanged (same suffix rules and collision behaviour).
6. **Evidence.** The tests are named in the PR, they run in CI, and the run id is quoted.

Automatic FAIL: a fix that only wraps the write in `try/catch` (fails 1 and 2), a fix that leaves a `.part`
behind, or a fix that changes output naming to avoid the problem.

---

## UP-01 - a corrupt settings.json must never be destroyed silently

Source finding: `docs/audit/BUG-HUNT-UPGRADE.md` UP-01 (`AppSettings.LoadFrom`/`SaveTo`).
Defect: a parse or IO failure returns defaults and the next `Save()` writes those defaults over the
original file, with no backup and no notice - the user's settings are lost.

A passing fix must show ALL of:
1. **Backup before defaults.** On parse/IO failure, `LoadFrom` copies the original to
   `settings.bad-<timestamp>.json` **before** returning defaults. The original file is never deleted or
   overwritten in that pass.
2. **The user is told once.** A one-time, localized notice that settings were reset and a backup was kept,
   present in **both** `en` and `ar` (real keys in both tables, not just "a notice exists").
3. **Test A - corrupt file.** Write invalid JSON, load: defaults are returned AND the backup exists holding
   the **original bytes**; then call `Save()` and assert the backup is still intact and `settings.json` is
   valid JSON afterwards.
4. **Test B - first run stays silent.** With no settings file present, loading returns defaults, **no backup
   is created**, and no notice is shown. First run must not look like a failure.
5. **Test C - write failure.** With the settings path unwritable, the app still starts, does not crash, and
   leaves no orphan `settings.json.tmp`.
6. **Evidence.** Tests named, run in CI, run id quoted.

Automatic FAIL: a backup written on the first-run path, or any path on which the original file can be lost.

---

## How a fixer claims done

In the PR body: `RC-01: criteria 1-6 met - tests <names> - run <id>` (or `UP-01: ...`). The independent
re-tester posts `PASS/FAIL per criterion 1-6` as a PR comment. The finding is closed in the bug-hunt file
only after the fixer has merged and a run on `main` is green.