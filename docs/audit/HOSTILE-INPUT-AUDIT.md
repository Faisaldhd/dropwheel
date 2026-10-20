# Hostile-input audit — DropWheel 2.4.0

TEAM B lane 8 (`audit/asec`). Static reading of the Core entry points plus a deterministic test suite that
runs in CI. **Nothing in this lane was executed locally**: no build, no `dotnet test`, no installer, no
`--self-test`, no signing or update-verification code was read for modification or run. Every result below
comes from either (a) reading the shipped source in this worktree or (b) a GitHub Actions run of the
new suite.

The crypto/container side (Protect, Unlock, Lock, `Lock*`) is **not** repeated here — it is covered by
[`SECURITY-AUDIT.md`](SECURITY-AUDIT.md) in this folder, which is unchanged by this lane.

---

## 1. Evidence inventory

| Evidence | Path | Notes |
| --- | --- | --- |
| Regression suite (71 cases) | `tests/FileAction.Tests/HostileInputTests.cs` | 29 `[Fact]` + 42 theory cases; serialised collection `HostileIo` |
| Fixture isolation | same file, `HostileRoot` | one root per test under `_local/tmp/asec` (owner machine, git-ignored) or `_audit-ci` (CI); `TMP`/`TEMP` redirected into it; the test deletes the tree |
| H-01 fix | `e347abc`…`7fd3d7a` | `src/FileAction.Core/Backends/ArchiveBackend.cs:115` |
| H-02 fix | `e3ee779` | `src/FileAction.App/Jobs/JobRunner.cs:221` |
| CI run | see §5 | required for every PASS in the matrix |

Reproduce a family locally only on a machine allowed to build:

```
dotnet test tests/FileAction.Tests/FileAction.Tests.csproj -c Release --filter "FullyQualifiedName~HostileInput"
```

The suite needs no helper tool, no network, no desktop session and no sample files.

---

## 2. What was thrown at the product

* **Names** — leading/trailing dots and spaces, `CON PRN AUX NUL COM1 LPT9` reserved forms, emoji,
  Arabic RTL, mixed bidi with U+202E, combining marks (NFD) vs precomposed (NFC), 255-character basename,
  paths longer than 260 characters, names that differ only by case, `.`/`..`-shaped strings, empty and
  whitespace names.
* **Content** — 0-byte, 1-byte, a PNG signature with nothing behind it, text renamed to `.png`, a valid
  PNG renamed to `.jpg` (extension lies), a valid header with a corrupted footer, an empty PDF, a PDF with
  a nonsense xref, an empty ZIP, a ZIP whose entry escapes the destination, an entry name with an embedded
  NUL.
* **Size** — a 5 GiB file created with `SetLength` (no payload written) for the ZIP32 refusal, and a
  64 MiB sparse file for the streaming path.
* **Access** — a file held with `FileShare.None`, a read-only file, the same path given twice, a source
  whose name collides with its own output, two identical files (the benign stand-in for a hardlink).
* **Cancellation/teardown** — a pre-cancelled token, cancellation during protect, a producer that throws.

Every case asserts the same three invariants: **an actionable message** (localized key, no framework type
name, no stack trace), **no crash**, and **zero partial output** (no result file, no `.part` leftover,
source bytes unchanged).

---

## 3. Per-tool matrix

Reachability is all 26 registered tool windows after `4f23c9c` wired `tools.annotate` and `tools.redact`
into `WheelController.BuildOperations()`; this branch merges that commit (`96eaa72`). Before the merge both
were unreachable from the wheel, which is why their hostile coverage is thin rather than absent.

