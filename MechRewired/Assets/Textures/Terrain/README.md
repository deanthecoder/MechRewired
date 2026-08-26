# Terrain textures

The terrain detail materials are [Ground054](https://ambientcg.com/view?id=Ground054),
[Ground097](https://ambientcg.com/view?id=Ground097),
[Ground051](https://ambientcg.com/view?id=Ground051),
[Ground088](https://ambientcg.com/view?id=Ground088), and
[Rocks021](https://ambientcg.com/view?id=Rocks021) from ambientCG. The rocky-mountain biome uses
[Ground085](https://ambientcg.com/view?id=Ground085) and
[Ground067](https://ambientcg.com/view?id=Ground067) for compact brown, stony ground, with
[Rock050](https://ambientcg.com/view?id=Rock050) and
[Rock030](https://ambientcg.com/view?id=Rock030) on cliff faces. All are provided under
the [CC0 1.0 licence](https://creativecommons.org/publicdomain/zero/1.0/).

Only the 1K colour, displacement, OpenGL normal and roughness maps are retained. MechRewired uses
their colour as the primary world-space albedo, with a restrained normalized palette grade that
shifts biome hue without importing the original software renderer's baked brightness. The original
terrain triangles become a welded control surface: curved interpolation rounds severe authored
joins, dense sampling supplies rendering geometry, and a coarser sampling of the same surface
supplies collision. Planar neighbourhoods remain single triangles rather than acquiring redundant
coplanar vertices. Macro relief is baked into both representations; parallax and normal mapping
retain detail below their vertex spacing. The original palette colours do not bake 1995 lighting
into the live PBR terrain.
Ground054 remains the pocketed-sand base. Ground097 is restricted to broad, low, flat dune fields;
its repeated ripples retain one wind direction within a mission. Ground051 forms sparse, irregular
hardpan around hill feet and on wind-scoured flats. Ground088 remains the slope-based sandstone
material and uses a four-times-larger world footprint so hills read as rock formations rather than
compressed tiles. Rocks021 is restricted to irregular patches away from dune fields rather than
replacing the authored slope-based sandstone material. The full authored colour relationship is
retained inside rock and hardpan patches so the sandy background remains tan, the granite stones stay dark,
and the compacted soil remains visibly brown. A separate broad height field adds at most 0.85m of
genuine undulation to the desert floor and blends it through the lowest 10m of authored landforms;
this sits beneath the existing higher-frequency material dunes and authored-hill macro relief. As
on the rocky terrain, an irregular eight-metre material transition also lets the floor sand replace
slope rock at landform and sealing-skirt feet instead of exposing a sharp sand/rock boundary.

The rocky material's Ground085, Ground067, Rock050 and Rock030 maps are kept strictly out of the
desert material. Ground085 remains the compact stony base, with Ground067 appearing in broad,
irregular patches on flatter terrain. Mountain surfaces blend toward Rock050 on authored slopes;
Rock030 is stretched vertically and introduced most strongly on long, near-vertical faces. The
cliff maps tile across larger areas than the previous rocky pass. An offset second projection of
the same primary ground and rock maps spans roughly 150-190m, adding formation-scale colour and
normal variation without lining up with the main 40-65m tiles. The physical texture colour remains
primary; slight desaturation suppresses terrestrial moss and leaves, while a normalized 22% palette
grade preserves the mission's broad brown/red art direction. No material height-map displacement is
used. Instead, one
very broad procedural height field adds at most 1.25m of genuine relief to the shared ground and
fades out through the lowest 18m of mountain geometry. Rendering, collision, terrain queries and
boundary skirts sample the same field, while the authored mountain silhouettes remain unchanged.
The lowest roughly eight metres of each rocky landform, plus the lower portion of every sealing
skirt, blend irregularly into the same world-space ground projection. This creates a weathered,
scree-like material transition across both authored ground-height edges and raised skirt edges
without changing their closed collision geometry.
