// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Godot;

namespace MechRewired;

public enum TerrainSurfaceKind
{
    Desert,
    RockyGround,
    RockyMountain
}

/// <summary>
/// Builds the shared PBR material used by derived MW2 terrain and its implicit fill.
/// </summary>
/// <remarks>
/// World-space triplanar projection avoids inventing UVs for the original terrain. Pocketed sand
/// is the base, wavy sand settles in low flats, hardpan gathers around exposed ground and
/// sandstone is introduced by slope. The physical texture albedo remains primary while MW2's
/// authored palette provides a restrained biome grade.
/// </remarks>
public static class TerrainSurfaceMaterial
{
    public const float Roughness = 0.9f;
    public const float TextureScale = 0.05f;
    public const float DetailStrength = 0.78f;
    public const float NormalStrength = 1.0f;
    public const float DunePatchCoverage = 0.25f;
    public const float HardpanPatchCoverage = 0.08f;
    public const float StonePatchCoverage = 0.10f;
    public const float StoneTextureScale = 0.50f;
    public const float ParallaxDepthMetres = 0.15f;
    public const float GeometryDisplacementStrength = 1.0f;
    public const float MountainMacroReliefMetres = 1.15f;
    public const float RockSlopeStartDegrees = 12.0f;
    public const float RockSlopeEndDegrees = 38.0f;
    // Fine photographed texture is important underfoot, but it reads as noisy repeated cracks
    // across the far floor. Keep the near field unchanged, then hand visual weight to the
    // material's existing broad projections over a deliberately long, ring-free transition.
    public const float DistanceDetailFadeStartMetres = 82.0f;
    public const float DistanceDetailFadeEndMetres = 260.0f;
    public static readonly Color DesertBaseColor = new("9c8b6e");

    private const string SandColorPath =
        "res://Assets/Textures/Terrain/Ground054/Ground054_1K-PNG_Color.png";
    private const string SandNormalPath =
        "res://Assets/Textures/Terrain/Ground054/Ground054_1K-PNG_NormalGL.png";
    private const string SandRoughnessPath =
        "res://Assets/Textures/Terrain/Ground054/Ground054_1K-PNG_Roughness.png";
    private const string SandHeightPath =
        "res://Assets/Textures/Terrain/Ground054/Ground054_1K-PNG_Displacement.png";
    private const string DuneColorPath =
        "res://Assets/Textures/Terrain/Ground097/Ground097_1K-PNG_Color.png";
    private const string DuneNormalPath =
        "res://Assets/Textures/Terrain/Ground097/Ground097_1K-PNG_NormalGL.png";
    private const string DuneRoughnessPath =
        "res://Assets/Textures/Terrain/Ground097/Ground097_1K-PNG_Roughness.png";
    private const string DuneHeightPath =
        "res://Assets/Textures/Terrain/Ground097/Ground097_1K-PNG_Displacement.png";
    private const string HardpanColorPath =
        "res://Assets/Textures/Terrain/Ground051/Ground051_1K-PNG_Color.png";
    private const string HardpanNormalPath =
        "res://Assets/Textures/Terrain/Ground051/Ground051_1K-PNG_NormalGL.png";
    private const string HardpanRoughnessPath =
        "res://Assets/Textures/Terrain/Ground051/Ground051_1K-PNG_Roughness.png";
    private const string HardpanHeightPath =
        "res://Assets/Textures/Terrain/Ground051/Ground051_1K-PNG_Displacement.png";
    private const string RockColorPath =
        "res://Assets/Textures/Terrain/Ground088/Ground088_1K-PNG_Color.png";
    private const string RockNormalPath =
        "res://Assets/Textures/Terrain/Ground088/Ground088_1K-PNG_NormalGL.png";
    private const string RockRoughnessPath =
        "res://Assets/Textures/Terrain/Ground088/Ground088_1K-PNG_Roughness.png";
    private const string RockHeightPath =
        "res://Assets/Textures/Terrain/Ground088/Ground088_1K-PNG_Displacement.png";
    private const string StoneColorPath =
        "res://Assets/Textures/Terrain/Rocks021/Rocks021_1K-PNG_Color.png";
    private const string StoneNormalPath =
        "res://Assets/Textures/Terrain/Rocks021/Rocks021_1K-PNG_NormalGL.png";
    private const string StoneRoughnessPath =
        "res://Assets/Textures/Terrain/Rocks021/Rocks021_1K-PNG_Roughness.png";
    private const string StoneHeightPath =
        "res://Assets/Textures/Terrain/Rocks021/Rocks021_1K-PNG_Displacement.png";
    private const string RockyGroundPrimaryColorPath =
        "res://Assets/Textures/Terrain/Ground085/Ground085_1K-PNG_Color.png";
    private const string RockyGroundPrimaryNormalPath =
        "res://Assets/Textures/Terrain/Ground085/Ground085_1K-PNG_NormalGL.png";
    private const string RockyGroundPrimaryRoughnessPath =
        "res://Assets/Textures/Terrain/Ground085/Ground085_1K-PNG_Roughness.png";
    private const string RockyGroundSecondaryColorPath =
        "res://Assets/Textures/Terrain/Ground067/Ground067_1K-PNG_Color.png";
    private const string RockyGroundSecondaryNormalPath =
        "res://Assets/Textures/Terrain/Ground067/Ground067_1K-PNG_NormalGL.png";
    private const string RockyGroundSecondaryRoughnessPath =
        "res://Assets/Textures/Terrain/Ground067/Ground067_1K-PNG_Roughness.png";
    private const string MountainRockPrimaryColorPath =
        "res://Assets/Textures/Terrain/Rock050/Rock050_1K-PNG_Color.png";
    private const string MountainRockPrimaryNormalPath =
        "res://Assets/Textures/Terrain/Rock050/Rock050_1K-PNG_NormalGL.png";
    private const string MountainRockPrimaryRoughnessPath =
        "res://Assets/Textures/Terrain/Rock050/Rock050_1K-PNG_Roughness.png";
    private const string MountainRockVerticalColorPath =
        "res://Assets/Textures/Terrain/Rock030/Rock030_1K-PNG_Color.png";
    private const string MountainRockVerticalNormalPath =
        "res://Assets/Textures/Terrain/Rock030/Rock030_1K-PNG_NormalGL.png";
    private const string MountainRockVerticalRoughnessPath =
        "res://Assets/Textures/Terrain/Rock030/Rock030_1K-PNG_Roughness.png";

