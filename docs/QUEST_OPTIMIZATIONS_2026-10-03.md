# Quest combat optimization pass — 3 October 2026

Starting point: Release `18c3c1c`, combat run `20261003-170823-0e1713`,
mean app pacing 48.2 FPS / 20.74 ms and GPU 17.35 ms. Direct-shot queries
averaged approximately 81 ms; visual construction averaged 11–18 ms per call.
The full capture and limitations are in [the regression report](QUEST_REGRESSION_2026-10-03.md).

## Implemented for the next headset measurement

- Group immutable terrain triangles into a hierarchy of horizontal XZ bounds.
  Reject groups that the ray cannot cross before exact triangle checks. Moving
  aircraft and authored paths explicitly expose their changing triangle slots;
  those and other scenery remain live queries. Destroyed-object filtering still
  applies. Scene-list length changes cause a full-scan fallback.
- Cache original mech mesh vertices/indices during part registration. A resource
  change invalidates copied geometry. Transform the ray into the current part
  pose, reject its bounds, and retain exact original mesh/body-section hit tests.
  Ray directions remain unnormalized after the transform so world hit distances
  remain correct under nonuniform scale. Singular transforms use world-space tests.
- Prewarm mission-owned laser/tracer pools, 64 slots per effect family in each of
  the player and enemy parent pools. Inactive slots do not process or show lights.
  Meshes are shared; laser slot materials are independently updated for color.
  Retain up to 128 slots per family; overflow preserves visuals through allocation
  and reports timed pool builds/fallbacks. Normal reuse retains pulse, halo, light,
  radius, travel speed and delays.
- Separate direct-shot world and mech query timings in schema 5 telemetry.
  These sub-timings exclude missile-target and target-selection queries.
- Run 15-second baseline, smoke-off, lights-off, baseline combat trials. Baselines
  force smoke/dust and weapon lights on. Smoke-off suppresses global smoke/dust
  and missile smoke; lights-off retains smoke. Record the actual flags and reject
  analyzer comparisons with mismatched states. Restore saved mission settings
  afterward. The second baseline reveals drift; the full suite takes longer than
  the previous two-baseline quick run.
- Require one right-trigger squeeze per firing request on Quest. Fire above 65%;
  rearm only below 45%. Holding or jitter around the firing threshold cannot
  advance through several weapons. Pause/tracking-loss release protection remains.
  This does not change desktop firing or an explicitly selected multiweapon group.

These are implementation changes, not measured Quest savings. They require the
next headset capture, including validation that section damage and effect
appearance remain correct.

## Further options raised by the user

A line/sphere test is a reasonable broad phase. Current mesh bounds serve that
purpose more tightly while preserving exact hits. A whole-mech sphere could be
added later if remaining per-part query overhead warrants it, with bounds that
cover all animated limb and torso poses.

Enemy sensing is already staggered at 0.25-second intervals on Quest. Further
spreading visibility work across frames could smooth peaks, but firing needs a
fresh obstruction check. Do not defer player damage raycasts without explicitly
handling the shot's captured aim, pose and response latency.

Worker threads are a later option for pure geometry on immutable snapshots.
Current queries read mutable scene lists and live node transforms; an asynchronous
solution needs snapshot ownership, invalidation, cancellation on mission reload,
and rules for stale results. First measure the simpler bounds/cache improvements.

After isolating smoke/light costs, try smoke on alternating missiles and/or a
limited number of active missile lights as explicitly labeled variants. Keep
smoke and lights on in the accepted baseline. Alternation must be stable per
salvo/launch and must not silently remove gameplay or targeting feedback.

Automatic torso following remains disabled following the camera regression.

## Validation

263 .NET tests and six Python analyzer tests passed. Native Godot checks reported
`RAY_CACHE_CHECK_PASS` and `WEAPON_EFFECT_POOL_SMOKE_PASS`. Rendered VR smoke
reported `QUEST_VR_SMOKE_PASS`, including held-trigger suppression and partial
release/repress; the cockpit preview retains seated aim and HUD placement.
A synthetic desktop terrain test with 8,194 triangles and 100 vertical rays took
approximately 75–77 ms with a linear scan versus 6–6.5 ms indexed. This is a
microbenchmark, not a predicted Quest saving or a full-combat result.

The private Android Release export passed package/signature checks, excludes the
debuggable manifest flag and includes MW2.PRJ (21,893,380 bytes). Live Quest effect
appearance, collision behavior and performance still require the next run.

## First optimized Quest run

Release `ff445df`, run `20261003-173451-df5b18`: all four stages completed and
the mission resumed `paused=False`. All four JSON summaries reassembled with
zero malformed records. Sources: [tagged telemetry](data/quest-combat-2026-10-03-optimized.log),
[complete summaries](data/quest-combat-2026-10-03-optimized-summary.json),
[analyzer output](data/quest-combat-2026-10-03-optimized-analysis.txt).

| Trial | State | Mean frame | App FPS | GPU | p95 | p99 |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| 1 | Smoke ON, lights ON | 18.93 ms | 52.84 | 17.47 ms | 26.97 ms | 31.56 ms |
| 2 | Smoke OFF, lights ON | 17.92 ms | 55.79 | 17.27 ms | 22.01 ms | 26.61 ms |
| 3 | Smoke ON, lights OFF | 17.77 ms | 56.27 | 16.46 ms | 23.17 ms | 27.14 ms |
| 4 | Smoke ON, lights ON | 19.82 ms | 50.45 | 18.46 ms | 27.37 ms | 33.67 ms |

These are raw observed stages, not established effect savings. Trial 1 logged
5.27 degrees of head movement, above the five-degree validation threshold.
The analyzer therefore correctly withheld variant deltas because only one valid
baseline remained. Both baselines also drifted (18.93 to 19.82 ms frame time;
17.47 to 18.46 ms GPU). Repeat with the head still before selecting smoke or light
reductions. Both remain on in the accepted baseline.

