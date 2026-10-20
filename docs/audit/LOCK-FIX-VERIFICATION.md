# Lock fix verification — is the round-1 audit closed on `feat/2.4.0-lock`?

Independent re-check of the Lock security audit (`SECURITY-AUDIT.md`, the committed 301-line round-1 part)
against the candidate fix branch. Requested because an unvouched draft in this worktree claimed that `main`
contains none of the Lock fixes and that they live only on that branch. **This file is the verification that
claim needs before the flagship feature ships.**

* **Branch examined:** `origin/feat/2.4.0-lock`, head **`55eb4e5`** (read-only: fetched, never checked out,
  never edited, never pushed).
* **Baseline:** `origin/main` @ `4f23c9c`.
* **Method:** `git show <ref>:<path>`, `git grep <ref>`, `git log -S` on the branch. No build, no test run, no
  executable, no installer, no network beyond `git fetch`. Nothing outside this worktree was touched.
* **Round-1 source of truth:** `git show 1d4188b:docs/audit/SECURITY-AUDIT.md` — the committed file, not the
  uncommitted round-2 block sitting in this worktree.

## Supervisor decisions (2026-10-20)

Recorded by the TEAM B supervisor after reading §1–§5. **Verified branch head: `55eb4e5`**; the unvouched
draft's `5bffa9d` is **stale** (two later commits: `53e9a14`, then a test fix).

1. **B-3 — ACCEPTED for 2.4.0.** Fail-closed slot verification (every written slot test-opened after Protect,
   a mismatch fails the operation, plus the key-only prompt) closes the harm. The missing clause — pre-verifying
   the typed Rescuer password against an existing container — is a UX safeguard, not a security hole.
   `decision: accepted (supervisor, 2.4.0) → BACKLOG` (schedule the pre-check).
2. **M-4 — DEFERRED, not fixed.** No KDF disclosure for Protect's ZIP/PDF ships in 2.4.0: a disclosure change
   to `en.json`/`SECURITY.md` at T-2h is not worth the risk. `decision: deferred → BACKLOG, owner: unassigned`;
   the exact finding is kept above (`SECURITY.md:115` documents the Lock container only).
3. **Minors — all 13 stay open.** `decision: accepted as scheduled work → BACKLOG`, order **m-7 first, then
   m-12**, no code changes now.

## 0. Premise correction (important for whoever acts on this)

The unvouched draft examined **`5bffa9d`**, which is **not** the branch head. Two commits landed after it:
`53e9a14` (*“N-1 re-walk relock, N-2 journal sweep, N-3 volume staging, M-1 reset, M-3 per-entry size”*) and
`55eb4e5` (a test fix). Any “still absent” conclusion the draft reached for **M-3 (per-entry ZIP size)** or
for the **startup sweep** is stale: both are present at `55eb4e5` (evidence below). The draft's premise that
`main` has none of the fixes is correct — `git diff 268a2d7 4f23c9c -- src/FileAction.Core/Lock
src/FileAction.App/Shell/Protection SECURITY.md` is empty — but its branch state is one iteration behind.

Commits on the branch above `main`, in order: `a06528a` (icons, not an audit fix) · `a06dbd2` (blockers
B-1..B-4, M-1/M-2/M-3/M-5) · `5bffa9d` (synchronous exit relock) · `53e9a14` (N-1..N-3, M-1 reset, M-3
per-entry size) · `55eb4e5` (test).

---

## 1. Blockers