    /// <summary>
    /// Creates one terrain material using the remastered biome colour unless an explicit tint is supplied.
    /// </summary>
    public static ShaderMaterial Create(
        TerrainSurfaceKind surfaceKind = TerrainSurfaceKind.Desert,
        Color? albedoTint = null)
    {
        var useDesertDetails = surfaceKind == TerrainSurfaceKind.Desert;
        var material = new ShaderMaterial
        {
            Shader = new Shader { Code = useDesertDetails ? ShaderCode : RockyPlainsShaderCode }
        };
        material.SetShaderParameter("albedo_tint", albedoTint ?? DesertBaseColor);
        material.SetShaderParameter("normal_strength", NormalStrength);
        material.SetShaderParameter("distance_detail_fade_start", DistanceDetailFadeStartMetres);
        material.SetShaderParameter("distance_detail_fade_end", DistanceDetailFadeEndMetres);
        if (!useDesertDetails)
        {
            material.SetShaderParameter(
                "ground_primary_color",
                GD.Load<Texture2D>(RockyGroundPrimaryColorPath));
            material.SetShaderParameter(
                "ground_primary_normal",
                GD.Load<Texture2D>(RockyGroundPrimaryNormalPath));
            material.SetShaderParameter(
                "ground_primary_roughness",
                GD.Load<Texture2D>(RockyGroundPrimaryRoughnessPath));
            material.SetShaderParameter(
                "ground_secondary_color",
                GD.Load<Texture2D>(RockyGroundSecondaryColorPath));
            material.SetShaderParameter(
                "ground_secondary_normal",
                GD.Load<Texture2D>(RockyGroundSecondaryNormalPath));
            material.SetShaderParameter(
                "ground_secondary_roughness",
                GD.Load<Texture2D>(RockyGroundSecondaryRoughnessPath));
            material.SetShaderParameter(
                "mountain_primary_color",
                GD.Load<Texture2D>(MountainRockPrimaryColorPath));
            material.SetShaderParameter(
                "mountain_primary_normal",
                GD.Load<Texture2D>(MountainRockPrimaryNormalPath));
            material.SetShaderParameter(
                "mountain_primary_roughness",
                GD.Load<Texture2D>(MountainRockPrimaryRoughnessPath));
            material.SetShaderParameter(
                "mountain_vertical_color",
                GD.Load<Texture2D>(MountainRockVerticalColorPath));
            material.SetShaderParameter(
                "mountain_vertical_normal",
                GD.Load<Texture2D>(MountainRockVerticalNormalPath));
            material.SetShaderParameter(
                "mountain_vertical_roughness",
                GD.Load<Texture2D>(MountainRockVerticalRoughnessPath));
            material.SetShaderParameter(
                "mountain_surface",
                surfaceKind == TerrainSurfaceKind.RockyMountain ? 1.0f : 0.0f);
            material.SetShaderParameter("macro_variation_strength", 0.065f);
            return material;
        }

        material.SetShaderParameter("sand_color", GD.Load<Texture2D>(SandColorPath));
        material.SetShaderParameter("sand_normal", GD.Load<Texture2D>(SandNormalPath));
        material.SetShaderParameter("sand_roughness", GD.Load<Texture2D>(SandRoughnessPath));
        material.SetShaderParameter("sand_height", GD.Load<Texture2D>(SandHeightPath));
        material.SetShaderParameter("dune_color", GD.Load<Texture2D>(DuneColorPath));
        material.SetShaderParameter("dune_normal", GD.Load<Texture2D>(DuneNormalPath));
        material.SetShaderParameter("dune_roughness", GD.Load<Texture2D>(DuneRoughnessPath));
        material.SetShaderParameter("dune_height", GD.Load<Texture2D>(DuneHeightPath));
        material.SetShaderParameter("hardpan_color", GD.Load<Texture2D>(HardpanColorPath));
        material.SetShaderParameter("hardpan_normal", GD.Load<Texture2D>(HardpanNormalPath));
        material.SetShaderParameter("hardpan_roughness", GD.Load<Texture2D>(HardpanRoughnessPath));
        material.SetShaderParameter("hardpan_height", GD.Load<Texture2D>(HardpanHeightPath));
        material.SetShaderParameter("rock_color", GD.Load<Texture2D>(RockColorPath));
        material.SetShaderParameter("rock_normal", GD.Load<Texture2D>(RockNormalPath));
        material.SetShaderParameter("rock_roughness", GD.Load<Texture2D>(RockRoughnessPath));
        material.SetShaderParameter("rock_height", GD.Load<Texture2D>(RockHeightPath));
        material.SetShaderParameter("stone_color", GD.Load<Texture2D>(StoneColorPath));
        material.SetShaderParameter("stone_normal", GD.Load<Texture2D>(StoneNormalPath));
        material.SetShaderParameter("stone_roughness", GD.Load<Texture2D>(StoneRoughnessPath));
        material.SetShaderParameter("stone_height", GD.Load<Texture2D>(StoneHeightPath));
        material.SetShaderParameter("texture_scale", TextureScale);
        material.SetShaderParameter("detail_strength", DetailStrength);
        material.SetShaderParameter("dune_patch_coverage", DunePatchCoverage);
        material.SetShaderParameter("hardpan_patch_coverage", HardpanPatchCoverage);
        material.SetShaderParameter("stone_patch_coverage", StonePatchCoverage);
        material.SetShaderParameter("stone_texture_scale", StoneTextureScale);
        material.SetShaderParameter("parallax_depth_metres", ParallaxDepthMetres);
        material.SetShaderParameter("macro_variation_strength", 0.07f);
        material.SetShaderParameter("debug_wireframe", 0.0f);
        material.SetShaderParameter("slope_blend_start", ToSteepness(RockSlopeStartDegrees));
        material.SetShaderParameter("slope_blend_end", ToSteepness(RockSlopeEndDegrees));
        return material;
    }

    /// <summary>
    /// Creates an unlit wireframe variant of the generated terrain geometry.
    /// </summary>
    public static ShaderMaterial CreateWireframe(
        TerrainSurfaceKind surfaceKind = TerrainSurfaceKind.Desert,
        Color? albedoTint = null)
    {
        var material = Create(surfaceKind, albedoTint);
        var cullMode = surfaceKind == TerrainSurfaceKind.Desert
            ? "render_mode cull_back;"
            : "render_mode cull_disabled;";
        material.Shader.Code = material.Shader.Code.Replace(
            cullMode,
            surfaceKind == TerrainSurfaceKind.Desert
                ? "render_mode cull_back, wireframe, unshaded;"
                : "render_mode cull_disabled, wireframe, unshaded;");
        if (surfaceKind == TerrainSurfaceKind.Desert)
        {
            material.SetShaderParameter("debug_wireframe", 1.0f);
        }

        return material;
    }