All stages fired 16 player shots and eight enemy shots, launched 56 missiles,
and recorded 61 impacts. Timed missile pool builds, weapon effect pool builds
and weapon effect fallback counts were all zero.

Direct-shot queries now take 1.29–2.53 ms per call, versus approximately 81 ms in
the prior build. World checks total 4.54–6.71 ms and mech checks 3.16–7.47 ms per
six calls. Beam/tracer launch costs 0.13–0.37 ms per call, versus 11–18 ms before.
LOS time totals 12.50–13.29 ms per stage, versus approximately 529 ms before.
These large reductions support the bounds/cache/pooling changes, but do not
separate every implementation's contribution or remove device-state differences.

With smoke and lights on, the final baseline p99 is 33.67 ms versus approximately
94–102 ms in the prior capture. Its first-impact frame is 16.86 ms versus the
previous 145.59–176.28 ms. The first optimized baseline's first-impact frame is
35.55 ms; distinguish this initial warm behavior from the final baseline.
The repeated large firing stalls have reduced substantially in this run.

The final valid baseline still has 18.46 ms GPU time against a 13.89 ms budget.
Even the lights-off stage reports 16.46 ms GPU. Rendering cost is now the main
remaining pacing constraint in these stages; further CPU work alone will not
establish 72 Hz. Repeat the effect comparison, then consider reduced smoke/light
variants and finer GPU workload attribution while retaining the requested visual
baseline. This is one optimized run, not proven repeatability across missions.

After resume, diagnostics show focused head and both controllers tracked, seat
centered, menu closed, valid aim and no pause. Headset validation of firing cadence
and appearance still relies on user feedback rather than these gate logs alone.

## Repeat optimized Quest run

Run `20261003-174229-fb9d98` completed all four 15-second trials and resumed
`YELLSCN1` with `paused=False`. Its four summary records reassembled with zero
malformed summaries. Head movement stayed inside the analyzer limits in every
stage (maximum 0.0053 m and 1.72 degrees). Sources are the [PID 19671 tagged
capture](data/quest-combat-2026-10-03-optimized-still.log), [reassembled
summaries](data/quest-combat-2026-10-03-optimized-still-summary.json), and
[analyzer output](data/quest-combat-2026-10-03-optimized-still-analysis.txt).

| Trial | State | Mean frame | App FPS | GPU | p99 |
| --- | --- | ---: | ---: | ---: | ---: |
| 1 | Smoke ON, lights ON | 18.36 ms | 54.46 | 16.90 ms | 31.59 ms |
| 2 | Smoke OFF, lights ON | 16.50 ms | 60.59 | 15.82 ms | 28.06 ms |
| 3 | Smoke ON, lights OFF | 18.55 ms | 53.91 | 17.17 ms | 28.73 ms |
| 4 | Smoke ON, lights ON | 18.12 ms | 55.19 | 16.81 ms | 30.78 ms |

The two baselines average 18.24 ms, 54.82 app FPS, 16.86 ms GPU, and 31.19 ms
p99. The smoke-off candidate measured 1.74 ms lower mean frame time and 1.04 ms
lower GPU time than that average. The lights-off candidate was slower than the
baseline average at 18.55 ms mean and 17.17 ms GPU, so this capture shows no
reliable benefit from removing weapon lights. There is only one candidate trial
for each effect; repeat the suite before changing the accepted baseline.

Each stage recorded 16 player shots, eight enemy shots, 56 missile launches, and
56 impacts. The preceding run recorded 61 impacts, which reflects live-fight
variation; it is not a missing-impact indication in this capture. Logged states
matched the planned variants. Immediately after resume, input diagnostics showed
`head=True left=False right=True seat=True menu=False aim=True` while focused.
That asymmetric controller state helps explain why the old all-controller
availability gate could block right-hand actions; right-hand action handling is
being corrected separately, with left-stick locomotion still requiring both
controllers.

## Independent controller follow-up

The Quest log shows the asymmetric state both at benchmark startup and after
resume: `head=True left=False right=True`, with the right controller and aim
active after resume. The follow-up rendered VR smoke passed checks for right-hand
fire and weapon cycling while the left controller is absent, single-squeeze
firing in that state, locomotion stopping when either controller is absent, and
trigger release before firing after right-controller reacquisition. The smoke
ended with `QUEST_VR_SMOKE_PASS`; these checks exercise the synthetic preview
controller path, while live tracking behavior still needs headset confirmation.

The PID-filtered repeat capture also retains renderer `free_rid` errors from
`GodotObject.Finalize()` during each trial. The same error exists in local captures
from before the bounds/pooling pass; it is not established as a new pooling
regression. Resource cleanup on the finalizer thread is a separate investigation
lead. The source log retains its original trailing spaces on those error lines;
whitespace diagnostics on that evidence file do not indicate code whitespace.

## Quest lower HUD spacing follow-up

User reported navigation overlapping the first horizontal gauge and the vertical
speed control sitting far from player damage. The Quest lower row now places a
narrower navigation panel before Heat, dH/dT and Jets with explicit clear gaps,
then the original chassis damage silhouette and adjacent speed/throttle bar.
Actual speed remains labeled beneath the damage silhouette. Navigation/enemy/actor
text is measured and shortened with `...` within its own column to prevent long
names extending into gauge labels. Desktop positions retain their existing layout.

The rendered VR smoke passed and its cockpit preview confirms the compact row and
clear spacing. Real headset fit/readability still need confirmation. The Release
package includes this HUD adjustment and the independently tracked right-hand
weapon-control fix. Device installation is held at the user's request.
