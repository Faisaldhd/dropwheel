# Lock Round-3 — independent verification on the merged SHA

Second-reader verification of the Lock fixes and the new N-items, run **after** the Lock PR reached `main`.
Security lane, with the release lane cross-checking. The table is filled from the merged SHA, never from
inference.

* **Merged SHA verified: `8b28758`** (`origin/main` at verification time). Line numbers are for that commit.
* **Read-only method:** `git show origin/main:<path>`, `git grep origin/main`, and CI logs via `gh`.
  **No local build, exe, installer or test run**; nothing checked out from a foreign branch; no push to
  another lane's branch.
* **Baseline for “fixed”:** round-1 `SECURITY-AUDIT.md` (committed at `1d4188b`), then the branch-level
  verification in `LOCK-FIX-VERIFICATION.md` (branch head `55eb4e5`).
* **Follow-up that landed between rounds:** PR **#42** (`fix/2.4.0-lock-m1`) merged at `ab924de`.

## 0. History that matters: the merged SHA *was* red, inside Lock

At `bc204c4` the shipped `--self-test` died building the Lock window (run
[39380479876](https://github.com/Faisaldhd/dropwheel/actions/runs/39380479876)):

```
FAILED: System.ArgumentException: Value does not fall within the expected range.
   at FileAction.App.Shell.Protection.LockIcons.Compose(String path, Boolean folder, Int32 size)
   at FileAction.App.Shell.Protection.LockIcons.PreviewForPath(String path, Int32 size)
   at FileAction.App.UI.Tools.Lock.LockProtectView..ctor(...)
   at FileAction.App.UI.Tools.Lock.LockTool.Create(...)
self-contained --self-test failed with 1
```

**That blocker is fixed at `8b28758`:** `LockIcons.Compose` now renders through `ComposeCore` inside a
try/catch and falls back to the padlock (`LockIcons.cs:47-52`, `:56`, and the `PreviewForPath` guard at
`:101`). The comment states the rule: *“Rendering the badge must never take down the Lock window or the app
self-test.”* This row (**R3-0**) is **FIXED (source)**; the confirming CI run is cited in §1.

## 1. Evidence table

`Result (source)` = what reading `8b28758` shows. `Test evidence @ 8b28758` = what CI can prove for that
commit. No row is PASS on source alone.

| # | Row / assertion | Source verified (`path:line` @ `8b28758`) | Result (source) | Test that proves it | Test evidence @ `8b28758` |
| --- | --- | --- | --- | --- | --- |
| **R3-0** | Building the Lock window cannot crash the app self-test | `LockIcons.cs:47-52` (try/catch → `Padlock`), `:56` (`ComposeCore`), `:101` | **PASS (source)** | shipped `--self-test` (step 9) + `LockSafetyTests.Lock_icons_are_shipped_multi_size_png_frames_and_use_the_purple_tokens` | run **39382743079** — see §2 |
| **N-1** | Relock re-reads the container header, refuses if it changed, re-validates the entry count before replacing | `LockService.cs:264-271` (`HeaderMatchesKey` + `FileId`), `:301-302`, `:664-665`; `LockArchive.TreeHash` | **PASS (source)** | `LockSafetyTests` relock/recovery cases | §2 |
| **N-2** | Startup sweep offers orphaned session folders and leftover restore temp for deletion | `LockSessions.cs:320`, `:381`, `:416`, `:418`; strings `tool.lock.recovery.{temp,orphan,incomplete}*` | **PASS (source)** | `LockSafetyTests` journal/recovery cases | §2 |
| **N-3** | Restore staging lives in a dedicated temp root, not the user’s folder | `LockGuard.cs:25-30`, `LockService.cs:108`, `LockSessions.cs:418`; `SECURITY.md` `%TEMP%\DropWheel\restore` | **PASS (source)** | `LockSafetyTests` unprotect-staging cases | §2 |
| **N-4a** | ZIP stores the **per-entry** uncompressed size, not the cumulative progress | `PasswordZip.cs:129,149,171,174,177` | **PASS (source)** | `PasswordProtectionTests` multi-entry round-trip | §2 |
| **N-4b** | Folder protect re-walks and compares the entry count before deleting; recovery refuses a changed tree | `LockService.cs:205-209`, `:301-302`, `:664-665`; `LockFormat.cs:207,236,280,322` | **PASS (source)** | `LockSafetyTests` folder-protect/rollback | §2 |
| **M-1a** | Key-only Rescuer reset reads and honours `_knownKey` | `RescuerWindows.cs:396-399` → `LockSecrets.ChooseResetKey(_knownKey, stored)` (`LockSecrets.cs:14-15`) | **PASS (source)** — fixed by #42 (`ab924de`) | needs the reset path test | §2 |
| **M-1b** | A reset during an open session is not silently reverted at relock | `LockService.cs:264-271`, `:296-297`, `:690-699` | **PASS (source)** | `LockSafetyTests` rotation + relock | §2 |
| **B-1** | Crash recovery, `--lock-open`, exit relock, Protection settings and the Lock self-test are wired | `App.cs:201,218,507`; `SelfTest.cs:93`; `SettingsWindow.cs:338` | **PASS (source)** | `LockWiringTests.App_startup_exit_open_settings_and_self_test_hooks_invoke_the_lock_entry_points` | §2 |
| **B-2** | Journal exists before plaintext is extracted; cleanup failure keeps the journal and is logged | `LockSessions.cs:96` before `:102`; `:106-121`; `LockJournal.cs` `Extracting` state | **PASS (source)** | `LockSafetyTests` journal/recovery | §2 |
| **B-3** | Every written slot is test-opened after Protect; a mismatch fails the operation | `LockService.cs:211` → `VerifyContainer`, `:776-783` | **PASS (source, harm closed)**; pre-verification clause not implemented, accepted by decision | `LockSafetyTests` protect-keeps-all-slots | §2 |
| **B-4** | Entry counts re-validated before delete and every replace; a partially deleted tree is never re-encrypted | `LockService.cs:205-209,301-302,664-665`; `LockArchive.TreeHash` | **PASS (source)** | `LockSafetyTests` | §2 |
| **M-4** | Protect’s ZIP/PDF KDF weakness is disclosed | `SECURITY.md:115` documents the **Lock container** only; no Protect KDF text in `en.json` | **DEFERRED** (supervisor decision → BACKLOG, owner unassigned) | n/a | n/a |

## 2. CI evidence for `8b28758`

Run **39382743079** (push, `8b28758`) — **still in progress when this document was finalised** (step 9,
“Self-contained UI self-test (shipped publish)”, reached and running). The unit `Test` step has therefore not
produced a result yet, so **every “test evidence” cell above stays `cannot verify`**: the source rows are
verified by reading `8b28758`, and none of them is claimed as *proven by tests*.

Fill rule for the follow-up: when this run reaches step 12 green, replace each cell with the run id and the
`Passed! … Total: N` line; if it is red, quote the failing step instead and the cells stay `cannot verify`.
Note that `SelfTest.cs:93` now also runs `SelfTestUiCoverage.Run`, so step 9 carries both the Lock self-test
and the GAP-2 coverage pass — a red step 9 no longer isolates which of the two failed.

## 3. Limits

* Static + CI-log evidence only. No debugger, no local execution, no UI observation.
* Line numbers are for `8b28758`; `main` moving invalidates them.
* R3-0’s root cause is narrowed to `ShellIcon.Get` / the `RenderTargetBitmap` ctor inside `ComposeCore`; the
  fix is a fail-safe, not a proven root-cause repair — that distinction is deliberate.
* Crypto/format layers are not re-audited here; they remain in `SECURITY-AUDIT.md`.
