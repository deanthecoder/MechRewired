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
    private const float DefaultSunShadowOpacity = 0.72f;
    private const float DefaultCloudCoverage = 0.40f;
    private const float DefaultCloudDensity = 1.00f;
    private const float DefaultCloudHeight = 1.8f;
    private const float DefaultFogStartFraction = 0.35f;

    private readonly Node m_sky3D;
    private readonly Node m_skyDome;
    private readonly DirectionalLight3D m_sunLight;
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
        Godot.Environment environment,
        MissionSkyProfile profile)
    {
        m_sky3D = sky3D;
        m_skyDome = skyDome;
        m_sunLight = sunLight;
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
        var sky3D = skyScript.Call("new").As<Node>() ??
                    throw new InvalidOperationException("Sky3D did not create a Godot node.");
        sky3D.Name = "MissionSky";
        owner.AddChild(sky3D);

        var skyDome = sky3D.GetNodeOrNull<Node>("SkyDome") ??
                      throw new InvalidOperationException("Sky3D did not create its SkyDome child.");
        var sunLight = sky3D.GetNodeOrNull<DirectionalLight3D>("SunLight") ??
                       throw new InvalidOperationException("Sky3D did not create its directional sunlight.");
        var environment = sky3D.Get("environment").As<Godot.Environment>() ??
                          throw new InvalidOperationException("Sky3D created no usable Godot environment.");
        var controller = new MissionSkyController(sky3D, skyDome, sunLight, environment, profile);
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
            ApplyAuthoredSunDirection();
        }
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
            m_sunAzimuthOffset = value;
            ApplyAuthoredSunDirection();
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

    public float Exposure
    {
        get => m_sky3D.Get("tonemap_exposure").AsSingle();
        set => m_sky3D.Set("tonemap_exposure", Mathf.Clamp(value, 0.0f, 16.0f));
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
        $"time {TimeOfDay:F2}h; authored {m_profile.TimeOfDay:F2}h; fog x{FogMultiplier:F2}; " +
        $"cirrus coverage {CloudCoverage:F2}, density {CloudDensity:F2}, scale {CloudHeight:F2}; " +
        $"sun azimuth offset {SunAzimuthOffsetDegrees:F1} degrees; shadow distance " +
        $"{SunShadowDistance:F0}m, opacity {SunShadowOpacity:F2}; exposure {Exposure:F2}.";

    private void ApplyProfile()
    {
        // MW2's INIT tag provides the starting time, rather than a permanently frozen sky.
        // Keep a calm two-hour full-day cycle so clouds, sunlight and celestial bodies
        // visibly progress during a normal test session.
        m_sky3D.Set("game_time_enabled", true);
        m_sky3D.Set("minutes_per_day", 120.0f);
        // Sky3D's defaults are stored without invoking their setters when it is created at
        // runtime from C#. Trigger them once after its child nodes exist.
        m_sky3D.Set("sky_enabled", true);
        m_sky3D.Set("clouds_enabled", true);
        m_sky3D.Set("lights_enabled", true);
        // Preserve the established remaster balance: direct sun 65%, broad sky fill 25%,
        // neutral ambient 10%.  Sky3D expresses the former fill as a sky-contribution ratio.
        m_sky3D.Set("sky_contribution", DefaultSkyFillLightEnergy);
        m_sky3D.Set("ambient_energy", DefaultAmbientLightEnergy);
        m_sky3D.Set("sun_energy", DefaultSunLightEnergy);
        ConfigureSunShadows();
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
        var atmosphericTint = m_profile.SkyTopColor.Lerp(Colors.White, 0.70f);
        var horizonTint = m_profile.HorizonColor.Lerp(Colors.White, 0.60f);
        m_skyDome.Set("atm_day_tint", atmosphericTint);
        m_skyDome.Set("atm_horizon_light_tint", horizonTint);
        m_skyDome.Set("ground_color", m_profile.HorizonColor);
        m_skyDome.Set("sun_light_color", m_profile.SunColor);
        m_skyDome.Set("sun_horizon_light_color", m_profile.HorizonColor.Lerp(m_profile.SunColor, 0.45f));
        m_skyDome.Set("atm_sun_mie_tint", m_profile.HorizonColor.Lerp(Colors.White, 0.35f));
        m_skyDome.Set("atm_darkness", 0.38f);
        m_skyDome.Set("atm_thickness", 0.9f);
        m_skyDome.Set("atm_mie", 0.055f);
        m_skyDome.Set("atm_turbidity", 0.0015f);
        m_skyDome.Set("cirrus_visible", true);
        m_skyDome.Set("cirrus_coverage", DefaultCloudCoverage);
        m_skyDome.Set("cirrus_intensity", DefaultCloudDensity);
        m_skyDome.Set("cirrus_size", DefaultCloudHeight);
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
        m_environment.FogEnabled = true;
        m_environment.FogMode = Godot.Environment.FogModeEnum.Depth;
        m_environment.FogLightColor = m_profile.HorizonColor;
        m_environment.FogLightEnergy = 1.0f;
        m_environment.FogDensity = 1.0f;
        m_environment.FogDepthCurve = 1.0f;
        m_environment.FogSkyAffect = 0.0f;

        TimeOfDay = m_profile.TimeOfDay;
        m_sky3D.Call("resume");
        ApplyFog();
    }

    private void ConfigureSunShadows()
    {
        // Godot's short default directional-shadow range causes entire hills to acquire shadows
        // only as the camera approaches them. Four cascades preserve cockpit detail while the
        // far cascade covers the battlefield out to roughly twice MW2's authored depth cue.
        m_sunLight.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits;
        m_sunLight.DirectionalShadowSplit1 = 0.08f;
        m_sunLight.DirectionalShadowSplit2 = 0.22f;
        m_sunLight.DirectionalShadowSplit3 = 0.50f;
        m_sunLight.DirectionalShadowBlendSplits = true;
        m_sunLight.DirectionalShadowFadeStart = 0.90f;
        SunShadowDistance = Mathf.Clamp(m_profile.DepthCueDistance * 2.0f, 1800.0f, 4000.0f);

        // A bright desert sky scatters substantial light into nominally shadowed surfaces. This
        // inexpensive approximation retains readable terrain without changing direct sunlight.
        SunShadowOpacity = DefaultSunShadowOpacity;
    }

    private void ApplyFog()
    {
        // The MW2 shade distance is where the palette depth cue has converged.  Sky3D performs
        // that cue in screen space, so preserve a clear foreground before driving its end from
        // the same authored distance. The later onset keeps terrain texture readable without
        // changing the mission's final visibility range.
        var fogEnd = Math.Max(100.0f, m_profile.DepthCueDistance / m_fogMultiplier);
        m_environment.FogDepthBegin = fogEnd * m_fogStartFraction;
        m_environment.FogDepthEnd = fogEnd;
    }

    private void ApplyAuthoredSunDirection()
    {
        // INIT supplies the level's broad time-of-day state.  LITE supplies the authored light
        // position; use it when present, after applying the same handedness conversion as world
        // geometry.  If a malformed/zero light vector is encountered, retain Sky3D's time-based
        // solar elevation and only use the level-independent default azimuth.
        var lightDirection = m_profile.AuthoredSunDirection;
        var azimuth = lightDirection.LengthSquared() > 0.0001f
            ? Mathf.Atan2(lightDirection.X, lightDirection.Z)
            : Mathf.DegToRad(25.0f);
        azimuth += Mathf.DegToRad(m_sunAzimuthOffset);
        m_skyDome.Set("sun_azimuth", azimuth);
        var zenithAngle = lightDirection.LengthSquared() > 0.0001f
            ? Mathf.Acos(Mathf.Clamp(lightDirection.Normalized().Y, -1.0f, 1.0f))
            : Mathf.DegToRad(GetTimeOfDayZenithAngleDegrees(m_time));
        m_skyDome.Set("sun_altitude", zenithAngle);
    }

    private static float GetTimeOfDayZenithAngleDegrees(float timeOfDay)
    {
        // MW2 has no real-world latitude/date.  Treat INIT as an authored local solar time:
        // horizon at 06:00/18:00, a 65-degree high sun at midday, and a safely below-horizon
        // source at night.  This makes its level data meaningful without pretending it carries
        // terrestrial orbital information.
        var daylightProgress = (Mathf.PosMod(timeOfDay, 24.0f) - 6.0f) / 12.0f;
        if (daylightProgress is < 0.0f or > 1.0f)
        {
            return 105.0f;
        }

        var solarElevation = Mathf.Sin(daylightProgress * Mathf.Pi) * 65.0f;
        return 90.0f - solarElevation;
    }
}

