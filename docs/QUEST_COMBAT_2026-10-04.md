# Quest combat effect comparison — 4 October 2026

Run `20261004-182729-1d1caa` completed all four 15-second stages against
`Enemy-Falcon Nova-1` and ended with `status=complete`. The 1,260-line tagged
capture reassembled into four summaries with zero malformed records. The source
log, reassembled JSON and analyzer output are preserved as [tagged telemetry](data/quest-combat-2026-10-04-repaired.log),
[summary records](data/quest-combat-2026-10-04-repaired-summary.json), and
[analysis](data/quest-combat-2026-10-04-repaired-analysis.txt).

All stages recorded 16 player shots, eight enemy shots, 56 missile launches,
and 58 impacts. Each passed the analyzer's combat and head-stability checks;
maximum head movement was 0.0044 m and 1.26 degrees. Graphics settings matched
across stages (`baked-profile`, baked sky, cockpit UV and baked interior
lighting enabled). Missile-pool builds, weapon-effect-pool builds, and pool
fallbacks were all zero.

| Trial | State | Mean frame | App FPS | GPU | p99 |
| --- | --- | ---: | ---: | ---: | ---: |
| 1 | Smoke ON, lights ON | 16.13 ms | 62.01 | 14.95 ms | 28.58 ms |
| 2 | Smoke OFF, lights ON | 15.49 ms | 64.54 | 14.93 ms | 22.57 ms |
| 3 | Smoke ON, lights OFF | 15.15 ms | 65.99 | 14.05 ms | 24.12 ms |
| 4 | Smoke ON, lights ON | 15.84 ms | 63.12 | 14.66 ms | 25.15 ms |

The two baselines average 15.99 ms mean frame, 62.56 app FPS, 14.81 ms GPU,
and 26.87 ms p99. The smoke-off stage was 0.49 ms faster on mean frame time
(-3.1%) while GPU time was effectively unchanged at 14.93 ms (+0.8%). The
lights-off stage was 0.83 ms faster on mean frame time (-5.2%) and 0.75 ms
faster on GPU time (-5.1%). These are single candidate samples, so neither
effect has a repeatable measured saving established yet; keep the baseline
visuals until repeated captures confirm the differences.

These FPS values are application telemetry, not compositor FPS. The report
describes one live Quest combat run; frame pacing, effects and the fight can
vary between trials and sessions. No direct comparison with the 3 October run
is used here because it is a different capture and the purpose of this record
is to preserve this run's controlled within-run effect comparison.

## Follow-up changes

Right-stick click now captures the damped reticle's horizontal heading and turns
both chassis and torso toward it. The torso finishes centered over the legs;
pitch is retained, native headset pose is unchanged, and manual steering still
cancels the maneuver.

The next benchmark uses schema 6 and measures missile terrain-query count and
CPU time separately. This captured schema 5 run cannot establish that cost.
Collision behavior and the smoke/lights-on baseline are unchanged. Enemy-only
smoke reduction remains a candidate to measure, retaining player rocket trails.

## Salvo guidance idea

Share target-position sampling, target velocity estimation, and lead prediction
per salvo/target, then steer each missile from its own position. Followers should
retain individual swept collision checks and independent impacts: launcher
offsets and obstacle edges make a leader's terrain result unsafe to reuse.
This is a proposed optimization to profile, not implemented behavior.

## Implemented missile optimizations after this capture

The next Release reuses one terrain query-parameter resource per pooled missile,
caches the borrowed physics space state until a world transition, and disposes
returned hit dictionaries immediately. Per-segment collisions are retained; no
launch-time endpoint cache is used. This removes query-resource creation from
every flight frame.

Player and enemy salvos now share one target callback and filtered target-velocity
estimate per process frame, plus one damage callback per salvo. Each missile
still computes its own distance-dependent lead and steering, observes its own
arming/range limits, and checks its own swept segment and target impact. A first
missile ending or being reused cannot stop the remaining followers. The velocity
filter history is now shared across staggered launches, so later missiles inherit
the salvo's existing estimate instead of restarting it at zero.

The local Godot native test passes terrain impacts, independent launcher offsets,
pooled endpoint reset, physics world transitions, shared sampling, slot reuse,
and ballistic fall. Its final 1,000-query allocation comparison measured 891,152
managed bytes with fresh query resources versus 96,000 bytes with reused queries.
Other local passes measured 640,000 fresh bytes; native wrapper/registry overhead
varies, but reuse consistently allocated less (about 85–89%). Both paths used the
same endpoints and disposed result dictionaries. This is a local managed-allocation
comparison, not a measured Quest frame-time gain. See the
[local validation log](data/quest-missile-optimization-2026-10-04-local.log).

Run metadata records `missileQueryPolicy=pooled-parameters-disposed-results-v1`
and `missileGuidancePolicy=salvo-target-velocity-per-frame-v1` so the next Quest
run can be distinguished from the capture above. Smoke and weapon lights remain
on. No additional benchmark stages were added.

A cheap target-alive check invalidates same-frame target destruction even after
the position was sampled; gaps between armed followers reset velocity history
rather than dividing multi-frame displacement by a single-frame delta. Both
edge cases pass the native check. Debug build completed with zero warnings and
errors; 263 .NET tests and 13 Python analyzer/APK-validation tests passed.

The Release package is built locally for `./scripts/quest.sh install`; this
optimization build was not installed by Codex. Quest frame-time impact remains
to be measured in the next normal four-stage combat run.
