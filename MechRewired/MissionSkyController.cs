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
using MechRewired.Resources;

namespace MechRewired;

/// <summary>
/// Adapts the small amount of lighting and palette information authored by an MW2 world file
/// into the modern Sky3D atmosphere.  Keeping this boundary here means the rest of the game
/// never needs to know which sky implementation happens to be in use.
/// </summary>
public sealed class MissionSkyController
{
    private const string Sky3DScriptPath = "res://addons/sky_3d/src/Sky3D.gd";
    private const float DefaultAmbientLightEnergy = 0.10f;
    private const float DefaultSunLightEnergy = 0.65f;
    private const float DefaultSkyFillLightEnergy = 0.25f;
    private const float DefaultSunShadowOpacity = 0.90f;
    private const float SolarAngularDiameterDegrees = 0.25f;
    private const float DefaultSunShadowBlur = 1.0f;
    private const float WarmMountainSolarAngularDiameterDegrees = 0.90f;
    private const float WarmMountainSunShadowBlur = 1.50f;
    private const float WarmMountainSunShadowOpacity = 0.85f;
    private const float DefaultCloudCoverage = 0.40f;
    private const float DefaultCloudDensity = 1.00f;
    private const float DefaultCloudHeight = 1.8f;
    private const float WarmMountainCloudCoverage = 0.16f;
    private const float WarmMountainCloudDensity = 0.52f;
    private const float WarmMountainCloudHeight = 2.4f;
    private const float DefaultFogStartFraction = 0.35f;
    private const float DesertFogAerialPerspective = 0.72f;
    private const float DesertFogSunScatter = 0.09f;
    private const float WarmMountainFogMultiplier = 1.35f;
    private const float WarmMountainFogStartFraction = 0.20f;
    private const float WarmMountainFogAerialPerspective = 0.32f;
    private const float WarmMountainFogSunScatter = 0.055f;
    private const float DesertGodRayStrength = 0.006f;
    private const int DesertGodRaySamples = 18;
    private const float WarmMountainGodRayStrength = 0.018f;
    private const int WarmMountainGodRaySamples = 24;
    private const float DefaultSsaoRadius = 1.35f;
    private const float DefaultSsaoIntensity = 1.35f;
    private const float DefaultSsaoDetail = 0.35f;
    // The desert's strong, direct sun otherwise hides SSAO entirely.  This deliberately gives
    // contact cavities some influence over direct light so scenery and mechs read as grounded.
    private const float DefaultSsaoDirectLightAffect = 0.70f;

    private readonly Node m_sky3D;
    private readonly Node m_skyDome;
    private readonly DirectionalLight3D m_sunLight;
    private readonly SunLensFlare m_sunLensFlare;
    private readonly Godot.Environment m_environment;
    private readonly MissionSkyProfile m_profile;
    private float m_time;
    private float m_fogMultiplier = 1.0f;
    private float m_fogStartFraction = DefaultFogStartFraction;
    private float m_sunAzimuthOffset;

    private MissionSkyController(
        Node sky3D,
        Node skyDome,
        DirectionalLight3D sunLight,
        SunLensFlare sunLensFlare,
        Godot.Environment environment,
        MissionSkyProfile profile)
    {
        m_sky3D = sky3D;
        m_skyDome = skyDome;
        m_sunLight = sunLight;
        m_sunLensFlare = sunLensFlare;
        m_environment = environment;
        m_profile = profile;
        m_time = profile.TimeOfDay;
    }

    /// <summary>
    /// Adds a ready-to-use Sky3D environment to <paramref name="owner"/>.
    /// </summary>
    public static MissionSkyController Create(Node owner, MissionSkyProfile profile)
    {
        var skyScript = GD.Load<Script>(Sky3DScriptPath) ??
                        throw new InvalidOperationException($"Sky3D script is missing at {Sky3DScriptPath}.");
        var sky3D = skyScript.Call("new").As<WorldEnvironment>() ??
                    throw new InvalidOperationException("Sky3D did not create a Godot node.");
        sky3D.Name = "MissionSky";
        owner.AddChild(sky3D);

        var skyDome = sky3D.GetNodeOrNull<Node>("SkyDome") ??
                      throw new InvalidOperationException("Sky3D did not create its SkyDome child.");
        var sunLight = sky3D.GetNodeOrNull<DirectionalLight3D>("SunLight") ??
                       throw new InvalidOperationException("Sky3D did not create its directional sunlight.");
        var environment = sky3D.Get("environment").As<Godot.Environment>() ??
                          throw new InvalidOperationException("Sky3D created no usable Godot environment.");
        var sunLensFlare = new SunLensFlare(sky3D, sunLight, profile.SunColor);
        owner.AddChild(sunLensFlare);
        var controller = new MissionSkyController(
            sky3D,
            skyDome,
            sunLight,
            sunLensFlare,
            environment,
            profile);
        controller.ApplyProfile();
        // SkyDome is created dynamically and initially disables its own processing in _ready.
        // Re-enable its normal-process cloud drift after that setup has completed.
        skyDome.CallDeferred("set", "process_method", 1);
        skyDome.CallDeferred("set", "wind_speed", 1.5f);
        return controller;
    }

