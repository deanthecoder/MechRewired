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
    private readonly PlayerHud m_hud;
    private readonly float m_defaultHudGlow;
    private readonly PlayerCockpit m_cockpit;
    private readonly BattlefieldEffects m_effects;
    private readonly ShaderMaterial[] m_terrainMaterials;
    private bool m_terrainTriplanarEnabled;

    public QuestGraphicsSettings(
        MissionSkyController sky,
        PlayerHud hud,
        PlayerCockpit cockpit = null,
        BattlefieldEffects effects = null,
        IEnumerable<ShaderMaterial> terrainMaterials = null)
    {
        m_sky = sky ?? throw new ArgumentNullException(nameof(sky));
        m_hud = hud ?? throw new ArgumentNullException(nameof(hud));
        m_defaultHudGlow = hud.HudGlow;
        m_cockpit = cockpit;
        m_effects = effects;
        m_terrainMaterials = terrainMaterials?
            .Where(TerrainSurfaceMaterial.SupportsTriplanarToggle).Distinct().ToArray() ?? [];
        m_terrainTriplanarEnabled = QuestGraphicsPreferences.LoadTerrainTriplanar();
        ApplyTerrainTriplanar();

        // The headset baseline avoids the full-screen and compositor passes first.
        SunShadowsEnabled = false;
        AmbientOcclusionEnabled = false;
        ScreenReflectionsEnabled = false;
        GlowEnabled = false;
        HudGlowEnabled = false;
        // The compositor flare has not been validated in stereo, so it remains unavailable
        // from the headset menu and disabled for the Quest baseline.
        LensFlareEnabled = false;
        CockpitGlassEnabled = false;
        SmokeAndDustEnabled = false;
    }

    public bool SunShadowsEnabled
    {
        get => m_sky.SunShadowsEnabled;
        set => m_sky.SunShadowsEnabled = value;
    }

    /// <summary>Uses a once-per-mission sky panorama for normal Quest gameplay.</summary>
    public bool BakedSkyEnabled
    {
        get => m_sky.BakedSkyEnabled;
        set => m_sky.BakedSkyEnabled = value;
    }

    /// <summary>Persists the Quest-only mapping choice and updates all loaded terrain immediately.</summary>
    public bool TerrainTriplanarEnabled
    {
        get => m_terrainTriplanarEnabled;
        set
        {
            if (m_terrainTriplanarEnabled == value) return;
            m_terrainTriplanarEnabled = value;
            ApplyTerrainTriplanar();
            QuestGraphicsPreferences.SaveTerrainTriplanar(value);
        }
    }

    private void ApplyTerrainTriplanar()
    {
        foreach (var material in m_terrainMaterials)
        {
            TerrainSurfaceMaterial.SetTriplanarEnabled(material, m_terrainTriplanarEnabled);
        }
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

    public bool HudGlowEnabled
    {
        get => m_hud.HudGlow > 0.0f;
        set => m_hud.HudGlow = value ? m_defaultHudGlow : 0.0f;
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
