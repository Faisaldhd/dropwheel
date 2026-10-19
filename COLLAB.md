# DropWheel — How the two AIs work together

| Role | Who | Does |
|---|---|---|
| **Builder** | DeepSeek (Harness) | Writes code, builds, tests, commits, pushes. |
| **Reviewer / coordinator** | Claude (Cowork) | Reads the code and builds, verifies binaries with REA, writes review findings and the next tasks. **Never edits source code.** |
| **Owner** | Faisal | Decides, tests on his PC and phone, approves releases. |

## Shared channels (all in this folder, all committed to git)

| File | Written by | Purpose |
|---|---|---|
| `STATUS.md` | DeepSeek | Always current. Top line: `NOW: <task> — <state> — last build: <ok/fail> — last commit: <hash>`. Below it: done / in progress / blocked / questions for Claude. |
| `REVIEW.md` | Claude | Review findings with IDs `R-<n>`, each with severity (P0–P3), evidence and the required fix. Plus `NEXT:`, the ordered task list for DeepSeek. |
| `CHANGES.md` | DeepSeek | User-facing changelog per version. |
| `*_PROMPT.md` | Claude | Task specifications (FIX, THEME, UPDATE, …). |

## Rules for DeepSeek
1. **At the start of every session:** read `REVIEW.md` first. Fix all open P0 and P1 items before new features. Mark each item `fixed in <commit>` in your `STATUS.md`. Do not edit `REVIEW.md`.
2. **After every task:** build, run `dotnet test`, commit with a clear message, `git push origin main`, then update `STATUS.md`.
3. **Ask, don't guess:** if a spec is unclear, write the question under "Questions for Claude" in `STATUS.md` and continue with other tasks.
4. **Never claim done without evidence:** a build log, a test count, or a REA output.
5. **Keep it clean:** no scratch files in the root; put tools in `scripts\`.

## Rules for Claude
1. Review after each push or when Faisal asks: read `STATUS.md` and the latest commits, verify the release with REA, then write `REVIEW.md`.
2. Only touch: `REVIEW.md`, `*_PROMPT.md`, `branding\`. Never source files.

## Folders (owner decision)
| Folder | What it is | Who touches it |
|---|---|---|
| `Downloads\DropWheel_1\` | **The ONLY workspace.** All work happens here: `DropWheel\` (the git repo), `_local\` (testdata, sdk, old builds; never committed), worktrees. | DeepSeek and Claude |
| `Documents\DropWheel\` | The owner's personal test copy of the app. | **The owner only.** DeepSeek and Claude never read, write, build into, copy to, or delete anything under `%USERPROFILE%\Documents`. |
| `Desktop\DropWheel_1\` | Old copy. The owner is deleting it. | Nobody. Never recreate it. |

## Git
- Remote: `origin` = `https://github.com/Faisaldhd/dropwheel` (PUBLIC). This is the ONLY remote. It holds the source and the GitHub Releases.
- Never commit secrets or personal paths. The update-signing key stays outside the repo.
- Branch: `main`. Tag every release `vX.Y.Z`.