    /// <summary>
    /// The live mission time in hours.  MW2's INIT tag supplies its starting point.
    /// </summary>
    public float TimeOfDay
    {
        get => m_sky3D.Get("current_time").AsSingle();
        set
        {
            m_time = Mathf.PosMod(value, 24.0f);
            m_sky3D.Set("current_time", m_time);
            ApplyTimeBasedSunDirection();
        }
    }

    /// <summary>
    /// Enables the near-field volumetric buffer used by localized effects such as
    /// windblown sand or valley haze. The scene deliberately keeps the global density at zero:
    /// only authored <see cref="FogVolume"/> nodes contribute visible volume.
    /// </summary>
    public bool EnableLocalizedVolumetricFog(float visibleDistance = 160.0f)
    {
        if (!string.Equals(
                RenderingServer.GetCurrentRenderingMethod().ToString(),
                "forward_plus",
                StringComparison.Ordinal))
        {
            GD.Print(
                "MechRewired: localized volumetric fog is unavailable outside the Forward+ renderer; " +
                "skipping localized atmospheric effects.");
            return false;
        }

        m_environment.VolumetricFogEnabled = true;
        m_environment.VolumetricFogDensity = 0.0f;
        m_environment.VolumetricFogLength = Mathf.Clamp(visibleDistance, 80.0f, 400.0f);
        m_environment.VolumetricFogDetailSpread = 1.8f;
        // Sand and future projectile lights move too quickly for history blending.
        m_environment.VolumetricFogTemporalReprojectionEnabled = false;
        m_environment.VolumetricFogSkyAffect = 1.0f;
        return true;
    }

    /// <summary>
    /// Scales the level-authored depth cue without changing the source data.
    /// </summary>
    public float FogMultiplier
    {
        get => m_fogMultiplier;
        set
        {
            m_fogMultiplier = Mathf.Clamp(value, 0.05f, 4.0f);
            ApplyFog();
        }
    }

    /// <summary>
    /// Controls how far through the authored depth-cue range fog starts affecting the scene.
    /// </summary>
    public float FogStartFraction
    {
        get => m_fogStartFraction;
        set
        {
            m_fogStartFraction = Mathf.Clamp(value, 0.0f, 0.95f);
            ApplyFog();
        }
    }

    /// <summary>
    /// Blends distant geometry toward the Sky3D colour behind it instead of one uniform horizon
    /// tint. This is especially important for airborne objects viewed against the blue upper sky.
    /// </summary>
    public float FogAerialPerspective
    {
        get => m_environment.FogAerialPerspective;
        set => m_environment.FogAerialPerspective = Mathf.Clamp(value, 0.0f, 1.0f);
    }

    /// <summary>
    /// Adds restrained directional-sun scattering to the depth fog.
    /// </summary>
    public float FogSunScatter
    {
        get => m_environment.FogSunScatter;
        set => m_environment.FogSunScatter = Mathf.Clamp(value, 0.0f, 1.0f);
    }

    public float CloudCoverage
    {
        get => m_skyDome.Get("cirrus_coverage").AsSingle();
        set => m_skyDome.Set("cirrus_coverage", Mathf.Clamp(value, 0.0f, 1.0f));
    }

    public float CloudDensity
    {
        get => m_skyDome.Get("cirrus_intensity").AsSingle();
        set => m_skyDome.Set("cirrus_intensity", Mathf.Clamp(value, 0.0f, 16.0f));
    }

    public float CloudHeight
    {
        get => m_skyDome.Get("cirrus_size").AsSingle();
        set => m_skyDome.Set("cirrus_size", Mathf.Clamp(value, 0.1f, 8.0f));
    }

