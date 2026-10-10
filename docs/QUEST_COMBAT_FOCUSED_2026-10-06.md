# Quest focused combat effects, 6 October 2026

Run `20261006-192713-150bef` on the latest uploaded build after pulling `eb7a613`. All five trials complete; five decoded summaries, zero malformed records. Schema 8, effect policy `focused-effects-v2`, 72 Hz, baked sky/cockpit, 96 enemy missile pool slots ready. Maximum measured head displacement 5.2 mm and rotation 1.65 degrees. Every trial recorded 16 player shots, 8 enemy shots, 56 missile launches and 61 impacts. No runtime missile/effect pool builds or effect fallbacks.

Game log capture ran before launch through completion (20:29:35 BST). No additional Meta profiling was started for this run; its service state was not independently checked. Previous day's run used Meta profiling, so cross-day timings are not strictly controlled.

| Stage | Mean frame | p95 | GPU |
|---|---:|---:|---:|
| Baseline 1 | 15.53 ms | 19.49 ms | 14.63 ms |
| Reduced missile smoke | 15.67 ms | 18.64 ms | 14.94 ms |
| Smaller projectile lights | 15.17 ms | 18.07 ms | 14.35 ms |
| Detailed building smoke | 15.16 ms | 18.50 ms | 14.08 ms |
| Baseline 2 | 15.22 ms | 18.23 ms | 14.26 ms |

Baseline average: 15.37 ms frame, 14.45 ms GPU, p95 18.86 ms, p99 23.46 ms. Previous day: 15.36 / 14.45 / 18.45 / 22.31 ms respectively. No demonstrated frame-time improvement. Remaining average-frame gap to 72 Hz (13.89 ms) is about 1.48 ms; tails need improvement too. These are app pacing/main-viewport statistics, not compositor FPS. Engine process/physics counters overlap and must not be added to frame/GPU time.

Managed allocations averaged 7,191,912 B per baseline trial, versus 7,985,948 B previously: about 9.9% lower. GC0 averaged 1 rather than 1.5; no GC1/2 occurred in either baseline (previously 0.5 each). Allocation/collection counts vary between runs; do not infer zero future major collections.

Baseline averaged about 161 draw calls and 258,078 primitives per frame across all viewports. These counters do not directly measure pixel shading cost.

## Effect decisions

- Reduced missile smoke disables enemy trails and sets smoke scale to 0.75. Its mean was 0.30 ms slower than baseline; no demonstrated saving. Retain the existing sparse player and enemy smoke.
- Projectile light scale 0.5 was about 0.20 ms faster, but baseline-to-baseline drift was 0.31 ms. Too small to establish a reliable benefit from one trial; retain current lights for now.
- Detailed lit building smoke was about 0.21 ms faster than the simple unshaded baseline. This does not prove the detailed material is cheaper; only two building emitters were present, and noise/drift is comparable. This run does not establish a worthwhile saving from the simpler material.

Avoid another identical benchmark merely to chase differences this small. The next meaningful experiment remains a modest Quest render-scale reduction, checked for cockpit/HUD readability, or a targeted rendering investigation. No rendering settings were changed during this analysis.

## Evidence

- `data/quest-combat-2026-10-06-focused-tagged.log`: complete benchmark records and settings.
- `data/quest-combat-2026-10-06-focused-summary.json`: decoded summaries.
- `data/quest-combat-2026-10-06-focused-analysis.txt`: analyzer output.
- Previous comparison: `QUEST_COMBAT_ALLOCATIONS_2026-10-05.md`.
