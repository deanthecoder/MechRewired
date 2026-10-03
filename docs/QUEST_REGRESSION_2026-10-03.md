# Quest combat capture and control regression — 3 October 2026

The headset ran Release commit `97f0ae9`. Combat run
`20261003-164453-dd9075` completed both baseline trials and resumed the mission
with `paused=False`. The user reported that automatic torso turning moved the
camera outside the cockpit and lost the reticle. At initial startup, movement
and firing were unavailable while the combat profiler remained accessible.

## Measured state and results

Quest 3, Godot 4.7.1 Mobile Vulkan, 72 Hz. Baked sky, UV cockpit materials,
baked cabin lighting, cheap lit UV terrain, smoke/dust and weapon lights were
on. Glass, sun shadows, scene glow and HUD glow were off; all HUD sections were
on. Each trial lasted 15 seconds. These are app pacing and main viewport GPU
timings, not compositor FPS.

| Trial | Mean frame | GPU | p95 | p99 | App FPS | First impact frame |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Baseline 1 | 20.36 ms | 17.09 ms | 28.86 ms | 92.90 ms | 49.13 | 169.85 ms |
| Baseline 2 | 21.40 ms | 18.32 ms | 31.89 ms | 93.46 ms | 46.72 | 153.85 ms |

Each summary prefix records six player direct-shot raycasts. Their total times
were 474.62 ms and 465.81 ms, approximately 78.37 ms per call across the run.
The six direct damage calls per trial took only 0.91 ms and 0.56 ms total.
Timed pool builds were zero. Investigate direct raycasts first for combat
stalls, and reduce GPU cost to meet the 13.89 ms budget. Smoke was on here and
off in the earlier run, but differing builds and device conditions prevent
attributing the difference to smoke alone. Keep the user's accepted smoke-on,
lights-on baseline while profiling.

## Capture limitation

Android truncated 42 of 286 tagged records at 1023 characters, including both
summary JSON records. Completion and resume records survived. The source log is
preserved exactly in [tagged telemetry](data/quest-combat-2026-10-03-smoke-on-partial.log).
The [prefix CSV](data/quest-combat-2026-10-03-smoke-on-prefix.csv) contains only
individually complete scalar fields before truncation, explicitly marked
`record_truncated=True`. It is not a recovered complete summary. Later summary
fields, including visual timings and full event counts, cannot be reconstructed.

Large future records use bounded 900-character/byte JSON chunks, identified by
kind, run, record, index and count. The analyzer accepts only complete valid
chunk sets and remains compatible with old unchunked summaries. Missing or
duplicate chunks cannot produce a summary row. Formatting happens when reporting
results, outside the measured loop.

## Control correction

Automatic torso following counter-rotated the XR origin's basis without adjusting
its translation for the headset's non-zero local tracked position. This swept a
real headset away from the seat. The earlier synthetic pose checks did not expose
it. Automatic following and that origin counter-rotation are now removed.
Manual right-stick torso pitch, head-aim damping, and right-click leg alignment
remain. Any future automatic design must rotate around the actual eye position
and pass realistic translated-pose tests before headset deployment.

Initial seating now waits for two focused, tracked frames, and focus-loss pausing
starts only after a focused seated session. Startup does not release pause while
a menu is open. Compact `QUEST_VR_INPUT` state-change logs identify pause, focus,
head/controller tracking, seat, menu and valid aim gates outside benchmarks.
The exact cause of the user's blocked startup controls was not logged; these
changes require headset confirmation rather than claiming a proven diagnosis.

Synthetic smoke checks cover a translated headset pose (0.35, 1.6, -0.4 metres),
seat stability, visible aim and no automatic torso movement at windshield edges,
as well as startup, manual controls and leg alignment. Desktop preview checks
cannot establish real headset comfort or runtime focus/controller behavior.

## Validation and delivery

All 252 .NET tests and three Python analyzer tests passed. The synthetic rendered
VR smoke check reported `QUEST_VR_SMOKE_PASS`. The corrected private Android
Release export completed with MW2.PRJ bundled (21,893,380 bytes), no manifest
debuggable flag, and the existing signing certificate. Real headset movement,
reticle visibility and startup controls still need confirmation after installation.

## Rerun after the correction

User: “No auto-turning torso now, but better.” The corrected Release `18c3c1c`
completed combat run `20261003-170823-0e1713` and resumed `paused=False`.
The chunk protocol retained both complete summaries with zero malformed records.
Sources: [tagged capture](data/quest-combat-2026-10-03-safe-controls.log),
[reassembled summaries](data/quest-combat-2026-10-03-safe-controls-summary.json),
[analyzer output](data/quest-combat-2026-10-03-safe-controls-analysis.txt).
The smoke-on, lights-on baseline and two 15-second trials are unchanged.

| Trial | Mean frame | GPU | p95 | p99 | App FPS | First impact frame |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Baseline 1 | 20.85 ms | 17.26 ms | 29.64 ms | 101.56 ms | 47.97 | 145.59 ms |
| Baseline 2 | 20.63 ms | 17.44 ms | 29.88 ms | 94.28 ms | 48.48 | 176.28 ms |

Performance is similar to the previous partial run. Direct-shot raycasts cost
488.79 ms / six calls and 484.58 ms / six calls, or 81.47 and 80.76 ms per call.
Damage cost is only 0.67 and 0.58 ms total. Beam/tracer creation costs 106.61
and 64.03 ms total, or 17.77 and 10.67 ms per call. Raycast cost is repeatable
and remains the first investigation target; visual construction is a secondary
lead. Some AI spikes also exceed 80 ms and must be investigated separately.
These timings identify expensive regions, not a proven underlying mechanism.

Both trials have 16 player shots, eight enemy shots, 56 missile launches,
60 impacts and zero timed pool builds. Average GPU time is 17.35 ms, still above
the 13.89 ms budget. CPU stall work alone will not establish smooth 72 Hz combat.
Retain smoke and lights as requested while investigating these costs.

At 18:06:38 and again after benchmark resume at 18:09:40, input diagnostics show
`paused=False focus=Focused head=True left=True right=True seat=True menu=False
aim=True`. This confirms the runtime gates opened; it does not independently
prove movement/fire actions or headset comfort. The user reported improved
camera behaviour. Automatic torso following remains deliberately disabled.
