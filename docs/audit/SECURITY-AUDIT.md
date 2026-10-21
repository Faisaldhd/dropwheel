# Security audit — Lock (`.dwenc` / `.dwdir`) and Protect (PDF / ZIP)

| | |
|---|---|
| Worktree | `.worktrees/asec` (branch `audit/asec`, from `main` @ `268a2d7`) |
| Auditor role | Independent security review (read-only on source) |
| Scope | `src/FileAction.Core/Lock/*` (`LockCrypto`, `LockFormat`, `LockArchive`, `LockJournal`, `LockService`, `LockGuard`, `LockPaths`, `LockHeaderRewriter`, `LockSecrets`, `RescuerKey`, `FolderAppearance`), its App wiring (`Shell/Protection/*`, `UI/Tools/Lock/*`), `src/FileAction.Core/PasswordPdf.cs`, `PasswordZip.cs`, `PasswordProtection.cs`, `UI/Tools/Password/*`, tests `LockContainerTests.cs`, `LockSafetyTests.cs`, `PasswordProtectionTests.cs`. |
| Method | Full source read + call-site tracing + test-source read. **No code was compiled or executed, no built exe was run, no network was used, no source was modified.** |
| Evidence convention | `path:line` = current source. `test:Name` = a test in `tests/FileAction.Tests` that asserts the behaviour (test *source* was read, not run). |

> **Status update (2.4.9).** This audit was taken against `main` @ `268a2d7`. The 2.4.0 statement that no
> Rescuer secret is stored at rest is **superseded**: 2.4.9 keeps the Rescuer key, password and `KeyUsed`
> flag in a current-user DPAPI vault. See §9 for the current storage, threat model and tests. The rest of
> this document remains the audit of the older commit.

## 1. Verdict

The **cryptographic container itself is well built** (per-file 256-bit data keys, AES-256-GCM with
authenticated header + frames, whole-file SHA-256/length footer, 600k PBKDF2-SHA256 password slots,
buffer wiping on the hot paths). I found no practical way to get plaintext out of a container without a
secret, and tamper/truncation fails closed at the container level.

The **feature around it is not release-ready**, for reasons that are almost entirely outside the crypto:
the crash-recovery / file-association / settings entry points are never called by the app, decrypted
staging files can be left behind with no sweep, the Rescuer password/key are used without any verification,
and the "delete the plaintext after encrypting" step has data-loss races. Per the audit rule (any
data-loss or secret-leak is a release blocker), **B-1 … B-4 block release of Lock**.

Protect (PDF/ZIP) has no data-loss path (sources are only read) and no secret leak in the code I read, but it
ships a ZIP-writing defect (M-3) and two format-mandated weak KDFs (M-4) that must be disclosed at minimum.

| Severity | Count | Meaning here |
|---|---|---|
| **Blocker** | 4 | Must fix before release; includes all data-loss / secret-leak findings |
| **Major** | 5 | Should fix before release; security-relevant but not an immediate leak/loss |
| **Minor** | 13 | Hardening / defence-in-depth / documentation; schedule |

## 2. Findings (required table)

