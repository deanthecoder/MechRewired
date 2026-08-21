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

public enum TerrainDiagnosticMode
{
    Lit,
    LumaAlbedo,
    RawPaletteAlbedo,
    GeometricNormal,
    RockBlend,
    DirectSun,
    Roughness
}

/// <summary>
/// Applies debug-only material views to decoded terrain and the implicit ground without
/// changing scenery, actors or gameplay collision.
/// </summary>
public sealed partial class TerrainDiagnostics : Node
{
    private const float CurrentTerrainRoughness = 0.9f;
    private readonly List<TerrainMesh> m_meshes = new();
    private readonly HashSet<ShaderMaterial> m_litMaterials = new();
    private readonly ShaderMaterial m_vertexColorMaterial = CreateVertexColorMaterial();
    private readonly ShaderMaterial m_geometricNormalMaterial = CreateGeometricNormalMaterial();
    private readonly ShaderMaterial m_rockBlendMaterial = CreateRockBlendMaterial();
    private readonly ShaderMaterial m_directSunMaterial = CreateDirectSunMaterial();
    private readonly ShaderMaterial m_roughnessMaterial = CreateConstantMaterial(CurrentTerrainRoughness);
    private TerrainDiagnosticMode m_mode;

    public TerrainDiagnosticMode Mode
    {
        get => m_mode;
        set
        {
            m_mode = value;
            ApplyMode();
        }
    }

    public string ModeName => ToModeName(Mode);

    public int RegisteredMeshCount => m_meshes.Count;

    public float TextureScale { get; set; } = TerrainSurfaceMaterial.TextureScale;

    public float DetailStrength { get; set; } = TerrainSurfaceMaterial.DetailStrength;

    public float NormalStrength { get; set; } = TerrainSurfaceMaterial.NormalStrength;

    public float RockSlopeStartDegrees { get; set; } = TerrainSurfaceMaterial.RockSlopeStartDegrees;

    public float RockSlopeEndDegrees { get; set; } = TerrainSurfaceMaterial.RockSlopeEndDegrees;