| Tool id | Hostile cases covered | Status | Test |
| --- | --- | --- | --- |
| `tools.addbackground` | weird/emoji/Arabic/bidi/255-char source names, source-vs-output collision, no overwrite | **PASS** | `A_hostile_file_name_survives_output_naming`, `A_source_that_collides_with_its_own_output_is_never_overwritten`, `A_255_character_name_is_numbered_and_never_overwrites_the_source` |
| `tools.collage` | same shared naming/collision path | **PASS** (naming only) | as above |
| `tools.editphotos` | corrupt/0-byte/1-byte/lying-extension image, read-only source, cancelled token | **PASS** | `Corrupt_image_input_fails_without_partial_output`, `An_extension_that_lies_is_still_decoded_by_content`, `A_read_only_file_is_read_and_never_modified`, `A_cancelled_run_leaves_no_output_and_no_part_file` |
| `tools.compress` | format classification via `Formats`/`Compression.KindOf`; locked source; sparse 64 MiB | **PASS** (partial: numeric clamping not covered) | `Hostile_names_never_throw_in_the_name_helpers`, `A_locked_source_fails_cleanly_and_leaves_no_partial_archive` |
| `tools.metadata` | name/format classification only | **blocked — needs ffmpeg/desktop** | `Hostile_names_never_throw_in_the_name_helpers` |
| `tools.qr` | reserved/emoji/Arabic names only | **blocked — decoder not exercised** | `Emoji_and_arabic_names_keep_their_characters_through_the_name_helpers` |
| `tools.pdfmerge` | empty PDF, corrupt PDF (localized `error.pdf.cannotOpen`) | **PASS** (error surface) | `An_empty_pdf_is_refused_and_leaves_no_output`, `A_corrupt_pdf_gets_the_localized_cannot_open_message` |
| `tools.pdforganize` | same PDF surface | **PASS** (error surface) | as above |
| `tools.protect` | empty plan, 5 GiB entry, 64 MiB sparse streaming, cancellation, duplicate path | **PASS** | `Protected_zip_refuses_an_empty_plan_with_a_localized_key`, `A_file_over_the_zip32_limit_is_refused_by_length_before_any_read`, `A_large_sparse_file_streams_without_buffering_it_all`, `Cancelling_protect_removes_the_half_written_container`, `The_same_path_twice_gets_two_distinct_entries` |
| `tools.unlock` | empty/corrupt archive, escaping entry | **PASS** | `An_empty_zip_is_refused_and_leaves_no_output`, `A_zip_entry_that_escapes_the_destination_is_refused` |
| `tools.lock` | — (folder lock; covered by `SECURITY-AUDIT.md` §protect/unlock and `LockSafetyTests`) | **out of scope here** | `LockSafetyTests` |
| `archive.extract` / `pack.*` / `archive.to.*` | zip-slip, illegal entry character, locked source, empty archive | **PASS** | `A_zip_entry_that_escapes_the_destination_is_refused`, `An_entry_name_with_an_illegal_character_is_refused_with_a_localized_key`, `A_locked_source_fails_cleanly_and_leaves_no_partial_archive` |
| `tools.annotate` | file-name/format surface only | **reachable since `4f23c9c`; annotate path not exercised** | `Hostile_names_never_throw_in_the_name_helpers` |
| `tools.redact` | degenerate box geometry | **PASS** (geometry; reachable since `4f23c9c`) | `Redaction_boxes_stay_sane_for_degenerate_geometry` |
| conversion pipeline (image/pdf/archive) | every file helper, plus the job-card error funnel | **PASS** | `The_job_funnel_maps_every_hostile_failure_to_a_localized_message`, `The_funnel_never_shows_a_raw_exception_type`, `Every_error_key_the_suite_relies_on_exists_in_both_languages` |

---

## 4. FAIL list

Severity: **High** = user-visible data loss or an unhandled hostile input on a normal path; **Medium** =
wrong or unusable message/behaviour reachable from a hostile input; **Low** = hardening.

### H-01 — Extraction path guard depended on the caller (zip-slip class) — **FIXED**

*owner: TEAM B lane 8 · status: **fixed** in `7fd3d7a` (evidence: run in §5)*

* `src/FileAction.Core/Backends/ArchiveBackend.cs:115` (before the fix: `SafeTarget`, lines 115–121).
* `SafeTarget` resolved `Path.Combine(fullDestination, entryKey)` and compared it with
  `path.StartsWith(fullDestination)`. Without a trailing separator `…\dest-evil\pwn.txt` starts with
  `…\dest`, so an entry such as `..\dest-evil\pwn.txt` passed the guard and would be written **outside**
  the destination. The only current caller (`ExtractTo`, line 74) appends the separator, so the shipped
  path was safe — but the guard's correctness lived in the caller, and `SafeTarget` is public.
  Separately, an entry name the file system cannot represent (`"bad\0name.txt"`) threw a raw
  `ArgumentException` out of the extraction instead of the localized unsafe-path error.
* **Fix** (`7fd3d7a`): the guard appends the separator itself, and `ArgumentException`,
  `NotSupportedException` and `PathTooLongException` now map to `error.archive.unsafePath` (key present in
  both tables).
* **Tests**: `A_zip_entry_that_escapes_the_destination_is_refused`,
  `An_entry_name_with_an_illegal_character_is_refused_with_a_localized_key` (both assert nothing is
  written outside the destination and that the message carries no type name).

### H-02 — Hostile path showed `ArgumentException: …` on the job card — **FIXED**

*owner: TEAM B lane 8 · status: **fixed** in `e3ee779` (evidence: run in §5)*

