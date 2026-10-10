# Quest combat chunk sizes — 8 October 2026

Run `20261008-173717-49a1a4`: all five trials complete, five decoded summaries, zero malformed records. Captured live over ADB. Source checkout: `5828ce3` plus uncommitted chunk-size, HUD coverage and controller changes. These results do not identify an immutable build commit.

All stages use vertex lighting with specular disabled. The baseline is unchunked terrain; this is a benchmark setting, not a change to production gameplay defaults. Existing sparse smoke and light settings remain in place.

| Terrain | Mean frame ms | GPU ms | p95 frame ms | Mean draws | Mean primitives |
| --- | ---: | ---: | ---: | ---: | ---: |
| Unchunked (two baselines averaged) | 14.17 | 11.27 | 17.94 | 161.59 | 257562 |
| 512 m / 190 chunks | 14.13 | 11.53 | 17.76 | 227.73 | 130315 |
| 1024 m / 51 chunks | 14.06 | 11.02 | 17.65 | 181.03 | 146684 |
| 2048 m / 19 chunks | 14.06 | 10.97 | 17.60 | 169.86 | 192675 |

## Interpretation

2048 m is the best provisional compromise: approximately 0.30 ms less GPU time, 25% fewer submitted primitives and only 5% more draw calls than unchunked terrain. 1024 m is essentially tied on mean frame time, with slightly higher GPU and submission cost. 512 m increases GPU time despite halving submitted primitives. This supports avoiding fine chunks and their extra draw calls.

The mean frame improvement is small (about 0.11 ms, 0.8%). Baseline drift was 0.07 ms in frame time and 0.07 ms in GPU time. Each chunk size has only one trial, so this is a candidate choice rather than proof of a repeatable winner. The previous 256 m regression is avoided, but the different vertex-lit baseline prevents a direct controlled comparison with that older trial.

At 72 Hz the frame budget is 13.89 ms. The best mean is about 0.17 ms above it, and p95 remains 17.60 ms; sustained delivery is not established. These are application pacing and main-viewport GPU timings, not compositor FPS or total compositor GPU cost. Do not add overlapping process, physics, rendering and GPU counters.

Next focus should be frame spikes and CPU/pacing costs, while retaining vertex lighting as the promising rendering option. No further identical benchmark is needed just to select the coarse-chunk candidate. Before enabling chunking for normal gameplay, inspect its allocation cost and verify visuals.

## Validity and caveats

Every trial fired 16 player shots and eight enemy shots, launched 56 missiles and recorded 60 impacts. Missile and weapon-effect pool builds/fallbacks were zero. All chunked stages preserve the 197386 source terrain triangles. Baseline mean head angle was 1.53 degrees; chunk variants ranged from 0.24 to 0.51 degrees, so view movement was not identical.

Baseline allocations averaged 6027000 bytes per trial; 1024 m recorded 12951112 and 2048 m 10987336. Each had one generation-0 collection and no higher-generation collections. Investigate whether chunk setup or other work contributes before treating these allocation totals as a steady combat regression. The changed HUD coverage mesh also prevents attributing cross-day allocation changes to terrain alone.

The user confirmed the HUD banding was fine after this run. Controller usability still needs separate headset feedback; this performance capture does not validate it.

The earlier run on this date rolled out of logcat before its summaries were collected; it is not included.

## Evidence

- [Raw tagged log](data/quest-combat-2026-10-08-chunk-sizes-tagged.log)
- [Decoded summaries](data/quest-combat-2026-10-08-chunk-sizes-summary.json)
- [Full analyzer output](data/quest-combat-2026-10-08-chunk-sizes-analysis.txt)
