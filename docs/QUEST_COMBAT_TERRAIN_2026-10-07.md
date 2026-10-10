# Quest terrain combat comparison, 7 October 2026

Run `20261007-182803-d5173f`, Release installed from `5828ce3`. All five trials completed; five decoded summaries and no malformed records. Log capture began before the user launched the game and ended after the completion marker at 19:30:26 BST. No Meta profiling was started for this run.

Each trial recorded 16 player shots, 8 enemy shots, 56 missile launches and 59 impacts. No runtime missile/effect pool builds or effect fallbacks. Terrain variants preserve physics and effects. Baselines use the latest cached-sun implementation, so this run does not isolate that change against the old sky.

| Stage | Mean frame | p95 | p99 | GPU | Mean draw calls | Mean primitives |
|---|---:|---:|---:|---:|---:|---:|
| Baseline average | 14.88 ms | 18.38 ms | 22.88 ms | 13.57 ms | 160.95 | 258,034 |
| Terrain chunked | 17.77 ms | 21.80 ms | 24.74 ms | 16.99 ms | 376.57 | 121,601 |
| Terrain vertex-lit | 14.26 ms | 17.90 ms | 23.82 ms | 11.64 ms | 162.70 | 258,357 |
| Both | 14.93 ms | 18.13 ms | 23.99 ms | 13.80 ms | 377.23 | 121,946 |

Draw calls and primitives cover all viewports. Frame time is application pacing and GPU time is the measured Godot viewport; neither is compositor FPS. CPU, physics, process and GPU timings overlap and must not be summed.

## Findings

Vertex lighting with terrain specular disabled is the strongest candidate: GPU time fell by about 1.93 ms (14.2%), with app frame time down 0.62 ms (4.2%). The trial averaged about 70.1 app FPS and remains 0.37 ms above the 13.89 ms average budget for 72 Hz. Its p99 did not improve, so this does not establish smooth sustained 72 FPS or eliminate CPU/frame-pacing spikes. Vertex lighting and specular removal are combined in this variant; the test cannot attribute the saving to either individually.

The current 670-chunk layout preserves all 197,386 source terrain triangles but culls enough geometry to reduce rendered primitives by 52.9%. It raises draw calls by 134%, renderer CPU from 0.56 to 0.88 ms, and GPU time by 3.42 ms. This implementation loses overall despite improved culling. Combining it with vertex lighting gives approximately baseline performance and is slower than vertex lighting alone. Reject this chunk layout as the default; this does not rule out coarser chunks or better material batching.

Baseline 1 was 15.21 ms frame / 14.07 ms GPU, versus baseline 2 at 14.56 / 13.06 ms: 0.65 ms frame and 1.01 ms GPU drift. There is one sample of each candidate. The exact savings are uncertain, but vertex-lit GPU time is below both baselines, and chunk-only is slower than both. Shader warmup, clocks and fighting remain possible sources of variation.

Baseline allocations averaged 4,825,540 B per 15-second trial, one GC0 and no GC1/2. Previous day was 7,191,912 B, a roughly 33% decrease, but trial allocations vary and fights differed (59 versus 61 impacts). No isolated causal allocation claim follows from this cross-day comparison.

## Next decision

Recommend Quest terrain vertex lighting plus specular disabled, provided terrain and weapon-light appearance are acceptable in the headset. Keep current terrain batching and the sparse smoke/lights preferences. Do not enable the current chunk layout. After adopting the candidate, remaining frame spikes deserve investigation rather than assuming a further resolution reduction will fix them. No production settings were changed during this analysis.

## Evidence

- `data/quest-combat-2026-10-07-terrain-tagged.log`
- `data/quest-combat-2026-10-07-terrain-summary.json`
- `data/quest-combat-2026-10-07-terrain-analysis.txt`
- Previous comparison: `QUEST_COMBAT_FOCUSED_2026-10-06.md`.