| # | Area | Finding | Severity | Evidence | Fix |
|---|---|---|---|---|---|
| **B-1** | App wiring | Crash recovery, `--lock-open` (double-click), Protection settings and the Lock self-test are **never called**; exiting the app with an unlocked file strands the decrypted file and a stale container | **Blocker** | `Shell/Protection/LockCli.cs:152,176` (no call sites in `src/`); `App.cs:214-221`; `App.cs:495-505`; `LockSettingsWindow.cs:131,133`; `LockSessions.cs:94-97` | Call `LockCli.TryHandle` from `HandleArgs`, `RunStartupRecovery` after startup, `LockAllAsync` in `OnExit`; wire settings + self-test; add a wiring test |
| **B-2** | Plaintext at rest | Unlock extracts plaintext **before** journaling; kill/disk-full leaves full plaintext in `%LOCALAPPDATA%\DropWheel\sessions` with no journal and no sweep. Unprotect also writes plaintext `.dwrestore-*.archive` / `.dir` / `.tmp` **into the user's folder**, removed only on the exception path, and cleanup failures are swallowed | **Blocker** | `LockSessions.cs:90-97`; `LockService.cs:410,445,446,430-434,477-482`; `LockService.cs:876-898`; nothing enumerates `sessions/` or `*.dwrestore-*` at startup (`ProtectionPaths` refs: `LockSessions.cs:90`, `LockService.cs:615`, `LockJournal.cs:271`) | Journal *before* extraction; add a real pending-restore marker + startup sweep of `*.dwrestore-*` and `sessions/*`; never swallow delete failure silently |
| **B-3** | Recovery secrets | Rescuer session prompt accepts **any** 8-char password and any well-formed key without confirmation or verification; new files are wrapped with the typo, silently voiding the advertised "one Rescuer password opens every file" | **Blocker** | `RescuerWindows.cs:508-609`; `LockRuntime.cs:75-99,93-98`; `LockTool.cs:356-365`; `LockCrypto.cs:80-86`; `LockService.cs:738-743` (only file-password slot verified) | Add confirm fields; verify against an existing container when one exists; extend `VerifyContainer` to test-open slot 1 and slot 2 after every Protect and fail the operation on mismatch |
| **B-4** | Data loss | (a) Folder Protect deletes the source tree after a snapshot with no re-check, and `EntryCount` is stored but never validated → concurrent new/edited files are deleted unencrypted. (b) After a partially failed `Directory.Delete`, the Retry button re-encrypts the **partial** tree over the good container | **Blocker** | `LockService.cs:802-812`; `LockFormat.cs:204,233,319` (no reader); `LockService.cs:282-299`; `LockSessions.cs:125-153`; `UnlockedPanel.cs:84`; test gap: `LockSafetyTests.cs:164,1061` only cover in-process failures | Rename source to a quarantine name before delete; re-enumerate and compare count/mtime before deleting; never re-encrypt a partial tree (mark session salvage-only) |
| **M-1** | Recovery flows | Rescuer reset is unreliable: the key-only reset is broken (`_knownKey` written, never read; `Vault` is empty on the key-only path) and a reset done while a session is open is **silently reverted** at relock because the session's old header is written back | **Major** | `RescuerWindows.cs:61,66,395-404`; `LockTool.cs:668-691`; `LockService.cs:253-257,282-283`; `LockCrypto.cs:415` | Use the supplied key in `ResetPassword`; re-read and compare the container header at relock, use the current header (same data key) or refuse |
| **M-2** | Policy / races | Removal of protection accepts the **Rescuer key alone** and has **no open-session guard**: decrypts the stale container and deletes it while the live session still holds unsaved edits (which then cannot relock) | **Major** | `LockService.cs:358-382` (any slot unwraps); `LockTool.cs:693-715`; `LockCli.cs:121-142`; contrast `LockService.cs:496-501` and `LockTool.cs:842-847` | Refuse `RescuerKey` for removal (as `SetFilePassword` does) or require a second confirmation; block removal when `Journal.FindByContainer` is non-null |
| **M-3** | Protect ZIP | `uncompressedBytes` receives the **cumulative** progress counter, so every entry after the first stores the wrong uncompressed size in the local header and central directory | **Major** | `PasswordZip.cs:149,243-253,173,176`; tests only use single-entry archives (`PasswordProtectionTests.cs:136-182`) | `uncompressedBytes += read;` and keep `AddProgress(read)` for progress only; add a multi-entry round-trip test asserting sizes |
| **M-4** | Protect KDFs | ZIP uses WinZip-AES PBKDF2-HMAC-**SHA1, 1000** iterations; PDF uses PDF-2.0 AES-256 V5. Both are format-mandated but very weak against offline guessing, and the UI only claims "AES-256" | **Major** | `PasswordProtection.cs:132-148,199-210`; `PasswordPdf.cs:45-57`; `Strings/en.json:168` | Disclose in the Protect window and docs; raise Protect minimum length (12+); point high-value files at the Lock container |
| **M-5** | Docs / claims | `SECURITY.md` lists everything the app writes but **omits the whole Lock feature**: `.dwenc`/`.dwdir`, the plaintext session folder, `.dwrestore-*` staging, `protection.json`, icons and the HKCU associations | **Major** | `SECURITY.md:23-35,109-119` vs `LockJournal.cs:257-274`, `LockService.cs:410,445,446`, `ProtectionAssociations.cs:71-100` | Add a Lock/Protect section: formats, key model, what is written where (including plaintext during unlock/relock), association keys, recovery model |
| m-1 | Crypto | Frame/slot nonces are 96-bit random per operation rather than a counter; safe because each file has its own key and frames ≪ 2³², but not structural | Minor | `LockCrypto.cs:215,620,617-634`; no nonce test | Derive `nonce = headerPrefix ‖ frameIndex`; keep the random per-file prefix in the header |
| m-2 | Secrets in memory/UI | Passwords are `string`s; Protect's `PasswordField` mirrors the masked value into a visible `TextBox` on every keystroke (Lock's `SecretField` only mirrors while revealed); `_form.Password` survives `ApplyAsync` until the window unloads | Minor | `PasswordViewParts.cs:57-74,142,202-215`; `LockUi.cs:63-68`; `ProtectTool.cs:331-333,466-474` | Only populate the plain mirror while revealed (gate on `_showing`); clear `_form` after submit; consider `SecurePassword` + byte marshalling |
| m-3 | Container | Header `Flags` come from the unauthenticated prefix and are bound only indirectly via AAD; a bit flip reports "wrong password" instead of "damaged" | Minor | `LockCrypto.cs:127`; `LockFormat.cs:164`; `LockCrypto.cs:241-247,677-684` | Include `Flags` in the header-MAC input (or MAC the prefix) |
| m-4 | DoS | Read-side allows 10,000,000 PBKDF2 iterations per slot (3 slots) → a hostile container can stall an unlock for ~tens of seconds; no total-work budget | Minor | `LockCrypto.cs:62,187`; `MaxMetaFrame` 4 MiB `LockFormat.cs:364` | Cap read-side iterations near 2× default, or confirm unusually high counts |
| m-5 | Journal | Journal writes are not fsynced before the rename; the `Syncing` write sits **outside** the try, so a failure leaves `session.Busy = true` forever and escapes as an unobserved task exception | Minor | `LockJournal.cs:51-71`; `LockSessions.cs:131-153` | Flush temp to disk before `File.Move`; wrap the journal write and reset `Busy`; treat journal failure as blocking |
| m-6 | Archive extraction | Paths are traversal-safe, but device names (`CON`, `NUL`, `COM1`), trailing dots/spaces are accepted; archive attributes (incl. `ReparsePoint`) are applied verbatim; `IsInside` is lexical so a **pre-existing junction** inside the destination is followed | Minor | `LockPaths.cs:98-132`; `LockArchive.cs:155,163,186,192`; test `LockSafetyTests.cs:801` only covers `../` | Reject reserved names/trailing dots; mask attributes; resolve the real path or extract under a freshly created private folder |
| m-7 | Journal trust | `RecoverSession` validates only that the *plaintext* is in the sessions folder; the **container** path is taken from the journal and later replaced/moved, so a planted journal can overwrite another user-writable file | Minor | `LockService.cs:613-615,634-639` | Require `.dwenc`/`.dwdir` + `LockGuard` on the container path; ignore otherwise |
| m-8 | PDF | Unprotect's fallback re-imports pages and **silently drops outlines and form fields**; both saves write directly to the output path | Minor | `PasswordPdf.cs:97-140` (comment admits the loss) | Return/warn when the fallback is used; save to a temp and move |
| m-9 | Rate limit | The wrong-secret delay is applied only in the two UI windows; restarting the app bypasses it. It is a UX bump, not a control | Minor | `LockSecrets.cs:39-45`; `LockCli.cs:109-111`; `LockTool.cs:655-658` | Do not describe it as rate limiting; keep 600k PBKDF2 as the real control |
| m-10 | Plaintext location | While open, plaintext sits in `%LOCALAPPDATA%\DropWheel\sessions\<guid>` with the default profile ACL (user + Administrators + SYSTEM) and no hidden/restricted marking | Minor | `ProtectionPaths.SessionsFolder` `LockJournal.cs:271`; `LockSessions.cs:89-91` | Apply a user-only DACL and Hidden/System; document it as temporary plaintext |
| m-11 | Recycle | With Recycle ON, a recycle failure falls back to **permanent** `File.Delete` | Minor | `ProtectionHost.cs:25-41` | If recycle was requested and fails, keep the container and report instead of deleting |
| m-12 | Header rewrite | A password/slot change copies the **entire payload** to `*.tmp.hdr` (84-byte change, full-size copy, needs 2× disk). `.tmp.hdr`/`.tmp.rollback` are never swept; a crash in the rollback window can leave a complete container openable with the **pre-rotation** header | Minor | `LockHeaderRewriter.cs:75-155,157-189`; no sweep | Rewrite the fixed-size header in place (offset 0) with a fsync; at minimum sweep `.tmp.hdr`/`.tmp.rollback` |
| m-13 | Buffer hygiene | `FrameSink._buffer` (1 MiB plaintext) is not cleared if `writeContent` throws; `metaData` plaintext is never cleared after `LockMeta.Deserialize` | Minor | `LockCrypto.cs:434-489,529-530` | Add `IDisposable`/`finally` that clears `_buffer`; clear `metaData` after parsing |

