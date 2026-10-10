# Cached sun colour — 7 October 2026

The Quest baked sky now samples a 256 × 256 HDR sun-colour patch instead of calling
the full procedural sky twice for each visible sun pixel. It retains the original
cheap mathematical disc mask, so its outline remains sharp regardless of texture
resolution. The existing 2048 × 1024 sunless sky panorama is unchanged.

The patch is generated once alongside the panorama, using the original shader's
sun-on minus sun-off result with the same frozen time and lighting. It therefore
retains atmospheric colour and cloud attenuation. The patch captures smooth colour
without a disc edge; the final sky applies that edge analytically. HDR sky-RID
readback avoids applying Environment tonemapping during capture. Temporary capture
resources are removed after baking.

Directional sunlight and procedural radiance-cubemap lighting remain unchanged.
This is part of the existing baked-sky path, not another player setting or combat
phase. Desktop defaults retain their procedural sky. The cache assumes the existing
stationary mission sky; changing sky authoring parameters requires a fresh bake.

## Local verification

`QuestSunCacheCheck.tscn` is a Debug-only native comparison against the previous
visible-disc shader. It uses Mobile/Vulkan, a 1680 × 1760 isolated sky viewport,
2× MSAA, and 75° / 12° fields of view. Each cached sample is bracketed by two
reference samples (one-second warm-up, two-second measurement each). The two views
both face the deployment mission's sun. This is not whole-game or Quest timing.

The first final comparison measured:

| FOV | Reference GPU, bracketing mean | Cached GPU | Saving |
| --- | ---: | ---: | ---: |
| 75° | 1.090 ms | 1.037 ms | 4.8% |
| 12° | 1.082 ms | 1.027 ms | 5.1% |

Maximum image difference was 1/255 in a colour channel in both views; no pixel
differed by more than 0.03. The zoomed captures were visually inspected: the
original outline, position and colour are retained. An earlier texture-edge
experiment visibly softened the disc and was replaced by the analytic boundary.
The repeat run also completed successfully with the same maximum image error;
[both runs' measurements are saved here](data/sun-cache-2026-10-07.log).

Debug and Release builds pass with no warnings/errors. Existing compositor startup
errors and the two sampler-RID shutdown warning still appear; no new sky shader
errors occurred. A real Quest gain and visual equivalence across all missions have
not been measured.

Run after a Debug build with original game data installed:

```powershell
& local/tools/godot-4.7.1-mono/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path MechRewired --rendering-method mobile --xr-mode off --disable-vsync res://QuestSunCacheCheck.tscn -- --vr-preview
```

The check writes comparison images only to `user://benchmarks/sun-cache`, reports
GPU and image differences to the console, and fails if the maximum colour-channel
difference exceeds 0.03 or GPU timestamps are unavailable. It does not persist
settings. Normal combat benchmarks continue to use logs only.