| # | Round-1 finding (short) | State on `55eb4e5` | Evidence | Added by |
| --- | --- | --- | --- | --- |
| **B-1** | Crash recovery, `--lock-open`, Protection settings and the Lock self-test never called | **FIXED-PRESENT** | `LockWiring.cs:11-29` (new facade); `App.cs:201` `LockWiring.Startup()`, `:218` `LockWiring.HandleArgs(args)`, `:507` `LockWiring.Exit()`; `SelfTest.cs:75` includes `LockWiring.RunSelfTest`; `SettingsWindow.cs:338` Protection button → `LockWiring.Settings` | `a06dbd2` (wiring), `5bffa9d` (sync exit relock) |
| **B-2** | Plaintext extracted before journaling; no sweep; `.dwrestore-*` written into the user folder; delete failures swallowed | **FIXED-PRESENT** | `LockSessions.cs:96` `_journal.Create(provisional)` **before** `:102` `ExtractTo`; new `Extracting` state `LockJournal.cs:36`; on failure the journal is kept when cleanup failed and the error is logged `LockSessions.cs:106-121`; startup sweep `LockSessions.cs:320,381,416`; restore staging moved out of the user folder — `LockGuard.cs` `RestoreTempFolder`, `SECURITY.md:131` `%TEMP%\DropWheel\restore`; user-facing sweep prompts `en.json` `tool.lock.recovery.{temp,orphan,incomplete}{Title,Body}` | `a06dbd2`, sweep in `53e9a14` |
| **B-3** | Rescuer prompt accepts any 8-char password / any well-formed key; a typo silently voids the “one Rescuer password” model | **PARTIAL — harm closed, one clause not implemented** | Every written slot is now test-opened after Protect and a mismatch fails the operation: `LockService.cs:211` calls `VerifyContainer`, which loops the file password, Rescuer password and key (`:776-783`) and throws `LockDamagedException("a written slot does not open the new container")`. Key-only reset prompt added (`RescuerKeyWindow`, `RescuerWindows.cs:611+`, `LockRuntime.cs:121-133`). **Not implemented:** pre-verifying the typed Rescuer password against an *existing* container before protecting a new file — the round-1 fix text asked for it explicitly | `a06dbd2` |
| **B-4** | (a) folder Protect deletes the tree with no re-check; `EntryCount` stored but never validated. (b) Retry re-encrypts a partially deleted tree over a good container | **FIXED-PRESENT** | (a) re-count before delete, fail closed: `LockService.cs:205-209` (`LockChangedException` “N entries were archived but the folder now has M”); `EntryCount` carried through `LockFormat.cs:207,236,280,322` and `LockJournal.cs:36,72`. (b) relock re-counts before replacing: `LockService.cs:301-302`; crash recovery re-counts before replacing the container: `:664-665`; `TreeHash` recorded at session open (`LockSessions.cs:130`, `LockArchive.TreeHash`) so an edited tree is visible to a retry | `a06dbd2` (counts), `53e9a14` (TreeHash, N-1 re-walk) |

---

## 2. Majors

| # | Round-1 finding (short) | State | Evidence | Added by |
| --- | --- | --- | --- | --- |
| **M-1** | Key-only reset broken (`_knownKey` never read, `Vault` empty); a reset during an open session is silently reverted at relock | **FIXED-PRESENT** | `_knownKey` is now read and compared: `RescuerWindows.cs:366`; the key-only path stores the key (`LockRuntime.cs:121-133`); relock **re-reads the container header** and refuses if it changed: `LockService.cs:264-271` (`"the container changed while it is open"`), then re-encrypts with the *current* header (`EncryptSource(..., existingHeader)` `:296-297`, `:690-699`) so a rotation is preserved | `a06dbd2` (header chain), `53e9a14` (reset) |
| **M-2** | Removal accepts the Rescuer key alone and has no open-session guard | **FIXED-PRESENT** | `LockService.cs:341-342` `if (_host.IsSessionOpen(full)) throw new LockPolicyException("Lock this file first…")`; `:345-347` refuses `RescuerKey` with an explanatory message; `ProtectionHost.cs:45` implements `IsSessionOpen` via `Journal.FindByContainer` | `a06dbd2` |
| **M-3** | ZIP `uncompressedBytes` got the cumulative progress counter → wrong per-entry sizes | **FIXED-PRESENT** | `PasswordZip.cs`: `entryBytes += read` and `AddProgress(read)` split; local header and central directory now use `entryBytes`; `-S entryBytes` → `53e9a14` | `53e9a14` |
| **M-4** | Format-mandated weak KDFs (ZIP PBKDF2-SHA1 1000; PDF AES-256 V5) undisclosed | **STILL ABSENT** | No disclosure string was added: the `en.json` delta contains no KDF/iteration/format-limit text (`tool.protect.warn.title` is the encryption warning, not the KDF disclosure). `SECURITY.md:115` documents the **Lock container** crypto (PBKDF2-SHA256 ≥600 000), not Protect's ZIP/PDF KDFs | — |
| **M-5** | `SECURITY.md` omits the whole Lock feature and what it writes | **FIXED-PRESENT** | `SECURITY.md:109` new `## Lock (2.4.0)` section: `.dwenc`/`.dwdir`, HKCU-only associations + `--lock-open`, `protection.json`, icons cache, `sessions` folder (journal + plaintext until a verified relock), `%TEMP%\DropWheel\restore` staging, auto-lock and failed-relock behaviour (`:109-135`) | `a06dbd2` |

