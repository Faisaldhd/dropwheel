# PR queue - TEAM B audit lanes

Each lane owns exactly ONE row below. Edit only your own row (PR number + CI state); never rewrite,
reorder or delete another lane's row. Branch prefix is `audit/`. TEAM A's supervisor merges; these
lanes never merge to `main` and never tag.

| PR | Branch | Lane | Deliverable | CI |
| --- | --- | --- | --- | --- |
| [#31](https://github.com/Faisaldhd/dropwheel/pull/31) | `audit/adesign` | UX/UI quality | `docs/audit/UX-AUDIT.md` | run 39377310570 — **green** (build + self-test + test, `e3c0554`) |
| pending | `audit/al10n` | Localization/RTL | `docs/audit/L10N-AUDIT.md` | pending |
| pending | `audit/acreative` | Creative direction | `docs/IDEAS.md`, `docs/CREATIVE-GUIDE.md` | pending |
| #40 | `audit/amotion` | Motion | `docs/audit/MOTION-AUDIT.md` | green 39379573357 at `3caf26c` (build + self-test + tests); merged with main `8b28758` |
| #33 | `audit/arelease` | Release/installer | `docs/audit/RELEASE-AUDIT.md` | green (build-test + package) — run [39375412602](https://github.com/Faisaldhd/dropwheel/actions/runs/39375412602) |
| pending | `audit/adocs` | Docs | `CHANGES.md`, `README*`, tool docs, release notes en+ar | pending |
| pending | `audit/afunc` | Drag and drop | `docs/FUNCTION-MATRIX.md` rows | pending |
| pending | `audit/asec` | Hostile input | `docs/audit/HOSTILE-INPUT-AUDIT.md` | pending |
| pending | `audit/aperf` | Performance (reserve) | `docs/audit/PERF-AUDIT.md` | pending |
| pending | `audit/aweb` | Website (reserve) | `docs/audit/WEB-AUDIT.md` | pending |