## 3. Blockers in detail

### B-1 — The feature's entry points are dead code (secret-leak + data-loss consequence)

**What the code says.** `LockCli.TryHandle` (`LockCli.cs:152`) is the only caller of `LockOpenWindow`
(the password prompt for a double-clicked container). `LockCli.RunStartupRecovery` (`LockCli.cs:176`) is
the only caller of `LockSessionManager.RecoverOnStartup` (`LockSessions.cs:271`). Neither method has a
call site anywhere in `src/` (`.cs` or `.xaml`). `App.HandleArgs` understands only `--settings` and
`--wheel` (`App.cs:214-221`); the association registered by
`ProtectionAssociations.Register` invokes `"<exe>" --lock-open "%1"` (`ProtectionAssociations.cs:97-98`).
`App.OnExit` disposes hotkey/input/updater/tray and saves settings, but never calls
`LockRuntime.Sessions.LockAllAsync` (`App.cs:495-505`). `LockSettingsWindow.ShowSingle` (the only path to
Rescuer rotation, bulk update, the Recycle toggle and association removal) has no caller
(`LockSettingsWindow.cs:131-136`). `LockSelfTest.Run` is not called from `SelfTest.Run`.

**Why it matters.**

1. Double-clicking a protected file starts the app, `--lock-open` is ignored, and the user gets no prompt
   (the app just starts in the tray). The headline UX is broken.
