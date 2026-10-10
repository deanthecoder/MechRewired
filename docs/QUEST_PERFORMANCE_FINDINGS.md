# Quest performance test record — 29 September 2026

This is the complete record of the tests and checks performed during the first
Quest rendering investigation. The **40 measured trials and all 18 reported
fields** are preserved in
[quest-benchmark-2026-09-29-summary.csv](data/quest-benchmark-2026-09-29-summary.csv).
That CSV preserves the fields from tagged headset logcat output. The full
50 KB log remains only in this machine's ignored
`local/benchmarks/quest-benchmark-20260929-182929.log`; the per-frame
`frames.csv` backup stayed in the app's private `user://benchmarks/` storage
and was not retrieved. A fresh checkout has the committed summary but not
those two local raw sources.

## Build, headset and run state

- Godot 4.7.1 .NET **Release** export, app version `0.1.0-alpha2`
  (`versionCode=2`), installed locally on a Quest 3. It used the Mobile Vulkan
  renderer and Adreno 740 GPU. The local APK was signed with the certificate
  matching the previously installed test app; the Meta Alpha channel was not
  updated. The private test APK bundled `MW2.PRJ`. Startup verified its
  SHA-256 and indexed **7,735 resources**.
- The benchmark's logged build state was `release`, OpenXR active, desktop
  preview false, mission `YELLSCN1`, headset refresh **72 Hz** (13.89 ms
  frame budget), XR target **1680×1760**, 2× MSAA, render scale 1. Radar,
  weapons, status, navigation and targeting HUD sections were enabled;
  HUD glow was zero.
- One benchmark run completed (`QUEST_BENCHMARK_STATUS: complete`) with
  **four fixtures × ten trials = 40 trials** and no logged skips. Each trial
  warmed up for three seconds, then measured six seconds. Each fixture ran
  baseline, eight one-feature variants, then baseline again. The script
  compared variants against the mean of those two bracketing baselines.
- Gameplay, AI and physics were held still while the benchmark moved the pilot
  through scripted views and rendered non-damaging missile effects. Headset
  tracking remained live. Measurements are app frame intervals and main-viewport
  renderer times, **not compositor-presented FPS** or a live battle. The GPU
  monitor may exclude HUD/offscreen work. No second run was recorded, so
  repeatability and thermal drift were not established.

## All variants: mean app frame time