    public float SunAzimuthOffsetDegrees
    {
        get => m_sunAzimuthOffset;
        set
        {
            var azimuthDelta = value - m_sunAzimuthOffset;
            m_sunAzimuthOffset = value;
            m_skyDome.Set(
                "sun_azimuth",
                m_skyDome.Get("sun_azimuth").AsSingle() + Mathf.DegToRad(azimuthDelta));
        }
    }

    public float SunShadowDistance
    {
        get => m_sunLight.DirectionalShadowMaxDistance;
        set => m_sunLight.DirectionalShadowMaxDistance = Mathf.Clamp(value, 250.0f, 6000.0f);
    }

    public float SunShadowOpacity
    {
        get => m_sunLight.ShadowOpacity;
        set => m_sunLight.ShadowOpacity = Mathf.Clamp(value, 0.0f, 1.0f);
    }

    /// <summary>
    /// Enables the inexpensive, half-resolution screen-space contact shading used by the
    /// Forward+ renderer. This can be toggled for visual comparisons without moving the sun.
    /// </summary>
    public bool AmbientOcclusionEnabled
    {
        get => m_environment.SsaoEnabled;
        set => m_environment.SsaoEnabled = value;
    }

    /// <summary>
    /// Applies a deliberately legible, local SSAO configuration for controlled renderer tests.
    /// Gameplay settings are restored by <see cref="ResetAmbientOcclusionSettings"/>.
    /// </summary>
    public void UseAmbientOcclusionContactTestSettings()
    {
        m_environment.SsaoRadius = 1.5f;
        m_environment.SsaoIntensity = 1.5f;
        m_environment.SsaoPower = 1.35f;
        m_environment.SsaoDetail = 0.5f;
        m_environment.SsaoHorizon = 0.06f;
        m_environment.SsaoSharpness = 0.98f;
        m_environment.SsaoLightAffect = 0.85f;
    }

    public void ResetAmbientOcclusionSettings() => ConfigureAmbientOcclusion();

    public float Exposure
    {
        get => m_sky3D.Get("tonemap_exposure").AsSingle();
        set => m_sky3D.Set("tonemap_exposure", Mathf.Clamp(value, 0.0f, 16.0f));
    }

    /// <summary>
    /// Controls the strength of the compositor flare around an unobstructed sun.
    /// </summary>
    public float SunLensFlareIntensity
    {
        get => m_sunLensFlare.Intensity;
        set => m_sunLensFlare.Intensity = value;
    }

    /// <summary>
    /// Controls the restrained, depth-occluded shafts mixed into the sun flare.
    /// </summary>
    public float SunGodRayStrength
    {
        get => m_sunLensFlare.GodRayStrength;
        set => m_sunLensFlare.GodRayStrength = value;
    }

    /// <summary>
    /// Applies a named visual baseline without changing the mission itself.  They deliberately
    /// cover only the sky/time axis: gameplay and camera remain exactly as the tester arranged
    /// them, making captures useful both at initial deployment and during a live mission.
    /// </summary>
    public bool TryApplyCapturePreset(string name)
    {
        switch (name.Trim().ToLowerInvariant())
        {
            case "authored":
            case "mission":
                TimeOfDay = m_profile.TimeOfDay;
                return true;
            case "day":
                TimeOfDay = 12.0f;
                return true;
            case "dusk":
                TimeOfDay = 18.25f;
                return true;
            case "night":
                TimeOfDay = 22.0f;
                return true;
            default:
                return false;
        }
    }

    public string Describe() =>
        $"time {TimeOfDay:F2}h; authored {m_profile.TimeOfDay:F2}h; fog x{FogMultiplier:F2}, " +
        $"depth cue {m_profile.DepthCueDistance:F0}m, visibility {m_profile.VisibilityDistance:F0}m, " +
        $"aerial {FogAerialPerspective:F2}, sun scatter {FogSunScatter:F2}; " +
        $"cirrus coverage {CloudCoverage:F2}, density {CloudDensity:F2}, scale {CloudHeight:F2}; " +
        $"sun azimuth offset {SunAzimuthOffsetDegrees:F1} degrees; shadow distance " +
        $"{SunShadowDistance:F0}m, opacity {SunShadowOpacity:F2}, angular distance " +
        $"{m_sunLight.LightAngularDistance:F2} degrees; flare {SunLensFlareIntensity:F2}, " +
        $"god rays {SunGodRayStrength:F3}; " +
        $"exposure {Exposure:F2}.";

