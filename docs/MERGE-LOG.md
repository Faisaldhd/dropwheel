# 2.4.0 merge train

| PR | Tool | Merge commit | CI |
| --- | --- | --- | --- |
| #7 | Add BG | b08d2f6 | CI green 39346189380 |
| #8 | Collage | f9c55d3 | CI green 39348532931 |
| #10 | Redact | 020f87a | CI green 39348483861 (merged outside the train; test fix landed in 3dd4cd6) |
| #9 | Edit photos | e87e3b4 | CI green 39350297389 |
| #13 | Video frames + redact | f01eda8 | CI green 39351209717 |
| #11 | Video trim/crop/speed | 5da0b89 | CI green 39352799362 |
| #12 | Split/Join | 0c462a9 | CI green 39354842668 |
| #15 | Audio FX | bdb7e89 | CI green 39355507301 |
| checkpoint 2026-10-20 07:44 | merged: #16 audiotrim, #14 pdfpages, #18 qr, #22 compressmeta | remaining: #19 protect, #21 lock, #24 perf, #23 sheet | agents: protect re-merging main 3d32231; lock badge done pending merge; perf/sheet waiting | no tag |
## Delivery schedule - 2.4.0 (deadline 11:30 Riyadh / 08:30 UTC)

| local | UTC | milestone | state |
|---|---|---|---|
| 09:30 | 06:30 | all tools + badge + sheet + integration merged on main | PASSED (tools, Lock, motion, perf pending) |
| 10:30 | 07:30 | audit FAILs fixed, FUNCTION-MATRIX all PASS, installer verified on clean profile | AT RISK (security blockers on Lock, perf #24 pending) |
| 11:00 | 08:00 | tag v2.4.0 after CI proves the shipped artifact | pending |
| 11:30 | 08:30 | release assets verified, website published | pending |

Gates never skipped: no data-loss bugs, CI green, shipped artifact verified.
