# Deferred from 2.4.0 — not release blockers

Everything here was known before the 2.4.0 tag and deliberately **not** allowed to delay it. None of these
items is a data-loss, security or correctness gate: the release gates (CI green, shipped artifact
verified, no data-loss bugs) are never skipped to make room. A row is added when work exists but cannot
honestly finish in the release window, with the next concrete step so it does not get lost.

**This file is append-only.** Add one row at the end of the table; do not reorder, rewrite or delete
another lane's row, and do not move a shipping item here. A deferred item that later lands stays in the
table with its outcome in **Next step** (for example "done in `<commit>`").

| Item | Lane | Why deferred | Next step |
|---|---|---|---|
| Per-window bilingual rendering gate (every tool window in English and Arabic, plus the portrait and 200 % DPI matrix) | `audit/selftest` (with `audit/al10n`) | The gate is still being implemented; until it is green the only provable claim is string-table parity (1,094 keys per table, enforced in CI), not per-window bilingual rendering | Land `audit/selftest`; extend its matrix to portrait orientation and 200 % DPI; then upgrade the README/CHANGES wording from "gate pending" to "asserted in CI" in one edit |
| UX-audit items that need a real desktop session: hit targets at 150 % scaling, focus-ring contrast, pixel-exact clipping after window shaping | `audit/adesign` | Cannot be verified headlessly in CI, and the owner's desktop is the only honest measuring surface | Re-run `docs/audit/UX-AUDIT.md`'s open items on a desktop session at 100/150/200 %, record before/after, then close or re-scope each row |
| Duplicate `tools.ocr` operation id in `src/FileAction.Linux/TesseractOcr.cs:15` (the Windows app registers `tools.ocr` in `src/FileAction.App/Ocr/OcrOperation.cs:29`), and that Linux copy still passes a legacy two-language `T(czech, english)` literal | Docs/cleanup (no owner yet) | Not shipped on Windows, so no user impact in this release; renaming the id touches the Linux harness and its tests | Give the Linux operation its own id (or delete the Linux duplicate if the harness no longer uses it) and drop the legacy two-argument `Loc.T` call, so the "no Czech" rule also covers `src/FileAction.Linux` |
| Download-page screenshots still show the 2.2.x Settings, action wheel and Jobs windows (they predate the 2.4.0 tool windows and the **Animations** card) | `audit/adocs` (site is docs-owned), rendered by the screenshots job | Screenshots need a real desktop session, and the CI screenshots job runs only on demand; the page was version-bumped for this tag without waiting for new renders | Run the CI screenshots job (or `scripts/screenshots.cmd`) for 2.4.0, copy the fresh English `settings` / `wheel-actions` / `jobs` PNGs into `docs/site/assets/`, update the `width`/`height` and both alt texts, then delete this row's "still shows" wording |

## Adding a row

Append exactly one line in this shape, then commit it on your own branch:

```
| <what is deferred> | <branch / lane> | <why it could not land before the tag> | <concrete next step and who does it> |
```

Keep it one sentence per cell, name the file or command where it helps, and never use this file for a
release gate. The row pattern above is the placeholder: copy it, fill the four cells, delete nothing else.