2. `RecoverOnStartup` never runs, so a journal entry is never read. Combined with `OnExit` not relocking,
   the following is a *normal*, supported sequence: open a file with the Lock tool → edit it → quit
   DropWheel. The edited plaintext stays in `%LOCALAPPDATA%\DropWheel\sessions\<guid>\<name>`, the
   container keeps the old version, and **nothing will ever surface it again**. That is both a secret
   leak (plaintext left on disk by a feature that promises none) and data loss (edits not in the
   container).
3. The same applies to the crash case the journal was written for: the journal record is created at
   `LockSessions.cs:97`, but the only consumer is dead.

**Not caught by tests** because the tests call the *service/manager* directly
(`LockSafetyTests.cs:564` calls `service.RecoverSession(...)`, not the startup path), and no test asserts
that the app handles `--lock-open` or calls `LockAllAsync`.

**Fix.** In `App`:
`HandleArgs` → `if (LockCli.TryHandle(args)) return;` before the `--wheel` branch (and also for forwarded
args, which use the same handler via `SingleInstance.Listen`). After the tray is created →
`LockCli.RunStartupRecovery()`. `OnExit` → best-effort `LockRuntime.Sessions.LockAllAsync()` with a
bounded wait (e.g. 5 s) before disposing. Add Settings → Protection. Add `LockSelfTest.Run` to
`--self-test`. Add tests: (a) an args test that `--lock-open <path>` routes to the unlock window, (b) a
lifecycle test that `OnExit` invokes `LockAllAsync`, (c) a startup test that a seeded journal record
produces a recovery window.

### B-2 — Decrypted plaintext can be left on disk with no recovery and no cleanup

Two independent mechanisms:

**(a) Extraction is not journaled.** `LockSessionManager.OpenAsync` creates the session folder, calls
`_service.ExtractTo(...)` (`LockSessions.cs:94`), and only then writes the journal (`LockSessions.cs:97`).
If the process is killed during extraction (large files take seconds to minutes), or if
`_journal.Create` throws (disk full, ACL, `UnauthorizedAccessException`), the full plaintext remains in
`%LOCALAPPDATA%\DropWheel\sessions\<guid>\` with **no journal record** and no code path that ever looks at
that folder. `ProtectionPaths.SessionsFolder` is referenced only by the journal, `OpenAsync` and
`RecoverSession`; nothing enumerates it at startup.

**(b) Unprotect stages plaintext in the user's own folder.** For a file,
`ExtractFile` writes `.dwrestore-<guid>.tmp` next to the container (`LockService.cs:410`) and only deletes
it in the `catch` (`:430-434`). For a folder, `ExtractFolder` writes the **entire plaintext archive** to
`.dwrestore-<guid>.archive` (`:445`) and the extracted tree to `.dwrestore-<guid>.dir` (`:446`), deleting
them only on the exception path (`:477-482`). `DeleteStale` and `TryDeleteDirectory` swallow
`IOException`/`UnauthorizedAccessException` (`:876-898`) — so even the exception path can silently leave
plaintext behind (e.g. an AV scanner holding the staging file). Kill / power loss / FailFast skip the catch
entirely. Nothing sweeps these names at startup.

Note the tests are green: `Corrupt_input_leaves_no_output_files`
(`LockSafetyTests.cs:1061`) proves the *in-process exception* path removes `.dwrestore-*`, and
`Cancel_mid_run_leaves_the_source_and_no_output` proves the *Protect* path. Neither test can cover a
process kill, and no test asserts a startup sweep.

**Fix.** (1) Write the journal record with a `Pending`/`Extracting` state **before** `ExtractTo`, update it
after, and delete it only after the plaintext is removed. (2) Add a startup sweep: for every
`sessions/*` record and every orphan directory, either offer salvage/recovery or delete-with-confirmation;
sweep `*.dwrestore-*` in folders the app recorded as restore destinations. (3) Replace the silent
`catch` in `DeleteStale`/`TryDeleteDirectory` with a warning + retry when the path matches
`*.dwrestore-*` (plaintext) — silent is only acceptable for the encrypted temp. (4) For Unprotect, extract
into the sessions folder and do one atomic move to the destination, so there is exactly one sweep
location.

### B-3 — The Rescuer ladder can be silently voided by a typo

The product states, verbatim: *"The Rescuer password: one password that opens all your protected
files"* and *"If you lose the file password, the Rescuer password and the key all at once, the file can
never be recovered"* (`Strings/en.json:1084,1090`).

`RescuerSessionWindow` is asked for both secrets every app session (`LockRuntime.cs:93-98`). It validates
only `password.Length >= 8` and `RescuerKey.TryParse` (`RescuerWindows.cs:593-607`) — no confirmation
field, and no comparison against any existing container. `LockProtectView.ApplyAsync` then puts those
values into `LockCredentials` (`LockTool.cs:360-365`) and `CreateHeader` wraps slot 1
(Rescuer password) and slot 2 (Rescuer key) with exactly those bytes (`LockCrypto.cs:80-86`). The only
post-write verification is `VerifyContainer`, which checks the data key and the **file-password** slot
only (`LockService.cs:721-743`).

So a mistyped Rescuer password (masked field, no confirm) produces a file whose Rescuer-password door is
wrapped with an unknown string; a mistyped key (24 characters, masked) does the same to the key door. The
UI tells the user the ladder exists. If they later forget the file password, the file is unrecoverable —
the exact failure the warning promises cannot happen while the Rescuer exists.

A cheap, complete fix exists and fits the existing design: `VerifyContainer` already opens the container
it just wrote; extend it to `TryUnwrap` with `credentials.RescuerPassword` and `credentials.RescuerKey` and
fail the Protect (deleting the temp, keeping the source, as it already does) with "the Rescuer password or
key you entered does not match the Rescuer used for your other files". That also gives the app its first
verification point for the session prompt.

### B-4 — Data-loss races around deleting the plaintext

**(a) Folder snapshot vs. deletion.** `ProtectCore` builds the archive from a live directory walk
(`LockArchive.Write`), then — after the container is verified and moved into place — deletes the source
folder recursively (`TryRemovePlaintext` → `Directory.Delete(path, recursive: true)`,
`LockService.cs:802-812`). Files created or modified after the walk are deleted without ever being
encrypted. Nothing detects this: `LockMeta.EntryCount` is captured (`LockService.cs:716`,
`LockFormat.cs:233`) and parsed (`:319`) but **never compared** to the archive contents or the extracted
count (grep: no reader). A sync client, a running installer, or a second Explorer copy is enough. There is
no lock on the source folder.

The same class of race exists after encryption for *file* content changes between the last read and the
delete, but for files the container's stored `Size` is cross-checked at decrypt time
(`LockCrypto.cs:570-571`), so a grow/shrink at least fails closed at verify time; for folders there is no
equivalent check.

**(b) Partial delete then retry destroys the good container.** `RelockFromSession` replaces the container
(`LockService.cs:289`), then deletes the plaintext (`:290`). If `Directory.Delete` partially fails (one
file held), `TryRemovePlaintext` returns `leftover` and `RelockFromSession` throws
(`:293-299`). The session stays in the panel in the Error state; the Retry button
(`UnlockedPanel.cs:84` → `LockNow` → `RelockAsync`, `LockSessions.cs:125-153`) has **no state guard** and
re-encrypts whatever is left of the partially deleted tree into the container. The good snapshot from the
first attempt is overwritten with a truncated tree. The same shape exists in `RecoverSession`
(`LockService.cs:640-642`), which keeps the journal on failure so the next attempt re-encrypts the
partial tree.

**Fix.** (a) Before deleting a source folder, atomically rename it to a quarantine name
(`<name>.dwquarantine-<guid>`), re-enumerate and compare `EntryCount` + per-file size/mtime against the
stored metadata, and only then delete; on mismatch, keep it and report which paths changed. (b) Never offer
a blind retry: persist a `PartiallyDeleted` flag in the journal, make retry require an explicit
"salvage what is left" confirmation, and verify the plaintext tree against the container's metadata before
any relock. Deleting the plaintext into a quarantined directory first also makes (b) recoverable.

## 4. Majors in detail

- **M-1** — In `OfferRescuerReset`, `knownKey` is passed into `RescuerSetupWindow`
  (`LockTool.cs:676-677`) and stored in `_knownKey` (`RescuerWindows.cs:61,66`) but never read; the
  `ResetPassword` branch requires `LockRuntime.Vault.Load()` to be non-null (`RescuerWindows.cs:395-404`),
  and the vault is empty on the key-only path — so the advertised "the key lets you set a new Rescuer
  password" flow cannot complete. Separately, the reset that *does* run is reverted: the session captured
  the old header at open (`LockSessions.cs:95-98`), `RelockFromSession` only checks the data key and
  `FileId` (`LockService.cs:253-257`) — neither changes in a slot rewrite — and then writes the session's
  old header back (`LockService.cs:282-283` → `LockCrypto.cs:415`). The user is shown "password changed"
  while the file still carries the old Rescuer password. Fix both: use `_knownKey` (show it, confirm
  last-4, save it to the vault), and at relock re-read the container header, compare it with the session
  copy, and use the **current** header if it changed (same data key), or refuse.
- **M-2** — `Unprotect`/`ExtractTo` unwrap with any slot and never expose the kind
  (`LockService.cs:358-382`), and both remove flows pass the typed text straight through
  (`LockTool.cs:702`, `LockCli.cs:129`). `SetFilePassword` deliberately refuses
  `LockSecretKind.RescuerKey` (`LockService.cs:496-501`), but removal — which decrypts to plaintext and
  deletes the container — does not. If the stated rule is "the key alone only opens and resets the
  Rescuer", removal must be refused for the key (or require a second, explicit confirmation naming the
  key). Independently, removal has no `Journal.FindByContainer` guard (contrast
  `ChangeFilePasswordWindow`, `LockTool.cs:842-847`): removing protection while a session is open decrypts
  the **stale** container, deletes it, and leaves the live session unable to relock
  (`FileNotFoundException` in `RelockFromSession`), with the user's latest edits stranded in the session
  folder.
- **M-3** — `AesZipWriter` derives each entry's `UncompressedSize` from `AddProgress`, whose return value
  is the *archive-wide* cumulative counter (`PasswordZip.cs:243-253`), not the entry's byte count
  (`PasswordZip.cs:149`). The value is written into the local header (`:173`) and the central directory
  (`:176`). A one-file archive is correct; every later entry is wrong, and strict extractors can report
  the archive as corrupt. Fix: a local `uncompressedBytes += read`, and a test that writes a folder with
  three files of different sizes and asserts each entry's reported size.
- **M-4** — WinZip AES fixes PBKDF2-HMAC-SHA1 at 1000 iterations (`PasswordProtection.cs:132-148`); PDF
  2.0 AES-256 (V5) uses its own low-iteration SHA-256/384/512 derivation via PDFsharp
  (`PasswordPdf.cs:44-57`). Both are correct implementations of their formats and both are far weaker than
  the Lock container's 600k PBKDF2-SHA256. An 8-character password is practical to recover offline. The UI
  says only "AES-256" (`Strings/en.json:168`) and the strength meter is advisory. Fix: disclose the KDF
  limitation in the window and in `SECURITY.md`, raise the Protect minimum to 12+, and state that the Lock
  container is the higher-assurance path.
- **M-5** — `SECURITY.md` is the reviewer-facing inventory of what the app writes, and it does not mention
  `.dwenc`/`.dwdir`, the decrypted session folder, the `.dwrestore-*` plaintext staging, `protection.json`
  or the HKCU associations. A reviewer reading only that document would not learn that DropWheel writes
  decrypted copies of user files to `%LOCALAPPDATA%` and to the working folder during unlock. Fix: add a
  Lock/Protect section with the data-flow and the locations.

## 5. What holds up (verified by reading, with test evidence where noted)

| Property | Evidence |
|---|---|
| Per-file random 256-bit data key, wrapped once per secret; never derived from a password; no key reuse across files | `LockCrypto.cs:75-86,345-346`; `LockService.cs:175`; test `LockContainerTests.Every_one_of_the_three_secrets_opens_the_file` |
| AES-256-GCM, 16-byte tag, per-frame authentication | `LockFormat.cs:345-359`; `LockCrypto.cs:617-634,647-675` |
| Header authenticated: HMAC-SHA256 under an HKDF-derived key (salt = `FileId`, info = `DropWheel-Lock-Header-v1`); slot AAD binds magic/version/flags/fileId/slot index | `LockCrypto.cs:252-270,241-247`; `LockFormat.cs:381-389`; test `Tampered_header_mac_is_damaged`, `Tampered_file_id_cannot_be_opened_with_any_secret` |
| Frame AAD binds file identity + frame index + kind ⇒ reorder/drop/splice detected; whole-file SHA-256 + length footer; trailing bytes rejected; file `Size` cross-checked | `LockCrypto.cs:536-579,677-684`; tests `Tampered_tag_is_damaged`, `Tampered_ciphertext_is_damaged`, `Trailing_bytes_are_refused`, `Truncated_file_is_refused`, `Wrong_length_footer_is_refused` |
| Tampered/truncated containers never produce a plaintext at the destination name (staging + verify + move) | `LockService.cs:402-435,437-483`; tests `Corrupt_input_leaves_no_output_files`, `Header_rewrite_is_atomic_under_a_failure_at_every_file_operation` |
| Password slots: PBKDF2-HMAC-SHA256, 600,000 iterations (OWASP 2023), 16-byte random per-slot salt, iterations stored per slot and bound by the header MAC; service refuses low iteration counts on create | `LockFormat.cs:366-373`; `LockCrypto.cs:209-250`; `LockService.cs:746-754`; tests `Production_service_refuses_low_iteration_slots`, `Production_iterations_meet_the_required_floor` |
| Key/password byte buffers and derived KEKs wiped in `finally` on every path I traced; data key wiped by every owner (`Encrypt`, `ProtectCore`, `ExtractTo`/`Unprotect`, rekey methods, `Probe`) | `LockSecrets.cs:20-32`; `LockCrypto.cs:150-172,203-206,225-227,368-371`; `LockService.cs:228-231,347-350,377-381,507-510,527-530,547-550` |
| No secrets in the journal or settings; generic wrong-secret message; no Lock logging at all | `LockJournal.cs:43-71` + test `Journal_never_contains_a_secret`; `LockJournal.cs:304-401` + test `Settings_file_never_contains_a_secret`; `LockFormat.cs:37-38` + test `Wrong_secret_is_generic_and_reveals_nothing`; no `AppLog` usage in `Shell/Protection` |
| No **plaintext** secret at rest; the Rescuer password, key and `KeyUsed` are sealed with current-user DPAPI (2.4.9, supersedes the 2.4.0 "memory only" row) | `RescuerVault.cs`; §9 below; tests `Dpapi_vault_roundtrips_the_pair_and_the_key_used_flag`, `Vault_falls_back_to_memory_and_never_writes_plaintext_when_dpapi_fails`, `No_secret_is_written_under_the_app_data_folder` |
| Recycle default **OFF**; encrypting never recycles; only the container is ever recycled, after a verified unlock | `LockJournal.cs:315`; `LockCrypto`/`LockService` never call the host during protect; `ProtectionHost.cs:20-42`; test `Recycle_bin_is_off_by_default_and_protection_never_recycles` |
| Header rewrite is atomic and verified (temp + fsync + full decrypt + secret test + replace + rollback), fault-injected at every file operation | `LockHeaderRewriter.cs:75-155`; test `Header_rewrite_is_atomic_under_a_failure_at_every_file_operation` |
| Protect pipeline order: temp → fsync → full read-back verify (length + SHA-256) → non-overwriting move → only then delete source; failure keeps the source | `LockService.cs:155-232`; tests `Protect_leaves_exactly_one_container_and_no_plaintext`, `Protect_never_overwrites_an_existing_container`, `Cancel_mid_run_leaves_the_source_and_no_output` |
| ZIP-slip / path traversal on restore blocked; links skipped on write; `CreateNew` on extract; `desktop.ini` restored byte-for-byte | `LockPaths.cs:98-132`; `LockArchive.cs:62-67,155-163`; tests `Archive_refuses_a_path_that_escapes_the_destination`, `Archive_writer_skips_symlinks_and_the_folder_desktop_ini`, `Desktop_ini_is_restored_byte_for_byte_or_removed` |
| Guard refuses drive roots, system/program/data/install folders, reparse points, already-protected names | `LockGuard.cs:68-126`; test `Guard_refuses_drive_roots_system_folders_and_containers` |
| ZIP AE-2 layout (salt + 2-byte verifier + AES-CTR + 10-byte HMAC-SHA1), random 16-byte salt per entry, never overwrites an existing output | `PasswordZip.cs:95,124-169,432`; tests `Zip_round_trips_with_the_correct_password`, `Zip_rejects_the_wrong_password`, `Zip_never_overwrites_an_existing_output` |
| PDF AES-256 (V5) with a random, never-recorded owner password; unprotect verified by loading the page tree | `PasswordPdf.cs:29-59,77-161`; test `Pdf_uses_aes_256_and_round_trips_with_the_correct_password` |
| Protect outputs never overwrite: unique final name, hidden partial removed on success/failure | `OutputPaths.cs:24-41` |

## 6. Test evidence vs. gaps

The Lock tests are unusually good at the *container* level and at *in-process* failure injection. They do
not cover the failure modes this audit ranks highest:

| Claim in the product / tests | Test coverage | Gap |
|---|---|---|
| "No plaintext survives" | `Protect_leaves_exactly_one_container_and_no_plaintext`, `Corrupt_input_leaves_no_output_files` | Both are in-process. No test kills the process at an unlock/relock/protect step boundary, and no test asserts a startup sweep of `sessions/` or `*.dwrestore-*`. |
| "Crash recovery restores edited content" | `Crash_recovery_reencrypts_the_edited_plaintext_into_the_same_container` | Calls `service.RecoverSession` directly. It proves the service works, **not** that startup ever invokes it (B-1). |
| "The journal survives a crash" | `Journal_roundtrip_and_recovery_plan` | Same process, no fsync/partial-file/power-loss case (m-5). |
| "Three doors" | `Every_one_of_the_three_secrets_opens_the_file` | Container-level only. No test asserts that a new container opens with the **Rescuer** password/key after `Protect` (B-3), and none asserts the session prompt rejects a wrong Rescuer secret. |
| "The key cannot do more than open/reset" | `Rescuer_key_alone_cannot_change_the_file_password` | Only covers `SetFilePassword`. No test asserts that removal (`Unprotect`) is refused for the key (M-2), and none covers removal during an open session. |
| "Folders round-trip completely" | `Folder_roundtrip_restores_content_and_desktop_ini_byte_for_byte` | No concurrent-writer test and no `EntryCount` integrity check (B-4). |
| "ZIP archives are valid" | Single-entry examples only | A multi-entry test would have caught M-3. |
| "The app opens protected files" | none | No test covers `--lock-open`, `OnExit` relock, Settings → Protection, or `--self-test` Lock coverage (B-1). |

## 7. Out of scope / not verified

- I did not build or execute the app, the tests, or a container; all findings are static. A dynamic
  confirmation is recommended for B-4 (concurrent writer), B-1 (double-click), and M-3 (extractor
  compatibility).
- Not reviewed: `ProtectionAssociations` registry ACL details beyond "HKCU only", `LockIcons` binary
  assets, installer behaviour, update/signing paths, and `AppSettings` (unrelated features).
- Platform assumptions: Windows NTFS semantics were assumed for atomicity claims (`File.Replace`,
  `File.Move(overwrite)`, `Directory.Delete`). The Lock service is used on Windows only; the tests run on
  the CI OS.
- Threat model: local, single-user desktop. A same-user or admin attacker can read the session plaintext
  and the plaintext settings file by design; this audit does not treat "same user can read the user's
  files" as a vulnerability (it does treat plaintext that outlives the operation as one).

## 8. Recommended fix order

1. **B-1** wiring (small, unblocks everything else and makes the recovery path observable).
2. **B-2** journal-before-extract + startup sweep (contains the largest leak).
3. **B-3** verify slot 1/2 after protect + confirm fields (contains an unrecoverable-loss path).
4. **B-4** quarantine-before-delete + no blind retry (contains the data-loss path).
5. **M-1 / M-2** (recovery-flow correctness, key/removal policy).
6. **M-3** ZIP size counter + multi-entry test (small, user-visible).
7. **M-4 / M-5** disclosure and documentation.
8. Minors in any order; m-1/m-3/m-5 are cheap and worth doing with the blocker work.

**Release gate:** Lock must not ship as a protected-data feature until B-1…B-4 and M-1…M-3 are fixed and
the missing tests in §6 exist. Protect (PDF/ZIP) may ship only after M-3, with M-4 disclosed.

## 9. 2.4.9 update — current-user DPAPI Rescuer vault

This section replaces the 2.4.0 "no secret at rest" statement; the rest of the audit describes the older
commit.

- **What is stored.** `%LOCALAPPDATA%\DropWheel\protection\rescuer.dat` is a DPAPI blob produced with
  `CryptProtectData` (current user, `CRYPTPROTECT_UI_FORBIDDEN`) containing the Rescuer key, Rescuer
  password and a `KeyUsed` flag. The plaintext is encoded only in memory and is wiped after the call. There
  is no plaintext fallback file: if DPAPI cannot protect it, the app keeps the pair in memory for the
  session and behaves as a first run at the next start.
- **Threat model.** Any process running as the same Windows user on the same machine, including malware,
  can call DPAPI and decrypt the blob. DPAPI is not a defence against a compromised account; it protects
  against other Windows users and a copied profile. The non-secret `RescuerConfigured` marker in
  `protection.json` remains readable and contains no secret.
- **Logs/telemetry.** No secret is logged; the vault catches I/O and cryptographic failures without writing
  details or plaintext. The `No_secret_is_written_under_the_app_data_folder` scan still covers the data
  folder because the DPAPI blob contains no plaintext.
- **First Lock.** A random provisional pair is wrapped into the first container, never shown and never
  saved outside the container; Protection settings opens afterwards and adopts the real pair by rewriting
  both Rescuer slots (`AdoptRescuer`) without changing the payload or file password. Cancelling leaves the
  container openable by its file password; the provisional password is unknown and is not reused.
- **Use and rotation.** `KeyUsed` is set only when the current key opens a file
  (`LockSessions.OpenAsync` → `LockRuntime.NoteRescuerKeyUsed`); the Rescuer password never sets it.
  Rotation is disabled until `KeyUsed`, then proves the current password and stores the new key as unused.
  A key that opens an older container cannot enable rotation for the current key.
- **Failure path.** A missing, corrupt or foreign blob makes `DpapiRescuerVault.Load` return null without
  throwing; with the `RescuerConfigured` marker set, the app shows the manual "Rescuer credentials" prompt
  and verifies the pair against an existing container.
- **Tests.** `LockRescuerFlowTests` now covers the six flows above, including the DPAPI round-trip,
  corrupt/foreign blob fallback, key-vs-password `KeyUsed`, old-key rotation refusal, cancel-first-setup
  and silent reuse of a complete stored pair.
