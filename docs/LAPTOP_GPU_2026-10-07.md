# Laptop GPU investigation — 7 October 2026

The laptop can run useful controlled rendering experiments without a headset. Terrain lighting is the strongest lead in this fixture; do not interpret these percentages as Quest gains.

## Method

- NVIDIA RTX PRO 1000 Blackwell Generation Laptop GPU, Godot 4.7.1 Mobile/Vulkan, Debug build.
- Quest VR preview, one 1680 × 1760 view, 2× MSAA, no SMAA/TAA, uncapped, VSync disabled. This is **mono**, without OpenXR/compositor cost or Quest GPU architecture.
- Explicit cheap UV terrain path. Desktop `.mobile` project overrides do not automatically apply just because the Mobile renderer was selected; the probe explicitly sets AA.
- Frozen enemy-front fixture, gameplay paused, particles stopped. This measures static rendering, **not combat simulation or active explosions**. The effects-hidden result cannot establish combat smoke cost.
- One second warm-up and two seconds of per-frame GPU timestamp samples for each stage. Every variant is bracketed by baseline; repeat in reverse order. Savings use the mean of the adjacent baseline GPU times.
- Main and HUD viewport GPU timing collected separately. Draw/primitive counts are whole-frame engine counters. GPU task output is also collected with `--gpu-profile`.
- Laptop baseline drifted from about 2.02 to 1.80 ms during the full run, so use paired comparisons, not first-to-last totals. Short runs are directional evidence, not precision guarantees.

## Component isolation

| Change | Main GPU saving across two passes | Interpretation |
|---|---:|---|
| Terrain hidden | 41–42% | Biggest component in this view |
| Terrain unshaded, textures retained | 35–37% | Lighting is much more promising than texture removal |
| Cockpit hidden | 22% | Second substantial component |
| 90% 3D resolution per axis | 16–17% | Useful pixel-cost control; not applied to normal play |
| Local lights hidden | 11% | Includes all non-directional scene lights |
| Plain sky, sky lighting retained | 6–10% | Smaller and less stable |
| HUD hidden and viewport stopped | 0–3% | Removes 57 draw calls but little main GPU time; HUD viewport itself costs about 0.03 ms |
| Frozen effects hidden | No saving | Not an active combat smoke test |

Baseline submits 251,340 primitives and 131 draws. Terrain hiding removes 197,266 primitives but only two draws: 153,376 derived-terrain triangles plus 43,890 implicit-ground triangles. The terrain is not as geometrically simple as previously assumed. Inventory includes out-of-view nodes and a shadow-only proxy; those inventory totals are not rendered triangle counts.

## Targeted follow-up

| Diagnostic change | Round 1 baseline → variant | Round 2 baseline → variant | Result |
|---|---:|---:|---|
| Vertex terrain lighting + disabled specular | 1.916 → 1.576 ms | 1.898 → 1.483 ms | 18–22% less main GPU time |
| 256 m terrain chunks, identical triangle data | 1.925 → 1.746 ms | 1.904 → 1.793 ms | 6–9% less main GPU time |
| Unshaded terrain | 1.898 → 1.256 ms | 1.874 → 1.355 ms | 28–34% less main GPU time |

Vertex lighting retains texture sampling and response to scene lights, but changes interpolation and removes specular. These two changes have **not** been isolated from each other. Visual quality and local-light falloff still need assessment before production adoption.

Chunking preserved all 197,266 source triangles; culling reduced whole-frame submitted primitives to 117,846. Draws increased to 340 and render CPU time increased. Do not ship this coarse experiment purely on laptop GPU results.

## Next work

Prioritise terrain lighting: isolate the specular change, assess the vertex-lit appearance, and compare a few fixed views locally. Keep production defaults intact until that work identifies the acceptable change. Then run one focused real-Quest confirmation with active combat. Stop spending device cycles on smaller smoke/HUD variations without a convincing local or device measurement.

The reusable diagnostic is `MechRewired/LaptopRenderProbe.tscn` (Debug only). It changes no saved graphics preferences. From the repository root, after a Debug build:

```powershell
& local/tools/godot-4.7.1-mono/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path MechRewired --rendering-method mobile --xr-mode off --gpu-profile --disable-vsync res://LaptopRenderProbe.tscn -- --vr-preview --terrain-only *> local/laptop-gpu-terrain.log
```

Omit `--terrain-only` for the full component comparison (~2 minutes). Original game data must be installed. Raw frame JSONL files are written under Godot's user data `benchmarks` directory; compact run metadata, inventories, and summaries are saved in [the evidence log](data/laptop-gpu-2026-10-07-summary.log).

Both recorded runs completed successfully. Existing lens-effects compositor shader initialization errors and the two sampler-RID shutdown warning remain in console output; this is not a clean validation of that compositor path. No new shader errors appeared for the terrain variants. The earlier exploratory moving-scene run was discarded after fixing pause inheritance; it is excluded from the evidence log.
