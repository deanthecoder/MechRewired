# Quest combat after missile optimizations — 4 October 2026

Run `20261004-191703-00a6da` completed all four 15-second trials in a Release
build on Quest 3, Mobile renderer, 2x MSAA. Schema 6 confirms the pooled missile
query and shared salvo guidance policies. All trials use baked sky/cockpit,
shadows/glow/glass/terrain triplanar off; smoke and lights are on in the baseline.
All four recorded 16 player shots, 8 enemy shots, 56 missiles and 59 impacts.
There were no missile/effect pool builds or fallback allocations. Head movement
was within the analyzer's validity limits.

The [tagged log](data/quest-combat-2026-10-04-missile-tagged.log),
[summary JSON](data/quest-combat-2026-10-04-missile-summary.json) and
[analyzer output](data/quest-combat-2026-10-04-missile-analysis.txt) preserve the run.

| Trial | Mean frame | GPU | p99 |
| --- | ---: | ---: | ---: |
| Baseline 1, smoke/lights ON | 16.45 ms | 15.29 ms | 27.05 ms |
| Smoke OFF, lights ON | 15.11 ms | 14.59 ms | 20.80 ms |
| Smoke ON, lights OFF | 15.36 ms | 14.33 ms | 24.03 ms |
| Baseline 2, smoke/lights ON | 16.90 ms | 15.78 ms | 25.44 ms |

## Before and after

Compare the two bracketing baselines with the
[earlier same-day run](QUEST_COMBAT_2026-10-04.md). These are separate live fights,
so this is an observational comparison, not a controlled A/B replay. The earlier
run recorded 58 impacts; this one recorded 59. Device pacing and GPU timing also
varied. Allocation figures cover all measured managed allocations, not missiles
alone.

| Baseline metric, mean of two trials | Before | After | Change |
| --- | ---: | ---: | ---: |
| Mean frame | 15.99 ms | 16.67 ms | +4.3% |
| GPU | 14.81 ms | 15.53 ms | +4.9% |
| p99 frame | 26.87 ms | 26.24 ms | -2.3% |
| Managed allocations per 15-second trial | 24.03 MB | 17.54 MB | -27.0% |
| Gen 0 collections per trial | 4.0 | 2.5 | -37.5% |

The allocation reduction is consistent with removing per-frame query resources
and per-missile salvo callbacks. There is no measured frame-time improvement in
this run, and the comparison does not establish a frame-time regression caused
by the optimization. Gen 1 and Gen 2 collections remained at one per baseline
trial. Keep the allocation improvements, but do not claim an FPS gain.

## Where to spend the next effort

The baseline terrain queries total 197.67 ms across 1,800 frames: about
**0.110 ms/frame**, or 10.35 microseconds per query. This is after the optimization;
there is no before-run terrain timer. Eliminating all remaining terrain-query
cost would recover only about 0.11 ms/frame in this workload. Do not add a risky
trajectory cache or leader-only collision shortcut for that budget.

Enemy AI totals about 384 ms per 15-second baseline (roughly 0.43 ms/frame), with
LOS around 13 ms per trial. Those counters cover their explicit timed sections,
not every combat subsystem. Do not sum engine process, physics, GPU and these
nested counters into a total frame time.

Within this run, smoke-off saved **1.56 ms mean frame (-9.4%)** and **0.94 ms GPU
(-6.1%)** versus the bracketing baselines. Lights-off saved **1.31 ms mean frame
(-7.9%)** and **1.21 ms GPU (-7.8%)**. Each is one candidate trial; smoke-off also
disables battlefield dust/impact smoke, so it cannot isolate enemy rocket trails.

GPU time at 15.53 ms exceeds a 72 Hz frame budget of 13.89 ms. The next practical
candidate is fewer enemy smoke particles or fewer enemy trails, retaining player
trails and weapon lights. Current missile smoke uses 144 particles per trail.
An enemy-only change still needs measurement; the global smoke-off result is not
its predicted gain. Smoke and lights remain enabled in production. No extra
benchmark run or gameplay setting change was requested for this analysis.

All FPS figures are application/main-viewport telemetry, not compositor FPS.
