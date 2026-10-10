# Quest combat with sparse missile effects — 4 October 2026

Release run `20261004-194054-2fcd2a` completed all four trials. Capture has no
malformed summaries; each trial records 16 player shots, 8 enemy shots,
56 missiles and 58 impacts. Head movement stayed below 0.0022 m / 0.507 degrees.
No missile/effect pools were built during measurement and no effect fallback
allocations occurred.

Run metadata confirms `missileVisualPolicy=quest-per-mech-launch-smoke3-light8-v1`;
all summaries record smoke stride 3 and light stride 8. Thus baseline smoke and
lights ON mean one trail per three launches and one missile light per eight,
for both player and enemy mechs. Other graphics settings match the previous
baked profile. Missile query, guidance and targeting policies are retained.

Evidence: [tagged log](data/quest-combat-2026-10-04-sparse-tagged.log),
[summary JSON](data/quest-combat-2026-10-04-sparse-summary.json),
[analysis](data/quest-combat-2026-10-04-sparse-analysis.txt).

| Trial | Mean frame | GPU | p99 |
| --- | ---: | ---: | ---: |
| Baseline 1, sparse smoke/lights ON | 15.22 ms | 14.38 ms | 21.99 ms |
| All smoke OFF, lights ON | 14.71 ms | 14.18 ms | 19.48 ms |
| Smoke ON, all weapon lights OFF | 15.76 ms | 15.00 ms | 20.63 ms |
| Baseline 2, sparse smoke/lights ON | 15.05 ms | 14.21 ms | 21.21 ms |

## Compared with the preceding run

Both baselines averaged 15.13 ms (66.08 application FPS), GPU 14.30 ms, p95
18.40 ms and p99 21.60 ms. Compared with the
[preceding missile-optimized run](QUEST_COMBAT_MISSILES_2026-10-04.md):

| Metric | Previous full-smoke baseline | Sparse baseline | Observed saving |
| --- | ---: | ---: | ---: |
| Mean frame | 16.67 ms | 15.13 ms | 1.54 ms / 9.2% |
| GPU | 15.53 ms | 14.30 ms | 1.24 ms / 8.0% |
| p95 | 21.96 ms | 18.40 ms | 3.56 ms / 16.2% |
| p99 | 26.24 ms | 21.60 ms | 4.64 ms / 17.7% |
| Managed allocation per trial | 17.54 MB | 16.25 MB | 7.4% |

This is an observational before/after comparison across separate live fights,
not a controlled replay. Previous impacts were 59 per trial versus 58 now.
The new bracketing baselines are close (0.17 ms apart), which supports retaining
this setting, but does not attribute every millisecond to the code change.
Player and enemy trails were both thinned; missile lights changed from pool-slot
selection to launch cadence; non-smoke missiles also avoid an invisible post-impact
fade wait. No visual acceptance feedback was supplied with this run.

## Remaining budget and recommendation

A 72 Hz average frame budget is 13.89 ms. The baseline still needs about
**1.24 ms/frame overall (8.2%)**, with GPU time **0.41 ms** above that budget.
CPU/GPU work overlaps, so these gaps are not added together. p99 remains above
the budget; reaching the average alone does not establish consistent 72 Hz pacing.

Smoke-off now saves only 0.42 ms mean / 0.12 ms GPU relative to the sparse
baselines. Lights-off is slower by 0.63 ms mean / 0.70 ms GPU in this single
candidate trial; treat that as live-run variability, not proof that lights make
the game faster. There is no demonstrated saving from further light removal
here. Each effect candidate still has only one sample.

Keep the one-in-three trails and one-in-eight missile lights. Turning all smoke
off would not close the remaining average gap in this run. Next candidates should
address other render cost, such as a modest Quest render-resolution reduction,
while checking cockpit/HUD readability. No new gameplay setting or resolution
change was made for this analysis, and no additional user benchmark is required
just to preserve this result.

Missile terrain queries total 265.46 ms across 1,983 baseline frames (0.134
ms/frame); this remains too small to justify replacing individual swept collisions
with risky leader-only collision shortcuts. Application FPS and main-viewport GPU
telemetry are not compositor FPS. Full native headset pacing requires separate
compositor-level evidence.