    private void ApplyProfile()
    {
        // MW2's INIT tag provides a fixed mission sun position. SkyDome updates cloud drift
        // independently, so pause only TimeOfDay and leave the cloud process running below.
        m_sky3D.Set("game_time_enabled", false);
        // Sky3D's defaults are stored without invoking their setters when it is created at
        // runtime from C#. Trigger them once after its child nodes exist.
        m_sky3D.Set("sky_enabled", true);
        m_sky3D.Set("clouds_enabled", true);
        m_sky3D.Set("lights_enabled", true);
        // Warm-palette rocky worlds need enough indirect light for their surface normals to read,
        // but must not inherit the desert world's high-key sunlight. Retain the authored ambient
        // level as the input while compensating for Sky3D's double attenuation.
        var usesWarmAtmosphere = m_profile.UsesWarmPaletteAtmosphere;
        var usesRockyMountainTerrain = m_profile.TerrainBiome == MechWarriorTerrainBiome.RockyMountain;
        var ambientEnergy = DefaultAmbientLightEnergy +
                            m_profile.AuthoredAmbientLevel * (usesRockyMountainTerrain ? 0.30f : 0.10f);
        m_sky3D.Set(
            "sky_contribution",
            usesRockyMountainTerrain ? 0.36f : DefaultSkyFillLightEnergy);
        m_sky3D.Set("ambient_energy", ambientEnergy);
        m_sky3D.Set(
            "sun_energy",
            usesRockyMountainTerrain ? 0.65f : DefaultSunLightEnergy);
        m_sky3D.Set("cloud_intensity", DefaultCloudDensity);
        m_sky3D.Set("tonemap_exposure", 1.0f);
        m_sky3D.Set("auto_exposure", false);
        // Sky3D's optional full-screen fog shader paints the empty depth behind this game's
        // sparse terrain black on Metal.  Retain Sky3D for the sky, clouds and celestial light,
        // but use Godot's reliable depth fog for the world cue below.
        m_sky3D.Set("fog_enabled", false);

        // Sky3D's atmospheric shader applies its tints twice while scattering.  Raw 6-bit-era
        // palette colours are consequently far too dark here (and produced a nearly black
        // sky on Pyre Light).  Preserve their hue but lift their value before handing them to
        // the physically-based shader; the original values remain the source of the art
        // direction rather than becoming a hard-coded level colour.
        // Palettes with a strongly warm horizon (for example Colmar's red/orange sky) need that
        // authored gradient to dominate physical Rayleigh blue. Cooler desert palettes retain the
        // established lifted tint. The sky asset stays modern, but its art direction remains data-driven.
        var atmosphericTint = m_profile.UsesWarmPaletteAtmosphere
            ? m_profile.SkyTopColor.Lerp(m_profile.HorizonColor, 0.55f).Lerp(Colors.White, 0.42f)
            : m_profile.SkyTopColor.Lerp(Colors.White, 0.70f);
        var horizonTint = m_profile.HorizonColor.Lerp(
            Colors.White,
            m_profile.UsesWarmPaletteAtmosphere ? 0.42f : 0.60f);
        m_skyDome.Set("atm_day_tint", atmosphericTint);
        m_skyDome.Set("atm_horizon_light_tint", horizonTint);
        m_skyDome.Set("ground_color", m_profile.HorizonColor);
        m_skyDome.Set("sun_light_color", m_profile.SunColor);
        m_skyDome.Set("sun_horizon_light_color", m_profile.HorizonColor.Lerp(m_profile.SunColor, 0.45f));
        m_skyDome.Set("atm_sun_mie_tint", m_profile.HorizonColor.Lerp(Colors.White, 0.35f));
        m_skyDome.Set("atm_darkness", m_profile.UsesWarmPaletteAtmosphere ? 0.16f : 0.38f);
        m_skyDome.Set("atm_thickness", m_profile.UsesWarmPaletteAtmosphere ? 0.62f : 0.9f);
        m_skyDome.Set("atm_mie", m_profile.UsesWarmPaletteAtmosphere ? 0.11f : 0.055f);
        m_skyDome.Set("atm_turbidity", m_profile.UsesWarmPaletteAtmosphere ? 0.0035f : 0.0015f);
        m_skyDome.Set("cirrus_visible", true);
        m_skyDome.Set(
            "cirrus_coverage",
            m_profile.UsesWarmPaletteAtmosphere ? WarmMountainCloudCoverage : DefaultCloudCoverage);
        m_skyDome.Set(
            "cirrus_intensity",
            m_profile.UsesWarmPaletteAtmosphere ? WarmMountainCloudDensity : DefaultCloudDensity);
        m_skyDome.Set(
            "cirrus_size",
            m_profile.UsesWarmPaletteAtmosphere ? WarmMountainCloudHeight : DefaultCloudHeight);
        m_skyDome.Set("cumulus_visible", false);
        m_skyDome.Set("wind_speed", 0.8f);

        m_environment.SsrEnabled = true;
        m_environment.SsrMaxSteps = 48;
        m_environment.SsrFadeIn = 0.12f;
        m_environment.SsrFadeOut = 2.5f;
        m_environment.GlowEnabled = true;
        m_environment.GlowIntensity = 0.8f;
        m_environment.GlowStrength = 1.0f;
        m_environment.GlowBloom = 0.05f;
        m_environment.GlowHdrThreshold = 1.5f;
        ConfigureAmbientOcclusion();
        m_environment.FogEnabled = true;
        m_environment.FogMode = Godot.Environment.FogModeEnum.Depth;
        // PINK's saturated orange horizon remains the sky art direction, but it is unsuitable as
        // the destination colour for Godot's additive depth fog: even at low energy, distant dark
        // mountains converge on a luminous orange slab. Derive a dark, low-chroma atmospheric
        // extinction colour from both ends of the authored sky gradient instead. The level data
        // still determines the hue and depth-cue distance without forcing geometry to become sky.
        var rockyFogColor = m_profile.SkyTopColor
            .Lerp(m_profile.HorizonColor, 0.20f)
            .Lerp(Colors.Black, 0.14f);
        m_environment.FogLightColor = usesRockyMountainTerrain
            ? rockyFogColor
            : m_profile.HorizonColor;
        m_environment.FogLightEnergy = usesRockyMountainTerrain ? 0.78f : 0.84f;
        m_environment.FogDensity = 1.0f;
        m_environment.FogDepthCurve = 1.0f;
        m_environment.FogSkyAffect = 0.0f;
        m_fogMultiplier = usesRockyMountainTerrain ? WarmMountainFogMultiplier : 1.0f;
        m_fogStartFraction = usesRockyMountainTerrain
            ? WarmMountainFogStartFraction
            : DefaultFogStartFraction;
        FogAerialPerspective = usesRockyMountainTerrain
            ? WarmMountainFogAerialPerspective
            : DesertFogAerialPerspective;
        FogSunScatter = usesRockyMountainTerrain
            ? WarmMountainFogSunScatter
            : DesertFogSunScatter;
        SunGodRayStrength = usesRockyMountainTerrain
            ? WarmMountainGodRayStrength
            : DesertGodRayStrength;
        m_sunLensFlare.GodRaySampleCount = usesRockyMountainTerrain
            ? WarmMountainGodRaySamples
            : DesertGodRaySamples;

        TimeOfDay = m_profile.TimeOfDay;
        ConfigureSunShadows();
        m_sky3D.Call("resume");
        ApplyFog();
    }

