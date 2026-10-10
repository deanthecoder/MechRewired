# Quest performance analysis — 3 October 2026

Release build `a95b69f`, Quest 3, Godot 4.7.1 Mobile Vulkan. Captured rendering
run `20261003-134014-a581f2` and combat run `20261003-134617-ed717d` both
completed. Rendering capture retained trials 2–20; its run/header and first
terrain baseline were lost from logcat. The CSV header was recovered from the
matching source, not the device. Combat capture includes all three trials,
run metadata, summaries, event/spike/second records, and mission resume with
`paused=False`.

Sources: [rendering CSV](data/quest-rendering-2026-10-03-partial.csv) and
[tagged telemetry](data/quest-tests-2026-10-03-tagged.log). Private raw per-frame
files were not retrieved from the Release package. These are app pacing and
main-viewport GPU measurements, not compositor FPS. Combat trials report 72 Hz,
giving a 13.89 ms frame budget. Missing rendering metadata prevents independent
verification of every rendering run setting.

## Rendering

Mean app frame time / renderer GPU time, in milliseconds. Baselines average
the two bracketing trials except terrain, where only the final baseline survived.

| Profile | Terrain* | Enemy | Building | Missile salvo |
| --- | ---: | ---: | ---: | ---: |
| Procedural baseline | 14.56 / 14.28 | 16.22 / 15.96 | 16.45 / 16.18 | 16.48 / 15.81 |
| Baked sky + cabin | 13.89 / 11.73 | 13.89 / 11.92 | 13.89 / 12.07 | 13.93 / 11.81 |
| Baked + unlit terrain | 13.89 / 11.24 | 13.89 / 11.70 | 13.89 / 11.62 | 13.91 / 10.96 |
| Baked + cockpit glass | 15.48 / 15.20 | 17.17 / 16.91 | 17.46 / 17.19 | 17.41 / 16.77 |

The combined baked profile reduces mean app frame time by 14.4–15.5% in the
three fixtures with both baselines. Its GPU time is about 11.7–12.1 ms,
leaving roughly 1.8–2.2 ms against the 72 Hz budget in these frozen views.
Its p95 frame intervals are 14.19–14.78 ms and p99 14.36–15.41 ms.
Head movement stayed within analyzer thresholds. This is one run, not established
repeatability, and the combined result cannot separate sky from cabin savings.

Glass adds 3.47–5.12 ms of GPU time compared with the baked profile, pushing
all four fixtures over budget. It adds 1.60–3.57 ms to mean app frame intervals.
Keep it off for Quest performance.

Unlit terrain saves another 0.22–0.84 ms of GPU time against the baked profile;
app pacing is already near its cap. Treat this as a target for a cheaper lit
terrain shader, not a recommendation to remove terrain lighting.

## Combat

Effective measured state: baked sky, cockpit UV materials and baked interior
lighting all **on**; cheap UV terrain; smoke, sun shadows, glow, glass and HUD
glow **off**; all HUD sections **on**. The run header's options are pre-test
preferences; trial and summary state confirms the forced baked profile.

| Trial | Mean frame | Mean GPU | p95 | p99 | Average app FPS |
| --- | ---: | ---: | ---: | ---: | ---: |
| Baseline 1 | 16.84 ms | 13.90 ms | 24.97 ms | 95.68 ms | 59.37 |
| Weapon lights off | 15.98 ms | 12.45 ms | 24.16 ms | 93.27 ms | 62.59 |
| Baseline 2 | 16.28 ms | 13.01 ms | 23.46 ms | 96.63 ms | 61.44 |

Against the mean of both baselines, lights off saves 0.58 ms (3.5%) of mean app
frame time and 1.01 ms (7.5%) of GPU time. Baseline app drift is 3.4%; repeat
the test before treating the small app improvement as stable. All trials had
16 player weapon shots, 8 enemy shots, 56 missile launches and 60 impacts,
70 line-of-sight calls, no excessive head movement and **zero timed pool builds**.