    public void Register(
        MeshInstance3D instance,
        Mesh rawPaletteMesh,
        Mesh lumaAlbedoMesh = null)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(rawPaletteMesh);
        m_meshes.Add(new TerrainMesh(
            instance,
            instance.Mesh,
            instance.MaterialOverride,
            lumaAlbedoMesh ?? instance.Mesh,
            rawPaletteMesh));
        RegisterLitMaterial(instance.MaterialOverride);
        for (var surfaceIndex = 0; surfaceIndex < instance.Mesh.GetSurfaceCount(); surfaceIndex++)
        {
            RegisterLitMaterial(instance.Mesh.SurfaceGetMaterial(surfaceIndex));
        }
        ApplyMode(m_meshes[^1]);
    }

    /// <summary>
    /// Applies live debug-console values to every terrain material, including the implicit ground.
    /// </summary>
    public void ApplySurfaceTuning()
    {
        TextureScale = Mathf.Clamp(TextureScale, 0.02f, 2.0f);
        DetailStrength = Mathf.Clamp(DetailStrength, 0.0f, 1.0f);
        NormalStrength = Mathf.Clamp(NormalStrength, 0.0f, 2.0f);
        RockSlopeStartDegrees = Mathf.Clamp(RockSlopeStartDegrees, 0.0f, 80.0f);
        RockSlopeEndDegrees = Mathf.Clamp(
            RockSlopeEndDegrees,
            RockSlopeStartDegrees + 1.0f,
            89.0f);
        var slopeBlendStart = TerrainSurfaceMaterial.ToSteepness(RockSlopeStartDegrees);
        var slopeBlendEnd = TerrainSurfaceMaterial.ToSteepness(RockSlopeEndDegrees);
        foreach (var material in m_litMaterials)
        {
            material.SetShaderParameter("texture_scale", TextureScale);
            material.SetShaderParameter("detail_strength", DetailStrength);
            material.SetShaderParameter("normal_strength", NormalStrength);
            material.SetShaderParameter("slope_blend_start", slopeBlendStart);
            material.SetShaderParameter("slope_blend_end", slopeBlendEnd);
        }
        m_rockBlendMaterial.SetShaderParameter("slope_blend_start", slopeBlendStart);
        m_rockBlendMaterial.SetShaderParameter("slope_blend_end", slopeBlendEnd);
    }

    private void RegisterLitMaterial(Godot.Material material)
    {
        if (material is not ShaderMaterial shaderMaterial || !m_litMaterials.Add(shaderMaterial))
        {
            return;
        }

        shaderMaterial.SetShaderParameter("texture_scale", TextureScale);
        shaderMaterial.SetShaderParameter("detail_strength", DetailStrength);
        shaderMaterial.SetShaderParameter("normal_strength", NormalStrength);
        shaderMaterial.SetShaderParameter(
            "slope_blend_start",
            TerrainSurfaceMaterial.ToSteepness(RockSlopeStartDegrees));
        shaderMaterial.SetShaderParameter(
            "slope_blend_end",
            TerrainSurfaceMaterial.ToSteepness(RockSlopeEndDegrees));
    }

    public bool TrySetMode(string name)
    {
        switch (name.Trim().ToLowerInvariant())
        {
            case "lit":
            case "final":
                Mode = TerrainDiagnosticMode.Lit;
                return true;
            case "albedo":
            case "luma":
            case "luma-albedo":
                Mode = TerrainDiagnosticMode.LumaAlbedo;
                return true;
            case "raw":
            case "palette":
            case "raw-palette":
                Mode = TerrainDiagnosticMode.RawPaletteAlbedo;
                return true;
            case "normal":
            case "normals":
                Mode = TerrainDiagnosticMode.GeometricNormal;
                return true;
            case "rock":
            case "sandstone":
            case "blend":
                Mode = TerrainDiagnosticMode.RockBlend;
                return true;
            case "directsun":
            case "sun":
                Mode = TerrainDiagnosticMode.DirectSun;
                return true;
            case "roughness":
                Mode = TerrainDiagnosticMode.Roughness;
                return true;
            default:
                return false;
        }
    }

    private void ApplyMode()
    {
        foreach (var mesh in m_meshes)
        {
            ApplyMode(mesh);
        }
    }

    private void ApplyMode(TerrainMesh terrainMesh)
    {
        terrainMesh.Instance.Mesh = Mode switch
        {
            TerrainDiagnosticMode.LumaAlbedo => terrainMesh.LumaAlbedoMesh,
            TerrainDiagnosticMode.RawPaletteAlbedo => terrainMesh.RawPaletteMesh,
            _ => terrainMesh.LitMesh
        };
        terrainMesh.Instance.MaterialOverride = Mode switch
        {
            TerrainDiagnosticMode.Lit => terrainMesh.LitMaterialOverride,
            TerrainDiagnosticMode.LumaAlbedo or TerrainDiagnosticMode.RawPaletteAlbedo =>
                m_vertexColorMaterial,
            TerrainDiagnosticMode.GeometricNormal => m_geometricNormalMaterial,
            TerrainDiagnosticMode.RockBlend => m_rockBlendMaterial,
            TerrainDiagnosticMode.DirectSun => m_directSunMaterial,
            TerrainDiagnosticMode.Roughness => m_roughnessMaterial,
            _ => null
        };
    }

    private static ShaderMaterial CreateVertexColorMaterial() => CreateShaderMaterial(
        """
        shader_type spatial;
        render_mode unshaded, cull_back, fog_disabled;

        void fragment() {
            ALBEDO = COLOR.rgb;
        }
        """);

    private static ShaderMaterial CreateGeometricNormalMaterial() => CreateShaderMaterial(
        """
        shader_type spatial;
        render_mode unshaded, cull_back, fog_disabled;
        varying vec3 world_geometric_normal;

        void vertex() {
            world_geometric_normal = normalize(MODEL_NORMAL_MATRIX * NORMAL);
        }

        void fragment() {
            ALBEDO = world_geometric_normal * 0.5 + 0.5;
        }
        """);

    private static ShaderMaterial CreateRockBlendMaterial() => CreateShaderMaterial(
        """
        shader_type spatial;
        render_mode unshaded, cull_back, fog_disabled;
        uniform float slope_blend_start = 0.021852;
        uniform float slope_blend_end = 0.211989;
        varying vec3 world_geometric_normal;

        void vertex() {
            world_geometric_normal = normalize(MODEL_NORMAL_MATRIX * NORMAL);
        }

        void fragment() {
            float steepness = 1.0 - abs(normalize(world_geometric_normal).y);
            float rock = smoothstep(slope_blend_start, slope_blend_end, steepness);
            ALBEDO = mix(vec3(0.76, 0.66, 0.34), vec3(0.76, 0.18, 0.04), rock);
        }
        """);

    private static ShaderMaterial CreateDirectSunMaterial() => CreateShaderMaterial(
        """
        shader_type spatial;
        render_mode ambient_light_disabled, shadows_disabled, specular_disabled, fog_disabled;

        void fragment() {
            ALBEDO = vec3(1.0);
            METALLIC = 0.0;
            ROUGHNESS = 1.0;
        }

        void light() {
            if (LIGHT_IS_DIRECTIONAL) {
                float direct = max(dot(normalize(NORMAL), normalize(LIGHT)), 0.0);
                DIFFUSE_LIGHT += vec3(direct);
            }
        }
        """);

    private static ShaderMaterial CreateConstantMaterial(float value) => CreateShaderMaterial(
        $$"""
        shader_type spatial;
        render_mode unshaded, cull_back, fog_disabled;

        void fragment() {
            ALBEDO = vec3({{value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}});
        }
        """);

    private static ShaderMaterial CreateShaderMaterial(string code) => new()
    {
        Shader = new Shader { Code = code }
    };

    private static string ToModeName(TerrainDiagnosticMode mode) => mode switch
    {
        TerrainDiagnosticMode.LumaAlbedo => "luma albedo",
        TerrainDiagnosticMode.RawPaletteAlbedo => "raw palette albedo",
        TerrainDiagnosticMode.GeometricNormal => "geometric normal",
        TerrainDiagnosticMode.RockBlend => "rock blend",
        TerrainDiagnosticMode.DirectSun => "direct sun",
        _ => mode.ToString().ToLowerInvariant()
    };

    private sealed record TerrainMesh(
        MeshInstance3D Instance,
        Mesh LitMesh,
        Godot.Material LitMaterialOverride,
        Mesh LumaAlbedoMesh,
        Mesh RawPaletteMesh);
}