    public static float ToSteepness(float slopeDegrees) =>
        1.0f - Mathf.Cos(Mathf.DegToRad(slopeDegrees));

    private const string RockyPlainsShaderCode =
        """
        shader_type spatial;
        // Mountain boundary skirts must remain visible from either side. Their authored edge
        // direction is not a reliable indication of which side a cockpit can approach from.
        render_mode cull_disabled;

        uniform sampler2D ground_primary_color : source_color, repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D ground_primary_normal : hint_normal, repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D ground_primary_roughness : repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D ground_secondary_color : source_color, repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D ground_secondary_normal : hint_normal, repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D ground_secondary_roughness : repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D mountain_primary_color : source_color, repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D mountain_primary_normal : hint_normal, repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D mountain_primary_roughness : repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D mountain_vertical_color : source_color, repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D mountain_vertical_normal : hint_normal, repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D mountain_vertical_roughness : repeat_enable, filter_linear_mipmap_anisotropic;
        uniform vec4 albedo_tint : source_color = vec4(1.0);
        uniform float mountain_surface = 0.0;
        uniform float ground_primary_texture_scale = 0.0095;
        uniform float ground_secondary_texture_scale = 0.0075;
        uniform float mountain_primary_texture_scale = 0.0115;
        uniform float mountain_vertical_texture_scale = 0.0085;
        // The rocky maps need a stronger response than the fine desert grain. The debug terrain
        // control still supplies normal_strength; this multiplier keeps Jade's relief legible
        // without changing the established desert tuning.
        uniform float normal_strength = 1.0;
        uniform float rocky_normal_response = 1.85;
        uniform float macro_normal_blend = 0.20;
        uniform float macro_variation_strength = 0.065;
        uniform float upward_lighting_compensation = 0.12;
        uniform float distance_detail_fade_start = 82.0;
        uniform float distance_detail_fade_end = 260.0;

        varying vec3 world_position;
        varying vec3 world_geometric_normal;

        float hash(vec2 point) {
            return fract(sin(dot(point, vec2(127.1, 311.7))) * 43758.5453);
        }

        float value_noise(vec2 point) {
            vec2 cell = floor(point);
            vec2 local = fract(point);
            local = local * local * (3.0 - 2.0 * local);
            return mix(
                mix(hash(cell), hash(cell + vec2(1.0, 0.0)), local.x),
                mix(hash(cell + vec2(0.0, 1.0)), hash(cell + vec2(1.0)), local.y),
                local.y);
        }

        float layered_noise(vec2 point) {
            return value_noise(point) * 0.62 +
                value_noise(point * 2.07 + vec2(17.3, -9.1)) * 0.27 +
                value_noise(point * 4.19 + vec2(-6.7, 23.4)) * 0.11;
        }

        void vertex() {
            world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
            world_geometric_normal = normalize(MODEL_NORMAL_MATRIX * NORMAL);
        }

        vec3 triplanar_weights(vec3 normal) {
            vec3 weights = pow(abs(normal), vec3(5.0));
            return weights / max(weights.x + weights.y + weights.z, 0.0001);
        }

        vec4 sample_triplanar(sampler2D map, vec3 position, vec3 weights) {
            vec4 x_projection = texture(map, position.zy);
            vec4 y_projection = texture(map, position.xz);
            vec4 z_projection = texture(map, position.xy);
            return x_projection * weights.x + y_projection * weights.y + z_projection * weights.z;
        }

        vec3 axis_normal_x(vec3 tangent_normal, float axis_sign) {
            return vec3(tangent_normal.z * axis_sign, tangent_normal.y, tangent_normal.x * axis_sign);
        }

        vec3 axis_normal_y(vec3 tangent_normal, float axis_sign) {
            return vec3(tangent_normal.x, tangent_normal.z * axis_sign, -tangent_normal.y * axis_sign);
        }

        vec3 axis_normal_z(vec3 tangent_normal, float axis_sign) {
            return vec3(tangent_normal.x * axis_sign, tangent_normal.y, tangent_normal.z * axis_sign);
        }

        vec3 sample_triplanar_normal(
            sampler2D map,
            vec3 position,
            vec3 geometric_normal,
            vec3 weights) {
            vec3 x_normal = texture(map, position.zy).xyz * 2.0 - 1.0;
            vec3 y_normal = texture(map, position.xz).xyz * 2.0 - 1.0;
            vec3 z_normal = texture(map, position.xy).xyz * 2.0 - 1.0;
            x_normal.xy *= normal_strength * rocky_normal_response;
            y_normal.xy *= normal_strength * rocky_normal_response;
            z_normal.xy *= normal_strength * rocky_normal_response;
            return normalize(
                axis_normal_x(normalize(x_normal), sign(geometric_normal.x)) * weights.x +
                axis_normal_y(normalize(y_normal), sign(geometric_normal.y)) * weights.y +
                axis_normal_z(normalize(z_normal), sign(geometric_normal.z)) * weights.z);
        }

        float luminance(vec3 color) {
            return dot(color, vec3(0.2126, 0.7152, 0.0722));
        }

        vec3 palette_grade(vec3 palette) {
            return clamp(
                palette / max(luminance(palette), 0.02),
                vec3(0.70),
                vec3(1.30));
        }

        void fragment() {
            vec3 geometric_normal = normalize(world_geometric_normal);
            vec3 weights = triplanar_weights(geometric_normal);
            float distance_detail_fade = smoothstep(
                distance_detail_fade_start,
                distance_detail_fade_end,
                distance(CAMERA_POSITION_WORLD, world_position));
            vec3 ground_primary_position = world_position * ground_primary_texture_scale;
            vec3 ground_secondary_position = world_position * ground_secondary_texture_scale;
            vec3 mountain_primary_position = world_position * mountain_primary_texture_scale;
            // Rock030's vertical coordinate is intentionally stretched. Its long strata read as
            // one formation instead of repeating in square stamps on tall cliff faces.
            vec3 mountain_vertical_position = world_position * vec3(
                mountain_vertical_texture_scale,
                mountain_vertical_texture_scale * 0.48,
                mountain_vertical_texture_scale);
            // Repeat the primary maps at formation scale, offset from the detail projection. The
            // different scale prevents their features from lining up while preserving the normal
            // maps' world-space orientation. This fills the gap between the 40-65m surface tiles
            // and the procedural 150-190m variation.
            vec3 ground_macro_position = (
                world_position + vec3(137.0, 41.0, 79.0)) * 0.00525;
            vec3 mountain_macro_position = (
                world_position + vec3(53.0, -67.0, 181.0)) * vec3(0.00675, 0.00486, 0.00675);
            float steepness = 1.0 - clamp(abs(geometric_normal.y), 0.0, 1.0);
            float rock_blend = mountain_surface * smoothstep(0.10, 0.58, steepness);
            // Derived landforms use a vertical sealing skirt where their open boundary meets the
            // implicit floor. Its vertex alpha rises toward the floor, feathering that lower band
            // into the exact same world-space ground material instead of drawing a hard rock/soil line.
            float skirt_blend_variation =
                (layered_noise(world_position.xz * 0.075 + vec2(19.0, -43.0)) - 0.5) * 0.18;
            float skirt_ground_blend = smoothstep(
                0.08,
                0.96,
                COLOR.a + skirt_blend_variation);
            // A large proportion of PINK's authored boundary already reaches the implicit floor,
            // so it never receives a sealing skirt. Feather the lowest eight metres of every
            // mountain into the ground material as well. World-space noise avoids replacing the
            // old straight material seam with a new perfectly level band.
            float base_blend_variation =
                (layered_noise(world_position.xz * 0.052 + vec2(-71.0, 13.0)) - 0.5) * 2.2;
            float landform_ground_blend = mountain_surface * (1.0 - smoothstep(
                0.10 + base_blend_variation,
                8.20 + base_blend_variation,
                world_position.y));
            float ground_transition_blend = max(skirt_ground_blend, landform_ground_blend);
            rock_blend *= 1.0 - ground_transition_blend;
            float ground_patch = smoothstep(
                0.34,
                0.70,
                layered_noise(world_position.xz * 0.011 + vec2(-31.0, 47.0)));
            float ground_secondary_blend = (0.16 + ground_patch * 0.48) *
                (1.0 - smoothstep(0.16, 0.48, steepness));
            float floor_ground_secondary_blend = 0.16 + ground_patch * 0.48;
            float vertical_patch = smoothstep(
                0.30,
                0.68,
                layered_noise(world_position.xz * 0.006 + vec2(73.0, -19.0)));
            float vertical_rock_blend = smoothstep(0.38, 0.82, steepness) *
                (0.18 + vertical_patch * 0.64);

            vec3 ground_primary = sample_triplanar(
                ground_primary_color,
                ground_primary_position,
                weights).rgb;
            vec3 ground_secondary = sample_triplanar(
                ground_secondary_color,
                ground_secondary_position,
                weights).rgb;
            vec3 mountain_primary = sample_triplanar(
                mountain_primary_color,
                mountain_primary_position,
                weights).rgb;
            vec3 mountain_vertical = sample_triplanar(
                mountain_vertical_color,
                mountain_vertical_position,
                weights).rgb;
            vec3 ground_macro = sample_triplanar(
                ground_primary_color,
                ground_macro_position,
                weights).rgb;
            vec3 mountain_macro = sample_triplanar(
                mountain_primary_color,
                mountain_macro_position,
                weights).rgb;
            vec3 ground = mix(ground_primary, ground_secondary, ground_secondary_blend);
            vec3 rock = mix(mountain_primary, mountain_vertical, vertical_rock_blend);
            vec3 surface = mix(ground, rock, rock_blend);
            vec3 macro_surface = mix(ground_macro, mountain_macro, rock_blend);
            if (ground_transition_blend > 0.0001) {
                vec3 floor_weights = vec3(0.0, 1.0, 0.0);
                vec3 floor_ground_primary = sample_triplanar(
                    ground_primary_color,
                    ground_primary_position,
                    floor_weights).rgb;
                vec3 floor_ground_secondary = sample_triplanar(
                    ground_secondary_color,
                    ground_secondary_position,
                    floor_weights).rgb;
                vec3 floor_ground = mix(
                    floor_ground_primary,
                    floor_ground_secondary,
                    floor_ground_secondary_blend);
                vec3 floor_macro = sample_triplanar(
                    ground_primary_color,
                    ground_macro_position,
                    floor_weights).rgb;
                surface = mix(surface, floor_ground, ground_transition_blend);
                macro_surface = mix(macro_surface, floor_macro, ground_transition_blend);
            }
            float broad = layered_noise(world_position.xz * 0.008);
            float medium = layered_noise(world_position.xz * 0.035 + vec2(41.0, -23.0));
            float variation = (broad - 0.5) * 2.0 * macro_variation_strength;
            variation += (medium - 0.5) * 0.025;
            float strata = 0.5 + 0.5 * sin(world_position.y * 0.34 + broad * 4.0);
            variation -= smoothstep(0.68, 0.96, strata) * rock_blend * 0.045;

            // Keep the photographed surfaces primary. A little desaturation suppresses isolated
            // terrestrial leaves and moss, while the normalized mission palette shifts hue
            // without replacing texture brightness or baking the old software lighting into PBR.
            // Near the cockpit the photographed rock remains primary. At distance, the same
            // material projected at formation scale replaces its busy tile detail, keeping the
            // large geological colour bands readable without a visible fade boundary.
            float near_macro_surface_blend = mix(0.14, 0.20, rock_blend);
            float far_macro_surface_blend = mix(0.48, 0.62, rock_blend);
            vec3 textured_albedo = mix(
                surface,
                macro_surface,
                mix(near_macro_surface_blend, far_macro_surface_blend, distance_detail_fade));
            textured_albedo = mix(
                textured_albedo,
                vec3(luminance(textured_albedo)),
                mix(0.16, 0.24, rock_blend));
            textured_albedo *= mix(vec3(1.0), palette_grade(albedo_tint.rgb), 0.22);
            // Retain only a small guard against the unusually broad PINK summit facets clipping
            // under direct sun. Surface colour and the level-authored atmosphere now do the work.
            float upward_face = mountain_surface * smoothstep(
                0.45,
                0.88,
                max(geometric_normal.y, 0.0));
            float upward_exposure = 1.0 - upward_face * upward_lighting_compensation;
            ALBEDO = clamp(
                textured_albedo * (1.0 + variation) * upward_exposure,
                vec3(0.0),
                vec3(1.0));

            vec3 ground_primary_world_normal = sample_triplanar_normal(
                ground_primary_normal,
                ground_primary_position,
                geometric_normal,
                weights);
            vec3 ground_secondary_world_normal = sample_triplanar_normal(
                ground_secondary_normal,
                ground_secondary_position,
                geometric_normal,
                weights);
            vec3 mountain_primary_world_normal = sample_triplanar_normal(
                mountain_primary_normal,
                mountain_primary_position,
                geometric_normal,
                weights);
            vec3 mountain_vertical_world_normal = sample_triplanar_normal(
                mountain_vertical_normal,
                mountain_vertical_position,
                geometric_normal,
                weights);
            vec3 ground_world_normal = normalize(mix(
                ground_primary_world_normal,
                ground_secondary_world_normal,
                ground_secondary_blend));
            vec3 mountain_world_normal = normalize(mix(
                mountain_primary_world_normal,
                mountain_vertical_world_normal,
                vertical_rock_blend));
            vec3 detail_world_normal = normalize(mix(
                ground_world_normal,
                mountain_world_normal,
                rock_blend));
            vec3 ground_macro_world_normal = sample_triplanar_normal(
                ground_primary_normal,
                ground_macro_position,
                geometric_normal,
                weights);
            vec3 mountain_macro_world_normal = sample_triplanar_normal(
                mountain_primary_normal,
                mountain_macro_position,
                geometric_normal,
                weights);
            vec3 macro_world_normal = normalize(mix(
                ground_macro_world_normal,
                mountain_macro_world_normal,
                rock_blend));
            float distance_macro_normal_blend = mix(
                macro_normal_blend,
                0.64,
                distance_detail_fade);
            detail_world_normal = normalize(mix(
                detail_world_normal,
                macro_world_normal,
                distance_macro_normal_blend));
            if (ground_transition_blend > 0.0001) {
                vec3 floor_weights = vec3(0.0, 1.0, 0.0);
                vec3 floor_primary_world_normal = sample_triplanar_normal(
                    ground_primary_normal,
                    ground_primary_position,
                    vec3(0.0, 1.0, 0.0),
                    floor_weights);
                vec3 floor_secondary_world_normal = sample_triplanar_normal(
                    ground_secondary_normal,
                    ground_secondary_position,
                    vec3(0.0, 1.0, 0.0),
                    floor_weights);
                vec3 floor_world_normal = normalize(mix(
                    floor_primary_world_normal,
                    floor_secondary_world_normal,
                    floor_ground_secondary_blend));
                detail_world_normal = normalize(mix(
                    detail_world_normal,
                    floor_world_normal,
                    ground_transition_blend));
            }
            // Preserve formation-scale relief while reducing the high-frequency shimmer that
            // makes far cliff faces look like a repeated normal map.
            detail_world_normal = normalize(mix(
                detail_world_normal,
                geometric_normal,
                distance_detail_fade * 0.34));
            NORMAL = normalize((VIEW_MATRIX * vec4(detail_world_normal, 0.0)).xyz);

            float ground_primary_surface_roughness = sample_triplanar(
                ground_primary_roughness,
                ground_primary_position,
                weights).r;
            float ground_secondary_surface_roughness = sample_triplanar(
                ground_secondary_roughness,
                ground_secondary_position,
                weights).r;
            float mountain_primary_surface_roughness = sample_triplanar(
                mountain_primary_roughness,
                mountain_primary_position,
                weights).r;
            float mountain_vertical_surface_roughness = sample_triplanar(
                mountain_vertical_roughness,
                mountain_vertical_position,
                weights).r;
            float ground_surface_roughness = mix(
                ground_primary_surface_roughness,
                ground_secondary_surface_roughness,
                ground_secondary_blend);
            float mountain_surface_roughness = mix(
                mountain_primary_surface_roughness,
                mountain_vertical_surface_roughness,
                vertical_rock_blend);
            float surface_roughness = mix(
                ground_surface_roughness,
                mountain_surface_roughness,
                rock_blend);
            float ground_macro_roughness = sample_triplanar(
                ground_primary_roughness,
                ground_macro_position,
                weights).r;
            float mountain_macro_roughness = sample_triplanar(
                mountain_primary_roughness,
                mountain_macro_position,
                weights).r;
            float macro_surface_roughness = mix(
                ground_macro_roughness,
                mountain_macro_roughness,
                rock_blend);
            if (ground_transition_blend > 0.0001) {
                vec3 floor_weights = vec3(0.0, 1.0, 0.0);
                float floor_primary_roughness = sample_triplanar(
                    ground_primary_roughness,
                    ground_primary_position,
                    floor_weights).r;
                float floor_secondary_roughness = sample_triplanar(
                    ground_secondary_roughness,
                    ground_secondary_position,
                    floor_weights).r;
                float floor_roughness = mix(
                    floor_primary_roughness,
                    floor_secondary_roughness,
                    floor_ground_secondary_blend);
                surface_roughness = mix(
                    surface_roughness,
                    floor_roughness,
                    ground_transition_blend);
            }
            float macro_roughness_variation =
                (broad - 0.5) * 0.050 + (medium - 0.5) * 0.018;
            float distance_roughness = macro_surface_roughness + macro_roughness_variation;
            ROUGHNESS = clamp(
                mix(surface_roughness, distance_roughness, distance_detail_fade * 0.52),
                0.78,
                0.95);
            SPECULAR = 0.20;
            METALLIC = 0.0;
        }
        """;

