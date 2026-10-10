# Quest player grounding — 10 October 2026

Run `20261010-154740-d54e48`, schema 12 / `player-grounding-v1`. All three trials completed. Both fetched mirrors are identical; zero malformed summaries, 2,533 archived frames matching summary counts, and final completion status present.

| Metric | Legacy baselines averaged | Optimized |
| --- | ---: | ---: |
| Player physics callback, ms/tick | 2.218 | 0.102 |
| Mean frame, ms | 14.225 | 14.215 |
| p95 frame, ms | 18.181 | 16.422 |
| p99 frame, ms | 20.547 | 18.172 |
| Main-viewport GPU, ms | 11.63 | 11.79 |
| Godot physics monitor, ms | 5.137 | 1.759 |

## Interpretation

This confirms a substantial real Quest CPU saving: about 2.12 ms per player physics tick, or 95.4% of that callback. Legacy ground-clearance work alone took approximately 2.05 ms per tick. The optimized trial spent 0.49 ms total on ground clearance over 721 ticks, and issued no chassis surface-index lookups during the stationary measured window after filling its cache during warmup. Gait/footfall clocks continued advancing. The player callback's recorded managed allocations dropped from about 92 KB to zero per trial.

The slower-frame tail improved: p95 by 1.76 ms and p99 by 2.38 ms relative to the averaged baselines. Both candidate percentiles also beat each individual baseline. This is one run, not a repeatability claim. The average changed by only 0.01 ms, so the change does not establish higher sustained FPS or meeting the 13.89 ms budget. Remaining slow frames include both GC and non-GC intervals. The candidate's worst interval was 26.75 ms without GC; its collection interval was 25.87 ms.

Keep the optimization subject to walking/jumping/landing visual feedback. The benchmark verifies stationary combat, not behavior on uneven terrain. Next investigate remaining frame pacing and unmeasured engine/render/compositor waits, plus allocation/GC stalls. Do not subtract callback savings directly from frame time or assume main-viewport GPU timing accounts for all headset GPU work. No physics-engine change is justified by this test alone.

## Comparison validity

Every trial recorded six enemy launches, 52 missiles and 59 impacts. Head movement stayed below 2.2 mm and 0.66 degrees. Rendering counts were similar. One generation-0 collection occurred in each trial, with no higher-generation collections; total process allocations remained around 8.05 MB despite the small scoped allocation saving. Legacy baseline frame means drifted from 14.286 to 14.164 ms, larger than the candidate's mean improvement. GPU mean did not improve.

CPU scopes are inclusive and nested; do not sum gait, clearance, jump-jet and surface counters into the enclosing player callback or add them to engine monitor timings. App frame intervals are not compositor-presented FPS.

## Evidence

- [Decoded summaries](data/quest-combat-2026-10-10-player-grounding-summary.json)
- [Analyzer output](data/quest-combat-2026-10-10-player-grounding-analysis.txt)
- [Complete archive including frame records, gzip](data/quest-combat-2026-10-10-player-grounding-tagged.log.gz)

Original fetched file: `local/quest-benchmarks/20261010-164917/app-storage/benchmarks/20261010-154740-d54e48.log`. Decompress the saved gzip archive before passing it to the analyzer. No code or production settings were changed during this analysis.
