# Quest live-combat benchmark

The first complete headset run and its tagged telemetry are recorded in
[QUEST_TESTS_2026-10-01.md](QUEST_TESTS_2026-10-01.md).

The live-combat test measures frame pacing while the mission is actively running
AI, physics, weapons, and damage. Unlike the rendering test, it does not replay a
fixed scene or guarantee the same combat sequence each time. Use it to compare
smoke and weapon-light changes across repeated runs; actual fighting can vary,
so the result cannot identify one unique bottleneck by itself.

## Run on the headset

1. Build and install the Release APK with `scripts/quest.sh install`.
2. On the Mac, start log capture before selecting the test:

   ```sh
   adb logcat -v raw -s godot:I > quest-combat.log
   ```

   The file is written in the terminal's current directory. The menu action
   intentionally resets mission progress; finish or abandon a mission whose
   progress you want to keep before starting the test.
3. Open the Quest menu, select **BENCHMARKS > COMBAT TEST (RESTARTS)**, and keep
   your head still. Menu cancels the suite; losing headset focus also cancels it.
   This action intentionally restarts the mission, as the menu label says.
4. The suite runs four fresh combat trials measured for 15 seconds each, then a
   fresh mission that restores the captured settings and resumes normal play
   after a complete run. Failed or cancelled runs leave the result menu paused.
   Expect 60 seconds plus mission loading, sky setup, and reporting overhead.
   Stop log capture after `QUEST_COMBAT_STATUS: ... "complete"` appears.
5. Analyze the log on the Mac:

   ```sh
   python3 scripts/analyze-quest-combat.py quest-combat.log
   ```

   You can also give Codex the log's absolute path. The report needs only the
   log; it does not use screenshots. Keep the log locally because the test does
   not export one.

Desktop VR preview can exercise the command-line code path:

```sh
godot --path MechRewired --rendering-method mobile -- --vr-preview --quest-combat-benchmark
```

That does not prove headset behavior or provide Quest performance numbers. Run
at the same headset refresh rate, graphics settings, battery/charging state, and
mission. Repeat the suite to see how much live-fight variation affects results.

## Workload

The suite prefers a surviving mobile, missile-armed, high-health enemy, with
node name breaking ties. On each fresh mission it searches eight directions at
65 m, then 45 m and 90 m, accepting only terrain-supported positions with clear
enemy line of sight. It fails setup instead of timing an obstructed encounter.
The player starts facing the enemy; the chosen target and pose are logged. Enemy AI, physics, and damage remain active.

The `segment-bounds-quest-250ms-v1` targeting policy checks awareness every 0.25
seconds on Quest, with staggered initial sensor phases. Targets outside observation
range skip the scene query. Sight queries reject terrain groups outside the segment's horizontal bounds,
then perform exact triangle checks and stop at the first blocker. Moving scenery
uses current vertices, so aircraft and scripted paths remain correct. A fresh muzzle-to-target check still runs before each shot.
Desktop awareness retains its 0.2-second interval. Compare LOS time per call and
combat p95/p99 with the 1 October logs; headset gains are not yet measured.
The player fires at 0.5 seconds and then every two seconds for the 15-second
measurement. Normal enemy actions and the resulting fight are not deterministic.

All measured trials explicitly enable the combined baked sky, UV cockpit and
baked cabin profile, waiting for sky capture before warm-up. Other captured
settings are held constant. The four trials are:

| Trial | Variant | Change |
| --- | --- | --- |
| 1 | `baseline` | Combined baked profile; smoke and weapon lights ON |
| 2 | `smoke-off` | Smoke/dust and missile smoke OFF; weapon lights ON |
| 3 | `weapon-lights-off` | Smoke/dust and missile smoke ON; weapon lights OFF |
| 4 | `baseline` | Repeat the initial baseline to check drift |

Each starts in a fresh mission, so damage, ammunition, and lazily-created pools
do not carry between trials. The final fresh mission restores the captured
settings. Trial cancellation, player death, or target destruction stops the
suite; its result is logged, and the analyzer excludes that incomplete trial
from variant deltas. A status other than `complete` is not a valid comparison.

Quest now prepares each missile-armed enemy's 24-projectile pool during mission
setup. Non-missile enemies allocate no pool; player missiles were already pooled.
Body/exhaust meshes and materials are shared, while each projectile retains its
own flight, impact and particle state. This shifts allocation to loading and
increases upfront pool memory; per-mech capacities and recycling rules stay the
same. It does not guarantee that every driver pipeline is compiled in advance.

Combat schema 5 records `poolPolicy=quest-prewarmed-per-mech-v1`, player world
and mech raycast timings, and weapon-effect pool builds/fallbacks. The run record
also declares `weaponEffectPoolPolicy=mission64-per-family-limit128-v1`. Trial metadata
reports the total enemy pool count and whether all missile pools are ready.
Measured pool creation should now be zero; a nonzero count identifies a fallback.
Shader and driver caches can remain warm across mission reloads.

