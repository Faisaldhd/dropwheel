# Accountability ledger - 2.4.0 release train

Owner of this file: TEAM B supervisor. Every violation of the release rules is logged here with the agent
that owns it, what happened, the evidence, and how it was closed. An unverified claim is a violation
(rule 5), so every entry carries a CI run id, a commit SHA, a test name or a file:line.

Rules in force: no-interference (ownership, append-only shared files, no force-push), CI-only execution
(no local build/exe/installer), location confinement to `Downloads\DropWheel_1`, and evidence-backed claims.

## Violations and corrective actions

| # | Owner (lane) | What happened | Evidence | Corrective action | Status |
|---|---|---|---|---|---|
| A-01 | TEAM B UX lane (`audit/adesign`, PR #31) | Rewrote **7 existing en/ar string values** (Title Case: `tool.protect.title`, `tool.unlock.title`, `tool.videoframes.label`, `tool.videoframes.title`, `tool.videoredact.label`, `tool.videoredact.title`, `tool.redact.title`) - violates the append-only strings rule - and carried a `WheelController.BuildOperations()` hunk duplicating executor-owned wiring | Original PR #31 diff: `en.json 15 +-`, `ar.json 1 +`; commits `6a04381`, `77b1ccb` | All 7 values reverted to main's text; only the additive `tool.qr.copyClose` kept (+1 line per table); `WheelController` hunk dropped (`git diff origin/main` empty); Title Case assertion removed from `UxContractTests`, item moved to `docs/BACKLOG.md`; pushed fast-forward only (`f8ed027`), PR now **1040 insertions / 0 deletions** | Fixed |
| A-02 | TEAM B UX lane (`audit/adesign`) | Wrote 152 files / 30.5 MB of scratch to `%TEMP%\adesign-audit`, outside the allowed root | Directory listing at 08:27 (PDF tiles, video frames, `slice.ps1`, `tref-*.png`) | Supervisor relocated it intact to `_local\tmp\adesign\pdf-frames`; old path verified gone; all 8 lanes re-briefed to use `_local\tmp\<role>`; a repeat sweep found no other writes outside `DropWheel_1` | Fixed |
| A-03 | TEAM B supervisor | Force-pushed `audit/adesign` (`1d7d5ac` -> `a11a836`) at 08:44, before the no-force-push rule existed | Push log; remote reflog | Disclosed to the owner; standing order issued to all lanes: rebase only **before** the first push, `git merge origin/main` afterwards; no force-push since, none planned | Disclosed |
| A-04 | TEAM B localization lane (`audit/selftest`, PR #37) | Carries the same two RTL fix files as its own `audit/al10n` (#30) - same owner, so not a foreign-file breach, but the fix is duplicated across two PRs | #37 diff: `Shell/Protection/RescuerWindows.cs -1`, `UI/Tools/Lock/LockTool.cs` | Merge order fixed in the PR body: **#30 first**, then #37 merges `origin/main` and the overlap becomes a no-op; executor informed | Open (coordination) |
| A-05 | Pre-existing (not this train) | `SelfTest.cs:449` claims it "builds every registered floating tool offscreen" but `SelfTest.cs:465-467` builds only `CropTool`; `ToolRegistry.All.Count` is merely asserted non-zero | `SelfTest.cs:449` vs `:465-467`; release lane R-04 | Claim frozen: no document may say "every window verified in Arabic" until #37 is green; the honest claim is "1,094 keys per table, parity enforced in CI"; #37 closes the gap | Open -> #37 |

## Standing checks performed by the supervisor

| Check | Method | Result |
|---|---|---|
| No local build / exe / installer / `--self-test` / `--diagnose` run by any lane | Lane briefs + reports; no `bin/obj/publish` artefacts in any lane worktree | None observed |
| Location confinement | `git worktree list` (all 46 paths under `DropWheel_1`); recent-write sweep over Documents, Desktop, profile root, `%TEMP%`, Downloads root, `C:\`, `D:\` | One exception (A-02), fixed |
| No test weakened or deleted | Removed `[Fact]`/`[Theory]`/test methods diffed on every pushed lane branch | Zero deletions on all 7 first-round lanes |
| No forbidden content committed | Branch diffs scanned for `_reference/`, `.mp4`, `.zip`, `.exe`, `.dll`, `.png`, `.webp`, `.pfx`, `SHA256SUMS` | Clean on every lane |
| Shared-file ownership | Protected-path scan (`CHANGES`, `README`, `en/ar.json`, `MERGE-LOG`, `WheelController`, `ToolRegistry`) per branch | Only A-01 (fixed); all other lanes clean |
| Claims carry evidence | Every lane report required to cite a run id, commit SHA or test name | Enforced; unverified statements rejected |

## How an entry is added

1. The supervisor records: owner lane, what happened, evidence (run id / SHA / file:line), corrective action.
2. The owning lane is told in writing and must fix it with new commits (never a force-push, never a rewrite).
3. The entry moves to `Fixed` only when the evidence shows the fix on the branch (and, for a merged item, on `main`).
4. This file is append-only: never delete or rewrite an entry, add a new row with the next id.

## 10:45 accountability line

Reported to the owner as: `violations: <none | list of open ids (A-04, A-05)>`. Closed entries stay in the
table as history; only open ones are named in the line.