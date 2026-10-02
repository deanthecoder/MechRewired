# Quest rendering benchmark (development branch)

The 1 October replacement-sky and baked-cockpit headset run, with its captured
trial data and visual notes, is in [QUEST_TESTS_2026-10-01.md](QUEST_TESTS_2026-10-01.md).
The first terrain baseline escaped log capture; the other three fixtures have
both baselines.

The first headset run's complete variant comparisons and validation record are
in [QUEST_PERFORMANCE_FINDINGS.md](QUEST_PERFORMANCE_FINDINGS.md), with all 40
summary trials in [the committed CSV](data/quest-benchmark-2026-09-29-summary.csv).
The September 29 baked-sky results are historical evidence for the superseded
implementation; the 1 October run covers the replacement. Current design notes and
historical findings are in the [sky note](QUEST_SKY_BAKE_FINDINGS.md).

This is a repeatable **rendering ablation**, available in Release builds on this
development branch. It holds gameplay/AI/physics still while moving the pilot
through scripted viewpoints and rendering real missile effects. It does not
measure the cost of a live battle, weapon simulation, destruction or AI.

## Run on the headset

1. Build/install the Release APK with `scripts/quest.sh install`.
2. On your Mac, start log capture in a terminal before running the benchmark:

   ```sh
   adb logcat -v raw -s godot:I > quest-benchmark.log
   ```

   Leave this running. The log is written directly on your Mac in the terminal's
   current directory. Restart the mission to restore its initial state.
3. Open the Quest menu, select **BENCHMARKS > RENDERING TEST**, and remain seated. Keep your
   head in the same position, facing forward. The sweeps translate/turn the mech;
   native headset tracking remains live. Press **Menu** to stop at any time.
   Loss of headset tracking/focus also stops the run.
4. Allow approximately three minutes plus setup/reporting overhead. The
   headset label identifies the current fixture and variant.
5. When the headset reports completion, stop log capture with Ctrl+C. Analyze it
   with `python3 scripts/analyze-quest-benchmark.py quest-benchmark.log`, or give
   Codex on your Mac the log's absolute path. No ZIP or screenshot transfer is needed.

Desktop VR preview can exercise the code path, but its numbers are not Quest
performance results:

```sh
godot --path MechRewired --rendering-method mobile -- --vr-preview --quest-benchmark
```

Do **not** use `--fixed-fps`, a headless/dummy renderer or Movie Maker to collect
performance evidence. Use the same headset refresh rate, resolution, graphics
options, battery/charging state and mission each time. Run at least twice;
reverse fixture variant order is already alternated, but thermal drift remains.
The comparison baseline uses cheap UV terrain, the procedural sky, no scene glow,
no sun shadows, no cockpit glass, and both cockpit UV materials and baked cabin
lighting off; other pre-existing scene/HUD choices remain as configured. The
normal BAKED SKY + CABIN menu choice is restored after the run.
Always start from the same settings and a fresh mission.

## Fixtures and comparisons

The harness chooses deterministic actor ordering and records the actual target
identities and poses. Missing enemy/building fixtures are explicitly omitted.

| Fixture | Workload |
| --- | --- |
| Terrain sweep | Deployment view; six-second sinusoidal ±3 m translation and ±0.2 rad yaw |
| Enemy front | Fixed view 65 m in front of the first surviving enemy by node name |
| Building sweep | Chemical plant preferred, otherwise a named-order damageable large actor; same sweep |
| Missile salvo | Three six-missile salvos at 0, 2 and 4 seconds; normal missile visuals/lights/smoke with fixed particle seeds; no damage callbacks |

Each fixture runs baseline, three combined profiles, then baseline again
(20 trials across four fixtures; 180 seconds of warm-up and sampling).
Every trial warms up for three seconds and measures six seconds. Rock cells
are populated before timing. Shader swaps, pool construction, cached-sky
capture, log output and disk writes occur outside the sample window. Schema 5
identifies the sky mode as `skyCache=hdr-2048x1024-separate-sun-v1` and the
cockpit mode contract as `cockpitMode=quest-uv-baked-interior-v1`; capture time is
excluded from the measured trial. There are no screenshots.
Normal frame-to-frame particle simulation and rendering still occur inside it.
Procedural sky animation also keeps running in the baseline; its cloud phase is
reset before each measured trial. The cached-sky case disables that animation.

| Variant | Difference from baseline |
| --- | --- |
| `baked-profile` | Cached HDR sky with separate procedural sun, plus UV cockpit materials and offline baked cabin lighting |
| `baked-profile-unlit-terrain` | The same combined profile, plus unshaded UV terrain using the existing colour textures |
| `baked-profile-glass` | The same combined profile as `baked-profile`, with cockpit glass enabled |