---

## 3. Minors (13) — all remain open

None of these is a release blocker (round 1 scheduled them). They are confirmed unchanged by a structural
argument plus direct checks: if the file that contains the defect is absent from the branch diff, its code is
byte-identical to `main`, so the defect cannot have been fixed. Branch diff (files touched): `App.cs`,
`SelfTest.cs`, `LockCli.cs`, `LockIcons/LockIconCache/ShellIcon`, `LockRuntime.cs`, `LockSessions.cs`,
`LockSettingsWindow.cs`, `LockWiring.cs`, `ProtectionAssociations.cs`, `ProtectionHost.cs`,
`RescuerWindows.cs`, `SettingsWindow.cs`, `LockTool.cs`, `WheelController.cs`, `LockArchive.cs`,
`LockCrypto.cs`, `LockFormat.cs`, `LockGuard.cs`, `LockJournal.cs`, `LockService.cs`, `PasswordZip.cs`,
`en/ar.json`, `SECURITY.md`, tests.

| # | Round-1 claim (short) | State | Why |
| --- | --- | --- | --- |
| m-1 | Random per-frame nonces instead of a counter | **STILL ABSENT** | `LockCrypto.cs` delta is one record type (`LockProtectResult`); nonce code untouched |
| m-2 | Protect's password mirror shows the masked value; `_form` survives submit | **STILL ABSENT** | `PasswordViewParts.cs` / `LockUi.cs` not in the branch diff |
| m-3 | Header `Flags` bound only indirectly by AAD | **STILL ABSENT** | `LockCrypto.cs` untouched apart from the record |
| m-4 | Read-side allows 10 000 000 PBKDF2 iterations per slot | **STILL ABSENT** | iteration caps live in `LockCrypto.cs` — unchanged |
| m-5 | Journal not fsynced before rename; `Syncing` outside the try leaves `Busy` true | **STILL ABSENT (fsync part)** | `LockJournal.cs` gained the `Extracting` state + `EntryCount`/`TreeHash`; no flush-before-rename was added |
| m-6 | Archive extraction accepts device names / trailing dots, applies attributes, follows a pre-existing junction | **STILL ABSENT** | `LockArchive.cs` gained only `TreeHash`; `LockPaths.cs` not touched |
| m-7 | A planted journal can name an arbitrary container path | **STILL ABSENT (verified directly)** | `LockService.cs:644-647` still validates only that the *plaintext* is inside `SessionsFolder`; the container path from the journal is used as-is |
| m-8 | PDF unprotect fallback silently drops outlines/form fields; saves straight to the output path | **STILL ABSENT** | `PasswordPdf.cs` not in the branch diff |
| m-9 | Wrong-secret delay is UI-only and bypassed by restart | **STILL ABSENT** | wording/behaviour unchanged (`LockSecrets.cs` untouched) |
| m-10 | Open plaintext has the default profile ACL, no hidden/restricted marking | **STILL ABSENT** | no ACL code added in `LockSessions.cs`/`LockJournal.cs` |
| m-11 | Recycle failure falls back to a permanent delete | **STILL ABSENT** | `ProtectionHost.cs` gained only `IsSessionOpen` |
| m-12 | Slot rewrite copies the whole payload to `*.tmp.hdr`; no sweep; rollback window can leave an openable pre-rotation header | **STILL ABSENT** | `LockHeaderRewriter.cs` not in the branch diff |
| m-13 | `FrameSink._buffer` / `metaData` plaintext not cleared | **STILL ABSENT** | `LockCrypto.cs` untouched apart from the record |

