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

/// <summary>
/// Builds the shared desert-biome PBR material used by derived MW2 terrain and its implicit fill.
/// </summary>
/// <remarks>
/// World-space triplanar projection avoids inventing UVs for the original terrain. Sand is used on
/// level ground, scattered stones break up open areas and sandstone is introduced by slope, while
/// MW2's authored palette remains the dominant source of color.
/// </remarks>
public static class TerrainSurfaceMaterial
{
    public const float Roughness = 0.9f;
    public const float TextureScale = 0.06f;
    public const float DetailStrength = 0.24f;
    public const float NormalStrength = 0.70f;
    public const float StonePatchCoverage = 0.10f;
    public const float StoneTextureScale = 0.50f;
    public const float ParallaxDepthMetres = 0.50f;
    public const float GeometryDisplacementStrength = 1.0f;
    public const float MountainMacroReliefMetres = 1.15f;
    public const float RockSlopeStartDegrees = 12.0f;
    public const float RockSlopeEndDegrees = 38.0f;
    public static readonly Color DesertBaseColor = new("9c8b6e");

    private const string SandColorPath =
        "res://Assets/Textures/Terrain/Ground054/Ground054_1K-PNG_Color.png";
    private const string SandNormalPath =
        "res://Assets/Textures/Terrain/Ground054/Ground054_1K-PNG_NormalGL.png";
    private const string SandRoughnessPath =
        "res://Assets/Textures/Terrain/Ground054/Ground054_1K-PNG_Roughness.png";
    private const string SandHeightPath =
        "res://Assets/Textures/Terrain/Ground054/Ground054_1K-PNG_Displacement.png";
    private const string RockColorPath =
        "res://Assets/Textures/Terrain/Ground088/Ground088_1K-PNG_Color.png";
    private const string RockNormalPath =
        "res://Assets/Textures/Terrain/Ground088/Ground088_1K-PNG_NormalGL.png";
    private const string RockRoughnessPath =
        "res://Assets/Textures/Terrain/Ground088/Ground088_1K-PNG_Roughness.png";
    private const string RockHeightPath =
        "res://Assets/Textures/Terrain/Ground088/Ground088_1K-PNG_Displacement.png";
    private const string StoneColorPath =
        "res://Assets/Textures/Terrain/Rocks019/Rocks019_1K-PNG_Color.png";
    private const string StoneNormalPath =
        "res://Assets/Textures/Terrain/Rocks019/Rocks019_1K-PNG_NormalGL.png";
    private const string StoneRoughnessPath =
        "res://Assets/Textures/Terrain/Rocks019/Rocks019_1K-PNG_Roughness.png";
    private const string StoneHeightPath =
        "res://Assets/Textures/Terrain/Rocks019/Rocks019_1K-PNG_Displacement.png";

    /// <summary>
    /// Creates one terrain material using the remastered biome colour unless an explicit tint is supplied.
    /// </summary>
    public static ShaderMaterial Create(Color? albedoTint = null, bool isImplicitGround = false)
    {
        var material = new ShaderMaterial
        {
            Shader = new Shader { Code = ShaderCode }
        };
        material.SetShaderParameter("albedo_tint", albedoTint ?? DesertBaseColor);
        material.SetShaderParameter("sand_color", GD.Load<Texture2D>(SandColorPath));
        material.SetShaderParameter("sand_normal", GD.Load<Texture2D>(SandNormalPath));
        material.SetShaderParameter("sand_roughness", GD.Load<Texture2D>(SandRoughnessPath));
        material.SetShaderParameter("sand_height", GD.Load<Texture2D>(SandHeightPath));
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
        material.SetShaderParameter("normal_strength", NormalStrength);
        material.SetShaderParameter("stone_patch_coverage", StonePatchCoverage);
        material.SetShaderParameter("stone_texture_scale", StoneTextureScale);
        material.SetShaderParameter("parallax_depth_metres", ParallaxDepthMetres);
        material.SetShaderParameter("debug_wireframe", 0.0f);
        material.SetShaderParameter("slope_blend_start", ToSteepness(RockSlopeStartDegrees));
        material.SetShaderParameter("slope_blend_end", ToSteepness(RockSlopeEndDegrees));
        return material;
    }

    /// <summary>
    /// Creates an unlit wireframe variant of the generated terrain geometry.
    /// </summary>
    public static ShaderMaterial CreateWireframe(
        Color? albedoTint = null,
        bool isImplicitGround = false)
    {
        var material = Create(albedoTint, isImplicitGround);
        material.Shader.Code = ShaderCode.Replace(
            "render_mode cull_back;",
            "render_mode cull_back, wireframe, unshaded;");
        material.SetShaderParameter("debug_wireframe", 1.0f);
        return material;
    }

    public static float ToSteepness(float slopeDegrees) =>
        1.0f - Mathf.Cos(Mathf.DegToRad(slopeDegrees));