The separate HUD, rocks, cockpit visibility, triplanar, sky-only and UV-only
ablations have been retired. These profiles measure the combined change directly;
do not add the savings from the older individual comparisons. The unlit terrain
profile remains a diagnostic of terrain shading cost, not a production quality
recommendation.

These modes are temporary. Original shaders/visibility, graphics state and pilot
pose are restored when the run ends or is cancelled. Preferences are not saved.
The mission remains paused at the result menu. No automatic APK upload or Git
commit happens.

The separate [live-combat test](QUEST_COMBAT_BENCHMARK.md) measures active AI,
physics, and weapon effects over three fresh 15-second missions. It restarts the
mission by design and provides a different workload from this rendering ablation.

## Results

Logcat is the primary report. Its tagged records contain:

- `QUEST_BENCHMARK_RUN:` device/build/settings and fixture metadata as JSON.
- `QUEST_BENCHMARK_CSV:` the summary header and one row per completed trial.
- `QUEST_BENCHMARK_TRIAL:` trial statistics and start/end poses as JSON.
- `QUEST_BENCHMARK_SKIP`: unsupported comparisons and their reasons.
- `QUEST_BENCHMARK_STATUS:` `complete`, `cancelled` or `failed`.

Summary/metadata logs are emitted between trials, not per frame during measurement.
The analysis script handles multiple runs in one log separately. Start capture
before the test: an old logcat ring buffer may not retain an entire run.

Local backups remain in `user://benchmarks/<UTC timestamp>-<id>/`:

- `run.json`: device, engine/build, renderer, refresh rate, render size/scale,
  AA, timings, fixture identities/poses and scope limitations.
- `summary.csv`: mean/median/p95/p99 frame time, average app FPS, 1% low,
  percent over refresh budget, renderer CPU/GPU time, draw calls/primitives,
  and head movement.
- `frames.csv`: raw wall-clock frame intervals and per-frame monitors.
- Numbered trial JSON: statistics and start/end poses.
- `status.txt`: `complete`, `cancelled` or `failed`. Partial completed trials
  remain useful; do not treat an unfinished fixture as a full comparison.
- `skipped.txt`, if present: unsupported variants (such as a failed panorama
  bake) and their reasons. They have no fabricated measurements in the CSV.

Quality is assessed in the headset; logs cannot establish visual quality or
stereo comfort. There are no screenshots or automated quality scores.
Raw per-frame data remains in the local backup; logcat carries the aggregate
statistics and metadata needed for routine analysis. `QUEST_BENCHMARK_OUTPUT:`
identifies the local backup path. There is no ZIP export menu.

## Interpret carefully

The script reports frame-time/GPU-time reductions relative to the first and last
baseline and flags baseline drift and head motion. Positive reductions suggest a
feature is worth investigating; they are not additive, causal proof of a single
bottleneck, or a guaranteed improvement in a real battle. Inspect p95/p99 as well
as means. The 1% low is 1000 divided by the mean of the slowest ceil(N/100) frames;
p95/p99 use nearest-rank percentiles.
When both profiles completed for a fixture, the script also prints glass-on minus
`baked-profile` app-frame and renderer-GPU time in milliseconds and percent, so
glass cost can be read directly without inferring it from the general baseline comparison.

Frame intervals use a monotonic wall clock rather than Godot's smoothed FPS
counter. They measure **app pacing**, not compositor-presented/reprojected FPS.
The strict over-budget percentage includes ordinary refresh-boundary jitter; it
is not a count of dropped or reprojected frames.
At the headset FPS cap, reductions may only show in renderer GPU/CPU timing.
Unavailable renderer timings are blank in the summary (raw zeros), not proof
that rendering costs nothing. GPU timing is the main viewport and may exclude
HUD/offscreen work; use it alongside global draw/primitive counts and frame time.
For deeper validation, compare with a headset performance overlay/profiler.

The Quest graphics menu exposes **BAKED SKY + CABIN** as one combined option.
The offline interior lightmap excludes sunlight, so the cockpit still receives
dynamic sun lighting. The 1 October headset notes found both the replacement sky
and baked cockpit acceptable; the combined profile still needs measurement.
Quest instrument canvases redraw on state changes (at most 30 Hz); targeting
stays full-rate. Trial JSON includes `hudInstrumentDraws`, `bakedSky`,
`cockpitUv`, `bakedInteriorLighting`, and `cockpitGlass` to verify effective state.
