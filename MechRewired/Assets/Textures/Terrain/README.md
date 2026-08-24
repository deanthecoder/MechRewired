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
them as world-space detail over a desert-biome base colour derived from Ground054. They tile twice
as densely as the original pass, but their colour, normal, roughness and parallax contribution is
restrained to 30% so the authored terrain read remains dominant. The original
terrain triangles become a welded control surface: curved interpolation rounds severe authored
joins, dense sampling supplies rendering geometry, and a coarser sampling of the same surface
supplies collision. Planar neighbourhoods remain single triangles rather than acquiring redundant
coplanar vertices. Macro relief is baked into both representations; parallax and normal mapping
retain detail below their vertex spacing. The original palette colours do not bake 1995 lighting
into the live PBR terrain.
Ground054 remains the pocketed-sand base. Ground097 is restricted to broad, low, flat dune fields;
its repeated ripples retain one wind direction within a mission. Ground051 forms sparse, irregular
hardpan around hill feet and on wind-scoured flats. Ground088 remains the slope-based sandstone
material. Rocks021 is restricted to irregular patches away from dune fields rather than replacing
the authored slope-based sandstone material. The full authored colour relationship is retained
inside rock and hardpan patches so the sandy background remains tan, the granite stones stay dark,
and the compacted soil remains visibly brown. A separate broad height field adds at most 0.85m of
genuine undulation to the desert floor and blends it through the lowest 10m of authored landforms;
this sits beneath the existing higher-frequency material dunes and authored-hill macro relief.

The rocky material's Ground085, Ground067, Rock050 and Rock030 maps are kept strictly out of the
desert material. Ground085 remains the compact stony base, with Ground067 appearing in broad,
irregular patches on flatter terrain. Mountain surfaces blend toward Rock050 on authored slopes;
Rock030 is stretched vertically and introduced most strongly on long, near-vertical faces. The
cliff maps tile across larger areas than the previous rocky pass. Their average colour is removed
in the shader before applying detail, keeping the mission palette responsible for the terrain's
overall brown/red direction. No material height-map displacement is used. Instead, one very broad
procedural height field adds at most 1.25m of genuine relief to the shared ground and fades out
through the lowest 18m of mountain geometry. Rendering, collision, terrain queries and boundary
skirts sample the same field, while the authored mountain silhouettes remain unchanged.
