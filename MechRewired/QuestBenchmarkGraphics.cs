// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.

using Godot;

namespace MechRewired;

/// <summary>
/// Temporary, in-memory graphics ablations for the repeatable Quest benchmark.
/// This deliberately bypasses the graphics preferences: every change is restored by <see cref="Dispose"/>.
/// </summary>
public sealed class QuestBenchmarkGraphics : IDisposable
{
    private const string AlbedoOnlyShaderCode = """
        shader_type spatial;
        render_mode unshaded, cull_back;
        uniform sampler2D uv_ground_color : source_color, repeat_enable, filter_linear_mipmap;
        uniform sampler2D uv_rock_color : source_color, repeat_enable, filter_linear_mipmap;
        uniform float uv_ground_scale = 0.05;
        uniform float uv_rock_scale = 0.05;
        uniform bool uv_mountain_surface = false;
        uniform vec4 albedo_tint : source_color = vec4(1.0);
        varying float rock_weight;
        void vertex() {
            float upward = abs(normalize(MODEL_NORMAL_MATRIX * NORMAL).y);
            rock_weight = (uv_mountain_surface ? 1.0 : smoothstep(0.021852, 0.211989, 1.0 - upward)) * (1.0 - COLOR.a);
        }
        void fragment() {
            vec3 ground = texture(uv_ground_color, UV * uv_ground_scale).rgb;
            vec3 rock = texture(uv_rock_color, UV * uv_rock_scale).rgb;
            vec3 colour = mix(ground, rock, rock_weight);
            float tint_luminance = dot(albedo_tint.rgb, vec3(0.2126, 0.7152, 0.0722));
            vec3 grade = clamp(albedo_tint.rgb / max(tint_luminance, 0.02), vec3(0.75), vec3(1.25));
            ALBEDO = clamp(colour * mix(vec3(1.0), grade, 0.16), vec3(0.0), vec3(1.0));
            ROUGHNESS = 1.0;
        }
        """;

    private static readonly string[] s_variantNames =
    ["baseline", "baked-profile", "baked-profile-unlit-terrain", "baked-profile-glass"];

    private readonly List<(ShaderMaterial Material, Shader Shader)> m_terrainShaders = [];
    private readonly PlayerCockpit m_cockpit;
    private readonly bool m_cockpitInteriorLightsEnabled;
    private readonly bool m_cockpitQuestUvMaterialsEnabled;
    private readonly bool m_cockpitBakedInteriorLightingEnabled;
    private readonly Godot.Environment m_environment;
    private readonly Godot.Environment.BGMode m_backgroundMode;
    private readonly Color m_backgroundColor;
    private readonly Sky m_backgroundSky;
    private readonly Sky m_proceduralSky;
    private readonly Node m_skyDome;
    private readonly Node.ProcessModeEnum m_skyDomeProcessMode;
    private readonly Variant m_cumulusPosition;
    private readonly Variant m_cirrusPosition1;
    private readonly Variant m_cirrusPosition2;
    private readonly ShaderMaterial m_skyMaterial;
    private readonly DirectionalLight3D m_sun;
    private readonly bool m_sunShadowsEnabled;
    private readonly bool m_glowEnabled;
    private readonly bool m_cockpitGlassEnabled;
    private readonly Dictionary<Shader, Shader> m_albedoOnlyShaders = [];
    private readonly Node m_owner;
    private Sky m_panoramaSky;
    private bool m_disposed;

    public bool BakedSkyEnabled => m_panoramaSky != null && m_environment?.Sky == m_panoramaSky;

    /// <summary>Baseline forces Quest's cheap two-sample UV terrain path; it does not save a preference.</summary>
    public static IReadOnlyList<string> VariantNames => s_variantNames;

