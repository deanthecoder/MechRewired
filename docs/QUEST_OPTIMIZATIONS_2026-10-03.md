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