/// <summary>
/// Level-authored sky inputs, kept independently from a particular rendering addon.
/// </summary>
public sealed record MissionSkyProfile(
    float TimeOfDay,
    Vector3 AuthoredSunDirection,
    float AuthoredAmbientLevel,
    float DepthCueDistance,
    Color SkyTopColor,
    Color HorizonColor,
    Color SunColor)
{
    public static MissionSkyProfile FromWorld(
        MechWarriorWorldFile world,
        MechWarriorPalette palette,
        float depthCueDistance,
        int skyTopPaletteIndex,
        int skyHorizonPaletteIndex,
        int sunPaletteIndex)
    {
        var authoredSunDirection = world.Lighting == null
            ? Vector3.Zero
            : MechWarriorCoordinateSystem.ToGodotPosition(world.Lighting.Position).Normalized();
        var militaryTime = world.TimeOfDay ?? 1200;
        var hours = Mathf.PosMod(militaryTime / 100, 24);
        var minutes = Mathf.Clamp(militaryTime % 100, 0, 59);
        return new MissionSkyProfile(
            hours + minutes / 60.0f,
            authoredSunDirection,
            Mathf.Clamp((world.Lighting?.AmbientLevel ?? 50) / 100.0f, 0.0f, 1.0f),
            depthCueDistance,
            ToGodotColor(palette[skyTopPaletteIndex]),
            ToGodotColor(palette[skyHorizonPaletteIndex]),
            ToGodotColor(palette[sunPaletteIndex]));
    }

    private static Color ToGodotColor(DTC.Core.Rgb color) =>
        new(color.R / 255.0f, color.G / 255.0f, color.B / 255.0f);
}
