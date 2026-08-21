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
    private readonly ShaderMaterial m_vertexColorMaterial = CreateVertexColorMaterial();
    private readonly ShaderMaterial m_geometricNormalMaterial = CreateGeometricNormalMaterial();
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
        ApplyMode(m_meshes[^1]);
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