* `src/FileAction.App/Jobs/JobRunner.cs:221` (the `Friendly` switch, before the fix line 219).
* `ArgumentException` and `NotSupportedException` — exactly what an illegal character, an empty path or an
  unsupported path form produces — fell through to `_ => $"{ex.GetType().Name}: {ex.Message}"`, so the user
  read `ArgumentException: Illegal characters in path.` instead of a localized sentence.
  `UnauthorizedAccessException` (`error.output.notWritable`) and `PathTooLongException` (an `IOException`,
  so `error.file`) were already mapped correctly.
* **Fix** (`e3ee779`): both types now use the localized `error.file` message; unknown exception types keep
  the type name because that is genuine bug evidence for the log.
* **Tests**: `The_funnel_never_shows_a_raw_exception_type`,
  `The_job_funnel_maps_every_hostile_failure_to_a_localized_message`.

### H-03 — Output names are never clamped to the 255-character component limit — **OPEN (Medium)**

*owner: TEAM A (Core) · status: open → `docs/BACKLOG.md`*

* `src/FileAction.Core/OutputPaths.cs:8` (`Unique`), `:24` (`ProduceAsync`).
* `Unique` builds `baseName + " (2)" + "." + ext` with no length check. A 251-character stem plus the
  numbering suffix, or plus a tool suffix (`" With Background"`, `" Edited"`, `" Protected"`), exceeds the
  Windows component limit. `File.Exists` on such a name simply returns `false`, so `Unique` hands back an
  unusable path and the failure surfaces later as a raw `PathTooLongException`/`IOException`. The temp
  name `.{baseName}.{8 hex}.part.{ext}` (line 27) is also longer than the final name, so a long stem fails
  one step earlier still.
* **Suggested fix**: clamp in `Unique` — if the candidate's file name exceeds ~255 characters, refuse with
  a new `error.output.nameTooLong` key (add it to both tables) instead of returning the name.
* **Test today**: `A_255_character_name_is_numbered_and_never_overwrites_the_source` asserts the invariant
  that survives either way (source unchanged, no `.part` leftover). Flip it to expect the localized refusal
  once H-03 is fixed.

### H-04 — Batch failures bypass the error funnel — **OPEN (Low)**

*owner: TEAM A (Core) · status: open → `docs/BACKLOG.md`*

* `src/FileAction.Core/Operations/PerFileOperation.cs:82` — `errors.Add($"{work[i].Name}: {ex.Message}")`.
* When the user drops several hostile files, the per-file message is the raw exception text (a Magick.NET
  message for a corrupt image, an OS message for a locked file). It is never passed through
  `JobRunner.Friendly`, so the batch card mixes languages and shows framework wording even though the
  single-file path is localized.
* **Suggested fix**: let the operation keep the exception and format it in the App layer, or introduce a
  `Core`-level formatter that only emits `ConversionException` messages.

### H-05 — PDFium failures are not wrapped although the key exists — **OPEN (Low)**

*owner: TEAM A (Core) · status: open → `docs/BACKLOG.md`*

* `src/FileAction.Core/Backends/PdfBackend.cs:13` (`PageCount`) and `:22` (`RenderAsync`) call
  `PDFtoImage.Conversion.GetPageCount` directly; the PDFsharp path (`Open`, line 256) wraps its failures as
  `ConversionException(T("error.pdf.cannotOpen", …))`.
* A 0-byte or truncated PDF therefore reaches the card as an unwrapped decoder exception, while the same
  file going through Optimize/Rotate/Merge gets the localized message.
* **Suggested fix**: catch the PDFium exception in `PageCount`/`RenderAsync` and throw
  `ConversionException(T("error.pdf.cannotOpen", name, message))`.

### H-06 — `PackAsync` leaks a partial archive when a source fails — **OPEN (Low, hardening)**

*owner: TEAM A (Core) · status: open → `docs/BACKLOG.md`*

* `src/FileAction.Core/Backends/ArchiveBackend.cs:138`, output created at `:160`.
* `PackAsync` creates the archive before reading any source and never deletes it on failure, so a locked
  or vanishing source leaves a truncated `.zip` at the requested path. Both current callers go through
  `OutputPaths.ProduceAsync`, which deletes its `.part` file, so the product does not ship the leak today.
* **Suggested fix**: `try { … } catch { OutputPaths.TryDelete(output); throw; }` around the body.

### H-07 — `tools.annotate` / `tools.redact` were not reachable from the wheel — **RESOLVED UPSTREAM**

They were missing from `WheelController.BuildOperations()`, so no hostile input could reach them from the
UI. Commit `4f23c9c` on `main` wires both; this branch merged it (`96eaa72`). `Redaction.Build`/
`RedactionBox` geometry is covered by the suite; the annotate path is still not exercised.