    public QuestBenchmarkGraphics(Node missionRoot, PlayerMech player, Sky proceduralSky)
    {
        ArgumentNullException.ThrowIfNull(missionRoot);
        ArgumentNullException.ThrowIfNull(player);
        m_owner = missionRoot;
        m_proceduralSky = proceduralSky ?? throw new ArgumentNullException(nameof(proceduralSky));
        m_cockpit = player.Cockpit;
        m_cockpitInteriorLightsEnabled = m_cockpit?.InteriorLightsEnabled ?? true;
        m_cockpitQuestUvMaterialsEnabled = m_cockpit?.QuestUvMaterialsEnabled ?? false;
        m_cockpitBakedInteriorLightingEnabled = m_cockpit?.BakedInteriorLightingEnabled ?? false;

        foreach (var node in Descendants(missionRoot))
        {
            if (node is WorldEnvironment world && m_environment == null)
            {
                m_environment = world.Environment;
                m_backgroundMode = m_environment?.BackgroundMode ?? Godot.Environment.BGMode.ClearColor;
                m_backgroundColor = m_environment?.BackgroundColor ?? Colors.Black;
                m_backgroundSky = m_environment?.Sky;
            }
            if (node.Name == "SkyDome")
            {
                m_skyDome = node;
                m_skyDomeProcessMode = node.ProcessMode;
            }
            if (node is DirectionalLight3D sun && node.Name == "SunLight")
            {
                m_sun = sun;
            }
            if (node is GeometryInstance3D geometry)
            {
                CaptureTerrainMaterial(geometry.MaterialOverride);
                if (geometry is MeshInstance3D meshInstance && meshInstance.Mesh != null)
                {
                    for (var index = 0; index < meshInstance.Mesh.GetSurfaceCount(); ++index)
                        CaptureTerrainMaterial(meshInstance.GetSurfaceOverrideMaterial(index) ?? meshInstance.Mesh.SurfaceGetMaterial(index));
                }
            }
        }
        m_sunShadowsEnabled = m_sun?.ShadowEnabled ?? false;
        m_glowEnabled = m_environment?.GlowEnabled ?? false;
        m_cockpitGlassEnabled = m_cockpit?.GlassEnabled ?? false;
        if (m_skyDome != null)
        {
            m_cumulusPosition = m_skyDome.Get("_cumulus_position");
            m_cirrusPosition1 = m_skyDome.Get("_cirrus_position1");
            m_cirrusPosition2 = m_skyDome.Get("_cirrus_position2");
            m_skyMaterial = m_skyDome.Get("sky_material").As<ShaderMaterial>();
        }
    }