The first impact coincides with a 130.50–155.27 ms frame in every trial,
including lights off. p99 remains 93–97 ms. The worst captured frame has GPU
time 11.09 ms, 7.34 MB of managed allocations and one generation-0 collection;
other large stalls have no logged allocation/collection. Impact/fire correlation
is a lead, not proof of the cause. Inspect impact/damage/VFX paths and frame
timing attribution rather than blaming all stalls on GC or missile pools.
The captured GPU time need not correspond exactly to the same wall-clock frame.
LOS totals are 0.51–0.56 seconds across each 15-second trial; worth profiling,
but these totals do not explain every large stall.

## Recommended Quest profile and next work

- Enable **BAKED SKY + CABIN**. User found the replacement sky/cabin acceptable;
  the sun halo position remains a visual defect to fix.
- Disable cockpit glass and detailed triplanar terrain. Glass has a direct cost
  in this run; triplanar was expensive in the historical September run, not
  retested here.
- Prefer weapon lights off for the performance profile, retaining emissive weapon
  visuals; verify appearance and repeat the comparison.
- Keep smoke, scene glow, sun shadows and HUD glow off while stabilizing combat.
  These were already off, so this run does not measure their incremental cost.
- Keep the cockpit, HUD and rocks. Historical removal tests were diagnostic;
  today's combined profile is substantially faster while retaining them.

First priority is the repeated fire/impact stall: lower average rendering cost
alone will not deliver smooth 72 Hz combat. Add focused CPU profiling around
weapon firing, impact/damage and VFX creation, checking allocations and shader
warm-up. Then repeat combat with lights off and the accepted baked profile.
After that, test smoke on/off from a genuinely smoke-on baseline and compare a
cheaper lit terrain shader. Procedural sky with cloud drift disabled remains
an unmeasured alternative, especially if halo correction proves awkward.

No graphics defaults were changed by this analysis. Earlier runs used different
code/profiles and cannot isolate the cause of today's improvement.

## Accepted baseline after review

The user chose to retain **weapon lights and smoke/dust ON**, with baked sky/cabin
ON, cheap lit UV terrain, and glass/shadows/scene glow/HUD glow OFF. The next
combat capture must use this state; the smoke-off measurements above are historical
and do not validate that new baseline. Combat schema 4 adds timings for player
direct-weapon raycasts, damage/impact handling and beam/tracer construction, while
retaining the lights-off trial as a diagnostic comparison.

HUD placement is corrected to navigation left, then Heat/dH/dT/Jets, then damage
and speed right. The baked halo longitude correction has passed a rendered
Mobile mapping check; Quest confirmation is pending.

## Scoped combat profiling validation

The new schema-4 instrumentation completed three desktop Debug/Mobile preview
trials with smoke and baked lighting ON, baseline weapon lights ON, 96 prewarmed
missile pools, zero timed pool builds, and successful mission resume. Its
[summary records](data/quest-combat-desktop-profile-2026-10-03.log) are **desktop
CPU diagnostics, not Quest performance measurements**; macOS GPU timings were
unavailable. Across the two baselines, 12 player direct-shot raycasts took
750.20 ms (62.52 ms/call), damage handling 1.36 ms (0.11 ms/call), and beam
creation 21.41 ms (1.78 ms/call). This makes raycasting the next investigation
target. The query scans scene triangles and retrieves each enemy part's surface
arrays; cache/index design must account for moving actors and detached sections.

Mech section hit tests now sample each part's transform once per synchronous
query rather than three native property reads per triangle, retaining the same
current world-space triangles and damage-section selection. No frame-time saving
has been established for this small change. An earlier desktop capture had
concurrent preview processes and is excluded from comparisons.

Validation: latest build has zero warnings/errors; 249/249 .NET tests pass;
rendered VR smoke passes and its cockpit image shows the corrected HUD order;
the panorama mapping regression passes. The private Release APK includes the
archive and has a verified matching release signature and no debuggable flag.
The next Quest combat run should validate the accepted effects and report the
new raycast/damage/visual timing fields. `analyze-quest-combat.py` prints both
totals and milliseconds per call for these fields.