    private const string ShaderCode =
        """
        shader_type spatial;
        render_mode cull_back;

        uniform sampler2D sand_color : source_color, repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D sand_normal : hint_normal, repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D sand_roughness : repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D sand_height : repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D dune_color : source_color, repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D dune_normal : hint_normal, repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D dune_roughness : repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D dune_height : repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D hardpan_color : source_color, repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D hardpan_normal : hint_normal, repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D hardpan_roughness : repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D hardpan_height : repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D rock_color : source_color, repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D rock_normal : hint_normal, repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D rock_roughness : repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D rock_height : repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D stone_color : source_color, repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D stone_normal : hint_normal, repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D stone_roughness : repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D stone_height : repeat_enable, filter_linear_mipmap_anisotropic;
        uniform vec4 albedo_tint : source_color = vec4(1.0);

        // The source tiles are 3.5m square, but MW2's decoded world scale looks most convincing
        // with a broader visual footprint. This remains live-tunable in debug builds.
        uniform float texture_scale = 0.05;
        uniform float detail_strength = 0.78;
        uniform float normal_strength = 1.0;
        // Ground088 appears only on slopes and uses four times the base material footprint.
        uniform float rock_texture_scale = 0.25;
        uniform float dune_patch_coverage = 0.25;
        uniform float hardpan_patch_coverage = 0.08;
        uniform float stone_patch_coverage = 0.10;
        uniform float stone_texture_scale = 0.50;
        uniform float parallax_depth_metres = 0.18;
        uniform float debug_wireframe = 0.0;
        uniform float macro_variation_strength = 0.07;
        uniform float slope_blend_start = 0.021852;
        uniform float slope_blend_end = 0.211989;
        uniform float distance_detail_fade_start = 82.0;
        uniform float distance_detail_fade_end = 260.0;

        varying vec3 world_position;
        varying vec3 world_geometric_normal;

        float hash(vec2 point) {
            return fract(sin(dot(point, vec2(127.1, 311.7))) * 43758.5453);
        }

        float value_noise(vec2 point) {
            vec2 cell = floor(point);
            vec2 local = fract(point);
            local = local * local * (3.0 - 2.0 * local);
            return mix(
                mix(hash(cell), hash(cell + vec2(1.0, 0.0)), local.x),
                mix(hash(cell + vec2(0.0, 1.0)), hash(cell + vec2(1.0)), local.x),
                local.y);
        }

        float layered_noise(vec2 point) {
            return value_noise(point) * 0.62 +
                value_noise(point * 2.07 + vec2(17.3, -9.1)) * 0.27 +
                value_noise(point * 4.19 + vec2(-6.7, 23.4)) * 0.11;
        }

        float stone_patch_at(vec2 world_xz) {
            float stone_noise =
                value_noise(world_xz * 0.012) * 0.65 +
                value_noise((world_xz + vec2(31.7, -19.2)) * 0.027) * 0.35;
            float stone_threshold = 0.72 - stone_patch_coverage * 0.70;
            return smoothstep(stone_threshold, stone_threshold + 0.14, stone_noise);
        }

        // Ground097 forms large, wind-aligned dune fields only in lower, flat parts of the map.
        // Ground051 is a rarer, broken hardpan at hill feet and wind-scoured flat transitions.
        float dune_patch_at(vec2 world_xz) {
            float dune_noise = layered_noise(world_xz * 0.003 + vec2(8.3, -14.6));
            float dune_threshold = 0.80 - dune_patch_coverage * 0.80;
            return smoothstep(dune_threshold, dune_threshold + 0.12, dune_noise);
        }

        float hardpan_patch_at(vec2 world_xz) {
            float hardpan_noise =
                value_noise(world_xz * 0.010 + vec2(-11.4, 6.8)) * 0.70 +
                value_noise(world_xz * 0.024 + vec2(21.7, -15.2)) * 0.30;
            float hardpan_threshold = 0.84 - hardpan_patch_coverage * 1.00;
            return smoothstep(hardpan_threshold, hardpan_threshold + 0.10, hardpan_noise);
        }

        void vertex() {
            world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
            world_geometric_normal = normalize(MODEL_NORMAL_MATRIX * NORMAL);
        }

        vec3 triplanar_weights(vec3 normal) {
            vec3 weights = pow(abs(normal), vec3(5.0));
            return weights / max(weights.x + weights.y + weights.z, 0.0001);
        }

        vec4 sample_triplanar(sampler2D map, vec3 position, vec3 weights, float lod) {
            vec4 x_projection = textureLod(map, position.zy, lod);
            vec4 y_projection = textureLod(map, position.xz, lod);
            vec4 z_projection = textureLod(map, position.xy, lod);
            return x_projection * weights.x + y_projection * weights.y + z_projection * weights.z;
        }

        vec4 sample_triplanar_auto(sampler2D map, vec3 position, vec3 weights) {
            vec4 x_projection = texture(map, position.zy);
            vec4 y_projection = texture(map, position.xz);
            vec4 z_projection = texture(map, position.xy);
            return x_projection * weights.x + y_projection * weights.y + z_projection * weights.z;
        }

        // Offset the dominant triplanar projection using the authored displacement map. The
        // source mesh and physics stay untouched: this adds close-range depth without changing
        // MW2's terrain silhouette, feet placement or collision response.
        vec3 parallax_position(
            sampler2D height_map,
            vec3 position,
            vec3 geometric_normal,
            vec3 world_view,
            float coordinate_depth) {
            if (coordinate_depth <= 0.00001) {
                return position;
            }

            vec3 dominant = abs(geometric_normal);
            float height_value;
            float baseline_height;
            vec2 tangent_view;
            float normal_view;
            float axis_sign;
            if (dominant.y >= dominant.x && dominant.y >= dominant.z) {
                height_value = texture(height_map, position.xz).r;
                baseline_height = textureLod(height_map, position.xz, 6.0).r;
                tangent_view = world_view.xz;
                normal_view = world_view.y;
                axis_sign = sign(geometric_normal.y);
                vec2 offset = clamp(
                    tangent_view / max(abs(normal_view), 0.28),
                    vec2(-2.0),
                    vec2(2.0));
                position.xz -= offset * clamp(
                    height_value - baseline_height,
                    -0.35,
                    0.65) * coordinate_depth * axis_sign;
            } else if (dominant.x >= dominant.z) {
                height_value = texture(height_map, position.zy).r;
                baseline_height = textureLod(height_map, position.zy, 6.0).r;
                tangent_view = world_view.zy;
                normal_view = world_view.x;
                axis_sign = sign(geometric_normal.x);
                vec2 offset = clamp(
                    tangent_view / max(abs(normal_view), 0.28),
                    vec2(-2.0),
                    vec2(2.0));
                position.zy -= offset * clamp(
                    height_value - baseline_height,
                    -0.35,
                    0.65) * coordinate_depth * axis_sign;
            } else {
                height_value = texture(height_map, position.xy).r;
                baseline_height = textureLod(height_map, position.xy, 6.0).r;
                tangent_view = world_view.xy;
                normal_view = world_view.z;
                axis_sign = sign(geometric_normal.z);
                vec2 offset = clamp(
                    tangent_view / max(abs(normal_view), 0.28),
                    vec2(-2.0),
                    vec2(2.0));
                position.xy -= offset * clamp(
                    height_value - baseline_height,
                    -0.35,
                    0.65) * coordinate_depth * axis_sign;
            }
            return position;
        }

        vec3 axis_normal_x(vec3 tangent_normal, float axis_sign) {
            return vec3(tangent_normal.z * axis_sign, tangent_normal.y, tangent_normal.x * axis_sign);
        }

        vec3 axis_normal_y(vec3 tangent_normal, float axis_sign) {
            return vec3(tangent_normal.x, tangent_normal.z * axis_sign, -tangent_normal.y * axis_sign);
        }

        vec3 axis_normal_z(vec3 tangent_normal, float axis_sign) {
            return vec3(tangent_normal.x * axis_sign, tangent_normal.y, tangent_normal.z * axis_sign);
        }

        vec3 sample_triplanar_normal(
            sampler2D map,
            vec3 position,
            vec3 geometric_normal,
            vec3 weights) {
            vec3 x_normal = texture(map, position.zy).xyz * 2.0 - 1.0;
            vec3 y_normal = texture(map, position.xz).xyz * 2.0 - 1.0;
            vec3 z_normal = texture(map, position.xy).xyz * 2.0 - 1.0;
            x_normal.xy *= normal_strength;
            y_normal.xy *= normal_strength;
            z_normal.xy *= normal_strength;
            x_normal = normalize(x_normal);
            y_normal = normalize(y_normal);
            z_normal = normalize(z_normal);
            return normalize(
                axis_normal_x(x_normal, sign(geometric_normal.x)) * weights.x +
                axis_normal_y(y_normal, sign(geometric_normal.y)) * weights.y +
                axis_normal_z(z_normal, sign(geometric_normal.z)) * weights.z);
        }

        float luminance(vec3 color) {
            return dot(color, vec3(0.2126, 0.7152, 0.0722));
        }

        vec3 palette_grade(vec3 palette) {
            return clamp(
                palette / max(luminance(palette), 0.02),
                vec3(0.75),
                vec3(1.25));
        }

        void fragment() {
            vec3 displaced_face_normal = normalize(cross(dFdx(world_position), dFdy(world_position)));
            if (dot(displaced_face_normal, world_geometric_normal) < 0.0) {
                displaced_face_normal = -displaced_face_normal;
            }
            // The source WTB faces are extremely coarse. Retain a little of the displaced surface
            // direction, but let the relaxed shared-vertex normal own the broad lighting. A strong
            // face contribution exposes the original 1995 control diagonals as long dark creases.
            vec3 geometric_normal = normalize(mix(
                world_geometric_normal,
                displaced_face_normal,
                0.20));
            vec3 weights = triplanar_weights(geometric_normal);
            vec3 sample_position = world_position * texture_scale;
            vec3 stone_sample_position = sample_position * stone_texture_scale;
            vec3 world_view = normalize(CAMERA_POSITION_WORLD - world_position);
            float distance_detail_fade = smoothstep(
                distance_detail_fade_start,
                distance_detail_fade_end,
                distance(CAMERA_POSITION_WORLD, world_position));
            float parallax_distance_fade = 1.0 - smoothstep(
                80.0,
                140.0,
                distance(CAMERA_POSITION_WORLD, world_position));
            float visible_parallax_depth = parallax_depth_metres * parallax_distance_fade;
            vec3 sand_sample_position = parallax_position(
                sand_height,
                sample_position,
                geometric_normal,
                world_view,
                visible_parallax_depth * texture_scale * 0.22);
            vec3 dune_sample_position = parallax_position(
                dune_height,
                sample_position * 0.68,
                geometric_normal,
                world_view,
                visible_parallax_depth * texture_scale * 0.18);
            vec3 hardpan_sample_position = parallax_position(
                hardpan_height,
                sample_position * 0.72,
                geometric_normal,
                world_view,
                visible_parallax_depth * texture_scale * 0.30);
            vec3 rock_sample_position = parallax_position(
                rock_height,
                sample_position * rock_texture_scale,
                geometric_normal,
                world_view,
                visible_parallax_depth * texture_scale * rock_texture_scale * 0.55);
            stone_sample_position = parallax_position(
                stone_height,
                stone_sample_position,
                geometric_normal,
                world_view,
                visible_parallax_depth * texture_scale * stone_texture_scale);
            float steepness = 1.0 - abs(geometric_normal.y);
            float rock_blend = smoothstep(slope_blend_start, slope_blend_end, steepness);
            // Derived landforms carry alpha zero, their sealing skirts ramp toward one, and the
            // implicit floor is alpha one. Use that shared mask plus world height to let sand climb
            // irregularly over the lowest part of Wolf's hills, matching Jade's ground-contact
            // treatment while leaving the open desert floor unchanged.
            float base_blend_variation =
                (layered_noise(world_position.xz * 0.052 + vec2(-71.0, 13.0)) - 0.5) * 2.2;
            float derived_landform = 1.0 - smoothstep(0.98, 1.0, COLOR.a);
            float landform_sand_blend = derived_landform * (1.0 - smoothstep(
                0.10 + base_blend_variation,
                8.20 + base_blend_variation,
                world_position.y));
            float skirt_sand_blend = smoothstep(0.08, 0.96, COLOR.a);
            float ground_transition_blend = max(landform_sand_blend, skirt_sand_blend);
            rock_blend *= 1.0 - ground_transition_blend;

            float flatness = 1.0 - smoothstep(0.018, 0.060, steepness);
            float lowland = 1.0 - smoothstep(4.0, 18.0, max(world_position.y, 0.0));
            float dune_patch = dune_patch_at(world_position.xz) * flatness * lowland;
            float foothill = smoothstep(0.004, slope_blend_start * 1.8, steepness) *
                (1.0 - smoothstep(slope_blend_end * 0.55, slope_blend_end * 0.95, steepness));
            float exposed_flat = flatness * (1.0 - dune_patch);
            float hardpan_patch = hardpan_patch_at(world_position.xz) *
                max(foothill, exposed_flat * 0.55) * (1.0 - rock_blend * 0.65);

            // Two differently scaled noise fields keep the scattered-rock material in broad,
            // irregular patches instead of exposing another repeated square tile.
            float stone_patch = stone_patch_at(world_position.xz) *
                (1.0 - rock_blend) * (1.0 - dune_patch * 0.85);

            vec3 sand = sample_triplanar_auto(sand_color, sand_sample_position, weights).rgb;
            vec3 dunes = sample_triplanar_auto(dune_color, dune_sample_position, weights).rgb;
            vec3 hardpan = sample_triplanar_auto(
                hardpan_color,
                hardpan_sample_position,
                weights).rgb;
            vec3 rock = sample_triplanar_auto(rock_color, rock_sample_position, weights).rgb;
            vec3 stones = sample_triplanar_auto(
                stone_color,
                stone_sample_position,
                weights).rgb;
            vec3 broad_sand = sample_triplanar(sand_color, sand_sample_position, weights, 6.0).rgb;
            vec3 broad_dunes = sample_triplanar(
                dune_color,
                dune_sample_position,
                weights,
                6.0).rgb;
            vec3 broad_hardpan = sample_triplanar(
                hardpan_color,
                hardpan_sample_position,
                weights,
                6.0).rgb;
            vec3 broad_rock = sample_triplanar(rock_color, rock_sample_position, weights, 6.0).rgb;
            vec3 broad_stones = sample_triplanar(
                stone_color,
                stone_sample_position,
                weights,
                6.0).rgb;
            vec3 flat_surface_color = mix(sand, dunes, dune_patch);
            flat_surface_color = mix(flat_surface_color, hardpan, hardpan_patch);
            flat_surface_color = mix(flat_surface_color, stones, stone_patch);
            vec3 flat_broad_color = mix(broad_sand, broad_dunes, dune_patch);
            flat_broad_color = mix(flat_broad_color, broad_hardpan, hardpan_patch);
            flat_broad_color = mix(flat_broad_color, broad_stones, stone_patch);
            vec3 surface_color = mix(flat_surface_color, rock, rock_blend);
            vec3 broad_color = mix(flat_broad_color, broad_rock, rock_blend);

            float macro = value_noise(world_position.xz * 0.006);
            float distance_macro_variation_strength = mix(
                macro_variation_strength,
                macro_variation_strength * 1.55,
                distance_detail_fade);
            float macro_multiplier = 1.0 + (macro - 0.5) * 2.0 *
                distance_macro_variation_strength;
            // Use the photographed material as albedo, including its hardpan and charcoal-stone
            // colour relationships. The broad mip remains a stable distance-scale base while the
            // live detail control chooses how much fine texture survives. The old palette now
            // supplies only a normalized biome grade; coloured sun and sky perform illumination.
            float distance_detail_strength = mix(
                detail_strength,
                detail_strength * 0.30,
                distance_detail_fade);
            vec3 textured_albedo = mix(
                broad_color,
                surface_color,
                distance_detail_strength);
            textured_albedo = mix(vec3(luminance(textured_albedo)), textured_albedo, 0.92);
            textured_albedo *= mix(vec3(1.0), palette_grade(albedo_tint.rgb), 0.16);
            ALBEDO = clamp(
                textured_albedo * macro_multiplier,
                vec3(0.0),
                vec3(1.0));

            vec3 sand_world_normal = sample_triplanar_normal(
                sand_normal,
                sand_sample_position,
                geometric_normal,
                weights);
            vec3 dune_world_normal = sample_triplanar_normal(
                dune_normal,
                dune_sample_position,
                geometric_normal,
                weights);
            vec3 hardpan_world_normal = sample_triplanar_normal(
                hardpan_normal,
                hardpan_sample_position,
                geometric_normal,
                weights);
            vec3 rock_world_normal = sample_triplanar_normal(
                rock_normal,
                rock_sample_position,
                geometric_normal,
                weights);
            vec3 stone_world_normal = sample_triplanar_normal(
                stone_normal,
                stone_sample_position,
                geometric_normal,
                weights);
            vec3 flat_world_normal = normalize(mix(sand_world_normal, dune_world_normal, dune_patch));
            flat_world_normal = normalize(mix(flat_world_normal, hardpan_world_normal, hardpan_patch));
            flat_world_normal = normalize(mix(flat_world_normal, stone_world_normal, stone_patch));
            vec3 detail_world_normal = normalize(mix(
                flat_world_normal,
                rock_world_normal,
                rock_blend));
            // The low mips already preserve broad dune and hardpan form. Relax the remaining
            // fine normal texture toward the smoothed terrain normal at range so cracks do not
            // keep reading as deep, full-size grooves across the horizon.
            detail_world_normal = normalize(mix(
                detail_world_normal,
                geometric_normal,
                distance_detail_fade * 0.76));
            NORMAL = normalize((VIEW_MATRIX * vec4(detail_world_normal, 0.0)).xyz);

            float sand_surface_roughness = sample_triplanar_auto(
                sand_roughness,
                sand_sample_position,
                weights).r;
            float dune_surface_roughness = sample_triplanar_auto(
                dune_roughness,
                dune_sample_position,
                weights).r;
            float hardpan_surface_roughness = sample_triplanar_auto(
                hardpan_roughness,
                hardpan_sample_position,
                weights).r;
            float rock_surface_roughness = sample_triplanar_auto(
                rock_roughness,
                rock_sample_position,
                weights).r;
            float stone_surface_roughness = sample_triplanar_auto(
                stone_roughness,
                stone_sample_position,
                weights).r;
            float flat_surface_roughness = mix(
                sand_surface_roughness,
                dune_surface_roughness,
                dune_patch);
            flat_surface_roughness = mix(
                flat_surface_roughness,
                hardpan_surface_roughness,
                hardpan_patch);
            flat_surface_roughness = mix(
                flat_surface_roughness,
                stone_surface_roughness,
                stone_patch);
            float detailed_roughness = mix(
                0.90,
                mix(flat_surface_roughness, rock_surface_roughness, rock_blend),
                0.135);
            float macro_roughness = 0.90 + (macro - 0.5) * 0.085;
            // Dune fields read a touch softer and exposed hardpan a touch harsher from afar;
            // the effect is deliberately small so this remains material variation, not bands.
            macro_roughness += (hardpan_patch - dune_patch) * 0.025;
            ROUGHNESS = clamp(mix(
                detailed_roughness,
                macro_roughness,
                distance_detail_fade * 0.58),
                0.82,
                0.98);
            METALLIC = 0.0;
            if (debug_wireframe > 0.5) {
                ALBEDO = vec3(0.20, 1.0, 1.0);
                EMISSION = vec3(0.10, 0.65, 0.65);
                ROUGHNESS = 1.0;
            }
        }
        """;
}