    /// <summary>Resets to the cheap Quest baseline before applying one named profile.</summary>
    public async Task ApplyAsync(string variant)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (!s_variantNames.Contains(variant, StringComparer.Ordinal))
            throw new ArgumentOutOfRangeException(nameof(variant), variant, "Unknown Quest benchmark graphics variant.");
        ResetToBaseline();
        if (variant is "baked-profile" or "baked-profile-unlit-terrain" or "baked-profile-glass")
        {
            await ApplyPanoramaSkyAsync();
            if (m_cockpit != null)
            {
                m_cockpit.QuestUvMaterialsEnabled = true;
                m_cockpit.BakedInteriorLightingEnabled = true;
            }
            if (variant == "baked-profile-unlit-terrain")
                foreach (var (material, shader) in m_terrainShaders) material.Shader = GetAlbedoOnlyShader(shader);
            if (variant == "baked-profile-glass" && m_cockpit != null) m_cockpit.GlassEnabled = true;
        }
    }

    public void Dispose()
    {
        if (m_disposed) return;
        m_disposed = true;
        foreach (var (material, shader) in m_terrainShaders) material.Shader = shader;
        if (m_cockpit != null) m_cockpit.InteriorLightsEnabled = m_cockpitInteriorLightsEnabled;
        if (m_cockpit != null) m_cockpit.QuestUvMaterialsEnabled = m_cockpitQuestUvMaterialsEnabled;
        if (m_cockpit != null) m_cockpit.BakedInteriorLightingEnabled = m_cockpitBakedInteriorLightingEnabled;
        if (m_cockpit != null) m_cockpit.GlassEnabled = m_cockpitGlassEnabled;
        if (m_sun != null) m_sun.ShadowEnabled = m_sunShadowsEnabled;
        if (m_environment != null) m_environment.GlowEnabled = m_glowEnabled;
        RestoreSky();
    }

    private void ResetToBaseline()
    {
        foreach (var (material, shader) in m_terrainShaders) material.Shader = shader;
        SetTerrainTriplanar(false);
        if (m_cockpit != null) m_cockpit.InteriorLightsEnabled = m_cockpitInteriorLightsEnabled;
        if (m_cockpit != null)
        {
            m_cockpit.QuestUvMaterialsEnabled = false;
            m_cockpit.BakedInteriorLightingEnabled = false;
        }
        if (m_cockpit != null) m_cockpit.GlassEnabled = false;
        if (m_sun != null) m_sun.ShadowEnabled = false;
        if (m_environment != null) m_environment.GlowEnabled = false;
        RestoreSky(false);
        if (m_skyDome != null) m_skyDome.ProcessMode = Node.ProcessModeEnum.Always;
        ResetAnimation();
    }

    /// <summary>Starts each sky sample at the same cloud phase without saving any game setting.</summary>
    public void ResetAnimation()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        SetSkyPositions(Vector2.Zero, Vector2.Zero, Vector2.Zero);
    }

    private void SetTerrainTriplanar(bool enabled)
    {
        foreach (var (material, _) in m_terrainShaders) TerrainSurfaceMaterial.SetTriplanarEnabled(material, enabled);
    }

    private async Task ApplyPanoramaSkyAsync()
    {
        if (m_environment?.Sky == null)
            throw new InvalidOperationException("The mission has no Sky resource to cache.");
        if (m_skyDome != null) m_skyDome.ProcessMode = Node.ProcessModeEnum.Disabled;
        m_panoramaSky ??= await QuestCachedSky.CreateAsync(m_owner, m_environment, m_proceduralSky);
        m_environment.Sky = m_panoramaSky;
    }

    private void RestoreSky(bool original = true)
    {
        if (m_environment == null) return;
        m_environment.BackgroundMode = m_backgroundMode;
        m_environment.BackgroundColor = m_backgroundColor;
        m_environment.Sky = original ? m_backgroundSky : m_proceduralSky;
        if (m_skyDome != null)
        {
            m_skyDome.ProcessMode = m_skyDomeProcessMode;
            SetSkyPositions(m_cumulusPosition, m_cirrusPosition1, m_cirrusPosition2);
        }
    }

    private void SetSkyPositions(Variant cumulus, Variant cirrus1, Variant cirrus2)
    {
        if (m_skyDome == null) return;
        m_skyDome.Set("_cumulus_position", cumulus);
        m_skyDome.Set("_cirrus_position1", cirrus1);
        m_skyDome.Set("_cirrus_position2", cirrus2);
        m_skyMaterial?.SetShaderParameter("cumulus_position", cumulus);
        m_skyMaterial?.SetShaderParameter("cirrus_position1", cirrus1);
        m_skyMaterial?.SetShaderParameter("cirrus_position2", cirrus2);
    }

    private bool CaptureTerrainMaterial(Godot.Material material)
    {
        if (material is not ShaderMaterial shader || !TerrainSurfaceMaterial.SupportsTriplanarToggle(shader) ||
            m_terrainShaders.Any(item => item.Material == shader)) return material is ShaderMaterial existing && TerrainSurfaceMaterial.SupportsTriplanarToggle(existing);
        m_terrainShaders.Add((shader, shader.Shader));
        return true;
    }

    private Shader GetAlbedoOnlyShader(Shader original)
    {
        if (m_albedoOnlyShaders.TryGetValue(original, out var shader)) return shader;
        shader = new Shader
        {
            Code = original.Code.Contains("cull_disabled", StringComparison.Ordinal)
                ? AlbedoOnlyShaderCode.Replace("cull_back", "cull_disabled")
                : AlbedoOnlyShaderCode
        };
        m_albedoOnlyShaders.Add(original, shader);
        return shader;
    }

    private static IEnumerable<Node> Descendants(Node root)
    {
        var pending = new Queue<Node>();
        pending.Enqueue(root);
        while (pending.Count > 0)
        {
            var node = pending.Dequeue();
            yield return node;
            foreach (Node child in node.GetChildren()) pending.Enqueue(child);
        }
    }
}
