# Quest combat CPU isolation — 8 October 2026

Run `20261008-181050-b076a0`, schema 11 / `cpu-isolation-v1`. All five stages completed; zero malformed summaries. App-storage and Downloads mirrors are byte-identical and contain final completion status. Automatic recording and subsequent ADB retrieval are now verified on Quest. Source checkout remains uncommitted on top of `5828ce3`.

| Stage | Mean frame ms | p95 ms | p99 ms | Main-viewport GPU ms |
| --- | ---: | ---: | ---: | ---: |
| Baselines averaged | 14.24 | 18.02 | 20.83 | 12.01 |
| Enemy half rate | 14.23 | 17.76 | 19.02 | 12.13 |
| HUD frozen | 14.15 | 17.55 | 20.01 | 11.72 |
| Combat callbacks frozen | 13.92 | 14.46 | 14.81 | 11.57 |

## Findings

The combat freeze strongly reduced the tail: p95 fell about 3.56 ms and p99 about 6.02 ms. The mean fell only 0.32 ms because headset pacing is already near the 72 Hz interval (13.89 ms). Near-budget means alone conceal the improvement in consistency. This does not mean removing simulation is a playable optimization, or that all the tail improvement is CPU-only: average draw calls also fell from 167.88 to 139.19, GPU time fell, and effects/HUD work changed with the frozen encounter.

The largest measured callback is **player physics**, approximately 2.11 ms per physics tick, or 1.81 ms per sampled app frame. HUD drawing is about 1.10 ms per app frame, missiles 0.34 ms, and all enemies 0.31 ms. These inclusive timings overlap with nested work and cannot be added to engine/GPU timings or assumed to translate directly into frame savings.

Half-rate enemy processing worked (2888 callbacks per baseline versus 1444), reducing enemy callback time to about 0.16 ms per frame, but produced no meaningful mean-frame improvement. HUD freezing eliminated HUD process/draw/coverage callbacks but improved mean frame time by only about 0.09 ms. Neither is currently a compelling gameplay compromise.

Both baselines had one collection coinciding with a large HUD scope interval (16.31 and 22.18 ms). This does not establish that HUD drawing caused the collection: the GC can interrupt that scope. Other large spikes had no GC, including occasional high GPU samples. Every stage had one generation-0 collection. Allocation totals rose across the run even with frozen callbacks, so unattributed runtime/recorder work needs care before assigning allocation changes to a subsystem.

## Next target

Inspect and subdivide the player physics callback first: gait/ground-clearance, jump-jet surface queries, torso/cockpit transforms, and audio. Code inspection shows grounded stationary play still calls `AdvanceJumpJets` (surface query) and `ApplyGaitGroundClearance` (foot/chassis terrain sampling) every physics tick. `TryMoveAcrossTerrain` already exits for zero movement, so merely adding a stationary guard there would not help. Reusing stable terrain/foot results or avoiding unchanged rig updates is a concrete hypothesis, not yet a measured cause. Keep heat, landing, damage and controller behavior live.

Do not adopt half-rate enemies or frozen HUD as production defaults from this run. Retire those diagnostic candidates once the next focused player-physics measurements exist. No resolution reduction is justified by these callback results alone, and main-viewport GPU timing excludes some compositor cost.

## Validity

All normal stages recorded six enemy weapon launches and matching encounter setup; head translation stayed below 3 mm and maximum rotation below 1.25 degrees. Baselines drifted from 14.31 to 14.16 ms (0.15 ms), with GPU 12.26 to 11.77 ms (0.49 ms). Small candidate mean/GPU differences are therefore not firm wins. Player/enemy/missile callbacks all reached zero in the simulation freeze; HUD calls reached zero in its freeze. Physics-server ticks and XR tracking remained enabled.

The suite warms live combat for three seconds then measures twelve seconds per stage. Do not directly compare allocation totals or exact encounter counts with the prior fifteen-second chunk suite. Frame intervals are app pacing, not compositor-presented FPS. Engine render counters can lag the sampled callback interval.

## Evidence

- [Decoded summaries](data/quest-combat-2026-10-08-cpu-isolation-summary.json)
- [Analyzer output](data/quest-combat-2026-10-08-cpu-isolation-analysis.txt)
- [Full tagged archive, gzip compressed](data/quest-combat-2026-10-08-cpu-isolation-tagged.log.gz), including every frame. Decompress before using the existing analyzer.
- Original fetched archive: `local/quest-benchmarks/20261008-191332/app-storage/benchmarks/20261008-181050-b076a0.log`.
