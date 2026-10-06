# Quest combat: allocation cleanup and Meta metrics, 5 October 2026

Run `20261005-175250-bdd0a3`, Release on Quest 3, Godot 4.7.1 Mobile renderer, 2x MSAA, 72 Hz. Built from `09c788f` plus the local allocation changes: registered leg-part lists, stack-buffer scenery overlap resolution, and direct mech hit-test loops. Changes remain uncommitted at analysis time.

All four 15-second trials completed; four intact summaries, zero malformed records. Baked sky/cockpit enabled, missile smoke cadence 1/3 and lights 1/8. Both baselines had smoke and weapon lights enabled. Each trial fired 16 player and 8 enemy weapons, launched 56 missiles, and recorded 60 impacts. No runtime missile/effect pool builds or effect fallbacks. Previous sparse run had 58 impacts, so fights are not identical.

## Allocation result

Two-baseline averages:

| Metric | 4 October sparse baseline | 5 October allocation cleanup |
|---|---:|---:|
| Managed allocations / trial | 16,247,840 B | 7,985,948 B |
| GC0 / trial | 2.5 | 1.5 |
| GC1 / GC2 / trial | 0.5 / 0.5 | 0.5 / 0.5 |
| Mean frame | 15.13 ms | 15.36 ms |
| p95 frame | 18.40 ms | 18.45 ms |
| p99 frame | 21.60 ms | 22.31 ms |
| Godot viewport GPU | 14.30 ms | 14.45 ms |

Allocations fell 50.9%; frame times did not improve. Individual baseline allocation totals were 4,793,496 and 11,178,400 B, so the average conceals considerable variability. This is a separate-day comparison with only two baseline trials; Meta GPU profiling and metrics recording were newly enabled, which further limits causal timing comparisons. Godot process/physics counters overlap other timings and must not be added to frame/GPU times.

Current game mean remains about 1.47 ms above the 13.89 ms budget for 72 FPS. Meeting the average would not guarantee consistently meeting the deadline.

## Meta recording

Retrieved the app-specific recording `uk.co.deanthecoder.mechrewired#GodotApp-20261005_185245.csv`. Casting and metrics HUD were off; Metrics Recording and live GPU profiling were on. CSV timestamps are elapsed milliseconds, aligned approximately to the filename's local 18:52:45 start. Trial starts in logcat were 18:53:01.028, 18:53:27.332, 18:53:53.568, and 18:54:19.775.

The following uses 14 interior samples per trial, excluding the first second to reduce contamination by preceding mission reload/warmup. Alignment is approximate to one second, not a frame-accurate join.

| Stage | Meta app FPS | GPU utilization | Meta app GPU | Timewarp GPU | GPU clock mean |
|---|---:|---:|---:|---:|---:|
| Baseline 1 | 65.86 | 97.86% | 13.99 ms | 1.10 ms | 548 MHz |
| Smoke off | 69.71 | 95.43% | 12.36 ms | 1.09 ms | 633 MHz |
| Weapon lights off | 70.43 | 94.86% | 12.37 ms | 1.09 ms | 574 MHz |
| Baseline 2 | 67.00 | 97.07% | 13.76 ms | 1.09 ms | 640 MHz |

Meta runtime app FPS and GPU measurements are different scopes/sampling from Godot's main-viewport measurements. Do not equate app FPS with compositor FPS, or add GPU and CPU timings. Clocks changed across trials, so effect-off differences do not isolate effect cost exactly.

Battery temperature was 31–33 C and `power_level_state` was zero in the selected samples: no positive evidence of power throttling, but battery temperature alone does not establish SoC thermal headroom. Eye buffers stayed 1680x1760. Fragment shading accounted for about 96% of the reported shading-time distribution; this is not a claim that 96% of the entire frame is fragment shading. High GPU utilization plus the app GPU timings strongly suggests GPU pressure is now the main limit.

## Decision

Keep the allocation changes: they substantially reduce managed allocation churn. Keep the user's chosen sparse smoke and lights baseline. In this run, global smoke-off averaged 14.52 ms and all weapon lights off 14.32 ms, versus 15.36 ms baseline; these switches include more effects than enemy missiles alone.

Next investigate pixel/render cost. A modest Quest render-scale experiment is a useful next controlled change, with cockpit/HUD readability checked on the headset. Particle material/overdraw and light influence radius are other candidates that could preserve visible effects. More small LINQ cleanups are unlikely to recover the missing GPU budget. No further gameplay or rendering changes were made during this analysis.

## Evidence

- `data/quest-combat-2026-10-05-allocations-tagged.log`: benchmark records, including run settings, all trial data and completion.
- `data/quest-combat-2026-10-05-allocations-summary.json`: four decoded summaries.
- `data/quest-combat-2026-10-05-allocations-analysis.txt`: existing analyzer output.
- `data/quest-combat-2026-10-05-allocations-meta.csv`: original app-specific Meta recording.
- Prior comparison: `QUEST_COMBAT_SPARSE_2026-10-04.md`.
