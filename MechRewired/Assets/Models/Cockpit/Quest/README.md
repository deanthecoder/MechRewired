# Quest cockpit bake

`quest-cockpit.glb` supplies replacement geometry for these opaque source mesh names:
`CockpitArmor`, `CockpitCeramic`, `CockpitFasteners`, `CockpitFrame`,
`CockpitSeals`, `CockpitStampMetal`, and `CockpitTrim`.  Their source-local
transforms and polygon counts are retained.  The GLB deliberately contains no
material textures; the runtime applies the PNG atlases below.  It does not
replace the source `cockpit.glb`, glass meshes, `CockpitAmber`, or
`CockpitCoreGlow`.

Both cockpit imports disable automatic mesh LOD generation: the enclosure stays
next to the pilot, so retain its authored detail without generating distance variants.

Each mesh has three files named exactly after its source node:

- `CockpitFrame-albedo.png`: sRGB baked base colour. Frame uses a 2048 atlas;
  armor uses 1024 and the auxiliary meshes use 512.
- `CockpitFrame-normal.png`: non-colour OpenGL tangent-space normal atlas.
- `CockpitFrame-interior-strength1.png`: sRGB colour contribution from the
  authored interior lamps at `LightingStrength = 1`. Assign it as an emission
  texture and use the existing lighting-strength value as its emission-energy
  scalar. A strength of 1 is the authored preset; 0 and 2 are intentionally
  simple darker/brighter approximations of the former live OmniLights.

The bake is generated with:

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --python scripts/bake-quest-cockpit.py
```

The script retains the original UV as `SourceUV` during baking and creates a
unique `QuestUV` atlas. The frame exports only `QuestUV`; the armor and fittings
keep `SourceUV` in UV1 for their original materials and use `QuestUV` in UV2 for
the interior-light texture. Islands have pixel-sized
padding rather than large percentage margins that would erase surface detail.
Frame colour is
baked from the original object-local 1.5-per-metre triplanar Metal029 setup;
armor includes its original MetalPlates013 tint and normal detail. The four
lamp positions, colours, baseline/lift values, and finite ranges come directly
from `PlayerCockpit.LoadCockpitModel`. Its interior pass evaluates Godot's
default finite Omni attenuation and Lambert diffuse response, includes albedo,
and excludes sun, world/environment, shadows, and specular. The game keeps its
normal runtime material lighting for the moving sun. This is deliberately a
diffuse cabin-light approximation, so it does not reproduce view-dependent
specular highlights from the desktop path.

Runtime frame metallic/roughness controls remain active. Armor and fittings
retain their original textures and material properties; their baked albedo/normal
PNGs are intermediate bake outputs, not replacements for those runtime textures.
Atlas textures use mipmaps and VRAM compression, including Android ASTC import.
The atlas captures the frame's default 1.5 repetitions/metre; changing the debug
texture-scale control affects only the original material until this bake is regenerated.
