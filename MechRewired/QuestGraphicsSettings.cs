// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.

using Godot;

namespace MechRewired;

/// <summary>
/// The small set of renderer switches which make a material difference on Quest.
/// Keeping these here gives the VR menu a deliberate, bounded surface instead of
/// exposing renderer internals or debug tuning controls.
/// </summary>
public sealed class QuestGraphicsSettings
{
    private readonly MissionSkyController m_sky;
    private readonly PlayerCockpit m_cockpit;
    private readonly IReadOnlyList<ShaderMaterial> m_terrainMaterials;
    private readonly BattlefieldEffects m_effects;
    private readonly float[] m_defaultParallaxDepths;

    public QuestGraphicsSettings(
        MissionSkyController sky,
        PlayerCockpit cockpit = null,
        IEnumerable<ShaderMaterial> terrainMaterials = null,
        BattlefieldEffects effects = null)
    {
        m_sky = sky ?? throw new ArgumentNullException(nameof(sky));
        m_cockpit = cockpit;
        m_effects = effects;
        m_terrainMaterials = terrainMaterials?.Where(material => material != null).Distinct().ToArray() ?? [];
        m_defaultParallaxDepths = m_terrainMaterials
            .Select(material => material.GetShaderParameter("parallax_depth_metres").AsSingle())
            .ToArray();

        // The headset baseline avoids the full-screen and compositor passes first.
        SunShadowsEnabled = false;
        AmbientOcclusionEnabled = false;
        ScreenReflectionsEnabled = false;
        GlowEnabled = false;
        // The compositor flare has not been validated in stereo, so it remains unavailable
        // from the headset menu and disabled for the Quest baseline.
        LensFlareEnabled = false;
        CockpitGlassEnabled = false;
        TerrainParallaxEnabled = false;
        SmokeAndDustEnabled = false;
    }

    public bool SunShadowsEnabled
    {
        get => m_sky.SunShadowsEnabled;
        set => m_sky.SunShadowsEnabled = value;
    }

    public bool AmbientOcclusionEnabled
    {
        get => m_sky.AmbientOcclusionEnabled;
        set => m_sky.AmbientOcclusionEnabled = value;
    }

    public bool ScreenReflectionsEnabled
    {
        get => m_sky.ScreenReflectionsEnabled;
        set => m_sky.ScreenReflectionsEnabled = value;
    }

    public bool GlowEnabled
    {
        get => m_sky.GlowEnabled;
        set => m_sky.GlowEnabled = value;
    }

    public bool LensFlareEnabled
    {
        get => m_sky.LensFlareEnabled;
        set => m_sky.LensFlareEnabled = value;
    }

    public bool CockpitGlassEnabled
    {
        get => m_cockpit?.GlassEnabled ?? false;
        set
        {
            if (m_cockpit != null)
            {
                m_cockpit.GlassEnabled = value;
            }
        }
    }

    public bool TerrainParallaxEnabled
    {
        get => m_terrainMaterials.Count > 0 &&
               m_terrainMaterials.Any(material => material.GetShaderParameter("parallax_depth_metres").AsSingle() > 0.0f);
        set
        {
            for (var index = 0; index < m_terrainMaterials.Count; index++)
            {
                m_terrainMaterials[index].SetShaderParameter(
                    "parallax_depth_metres",
                    value ? m_defaultParallaxDepths[index] : 0.0f);
            }
        }
    }

    public bool SmokeAndDustEnabled
    {
        get => m_effects?.SmokeAndDustEnabled ?? false;
        set
        {
            if (m_effects != null)
            {
                m_effects.SmokeAndDustEnabled = value;
            }
        }
    }
}
