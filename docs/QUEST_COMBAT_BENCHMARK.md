# Quest live-combat benchmark

The first complete headset run and its tagged telemetry are recorded in
[QUEST_TESTS_2026-10-01.md](QUEST_TESTS_2026-10-01.md).

The live-combat test measures frame pacing while the mission is actively running
AI, physics, weapons, and damage. Unlike the rendering test, it does not replay a
fixed scene or guarantee the same combat sequence each time. The focused
effects trials compare absent enemy trails plus smaller player smoke and smaller projectile
lights, plus the old detailed building-smoke material versus the new simple default,
while keeping global smoke and weapon lights enabled. Actual fighting can
vary, so results do not identify one unique bottleneck by themselves.

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
4. The suite runs five fresh combat trials measured for 15 seconds each, then a
   fresh mission that restores the captured settings and resumes normal play
   after a complete run. Failed or cancelled runs leave the result menu paused.
   Expect 75 seconds plus mission loading, sky setup, and reporting overhead.
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
settings are held constant. The five trials are:

| Trial | Variant | Change |
| --- | --- | --- |
| 1 | `baseline` | Combined baked profile; smoke and weapon lights ON |
| 2 | `terrain-chunked` | Split terrain rendering into 256 m sections; identical triangles and collision |
| 3 | `terrain-vertex-lit` | Terrain vertex lighting with specular disabled; textures and light response retained |
| 4 | `terrain-combined` | Both terrain changes together |
| 5 | `baseline` | Repeat the initial baseline to check drift |

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

Schema 9 uses `focused-terrain-v1`. All stages use cheap UV terrain, normal missile
smoke and projectile light sizes, and simple building smoke. The old effect phases
were removed after no convincing device gain. The suite still has five 15-second
trials. Chunking and lighting are benchmark-only changes shared with the laptop
probe; they are restored on completion, cancellation or failure and never saved
as player preferences. Chunk construction happens before warm-up and measurement.
Logs include chunk size/count, source and chunk triangle totals, effective lighting
and triplanar state. Physics geometry is unchanged. The 256 m size is the measured
experimental candidate, not a tuned production default; occlusion culling is not
enabled by this phase. Lighting combines vertex interpolation and disabled specular,
so this suite does not isolate those two changes from each other.

The baseline trials bracket the candidates and let the analyzer check drift.
The 7 October laptop integration run completed all five schema-9 trials and resumed
unpaused play. Both chunked trials conserved all 197,266 terrain triangles; the
analyzer accepted all three comparisons. Debug/Release builds and 21 analyzer
tests passed. This validates the suite, not Quest performance: the native preview
uses desktop resolution/AA overrides unless explicitly configured like the separate
laptop GPU probe. See [the integration analysis](data/laptop-combat-terrain-2026-10-07-analysis.txt).

Historical schema-8 `focused-effects-v2`, `smoke-off` and `weapon-lights-off` logs
remain supported. The separate rendering test compares
the same combined baked profile, its glass variant, and a version with unlit
terrain, bracketed by baselines: 20 trials, or 180 seconds of warm-up and
sampling, plus setup and reporting. Combat retains two bracketing baseline trials
to detect drift. Each combat trial and
summary records the effective baked sky and cockpit flags; the final mission
restores the user's original individual options, including after cancellation.

## Logs and interpretation

The runner emits:

- `QUEST_COMBAT_RUN:` device/build/settings and workload metadata as JSON,
  including the enemy count in the mission. Schema 8 identifies the
  `focused-effects-v2` trial policy. Schema 5 adds scoped direct-weapon
  profiling. `options` records the user's pre-run choices for restoration;
  `baselineSettings` explicitly records baked lighting, smoke/dust, and weapon
  lights as ON. A fresh Quest graphics profile also defaults baked lighting and
  smoke/dust to ON; the runner pins both ON for each measured trial.
- `QUEST_COMBAT_TRIAL:` variant, target, starting pose, refresh rate, actual
  global smoke/weapon-light state, enemy missile-smoke state, missile sprite scale, projectile
  light scale, and scene settings.
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