---

## 4. Work the branch adds that round 1 did not ask for

Not required by the audit, but relevant to “is Lock safe to ship”:

* **`Extracting` journal state** (`LockJournal.cs:36`) — the journal exists before extraction is trusted, so a
  kill mid-unlock is recoverable and distinguishable from a complete one (supports B-2).
* **Startup sweep with user consent** — orphaned session folders and leftover restore temp files are offered
  for deletion (`LockSessions.cs:320,381,416`, strings `tool.lock.recovery.*`).
* **Restore staging moved out of the user's folder** to `%TEMP%\DropWheel\restore` (`LockGuard.cs`
  `RestoreTempFolder`, `SECURITY.md:131`), which closes the “plaintext `.dwrestore-*` next to the user's files”
  half of B-2.
* **Header-identity check on relock** (`LockService.cs:267-271`) — a robust guard against the M-1(b) revert race.
* **`LockWiring` facade + `LockWiringTests.cs`** — the wiring B-1 asked for is now testable without a window.
* **`SECURITY.md` Lock chapter** (M-5) and the slot test-open after every Protect (B-3).

---

## 5. Bottom line for PR #27

**If #27 merges, the four round-1 blockers are closed in effect, with one letter-of-the-finding gap:**

* **B-1 — closed.** All five dead entry points are wired and covered by `LockWiringTests.cs`.
* **B-2 — closed.** Journal precedes plaintext, the sweep exists, staging left the user folder, cleanup failure
  is logged and journaled instead of swallowed.
* **B-3 — closed in effect, PARTIAL in letter.** A mis-typed Rescuer secret can no longer *silently* void the
  model, because the container is test-opened with every written slot and the Protect fails closed. The
  explicit “verify the typed Rescuer password against an existing container first” step is **not** there.
* **B-4 — closed.** Entry counts are re-validated before every delete and every replace, and a tree hash is
  journaled.

**Majors:** M-1, M-2, M-3, M-5 closed. **M-4 remains open** — Protect's ZIP/PDF KDF weakness is still
undisclosed in the UI and docs; the round-1 fix (disclose + raise the Protect minimum length) was not taken.
**All 13 minors remain open**; none is a release blocker, m-7 and m-12 are the two I would schedule first
(planted-journal container path; whole-payload header rewrite with no sweep).

**So:** merging #27 does **not** make `main` fully clean against round 1 — it makes it clean of every blocker
and three of five majors. Two items need a decision from the owner rather than more code: **M-4** (disclose or
accept) and **B-3's pre-verification clause** (accept the fail-closed slot check as sufficient, or schedule the
pre-check).

## 6. Limits of this verification

* Read-only, static. I did not build or run the branch, so every “FIXED-PRESENT” means *the fix is in the
  source at the cited line*, not *the scenario was executed*. The branch's own tests
  (`LockSafetyTests.cs` +363 lines, `LockWiringTests.cs`, `PasswordProtectionTests.cs` +25) are the runtime
  evidence, and they are CI-only.
* I did not audit anything the branch newly introduced beyond checking that it does not reopen a round-1
  finding; a fresh review of `LockWiring`, `LockIconCache`/`ShellIcon` and `LockSettingsWindow` (+40) is still
  worthwhile.
* `main` moving independently of this branch would invalidate the line numbers; they are for `55eb4e5`.