Smoke-off and weapon-lights-off trials isolate one effect at a time against
smoke-on/light-on baselines. Both baseline trials bracket the variants and let
the analyzer check drift. Trial metadata and summaries record smoke/dust, missile
smoke, and weapon-light state. Deltas are withheld when either the baselines or
the ablation trial does not report the required isolated states. Historical
logs remain parseable, though older records can lack the fields needed for a
variant comparison. The rendering test compares
the same combined baked profile, its glass variant, and a version with unlit
terrain, bracketed by baselines: 20 trials, or 180 seconds of warm-up and
sampling, plus setup and reporting. Combat retains two bracketing baseline trials
to detect drift. Each combat trial and
summary records the effective baked sky and cockpit flags; the final mission
restores the user's original individual options, including after cancellation.

## Logs and interpretation

The runner emits:

- `QUEST_COMBAT_RUN:` device/build/settings and workload metadata as JSON,
  including the enemy count in the mission. Schema 5 adds scoped direct-weapon
  profiling. `options` records the user's pre-run choices for restoration;
  `baselineSettings` explicitly records baked lighting, smoke/dust, and weapon
  lights as ON. A fresh Quest graphics profile also defaults baked lighting and
  smoke/dust to ON; the runner pins both ON for each measured trial.
- `QUEST_COMBAT_TRIAL:` variant, target, starting pose, refresh rate, actual
  smoke and weapon-light state, and scene settings.
- `QUEST_COMBAT_SUMMARY:` one JSON record per trial with frame-time percentiles,
  app FPS, main-viewport renderer CPU/GPU timing, process/physics timings,
  allocations and process-wide GC counts, AI/line-of-sight totals, pool creation
  totals, player direct-raycast, damage/impact, and beam/tracer construction
  call counts and elapsed milliseconds, player and enemy shots,
  missiles, impacts, and maximum head movement.
- `QUEST_COMBAT_SPIKE:`, `QUEST_COMBAT_SECOND:`, `QUEST_COMBAT_EVENTS:` and
  `QUEST_COMBAT_EVENT:` bounded worst-frame, per-second, first-volley, and event
  detail records, emitted after each trial's measurement window.
- `QUEST_COMBAT_STATUS:` final suite status.

The three scoped timings cover player direct-weapon raycasts, hit damage and
impact handling, and beam/tracer construction. Their call counts and elapsed
milliseconds also appear in each spike/event record's `Combat` snapshot. They
are sampled only while combat telemetry is active.

Enemy AI time includes its line-of-sight work, so those two values are nested and
must not be added together. `weaponShots` aggregates successful player and enemy
weapon launches; `enemyShots` isolates the enemy launches. The analyzer warns
when the enemy fired zero shots, since the enemy did not engage and the intended
combat workload is unconfirmed. GC counts are process-wide and include the
benchmark recorder's own allocations. GPU timing covers the main viewport and
may exclude compositor work. The analyzer compares each variant with the average
of at least two complete baseline trials for the same target, and warns about
baseline drift, head movement above 0.05 m or 5 degrees, missing shots or impacts,
and incomplete trials. Missing shots or impacts can indicate that the live
encounter did not provide the expected workload.

The summary also reports first-volley mean and worst-frame times over the first
one-second window beginning with the first recorded shot, the mean for subsequent
volley frames, and the first-impact frame time. These windows follow different
points in an active fight, so compare their results across trials cautiously; the
first window does not create a cold shader or driver cache.

Frame intervals describe app pacing, not compositor-presented or reprojected
FPS. Compare p95/p99 and FPS alongside the measured subsystems; differences can
reflect changes in live fighting as well as rendering. The test produces logs,
not screenshots or automated visual-quality evidence.

## Comparison validity and production changes

Historical weapon lights-off stages removed dynamic illumination. They did **not** bake
replacement lighting; emissive weapon appearances remain, and cockpit/ambient/sun
lights are outside the weapon-light test. The rendering test's offline baked
cabin lighting is part of the combined profile and excludes sun lighting. The
1 October headset notes found it visually acceptable; combined savings still
need measurement and must not be inferred by adding individual savings.

The analyzer prints per-trial timings even for incomplete runs. It withholds
percentage comparisons unless both bracketing baselines and the candidate have
actual enemy fire and impacts, valid frame counts, and head movement within
0.05 m / 5 degrees. It also withholds comparisons for baseline drift above 10%
or workload differences in enemy shots, missile launches or impacts exceeding
the larger of two events and 25% of the baseline count. Duplicate summary lines
do not count as additional baselines. These gates reduce misleading comparisons;
they do not turn live combat into a deterministic replay. Historical smoke-off
records are still accepted, but their deltas are withheld when baseline smoke
was disabled or its state was not recorded. New suites do not expect a smoke-off
trial. Mismatched effective graphics profiles also suppress comparisons.

Quest HUD instruments reuse their canvas commands while instrument state stays
unchanged, with a 30 Hz ceiling on updates during movement/combat. Reticles and
projected navigation remain full-rate. Weapon-column layout and damage-section
lists are cached. `hudInstrumentDraws` in combat summaries and rendering-trial
JSON records measures actual instrument canvas rebuilds. The dynamic layer still
requires the HUD viewport to render, so this is chiefly a CPU-side optimization.