    private const string ShaderCode =
        """
        shader_type spatial;
        render_mode cull_back;

        uniform sampler2D sand_color : source_color, repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D sand_normal : hint_normal, repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D sand_roughness : repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D sand_height : repeat_enable, filter_linear_mipmap_anisotropic;
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
        uniform float texture_scale = 0.06;
        uniform float detail_strength = 0.24;
        uniform float texture_color_strength = 0.08;
        uniform float normal_strength = 0.42;
        uniform float stone_patch_coverage = 0.10;
        uniform float stone_texture_scale = 0.50;
        uniform float parallax_depth_metres = 0.18;
        uniform float debug_wireframe = 0.0;
        uniform float macro_variation_strength = 0.07;
        uniform float slope_blend_start = 0.021852;
        uniform float slope_blend_end = 0.211989;

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

        void fragment() {
            vec3 displaced_face_normal = normalize(cross(dFdx(world_position), dFdy(world_position)));
            if (dot(displaced_face_normal, world_geometric_normal) < 0.0) {
                displaced_face_normal = -displaced_face_normal;
            }
            // The source WTB faces are extremely coarse. Retain the displaced surface direction,
            // but blend it with the shared-vertex terrain normal so lighting rolls across the new
            // tessellation rather than revealing every original 1995 polygon boundary.
            vec3 geometric_normal = normalize(mix(
                world_geometric_normal,
                displaced_face_normal,
                0.58));
            vec3 weights = triplanar_weights(geometric_normal);
            vec3 sample_position = world_position * texture_scale;
            vec3 stone_sample_position = sample_position * stone_texture_scale;
            vec3 world_view = normalize(CAMERA_POSITION_WORLD - world_position);
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
            vec3 rock_sample_position = parallax_position(
                rock_height,
                sample_position,
                geometric_normal,
                world_view,
                visible_parallax_depth * texture_scale * 0.55);
            stone_sample_position = parallax_position(
                stone_height,
                stone_sample_position,
                geometric_normal,
                world_view,
                visible_parallax_depth * texture_scale * stone_texture_scale);
            float steepness = 1.0 - abs(geometric_normal.y);
            float rock_blend = smoothstep(slope_blend_start, slope_blend_end, steepness);

            // Two differently scaled noise fields keep the scattered-rock material in broad,
            // irregular patches instead of exposing another repeated square tile.
            float stone_patch = stone_patch_at(world_position.xz) * (1.0 - rock_blend);

            vec3 sand = sample_triplanar(sand_color, sand_sample_position, weights, 0.0).rgb;
            vec3 rock = sample_triplanar(rock_color, rock_sample_position, weights, 0.0).rgb;
            vec3 stones = sample_triplanar(
                stone_color,
                stone_sample_position,
                weights,
                0.0).rgb;
            vec3 broad_sand = sample_triplanar(sand_color, sand_sample_position, weights, 6.0).rgb;
            vec3 broad_rock = sample_triplanar(rock_color, rock_sample_position, weights, 6.0).rgb;
            vec3 broad_stones = sample_triplanar(
                stone_color,
                stone_sample_position,
                weights,
                6.0).rgb;
            vec3 flat_surface_color = mix(sand, stones, stone_patch);
            vec3 flat_broad_color = mix(broad_sand, broad_stones, stone_patch);
            vec3 surface_color = mix(flat_surface_color, rock, rock_blend);
            vec3 broad_color = mix(flat_broad_color, broad_rock, rock_blend);

            // Remove the material's average brightness before applying it. The texture supplies
            // grain and restrained hue variation, but the MW2 vertex palette still chooses the land color.
            float local_contrast = clamp(
                luminance(surface_color) / max(luminance(broad_color), 0.02),
                0.45,
                1.35);
            vec3 texture_chroma = clamp(
                surface_color / max(vec3(luminance(surface_color)), vec3(0.02)),
                vec3(0.82),
                vec3(1.18));
            float surface_color_strength = mix(
                texture_color_strength,
                texture_color_strength * 2.0,
                rock_blend);
            float surface_detail_strength = mix(
                detail_strength,
                min(detail_strength * 1.55, 1.0),
                rock_blend);
            surface_detail_strength = max(surface_detail_strength, stone_patch * 0.75);
            vec3 detail_color = mix(vec3(1.0), texture_chroma, surface_color_strength);
            detail_color *= mix(1.0, local_contrast, surface_detail_strength);

            float macro = value_noise(world_position.xz * 0.006);
            float macro_multiplier = 1.0 + (macro - 0.5) * 2.0 * macro_variation_strength;
            // MW2's terrain polygons carry baked face-to-face shading intended for its software
            // renderer. A remastered terrain uses one biome colour and lets Godot's directional
            // sun, sky fill, shadows and PBR detail perform all illumination consistently.
            vec3 palette_albedo = albedo_tint.rgb * detail_color * macro_multiplier;

            // Rocks019 already contains sand that agrees with the mission surface and authored
            // charcoal stones. Preserve that complete colour relationship inside stone patches;
            // palette tinting the source made its granite appear sandy.
            ALBEDO = mix(palette_albedo, stones, stone_patch);

            vec3 sand_world_normal = sample_triplanar_normal(
                sand_normal,
                sand_sample_position,
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
            vec3 flat_world_normal = normalize(mix(
                sand_world_normal,
                stone_world_normal,
                stone_patch));
            vec3 detail_world_normal = normalize(mix(
                flat_world_normal,
                rock_world_normal,
                rock_blend));
            NORMAL = normalize((VIEW_MATRIX * vec4(detail_world_normal, 0.0)).xyz);

            float sand_surface_roughness = sample_triplanar(
                sand_roughness,
                sand_sample_position,
                weights,
                0.0).r;
            float rock_surface_roughness = sample_triplanar(
                rock_roughness,
                rock_sample_position,
                weights,
                0.0).r;
            float stone_surface_roughness = sample_triplanar(
                stone_roughness,
                stone_sample_position,
                weights,
                0.0).r;
            float flat_surface_roughness = mix(
                sand_surface_roughness,
                stone_surface_roughness,
                stone_patch);
            ROUGHNESS = mix(
                0.90,
                mix(flat_surface_roughness, rock_surface_roughness, rock_blend),
                0.45);
            METALLIC = 0.0;
            if (debug_wireframe > 0.5) {
                ALBEDO = vec3(0.20, 1.0, 1.0);
                EMISSION = vec3(0.10, 0.65, 0.65);
                ROUGHNESS = 1.0;
            }
        }
        """;
}
