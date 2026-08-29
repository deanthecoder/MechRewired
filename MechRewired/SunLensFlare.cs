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
/// Drives the Godot compositor lens-flare asset from the live Sky3D sun.
/// </summary>
/// <remarks>
/// The compositor uses Godot's depth buffer to suppress the effect when scenery or the cockpit
/// occludes the sun, while this node supplies the projected position and restrained game tuning.
/// </remarks>
public partial class SunLensFlare : Node
{
    private const string EffectScriptPath =
        "res://addons/lens_effects/lens_flare_compositor_effect.gd";
    private const float DefaultEffectMultiplier = 0.32f;
    private const float DefaultGodRayDecay = 0.96f;
    private const float DefaultGodRayDensity = 0.84f;
    private readonly DirectionalLight3D m_sunLight;
    private readonly CompositorEffect m_effect;
    private float m_intensity = 3.0f;
    private float m_godRayStrength;
    private int m_godRaySampleCount = 1;

    public SunLensFlare(WorldEnvironment worldEnvironment, DirectionalLight3D sunLight, Color tint)
    {
        ArgumentNullException.ThrowIfNull(worldEnvironment);
        ArgumentNullException.ThrowIfNull(sunLight);
        m_sunLight = sunLight;
        Name = "SunLensFlare";

        var effectScript = GD.Load<Script>(EffectScriptPath) ??
                           throw new InvalidOperationException(
                               $"Lens-flare asset is missing at {EffectScriptPath}.");
        m_effect = effectScript.Call("new").As<CompositorEffect>() ??
                   throw new InvalidOperationException("Lens-flare asset did not create a compositor effect.");
        m_effect.Set("sun_color", tint.Lerp(Colors.White, 0.62f));
        m_effect.Set("Effect_Easing", 0.58f);
        m_effect.Set("Anamorphic_Intensity", 240.0f);
        m_effect.Set("Anamorphic_Stretch", 0.28f);
        m_effect.Set("Anamorphic_Brightness", 0.12f);
        m_effect.Set("Decay", DefaultGodRayDecay);
        m_effect.Set("Density", DefaultGodRayDensity);
        m_effect.Set("Weight", 0.0f);
        m_effect.Set("SampleCount", 1);

        worldEnvironment.Compositor = new Compositor
        {
            CompositorEffects = new Godot.Collections.Array<CompositorEffect> { m_effect }
        };
    }

    /// <summary>
    /// Controls the maximum strength of the flare while the sun is unobstructed.
    /// The default value is 3; higher values remain available for live visual tuning.
    /// </summary>
    public float Intensity
    {
        get => m_intensity;
        set => m_intensity = Mathf.Max(value, 0.0f);
    }

    /// <summary>
    /// Controls the depth-occluded radial shafts mixed into the sun flare.
    /// </summary>
    public float GodRayStrength
    {
        get => m_godRayStrength;
        set
        {
            m_godRayStrength = Mathf.Clamp(value, 0.0f, 0.20f);
            m_effect.Set("Weight", m_godRayStrength);
        }
    }

    /// <summary>
    /// Controls radial depth samples used by the god-ray mask.
    /// </summary>
    public int GodRaySampleCount
    {
        get => m_godRaySampleCount;
        set
        {
            m_godRaySampleCount = Mathf.Clamp(value, 1, 64);
            m_effect.Set("SampleCount", m_godRaySampleCount);
        }
    }

    public override void _Process(double delta)
    {
        var viewport = GetViewport();
        var camera = viewport.GetCamera3D();
        var viewportSize = viewport.GetVisibleRect().Size;
        if (camera == null || viewportSize.X <= 0.0f || viewportSize.Y <= 0.0f)
        {
            m_effect.Set("Effect_Multiplier", 0.0f);
            return;
        }

        // Sky3D points the light's positive Z basis from the world toward the sun.
        var sunDirection = m_sunLight.GlobalBasis.Z.Normalized();
        var sunPosition = camera.GlobalPosition + sunDirection * Mathf.Max(camera.Near, 1.0f);
        var sunUv = camera.UnprojectPosition(sunPosition) / viewportSize;
        var edgeFade = CalculateEdgeFade(sunUv);
        var horizonFade = Mathf.SmoothStep(0.0f, 0.10f, sunDirection.Y);
        var cameraFacing = Mathf.Max(0.0f, (-camera.GlobalBasis.Z).Normalized().Dot(sunDirection));
        var effectStrength = Intensity * edgeFade * horizonFade * cameraFacing;

        // Keep the asset's depth samples in bounds while edgeFade hides off-screen sources.
        sunUv = sunUv.Clamp(Vector2.Zero, Vector2.One);
        m_effect.Set("sun_position", sunUv);
        m_effect.Set("sun_dir_sign", cameraFacing > 0.0f ? 1.0f : -1.0f);
        m_effect.Set("Effect_Multiplier", DefaultEffectMultiplier * effectStrength);
    }

    internal static float CalculateEdgeFade(Vector2 sunUv)
    {
        var distanceFromCenter = (sunUv - new Vector2(0.5f, 0.5f)).Length();
        return 1.0f - Mathf.SmoothStep(0.46f, 0.72f, distanceFromCenter);
    }
}
