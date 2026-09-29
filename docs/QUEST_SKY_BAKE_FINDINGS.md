# Quest baked-sky experiment: findings and state

Recorded 29 September 2026 on `codex/quest-performance-benchmark`. The Quest
build was a Godot 4.7.1 .NET **Release** export, version `0.1.0-alpha2`
(`versionCode=2`), locally signed with the certificate matching the installed
test app. It used the Mobile Vulkan renderer on a Quest 3 with an Adreno 740.
The private test APK bundled `MW2.PRJ`; startup verified its SHA-256 and indexed
7,735 resources. This was a local headset install, not a Meta Alpha update.

## Current implementation and observed states

- **BAKED SKY OFF** is the default for each mission. Sky3D draws the procedural
  atmosphere, clouds drift, and the existing directional sun lights the scene.
- **BAKED SKY ON** calls `RenderingServer.EnvironmentBakePanorama` once for that
  mission with `bake_irradiance=false` and a requested `1024×512` image. It
  switches the environment to `PanoramaSkyMaterial` and stops SkyDome processing.
  Switching OFF restores the procedural sky and cloud drift. The sun light
  remains enabled. The toggle is on the Quest menu's Graphics Settings page;
  its state is not saved across missions.
- The rendering benchmark forces a procedural-sky baseline even if the normal
  menu toggle was ON before the run, then restores the prior menu state. Its
  `sky-panorama` variant uses the same bake size and material approach.
- This toggle is **experimental and visually unacceptable at present**. Leave
  it OFF for ordinary play. Do not make it the Quest default on performance
  numbers alone.

Headset visual feedback after moving freely in normal play: the baked sky was
very low resolution; the sun appeared as a clump of a few pixels **and was in
the wrong place**; mountain tops appeared white or blown out. The visual test's
mission and exact view pose were not recorded. The earlier frozen benchmark
view had looked plausible, but could not establish appearance during head
movement. There are no matched before/after screenshots or colour measurements
for these defects yet.

## Performance measurements

The 29 September benchmark ran mission `YELLSCN1` on the Quest at 72 Hz,
`1680×1760` per-eye XR target, 2× MSAA, render scale 1. Each variant had a
three-second warm-up and six-second measured window. These are app frame
intervals and main-viewport renderer timings, not compositor-presented FPS or
a live AI/combat workload. The headset budget at 72 Hz is **13.89 ms**.

The cleanest fixed-view comparisons were:

| View and variant | Mean app frame | Change from paired baseline | Mean renderer GPU |
| --- | ---: | ---: | ---: |
| Enemy front, baseline | 17.29 ms | — | 17.00 ms |
| Enemy front, baked sky | 14.72 ms | 14.9% faster | 14.45 ms |
| Building sweep, baseline | 18.14 ms | — | 17.86 ms |
| Building sweep, baked sky | 16.08 ms | 11.4% faster | 15.81 ms |

The baked sky saved roughly **2.1–2.6 ms** in these fixtures, but neither
reached 13.89 ms. The other ablations point to cockpit rendering (about
20–22% lower app frame time when hidden) and terrain shading (about 19.5%
lower when unlit) as areas for further investigation; hiding the cockpit or
removing terrain lighting is not a finished visual solution. Detailed
triplanar terrain nearly doubled frame time in these views.

Do not use the terrain-sweep or missile-salvo sky result as clean visual or
performance evidence: the terrain baseline recorded 62.8° of head rotation,
the terrain baked-sky trial 7.8°, and the missile baked-sky trial 71.2° plus
0.164 m of translation. The full tagged report remains in this machine's
ignored `local/benchmarks/quest-benchmark-20260929-182929.log`; the aggregate
figures above are retained here for a later checkout or session.

## What is known, and what to investigate next

The requested output is `1024×512`, but that does **not** prove 1024×512 source
detail. Godot's `EnvironmentBakePanorama` exports the sky's radiance map, and
Godot documents that raising output height above `Sky.radiance_size` adds no
detail. The code does not explicitly set that radiance size. This is a strong
lead for the blocky sun, not yet a measured root cause. A 1024-wide panorama
also spans 360°, or about 0.35° per pixel at the equator, so a small sun is
intrinsically vulnerable to pixelation even if the source map is sharper.

The sun's wrong position is **unexplained**. Compare the procedural sun disc,
the baked panorama's bright spot, and `SunLight.GlobalBasis.Z` at the same
mission time and camera pose; check panorama orientation and whether baking
captures an outdated sky/light state. Do not compensate with an arbitrary
rotation before finding the mismatch.

The white mountain tops are also **unexplained**. A panorama replacement can
alter sky-derived ambient/reflection lighting and the visible sky behind fogged
terrain, but this run did not isolate which path changed. Capture matched
ON/OFF views with identical camera, exposure, fog, sun and graphics settings;
inspect terrain lit/albedo/direct-sun diagnostics and sky contribution before
changing mountain materials.

If continuing the experiment, separate the broad baked atmosphere/clouds from
the sharp sun disc, retain a correctly aligned live sun element, and test a
higher-detail source rather than merely upscaling the radiance export. Compare
stereo appearance while turning the head across the sun and mountain horizon
before repeating performance measurements. A lower-resolution cloud subpass
or a cached sky cubemap is another possible route if a panorama cannot preserve
the current lighting and sun alignment.

**Intermediate option to test:** keep the procedural sky and sun exactly as
they are, but stop cloud drift (for example, stop `SkyDome.process_tick` or set
wind speed to zero). This should preserve the current sky alignment and avoids
the panorama's visible resolution problem. It may reduce CPU work and repeated
sky-uniform updates, but it does not by itself remove the expensive sky shader
from visible pixels. `SkyMaterial.gdshader` also reads `TIME` for star
scintillation, so Godot may still refresh its radiance map every frame; measure
the actual Quest GPU/frame-time change before treating this as an optimisation.

Relevant code: `MechRewired/MissionSkyController.cs`,
`MechRewired/QuestBenchmarkGraphics.cs`, `MechRewired/QuestVrMenu.cs`, and
`MechRewired/addons/sky_3d/shaders/SkyMaterial.gdshader`.
Godot API reference:
<https://docs.godotengine.org/en/stable/classes/class_renderingserver.html#class-renderingserver-method-environment-bake-panorama>.