---

## 5. CI evidence

| Run | Commit | State | Result |
| --- | --- | --- | --- |
| [39375973532](https://github.com/Faisaldhd/dropwheel/actions/runs/39375973532) | `b26d3e4` | **RED** | `Failed: 2, Passed: 1055, Total: 1057` in 28 s. Both failures were bugs in the **new suite**, not product defects: `Hostile_names_never_throw_in_the_name_helpers("dot only", ".")` asserted that `"."` is not a directory, and `Names_that_differ_only_by_case_produce_two_outputs` assumed NTFS can hold two names that differ only by case (it cannot — they are one file). Every other hostile case passed, including the 5 GiB length refusal, the 64 MiB sparse stream, the escaping zip entry, the corrupt image/PDF/ZIP cases, cancellation and the job-funnel tests. |
| [39377045318](https://github.com/Faisaldhd/dropwheel/actions/runs/39377045318) | `a29ec73` (merged head, includes `4f23c9c`) | **GREEN** | `Failed: 0, Passed: 1058, Skipped: 0, Total: 1058` in 28 s — the whole repository suite, including all 72 hostile-input cases. This is the PASS evidence for every row in §3. |

The suite is part of `dotnet test tests/FileAction.Tests/FileAction.Tests.csproj` (CI step “Test”), so a
green `Build + test` job is the PASS evidence for every row in §3 that names a test.

---

## 6. Not covered / needs a desktop

These were deliberately left to other lanes or to a machine with a desktop session; they are not PASSes.

1. **Anything behind ffmpeg / Pandoc / LibreOffice** — audio tools, video tools, office conversions and
   most `tools.metadata` fields. The binaries are absent from CI and the lane rule forbids running them
   locally; only their format classification and shared naming path are covered here.
2. **Lock window flows** (protect/unlock UI, auto-lock, journal recovery) — static and container-level
   behaviour is in [`SECURITY-AUDIT.md`](SECURITY-AUDIT.md) and `LockSafetyTests`; no UI was driven.
3. **QR decoding** of a hostile image — `QrDecoder.Decode` needs a real decode path; only the file-name
   surface is covered.
4. **Localized rendering of the messages themselves** — the suite proves the key exists in both tables and
   that the funnel returns a localized sentence, not how it looks in an Arabic RTL card (desktop).
5. **The 255-character and >260-character cases on a runner without long-path support** — the test records
   whether the OS accepted the fixture and asserts the API boundary either way; the refusal branch was not
   exercised on this runner.
6. **Signing and update verification** — explicitly out of scope for this lane (rule: do not modify or test
   those paths).
7. **Concurrency** — two drops racing on the same destination is not covered; the suite is serialised by
   design because it redirects `TMP`/`TEMP`.

## 7. Fixture teardown — no-leak proof

Asked by the PM: do the long-path, read-only and embedded-NUL fixtures clean themselves up completely on
CI (no leftover files, no read-only directory blocking the delete)?

| Fixture | How it is created | How it is removed | Evidence |
| --- | --- | --- | --- |
| read-only file | `File.SetAttributes(path, ReadOnly)` | before `Directory.Delete(recursive)` the fixture clears every file to `Normal` and every directory to `Directory` | `Hostile_fixtures_remove_themselves_completely` |
| path beyond MAX_PATH | nested `d`×50 components until the path passes 290 characters | same recursive delete; the test asserts the deepest directory is gone afterwards | same |
| embedded NUL entry name | never reaches the disk — `SafeTarget` refuses it before a path is used | nothing to remove | same |
| locked file | `FileShare.None` inside a `using` that wraps only the assertion | the handle is released before the fixture disposes | `A_locked_source_fails_cleanly_and_leaves_no_partial_archive` |
| 5 GiB / 64 MiB sparse file | `FileStream.SetLength`, no payload written | ordinary delete (no reparse point, no payload) | `A_file_over_the_zip32_limit_is_refused_by_length_before_any_read`, `A_large_sparse_file_streams_without_buffering_it_all` |

Post-conditions asserted **after** `Dispose`: the per-test root no longer exists, the long-path directory no
longer exists, and the scratch parent holds no `hostile-*` directory. A silent leak therefore fails the
test instead of passing quietly. All three post-conditions held on CI in run
[39377045318](https://github.com/Faisaldhd/dropwheel/actions/runs/39377045318) (`Failed: 0, Total: 1058`).

---

*Related: [`SECURITY-AUDIT.md`](SECURITY-AUDIT.md) (crypto, container format, Lock/Protect behaviour).*
