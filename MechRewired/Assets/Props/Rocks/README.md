# Terrain rock runtime assets

`rock_00.obj` through `rock_05.obj` are generated, Quest-scale meshes. They contain 48–108
triangles each, are normalised to one metre across, use a shared material, and are intended only
for visual MultiMesh scatter. The runtime uses all six forms: Wolf's desert profile produces
wind-deposited stone patches, while Jade Falcon's mountain profile favours larger talus and
outcrop clusters at slope feet and basin edges. They do not receive collision bodies. Small forms
use inexpensive, surface-aligned contact-shadow cards and terrain-tinted ground skirts; selected
large, nearby rocks may cast directional shadows. Deterministic colour, scale, lean, jitter and
occasional companion stones keep the shared source material from producing repeated-looking rows.

They were derived from the user's external `highpoly-rocks-free-download.zip`, which is not copied
into this repository. Regenerate them with:

```sh
python3 Tools/build_rock_assets.py \
  --source /Users/dean/Downloads/highpoly-rocks-free-download.zip \
  --output MechRewired/Assets/Props/Rocks
```

The tool needs `assimp` and Pillow. It selects six source forms, uses deterministic vertex
clustering to reduce their geometry, centres each one at its base, normalises its footprint, and
resizes the shared colour, normal, and roughness maps to 1024².
