# Lock Round-3 — independent verification on the merged SHA

Second-reader verification of the Lock fixes and the new N-items, run **after** the Lock PR reached `main`,
with the release lane cross-checking. The table is filled from the merged SHA, never from inference.

* **Merged SHA verified: `8b28758`** (`origin/main` at verification time). Line numbers are for that commit.
* **Test evidence: CI run [39382743079](https://github.com/Faisaldhd/dropwheel/actions/runs/39382743079)** —
  artifact `test-results` (`results.trx`): **1109 tests, 1109 passed, 0 failed, 0 timeout**. Every “Passed”
  below is a test name from that file, not a guess.
* **Read-only method:** `git show origin/main:<path>`, `git grep origin/main`, `gh run download`. **No local
  build, exe, installer or test run**; nothing checked out from a foreign branch; no push to another lane.
* **Baseline:** round-1 `SECURITY-AUDIT.md` (`1d4188b`), then `LOCK-FIX-VERIFICATION.md` (branch `55eb4e5`).
* **Follow-up landed between rounds:** PR **#42** (`fix/2.4.0-lock-m1`) merged at `ab924de`.

## 0. History that matters: the merged tree *was* red, inside Lock

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

**Fixed at `8b28758`:** `LockIcons.Compose` renders through `ComposeCore` inside a try/catch and falls back to
the padlock (`LockIcons.cs:47-52`, `:56`; the `PreviewForPath` guard is at `:101`), with the comment
*“Rendering the badge must never take down the Lock window or the app self-test.”* The confirming CI run
reached step 9 **success** and every lock-icon test passed.

## 1. Evidence table

`Source` = what reading `8b28758` shows. `Test evidence` = the passing test in run 39382743079's `results.trx`.

| # | Row / assertion | Source (`path:line` @ `8b28758`) | Source verdict | Test evidence (all Passed) |
| --- | --- | --- | --- | --- |
| **R3-0** | Building the Lock window cannot crash the app self-test | `LockIcons.cs:47-52` (try/catch → `Padlock`), `:56` (`ComposeCore`), `:101` | **PASS** | `LockSafetyTests.Preview_for_a_temp_png_and_a_folder_returns_a_bitmap_without_throwing`; `Padlock_is_the_only_shipped_badge_and_uses_the_purple_tokens`; `Composition_keeps_the_real_shell_icon_and_adds_purple_pixels_bottom_left`; `Icon_cache_reuses_the_extension_cleans_items_and_builds_association_icons` |
| **N-1** | Relock re-reads the container header, refuses if it changed, re-walks and re-validates the entry count | `LockService.cs:264-271` (`HeaderMatchesKey` + `FileId`), `:301-302`, `:664-665`; `LockArchive.TreeHash` | **PASS** | `Relock_encodes_the_current_folder_after_add_remove_and_rename`; `Tree_hash_changes_for_add_remove_and_rename`; `Relock_keeps_a_rescuer_password_changed_during_the_session` |
| **N-2** | Startup sweep offers orphaned session folders and leftover restore temp | `LockSessions.cs:320`, `:381`, `:416`, `:418`; strings `tool.lock.recovery.*` | **PASS** | `Journal_roundtrip_and_recovery_plan`; `Journal_never_contains_a_secret`; `Crash_recovery_reencrypts_the_edited_plaintext_into_the_same_container` |
| **N-3** | Restore staging lives in a dedicated temp root, not the user’s folder | `LockGuard.cs:25-30`, `LockService.cs:108`, `LockSessions.cs:418`; `SECURITY.md` `%TEMP%\DropWheel\restore` | **PASS** | `Unlock_creates_an_unencrypted_zip_and_never_leaves_a_partial_file`; `Read_only_folder_still_roundtrips_without_partial_output` |
| **N-4a** | ZIP stores the **per-entry** uncompressed size | `PasswordZip.cs:129,149,171,174,177` | **PASS** | `PasswordProtectionTests.Zip_multi_entry_round_trip_keeps_each_uncompressed_size` |
| **N-4b** | Folder protect re-walks and compares the entry count before deleting; recovery refuses a changed tree | `LockService.cs:205-209`, `:301-302`, `:664-665`; `LockFormat.cs:207,236,280,322` | **PASS** | `Protect_refuses_when_the_folder_changes_after_the_archive_started`; `Protect_writes_three_slots_that_each_open_the_same_container` |
| **M-1a** | Key-only Rescuer reset reads and honours `_knownKey` | `RescuerWindows.cs:396-399` → `LockSecrets.ChooseResetKey(_knownKey, stored)` (`LockSecrets.cs:14-15`) | **PASS** | `Reset_key_prefers_the_key_from_the_just_opened_container` |
| **M-1b** | A reset during an open session is not silently reverted at relock | `LockService.cs:264-271`, `:296-297`, `:690-699` | **PASS** | `Relock_keeps_a_rescuer_password_changed_during_the_session` |
| **B-1** | Recovery, `--lock-open`, exit relock, settings and self-test are wired | `App.cs:201,218,507`; `SelfTest.cs:93`; `SettingsWindow.cs:338` | **PASS** | `LockWiringTests.App_startup_exit_open_settings_and_self_test_hooks_invoke_the_lock_entry_points` |
| **B-2** | Journal exists before plaintext extraction; cleanup failure keeps the journal and is logged | `LockSessions.cs:96` before `:102`; `:106-121`; `LockJournal.cs` `Extracting` | **PASS** | `Crash_recovery_reencrypts_the_edited_plaintext_into_the_same_container`; `Journal_roundtrip_and_recovery_plan` |
| **B-3** | Every written slot is test-opened after Protect; a mismatch fails the operation | `LockService.cs:211` → `VerifyContainer`, `:776-783` | **PASS** (harm closed; pre-check clause not implemented, accepted) | `Protect_writes_three_slots_that_each_open_the_same_container`; `LockContainerTests.Every_one_of_the_three_secrets_opens_the_file` |
| **B-4** | Counts re-validated before delete and every replace; a partially deleted tree is never re-encrypted | `LockService.cs:205-209,301-302,664-665`; `LockArchive.TreeHash` | **PASS** | `Protect_refuses_when_the_folder_changes_after_the_archive_started`; `Relock_encodes_the_current_folder_after_add_remove_and_rename` |
| **M-4** | Protect’s ZIP/PDF KDF weakness is disclosed | `SECURITY.md:115` covers the **Lock container** only | **DEFERRED** (decision → BACKLOG, unassigned) | n/a |

Bonus row observed while reading the results (round-1 **M-2** is covered by a test):
`Unprotect_refuses_the_key_alone_and_an_open_session` — **Passed**.

## 2. CI evidence for `8b28758`

* Run **39382743079**: `Build + test` **completed success**; step 9 (`Self-contained UI self-test`) ran
  `06:52:33Z → 07:02:01Z` and uploaded `self-contained-self-test` and `ui-coverage`; the unit `Test` step then
  ran and uploaded `test-results`.
* `results.trx`: **total 1109, executed 1109, passed 1109, failed 0, timeout 0**. Lock-related tests: 23
  `LockContainerTests`, 40+ `LockSafetyTests`, 1 `LockWiringTests`, 18 `PasswordProtectionTests` — all Passed.
* Environment note: this is the same runner class the release lane uses, so the evidence transfers to the
  packaged build as far as a headless run can.

## 3. Limits

* Static source reading + the CI unit-test artifact. **No debugger, no local execution, no UI observation.**
  R3-0’s root cause is narrowed to `ShellIcon.Get` / the `RenderTargetBitmap` ctor inside `ComposeCore`; the
  merged fix is a fail-safe, not a proven root-cause repair, and that distinction stays deliberate.
* Line numbers are for `8b28758`; `main` moving invalidates them.
* Crypto/format layers are not re-audited here; they remain in `SECURITY-AUDIT.md`.
