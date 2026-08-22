# Terrain textures

The terrain detail materials are [Ground054](https://ambientcg.com/view?id=Ground054),
[Ground088](https://ambientcg.com/view?id=Ground088), and
[Rocks019](https://ambientcg.com/view?id=Rocks019) from ambientCG. All are provided under
the [CC0 1.0 licence](https://creativecommons.org/publicdomain/zero/1.0/).

Only the 1K colour, displacement, OpenGL normal and roughness maps are retained. MechRewired uses
them as world-space detail over a desert-biome base colour derived from Ground054. The original
terrain triangles become a welded control surface: curved interpolation rounds severe authored
joins, dense sampling supplies rendering geometry, and a coarser sampling of the same surface
supplies collision. Planar neighbourhoods remain single triangles rather than acquiring redundant
coplanar vertices. Macro relief is baked into both representations; parallax and normal mapping
retain detail below their vertex spacing. The original palette colours do not bake 1995 lighting
into the live PBR terrain.
Rocks019 is restricted to irregular patches on flatter terrain rather than replacing the authored
slope-based sandstone material. Its complete authored colour relationship is retained inside those
patches so the sandy background remains tan and the granite stones stay dark.