    private void ConfigureAmbientOcclusion()
    {
        // Godot renders SSAO at half resolution by default. Keep the radius deliberately local,
        // so it grounds rocks, structures and mech details without dirtying broad desert slopes.
        m_environment.SsaoEnabled = true;
        m_environment.SsaoRadius = DefaultSsaoRadius;
        m_environment.SsaoIntensity = DefaultSsaoIntensity;
        m_environment.SsaoPower = 1.25f;
        m_environment.SsaoDetail = DefaultSsaoDetail;
        m_environment.SsaoHorizon = 0.06f;
        m_environment.SsaoSharpness = 0.92f;
        m_environment.SsaoLightAffect = DefaultSsaoDirectLightAffect;
    }

    private void ConfigureSunShadows()
    {
        // Godot's short default directional-shadow range causes entire hills to acquire shadows
        // only as the camera approaches them. Four cascades preserve cockpit detail while the
        // far cascade covers the battlefield out to roughly twice MW2's authored depth cue.
        m_sunLight.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits;
        // Reserve the first cascade for the player and nearby scenery. With an 1,800m+
        // battlefield range, the previous 8% split spread its texels across roughly 144m,
        // making the player's external-view shadow look faint and over-softened.
        m_sunLight.DirectionalShadowSplit1 = 0.025f;
        m_sunLight.DirectionalShadowSplit2 = 0.12f;
        m_sunLight.DirectionalShadowSplit3 = 0.42f;
        m_sunLight.DirectionalShadowBlendSplits = true;
        m_sunLight.DirectionalShadowFadeStart = 0.90f;
        SunShadowDistance = Mathf.Clamp(m_profile.DepthCueDistance * 2.0f, 1800.0f, 4000.0f);
        // Jade Falcon's warm mountain level is authored close to noon, which produces a tight
        // ground-space PCSS penumbra. Its muted sun reads as a broad, hazy source instead, so
        // give the mountain and external-mech shadows a deliberately softer, lighter transition.
        var softenWarmMountainShadows =
            m_profile.TerrainBiome == MechWarriorTerrainBiome.RockyMountain &&
            m_profile.UsesWarmPaletteAtmosphere;
        m_sunLight.LightAngularDistance = softenWarmMountainShadows
            ? WarmMountainSolarAngularDiameterDegrees
            : SolarAngularDiameterDegrees;
        m_sunLight.ShadowBlur = softenWarmMountainShadows
            ? WarmMountainSunShadowBlur
            : DefaultSunShadowBlur;

        // The warm mountain sky contributes more diffuse fill than Wolf's bright desert view.
        SunShadowOpacity = softenWarmMountainShadows
            ? WarmMountainSunShadowOpacity
            : DefaultSunShadowOpacity;
    }