Combat logs also sample Godot's total draw-call and primitive counters each frame.
Summaries and per-second records contain `drawCalls` / `primitives` averages and
`maxDrawCalls` / `maxPrimitives` peaks; spike and event records retain sampled counts.
These counters cover **all viewports**, including HUD rendering, and describe the
last rendered frame available at sampling time. They are not per-eye counts or
an exact same-frame attribution to a weapon event. Main-viewport GPU timings have
a different scope. The analyzer reports these counts and variant deltas; older
logs without them show `n/a`. This is an additive schema-7 extension and does not
add stages or extend the benchmark duration.

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
was disabled or its state was not recorded. New focused-effects comparisons require
matching schema/policy metadata, global smoke and lights enabled, default effect scales on
both baselines, and only the declared candidate scale change. Mismatched effective
graphics profiles also suppress comparisons.

Schema 8 uses `focused-effects-v2`. Persistent sooty building smoke defaults to
`simple-unshaded` on Quest: the existing smoke atlas and per-particle colour/opacity
are retained, with one texture sample and no fragment lighting or normal maps.
Particle count, size, lifetime and simulation are unchanged. Steam/vapor, fire,
dust, missile trails and explosion/lingering smoke retain their existing materials.
Desktop retains the detailed material.

The historical schema-8 `building-smoke-detailed` phase temporarily restored `detailed-lit` on existing
building-smoke emitters without restarting them. All other phases use the simple
default, and restoration also occurs on cancellation/failure. This comparison is
old detailed versus new simple: a positive frame-time delta means the old material
is slower. This phase is no longer part of the current suite.
Trial/summary logs record `buildingSmokeMaterial` and `buildingSmokeEmitters`.
The analyzer requires matching positive emitter counts for this comparison.
Counts describe instantiated emitters, not guaranteed on-screen coverage: keep the
smoking building in view and treat live-combat visibility changes as a limitation.
Schema-7 historical comparisons remain supported separately.

The native Debug material check runs without original game data:

```text
godot --path MechRewired --rendering-method mobile --xr-mode off res://BuildingSmokeMaterialCheck.tscn -- --vr-preview
```

Omit `--vr-preview` to check desktop preservation. It checks platform defaults,
existing/new emitter swaps, restoration, retained particle simulation state,
and unaffected vapor/explosion materials. These checks establish implementation
correctness; real Quest appearance and performance still need testing.

On 6 October, a desktop Mobile-renderer VR-preview integration run completed all
five 15-second phases and resumed normal unpaused play. Every phase recorded two
building-smoke emitters; phase 4 recorded `detailed-lit` and the others
`simple-unshaded`. The analyzer accepted the isolated comparison. These desktop
timings are not evidence of a Quest performance gain.

Quest HUD instruments reuse their canvas commands while instrument state stays
unchanged, with a 30 Hz ceiling on updates during movement/combat. Reticles and
projected navigation remain full-rate. Weapon-column layout and damage-section
lists are cached. `hudInstrumentDraws` in combat summaries and rendering-trial
JSON records measures actual instrument canvas rebuilds. The dynamic layer still
requires the HUD viewport to render, so this is chiefly a CPU-side optimization.

## Missile terrain collision timing (schema 6)

`missileTerrainQueryCalls` and `missileTerrainQueryMs` record the number and total
CPU time of missile terrain queries. The analyzer also reports milliseconds per
query. Summaries, per-second records, and spike/event `Combat` snapshots include
these counters. Sampling is active only during the benchmark and adds no managed
objects; measured time includes query endpoint updates, the native physics call,
returned-hit decoding, and result disposal. Query parameters are now preallocated
per pooled missile. It is part of existing process time,
so do not add it to process time when calculating a frame total.

Missiles query Godot's static concave terrain collision shape on the terrain
physics layer using their current swept movement segment. They do not scan the
CPU scene-triangle list. Target impacts separately compare the live target point
with the swept segment using the missile's impact radius. This does not test all
other mechs or moving scenery against their polygon geometry.

