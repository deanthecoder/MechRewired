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
/// Builds the shared, palette-preserving PBR material used by MW2 terrain and its implicit fill.
/// </summary>
/// <remarks>
/// World-space triplanar projection avoids inventing UVs for the original terrain. Sand is used on
/// level ground and sandstone is introduced by slope, while MW2's authored palette remains the
/// dominant source of color.
/// </remarks>
public static class TerrainSurfaceMaterial
{
    public const float Roughness = 0.9f;
    public const float TextureScale = 0.06f;
    public const float DetailStrength = 0.24f;
    public const float NormalStrength = 0.42f;
    public const float RockSlopeStartDegrees = 12.0f;
    public const float RockSlopeEndDegrees = 38.0f;

    private const string SandColorPath =
        "res://Assets/Textures/Terrain/Ground054/Ground054_1K-PNG_Color.png";
    private const string SandNormalPath =
        "res://Assets/Textures/Terrain/Ground054/Ground054_1K-PNG_NormalGL.png";
    private const string SandRoughnessPath =
        "res://Assets/Textures/Terrain/Ground054/Ground054_1K-PNG_Roughness.png";
    private const string RockColorPath =
        "res://Assets/Textures/Terrain/Ground088/Ground088_1K-PNG_Color.png";
    private const string RockNormalPath =
        "res://Assets/Textures/Terrain/Ground088/Ground088_1K-PNG_NormalGL.png";
    private const string RockRoughnessPath =
        "res://Assets/Textures/Terrain/Ground088/Ground088_1K-PNG_Roughness.png";

    /// <summary>
    /// Creates one terrain material, optionally tinting a synthetic mesh which has no vertex color.
    /// </summary>
    public static ShaderMaterial Create(Color? albedoTint = null)
    {
        var material = new ShaderMaterial
        {
            Shader = new Shader { Code = ShaderCode }
        };
        material.SetShaderParameter("albedo_tint", albedoTint ?? Colors.White);
        material.SetShaderParameter("sand_color", GD.Load<Texture2D>(SandColorPath));
        material.SetShaderParameter("sand_normal", GD.Load<Texture2D>(SandNormalPath));
        material.SetShaderParameter("sand_roughness", GD.Load<Texture2D>(SandRoughnessPath));
        material.SetShaderParameter("rock_color", GD.Load<Texture2D>(RockColorPath));
        material.SetShaderParameter("rock_normal", GD.Load<Texture2D>(RockNormalPath));
        material.SetShaderParameter("rock_roughness", GD.Load<Texture2D>(RockRoughnessPath));
        material.SetShaderParameter("texture_scale", TextureScale);
        material.SetShaderParameter("detail_strength", DetailStrength);
        material.SetShaderParameter("normal_strength", NormalStrength);
        material.SetShaderParameter("slope_blend_start", ToSteepness(RockSlopeStartDegrees));
        material.SetShaderParameter("slope_blend_end", ToSteepness(RockSlopeEndDegrees));
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
        uniform sampler2D rock_color : source_color, repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D rock_normal : hint_normal, repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D rock_roughness : repeat_enable, filter_linear_mipmap_anisotropic;
        uniform vec4 albedo_tint : source_color = vec4(1.0);

        // The source tiles are 3.5m square, but MW2's decoded world scale looks most convincing
        // with a broader visual footprint. This remains live-tunable in debug builds.
        uniform float texture_scale = 0.06;
        uniform float detail_strength = 0.24;
        uniform float texture_color_strength = 0.08;
        uniform float normal_strength = 0.42;
        uniform float macro_variation_strength = 0.07;
        uniform float slope_blend_start = 0.021852;
        uniform float slope_blend_end = 0.211989;

        varying vec3 world_position;
        varying vec3 world_geometric_normal;

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

        void fragment() {
            vec3 geometric_normal = normalize(world_geometric_normal);
            vec3 weights = triplanar_weights(geometric_normal);
            vec3 sample_position = world_position * texture_scale;
            float steepness = 1.0 - abs(geometric_normal.y);
            float rock_blend = smoothstep(slope_blend_start, slope_blend_end, steepness);

            vec3 sand = sample_triplanar(sand_color, sample_position, weights, 0.0).rgb;
            vec3 rock = sample_triplanar(rock_color, sample_position, weights, 0.0).rgb;
            vec3 broad_sand = sample_triplanar(sand_color, sample_position, weights, 6.0).rgb;
            vec3 broad_rock = sample_triplanar(rock_color, sample_position, weights, 6.0).rgb;
            vec3 surface_color = mix(sand, rock, rock_blend);
            vec3 broad_color = mix(broad_sand, broad_rock, rock_blend);

            // Remove the material's average brightness before applying it. The texture supplies
            // grain and restrained hue variation, but the MW2 vertex palette still chooses the land color.
            float local_contrast = clamp(
                luminance(surface_color) / max(luminance(broad_color), 0.02),
                0.70,
                1.30);
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
            vec3 detail_color = mix(vec3(1.0), texture_chroma, surface_color_strength);
            detail_color *= mix(1.0, local_contrast, surface_detail_strength);

            float macro = value_noise(world_position.xz * 0.006);
            float macro_multiplier = 1.0 + (macro - 0.5) * 2.0 * macro_variation_strength;
            ALBEDO = COLOR.rgb * albedo_tint.rgb * detail_color * macro_multiplier;

            vec3 sand_world_normal = sample_triplanar_normal(
                sand_normal,
                sample_position,
                geometric_normal,
                weights);
            vec3 rock_world_normal = sample_triplanar_normal(
                rock_normal,
                sample_position,
                geometric_normal,
                weights);
            vec3 detail_world_normal = normalize(mix(sand_world_normal, rock_world_normal, rock_blend));
            NORMAL = normalize((VIEW_MATRIX * vec4(detail_world_normal, 0.0)).xyz);

            float sand_surface_roughness = sample_triplanar(
                sand_roughness,
                sample_position,
                weights,
                0.0).r;
            float rock_surface_roughness = sample_triplanar(
                rock_roughness,
                sample_position,
                weights,
                0.0).r;
            ROUGHNESS = mix(0.90, mix(sand_surface_roughness, rock_surface_roughness, rock_blend), 0.45);
            METALLIC = 0.0;
        }
        """;
}