Cells show **milliseconds (change from that fixture's paired baseline)**.
Negative change is faster. Baseline cells average the first and last baseline
trials; each other cell is one six-second trial. The `terrain-sweep` baseline
and some sky trials had significant head movement, described below.

| Variant | Terrain sweep | Enemy front | Building sweep | Missile salvo |
| --- | ---: | ---: | ---: | ---: |
| Baseline | 17.23 | 17.29 | 18.14 | 18.47 |
| Detailed triplanar terrain | 33.69 (+95.5%) | 34.43 (+99.1%) | 34.04 (+87.6%) | 34.40 (+86.3%) |
| Unlit terrain textures | 13.98 (-18.9%) | 13.92 (-19.5%) | 14.60 (-19.5%) | 14.48 (-21.6%) |
| Terrain hidden | 15.40 (-10.6%) | 15.18 (-12.2%) | 15.76 (-13.1%) | 15.96 (-13.6%) |
| Baked sky panorama | 15.59 (-9.5%) | 14.72 (-14.9%) | 16.08 (-11.4%) | 15.42 (-16.5%) |
| Plain sky | 15.68 (-9.0%) | 15.12 (-12.6%) | 16.40 (-9.6%) | 17.09 (-7.5%) |
| HUD hidden | 16.71 (-3.0%) | 16.70 (-3.4%) | 17.80 (-1.9%) | 18.14 (-1.8%) |
| Cockpit hidden | 13.89 (-19.4%) | 13.89 (-19.7%) | 14.07 (-22.4%) | 14.53 (-21.3%) |
| Scattered rocks hidden | 17.06 (-1.0%) | 16.81 (-2.8%) | 17.52 (-3.4%) | 18.49 (+0.1%) |

## All variants: mean renderer GPU time

Milliseconds, from the same trials. These values move broadly with app frame
time, suggesting rendering cost is material in this frozen workload; they do
not identify a unique cause or guarantee the same gains in active combat.

| Variant | Terrain sweep | Enemy front | Building sweep | Missile salvo |
| --- | ---: | ---: | ---: | ---: |
| Baseline | 16.94 | 17.00 | 17.86 | 17.76 |
| Detailed triplanar terrain | 33.36 | 34.10 | 33.77 | 33.39 |
| Unlit terrain textures | 13.72 | 13.47 | 14.34 | 13.85 |
| Terrain hidden | 15.13 | 14.91 | 15.48 | 15.33 |
| Baked sky panorama | 15.31 | 14.45 | 15.81 | 14.59 |
| Plain sky | 15.39 | 14.84 | 16.14 | 16.37 |
| HUD hidden | 16.70 | 16.67 | 17.77 | 17.67 |
| Cockpit hidden | 12.94 | 12.97 | 13.81 | 13.89 |
| Scattered rocks hidden | 16.77 | 16.51 | 17.26 | 17.77 |

The committed CSV also contains per-trial **frame count, median, p95, p99,
average app FPS, 1% low FPS, percentage above the 72 Hz frame budget,
renderer CPU time, draw calls, primitives and maximum head movement**. Those
values should be consulted before acting on a particular trial; the tables
above are a comparison index, not a substitute for the full records.

## What the tests do and do not show

- Detailed triplanar terrain was consistently very expensive, nearly doubling
  frame time. Keep Quest's cheap UV terrain baseline until a materially cheaper
  detailed shader exists.
- Hiding the cockpit reduced mean frame time about **19–22%**. That points to
  cockpit materials/geometry as an investigation target, not to removing the
  cockpit from a seated VR game.
- Using existing terrain textures without lighting reduced mean frame time
  about **19–22%**. This supports investigating a cheaper *lit* terrain path;
  the unlit ablation is not a visual solution.
- Hiding the HUD saved about **2–3% app frame time**, with less change in the
  main-viewport GPU monitor. Hiding rocks saved about **0–3%**, depending on
  view. Neither was the largest effect in this test.
- The baked sky reduced mean frame time by **11–15%** in the cleaner enemy and
  building views (roughly 2.1–2.6 ms). It was subsequently found visually
  unacceptable in normal play: low resolution, a pixelated sun in the wrong
  place, and white/blown-out mountain tops. See
  [QUEST_SKY_BAKE_FINDINGS.md](QUEST_SKY_BAKE_FINDINGS.md). Its performance
  result is not a reason to turn it on by default.
- A plain sky was another useful rendering ablation, but its appearance was
  not evaluated as a shipping option. The per-feature savings are **not
  additive**: hiding terrain, cockpit and sky changes how much of the screen
  each other feature covers.

The analyzer flagged head rotation **62.8°** and translation **0.093 m** in
the terrain-sweep baseline, **7.8°** rotation in its baked-sky trial, **6.0°**
rotation in missile-salvo/plain-sky, and **71.2°** rotation plus **0.164 m**
translation in missile-salvo/baked-sky. Treat those comparisons cautiously.
The enemy-front and building-sweep sky comparisons did not trigger movement
warnings. Baseline first/last means were close within each fixture, but that
does not replace a second headset run.

## Other validation and later live test

- Before the toggle was added, the benchmark Release APK built, installed,
  launched with OpenXR/Mobile Vulkan and completed the 40-trial run. The app
  verified bundled private data at startup. The first install attempt exposed
  a signing mismatch with the older debug app; a later incremental install
  reported success while the package disappeared. Re-signing the Release APK
  with the installed app's local certificate and using ADB's
  `--no-incremental -r` install succeeded. The bundled archive then restored
  the required game data. Do not treat the earlier incremental `Success` line
  as proof of an installed app.
- The later baked-sky menu-toggle code passed a Release .NET build with **zero
  warnings and zero errors**, all **219** existing .NET tests, and
  `git diff --check`. A private-data Release APK using the same local signing
  key installed with `--no-incremental -r`; startup again reported OpenXR,
  Mobile Vulkan, private data checksum validation and 7,735 indexed resources.
- The user's subsequent free-head-movement check found the sky/sun/mountain
  visual defects above. **No benchmark was rerun after adding the menu
  toggle.** The benchmark code's procedural baseline restoration was reviewed
  and built, but not separately measured on the headset with the toggle ON.

Next experiments should compare a procedural sky with cloud drift stopped,
then isolate cockpit and terrain rendering costs while preserving their
appearance. A repeated benchmark and a live-battle profiler capture are needed
before selecting a new default graphics path.