A single predicted terrain hit could be reused while a powered, unguided missile
continues in one straight direction. Guided missiles change direction as their
target moves, and missiles fall under gravity after exhausting powered range;
either change invalidates a straight-ray prediction. The static terrain shape
already stays in the physics world between queries. These timings establish
whether its repeated queries merit a trajectory cache or reusable query objects
before changing collision behavior.

The smoke candidate removes enemy trails and reduces player smoke sprite width/height
by 25% (0.82–1.28 m becomes 0.615–0.96 m). It measures that combination, not each
change separately. The light candidate halves missile light range from 6 m to 3 m
and laser light range from 8 m to 4 m, without changing energy or cadence.
Impact lights stay unchanged in every trial, preserving hit illumination.
Building smoke changes only in its dedicated detailed-material phase. The missile
smoke/light candidates remain benchmark-only; simple building smoke is now the
production Quest default. Appearance still needs a headset check. Smoke and weapon
lights remain enabled in normal play.

Checking only objects that moved is not safe: a traveling projectile can reach a
stationary mech between checks. A spatial candidate cache must account for the
projectile’s swept path as well as object motion.

The 4 October follow-up implements pooled query parameters and immediate result
disposal, together with shared target sampling/velocity per salvo per frame.
Individual steering and collisions remain independent. The run header exposes
`missileQueryPolicy` and `missileGuidancePolicy`; compare these before attributing
changes across builds. No additional user-run trials are required by the new
instrumentation.

## Sparse Quest missile effects

Quest player and enemy missiles now select smoke once every three launches and
a dynamic light once every eight launches. Each mech retains its own cadence
across salvos, based on actual launch order rather than pool slot index. The
first launch carries both, followed by the continuous 24-launch repeating cycle.
Smoke carriers retain their existing 144-particle trails; other missiles emit no
trail. Non-smoke missiles release their pool slots immediately after impact
instead of processing an invisible smoke fade. Desktop gameplay defaults remain
full smoke with the existing pool-slot light selection.

Combat run metadata records `missileVisualPolicy=quest-per-mech-launch-smoke3-light8-v1`,
and each summary records `missileSmokeStride=3` and `missileLightStride=8`. Thus
`missileSmoke=true` and `weaponLights=true` now mean the sparse production effects
are enabled, not that every missile emits smoke/light. Global smoke-off and
lights-off variants still disable the respective effects, including other
battlefield smoke and direct-weapon lights as before. There are no new trials.
Rendering benchmark missile fixtures use the same cadence and record the policy.

Native validation confirms 24 pooled launches create eight emitting trails and
three visible lights, including slot reuse, global ablations/restoration and
smoke-tail expiry. Quest appearance and frame-time gain await the next headset
run. No automatic installation was performed.

## Managed allocation cleanup — 5 October 2026

Gait updates reuse registered part lists. Objective highlighting reuses the mission's
distinct actor roots and selects the nearest eligible actor in one pass, preserving
distance/object-ID ties and the existing-current-target rule. Eligibility, transforms
and destruction state remain live. Actor resource names are cached, and bounds use
indexed child traversal instead of allocating child collections. Missile selection
scans the current fire group directly; lock probe points use stack storage.

After a Debug build and asset import, run the focused native check with a graphical
Mobile renderer and the original game data available:

```sh
godot --path MechRewired --rendering-method mobile --xr-mode off res://HotPathAllocationCheck.tscn -- --vr-preview
```

It compares missile selection and objective highlighting against the former queries,
checks animated, detached, freed and newly registered rig parts, then measures 1,000
warmed calls per path. Local results were zero managed bytes for gait, missile
selection and objective searching with no nearby target. The check completed despite
the existing lens-flare compositor startup shader errors in this local import.
This is focused desktop allocation evidence, not a new Quest combat allocation total
or an FPS improvement. HUD, physics-result wrappers and benchmark overhead remain
outside this cleanup. No additional headset benchmark stages were added.
