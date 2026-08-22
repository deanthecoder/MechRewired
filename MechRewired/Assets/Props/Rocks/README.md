# Desert rock runtime assets

`rock_00.obj` through `rock_05.obj` are generated, Quest-scale meshes. They contain 48–108
triangles each, are normalised to one metre across, use a shared material, and are intended only
for visual MultiMesh scatter. The runtime currently uses the three most distinct forms for this
dense near-ground layer; the remainder are retained for occasional larger landmark rocks. None
must receive collision bodies or shadows.

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
