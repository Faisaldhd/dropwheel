# Fix acceptance — Team A fixer checklist (2.4.0)

Two P1 fixes need a precise, checkable acceptance test so a "green" fix cannot be a partial one. Each
section below is the acceptance contract for that finding: all numbered items must hold, and the PR that
claims the fix must link the evidence (test names + the CI run id where they passed).

Sources: `docs/audit/BUG-HUNT-RACES.md` (RC-01) and `docs/audit/BUG-HUNT-UPGRADE.md` (UP-01) in the
consolidated `docs/audit/BUG-HUNT.md`. Nothing here changes the shipped docs; it is the reviewer's list.

## RC-01 — closing a tool window during Apply must not corrupt output

Source: `BUG-HUNT-RACES.md` RC-01 (Crop, Edit Photos, Annotate, Redact).

A passing fix must show ALL of:

1. The window-close path cancels the in-flight write's cancellation token (`ToolWindow.OnClosing`/`OnClosed` -> view CTS), not just the fade/motion.
2. The four writers emit only through `OutputPaths.ProduceAsync` (`.part` -> verify -> atomic move), so no writer touches the final path directly.
3. Test A (cancel before completion): close mid-write -> **final output absent**, **no `.part` left in the user's output folder**, source file byte-identical.
4. Test B (cancel after completion): a write that finished before close leaves a **valid, fully-decodable** output (no truncated image).
5. No regression: existing Crop/EditPhotos/Annotate/Redact tests stay green and output naming is unchanged.
6. CI evidence: test names + the run id where they passed.

## UP-01 — a corrupt settings.json must never be destroyed silently

Source: `BUG-HUNT-UPGRADE.md` UP-01 (`AppSettings.LoadFrom/SaveTo`).

A passing fix must show ALL of:

1. On parse/IO failure `LoadFrom` copies the original to `settings.bad-<timestamp>.json` **before** returning defaults; the original is never deleted or overwritten in that pass.
2. The user gets a one-time notice that settings were reset and a backup was kept (localized, both en and ar).
3. Test A (corrupt file): write invalid JSON -> load -> defaults returned AND backup exists with the original bytes; then `Save()` and assert the backup is still intact and `settings.json` is valid.
4. Test B (missing file, first run): no backup is created and no notice is shown — first run must stay silent.
5. Test C (write failure/permissions): no crash, app still starts, no orphan `settings.json.tmp`.
6. CI evidence: test names + run id.
