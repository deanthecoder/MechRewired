# Quest benchmark and visual check — 1 October 2026

This is the handoff record for later analysis, not a new graphics default. The
tests ran on branch `codex/quest-performance-benchmark` at `eb77d8d` (Godot
4.7.1 .NET Release, version `0.1.0-alpha2`) on a Quest 3. The APK was locally
signed with the certificate matching the installed test app and updated it in
place. This was **not** a Meta Alpha upload. Startup confirmed OpenXR, Mobile
Vulkan on Adreno 740, private `MW2.PRJ` SHA-256 validation and 7,735 indexed
resources; mission `YELLSCN1` deployed. The private test APK also contained the
local archive as a fallback, but the existing private copy was used. No game
data is committed here.

The captured [tagged telemetry](data/quest-tests-2026-10-01-tagged.log) keeps
the rendering trial records, sky-cache diagnostics, and the complete combat
run, including per-second, spike, and event records. The
[rendering CSV](data/quest-rendering-2026-10-01-partial.csv) adds the schema-4
header from `QuestPerformanceBenchmark.cs` to the **39 captured rows** for use
with `scripts/analyze-quest-benchmark.py`. The raw Mac log stays ignored at
`local/benchmarks/quest-benchmarks-20261001.log`.

## Rendering test

The headset reported `QUEST_BENCHMARK_STATUS: complete`: four fixtures, ten
trials each, three seconds of warm-up and six seconds of measurement per trial.
Wi-Fi log capture began just after the run started, so **trial 1 and the run/CSV
headers were missed**. Trials 2–40, including both baselines for enemy-front,
building-sweep and missile-salvo, are present. Terrain-sweep has only its final
baseline; do not use the analyzer's terrain percentage deltas as paired-baseline
comparisons. The app's private `summary.csv`/`frames.csv` were not retrievable
from this non-debuggable Release package. No recorded trial exceeded the
analyzer's 0.05 m / 5° head-movement thresholds.

The table gives **mean app frame time in ms**. For the three complete fixtures,
the percentage is the reduction versus the average of their two baselines;
positive is faster. Terrain values are shown without a percentage because its
first baseline was missed. These are frozen rendering views, not live combat or
compositor-presented FPS.

| Variant | Terrain sweep* | Enemy front | Building sweep | Missile salvo |
| --- | ---: | ---: | ---: | ---: |
| Baseline | 18.00* | 18.12 | 18.95 | 19.43 |
| Detailed triplanar terrain | 34.45* | 36.62 (102.0% slower) | 36.03 (90.2% slower) | 36.22 (86.5% slower) |
| Unlit terrain textures | 14.40* | 14.36 (20.8% faster) | 15.24 (19.5% faster) | 15.27 (21.4% faster) |
| Cached sky panorama | 16.57* | 16.37 (9.7% faster) | 17.23 (9.1% faster) | 17.89 (7.9% faster) |
| HUD hidden | 17.68* | 17.90 (1.2% faster) | 18.52 (2.2% faster) | 19.12 (1.6% faster) |
| Cockpit hidden | 13.89* | 13.89 (23.4% faster) | 14.43 (23.9% faster) | 15.11 (22.2% faster) |
| Quest UV cockpit, no baked interior | 20.57* | 20.37 (12.4% slower) | 21.03 (11.0% slower) | 21.91 (12.8% slower) |
| Quest UV cockpit, baked interior | 16.75* | 16.60 (8.4% faster) | 17.40 (8.2% faster) | 18.19 (6.3% faster) |
| Scattered rocks hidden | 18.32* | 17.71 (2.3% faster) | 18.34 (3.2% faster) | 19.54 (0.6% slower) |

`*` Terrain-sweep numbers are individual recorded trials; its baseline is
trial 10 only. The new cached-sky diagnostics recorded a successful 2048×1024
RGBA float capture from `Size1024` source radiance and restoration to the
original `Size256` radiance setting. Capture was outside measured trial time.
The corresponding mean main-viewport GPU times are in the CSV; e.g. the
enemy-front baseline was 17.86 ms, cached sky 16.11 ms, and UV cockpit with
baked interior 16.34 ms. The analyzer can recompute the full matrix.

**Headset appearance, reported by the user:** the replacement baked sky looked
okay. The missing bloom/glow around the sun was noticeable but not a deal
breaker. Baking that halo into the sky is an idea to investigate, not a tested
change; preserve sun position and behaviour while checking it in stereo. The
Quest UV cockpit with baked interior lighting also looked okay. No matched
screenshots or objective image-quality measurements were captured. This
supersedes the old panorama's low-resolution/wrong-sun/white-mountain visual
feedback for the **replacement**, but does not erase that historical result.

## Live-combat test

`QUEST_COMBAT_STATUS` was `complete` for run `20261001-190404-95d826`:
four fresh 15-second missions against `Enemy-Falcon Nova-1`, with both
baselines, weapon-lights-off, and smoke-off trials captured. The Quest ran at
72 Hz (13.89 ms budget). All four trials had enemy fire and impacts, no
head-movement or workload warnings from `scripts/analyze-quest-combat.py`,
96 missile pools ready before timing, and **zero pool builds during timing**.
The two baseline mean frame times were 25.20 and 23.21 ms (7.9% drift).

| Trial | Mean app frame | p95 | p99 | Mean GPU | Average app FPS |
| --- | ---: | ---: | ---: | ---: | ---: |
| Baseline 1 | 25.20 ms | 42.07 ms | 105.77 ms | 21.36 ms | 39.69 |
| Weapon lights off | 22.13 ms | 38.11 ms | 108.60 ms | 18.51 ms | 45.19 |
| Smoke off | 23.74 ms | 38.69 ms | 105.64 ms | 20.65 ms | 42.13 |
| Baseline 2 | 23.21 ms | 39.42 ms | 106.27 ms | 19.65 ms | 43.09 |

Versus the mean of the baselines, weapon lights off measured **8.6% lower
mean app frame time** and **9.7% lower GPU time** in this single live-fight
run. This is worth repeating before drawing a causal or shipping conclusion:
AI, damage and weapon timing vary between fresh missions, and p99 did not
improve. The captured graphics state had `Smoke=false` already, so the
smoke-off trial did **not** switch smoke from on to off; its apparent 1.9%
mean-frame difference is not evidence of a smoke-saving effect. The combat
baseline had `BakedSky=false`, `QuestUvMaterials=true` and
`BakedInteriorLighting=false`; this run does not measure the user's preferred
baked sky/cockpit combination during live combat. Sun shadows, glow, glass and
HUD glow were off; radar, weapons, status, navigation and targeting HUD sections
were on.

For tomorrow: compare the complete rendering fixtures against 29 September,
then repeat the rendering run with capture active before it starts to recover a
paired terrain baseline. Repeat live combat with an actual smoke-on baseline if
smoke cost matters, and test the visually accepted baked sky/cockpit settings
in live combat before choosing defaults. Retain the current visual notes when
assessing any further sun-bloom implementation.
