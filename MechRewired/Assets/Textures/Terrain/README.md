# Terrain textures

The terrain detail materials are [Ground054](https://ambientcg.com/view?id=Ground054),
[Ground088](https://ambientcg.com/view?id=Ground088), and
[Rocks019](https://ambientcg.com/view?id=Rocks019) from ambientCG. All are provided under
the [CC0 1.0 licence](https://creativecommons.org/publicdomain/zero/1.0/).

Only the 1K colour, displacement, OpenGL normal and roughness maps are retained. MechRewired uses
them as world-space detail over the original MW2 palette colours; displacement supplies restrained
parallax while leaving the authored MW2 mesh and collision unchanged.
Rocks019 is restricted to irregular patches on flatter terrain rather than replacing the authored
slope-based sandstone material. Its complete authored colour relationship is retained inside those
patches so the sandy background remains tan and the granite stones stay dark.