    private void ApplyFog()
    {
        // The MW2 shade distance is where its palette depth cue has converged; VDIST is the outer
        // authored visibility limit. Desert sand can plausibly reach full density at SHADE, while
        // clear rocky air should use the broader VDIST band. Both inputs still come from the level,
        // but the biome determines how the modern renderer interprets them.
        var authoredFogEnd = m_profile.TerrainBiome == MechWarriorTerrainBiome.RockyMountain
            ? m_profile.VisibilityDistance
            : m_profile.DepthCueDistance;
        var fogEnd = Math.Max(100.0f, authoredFogEnd / m_fogMultiplier);
        m_environment.FogDepthBegin = fogEnd * m_fogStartFraction;
        m_environment.FogDepthEnd = fogEnd;
    }

    private void ApplyTimeBasedSunDirection()
    {
        // Sky3D recalculates both sun coordinates from current_time. INIT therefore remains the
        // sole source of the frozen sun position; apply only the optional debug azimuth offset
        // after that time-based update.
        if (!Mathf.IsZeroApprox(m_sunAzimuthOffset))
        {
            m_skyDome.Set(
                "sun_azimuth",
                m_skyDome.Get("sun_azimuth").AsSingle() + Mathf.DegToRad(m_sunAzimuthOffset));
        }
    }
}

/// <summary>
/// Level-authored sky inputs, kept independently from a particular rendering addon.
/// </summary>
public sealed record MissionSkyProfile(
    float TimeOfDay,
    float AuthoredAmbientLevel,
    float DepthCueDistance,
    float VisibilityDistance,
    Color SkyTopColor,
    Color HorizonColor,
    Color SunColor,
    MechWarriorTerrainBiome TerrainBiome)
{
    /// <summary>
    /// Whether the original palette describes a red/orange atmospheric gradient that should
    /// outweigh the replacement sky's natural blue scattering.
    /// </summary>
    public bool UsesWarmPaletteAtmosphere =>
        HorizonColor.R > HorizonColor.G * 1.45f &&
        HorizonColor.R > HorizonColor.B * 2.0f;

    public static MissionSkyProfile FromWorld(
        MechWarriorWorldFile world,
        MechWarriorPalette palette,
        float depthCueDistance,
        float visibilityDistance,
        int skyTopPaletteIndex,
        int skyHorizonPaletteIndex,
        int sunPaletteIndex,
        MechWarriorTerrainBiome terrainBiome)
    {
        var militaryTime = world.TimeOfDay ?? 1200;
        var hours = Mathf.PosMod(militaryTime / 100, 24);
        var minutes = Mathf.Clamp(militaryTime % 100, 0, 59);
        return new MissionSkyProfile(
            hours + minutes / 60.0f,
            Mathf.Clamp((world.Lighting?.AmbientLevel ?? 50) / 100.0f, 0.0f, 1.0f),
            depthCueDistance,
            visibilityDistance,
            ToGodotColor(palette[skyTopPaletteIndex]),
            ToGodotColor(palette[skyHorizonPaletteIndex]),
            ToGodotColor(palette[sunPaletteIndex]),
            terrainBiome);
    }

    private static Color ToGodotColor(DTC.Core.Rgb color) =>
        new(color.R / 255.0f, color.G / 255.0f, color.B / 255.0f);
}
