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
/// Renders a thin emissive laser pulse and light travelling from a weapon mount to its impact.
/// </summary>
/// <remarks>
/// Each pooled slot retains its pulse, halo and light; rapid fire can leave several bolts in flight.
/// </remarks>
public partial class LaserEffect : Node3D
{
    private const float TravelSpeedMetersPerSecond = 520.0f;
    private const float PulseLength = 12.0f;
    private const float LaunchDurationSeconds = 0.08f;
    private Vector3 m_start;
    private Vector3 m_direction;
    private float m_distance;
    private float m_delay;
    private readonly MeshInstance3D m_pulse;
    private readonly OmniLight3D m_light;
    private bool m_telemetryTracked;
    private bool m_reusable;
    private readonly StandardMaterial3D m_material;
    private readonly StandardMaterial3D m_haloMaterial;
    private float m_radius;
    private Color m_color = new(1.0f, 0.08f, 0.02f);
    private static readonly CylinderMesh s_pulseMesh = new() { TopRadius = 1.0f, BottomRadius = 1.0f, Height = 1.0f, RadialSegments = 8, Rings = 1 };
    private static readonly CylinderMesh s_haloMesh = new() { TopRadius = 3.5f, BottomRadius = 3.5f, Height = 1.0f, RadialSegments = 8, Rings = 1 };

    public bool IsActive { get; private set; }
    private float m_age;

    public LaserEffect(Vector3 start, Vector3 end)
        : this(start, end, new Color(1.0f, 0.08f, 0.02f), 0.06f, 0.0f)
    {
    }

    public LaserEffect(Vector3 start, Vector3 end, Color color, float radius, float delay = 0.0f)
        : this()
    {
        Launch(start, end, color, radius, delay, false);
    }

    public LaserEffect()
    {
        var color = new Color(1.0f, 0.08f, 0.02f);
        m_material = new StandardMaterial3D
        {
            AlbedoColor = color,
            EmissionEnabled = true,
            Emission = color,
            EmissionEnergyMultiplier = 12.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        m_pulse = new MeshInstance3D
        {
            Mesh = s_pulseMesh,
            MaterialOverride = m_material,
            Visible = false,
            ExtraCullMargin = PulseLength,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        m_haloMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(color.R, color.G, color.B, 0.22f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            EmissionEnabled = true,
            Emission = color,
            EmissionEnergyMultiplier = 3.0f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        m_pulse.AddChild(new MeshInstance3D
        {
            Mesh = s_haloMesh,
            MaterialOverride = m_haloMaterial,
            ExtraCullMargin = PulseLength,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        });
        AddChild(m_pulse);

        m_light = new OmniLight3D
        {
            LightColor = color,
            LightEnergy = 5.0f,
            OmniRange = 8.0f,
            ShadowEnabled = false,
            Visible = false
        };
        AddChild(m_light);
        Visible = false;
        SetProcess(false);
    }

    internal void Launch(Vector3 start, Vector3 end, Color color, float radius, float delay = 0.0f, bool reusable = true)
    {
        m_reusable = reusable;
        m_light.OmniRange = QuestVrRuntime.Active && QuestCombatTelemetry.SmallProjectileLights ? 4.0f : 8.0f;
        m_start = start;
        m_distance = start.DistanceTo(end);
        m_direction = m_distance > 0.0001f ? start.DirectionTo(end) : Vector3.Forward;
        m_delay = delay;
        m_age = 0.0f;
        m_radius = radius;
        if (m_color != color)
        {
            m_color = color;
            m_material.AlbedoColor = color;
            m_material.Emission = color;
            m_haloMaterial.AlbedoColor = new Color(color.R, color.G, color.B, 0.22f);
            m_haloMaterial.Emission = color;
            m_light.LightColor = color;
        }
        m_pulse.Basis = new Basis(new Quaternion(Vector3.Up, m_direction));
        m_pulse.Scale = new Vector3(radius, 1.0f, radius);
        m_pulse.Position = start;
        m_pulse.Visible = delay <= 0.0f;
        m_light.Position = start;
        m_light.Visible = delay <= 0.0f && !QuestCombatTelemetry.WeaponLightsDisabled;
        m_telemetryTracked = QuestCombatTelemetry.TrackLaserCreated();
        IsActive = true;
        Visible = true;
        SetProcess(true);
    }

    internal void ResetImmediately()
    {
        IsActive = false;
        Visible = false;
        m_light.Visible = false;
        SetProcess(false);
        if (m_telemetryTracked)
        {
            QuestCombatTelemetry.TrackLaserStopped();
            m_telemetryTracked = false;
        }
    }

    // Godot enables overridden process callbacks when a node enters the tree.
    public override void _Ready() => SetProcess(IsActive);

    public override void _Process(double delta)
    {
        m_age += (float)delta;
        if (m_age < m_delay)
        {
            return;
        }

        m_pulse.Visible = true;
        m_light.Visible = !QuestCombatTelemetry.WeaponLightsDisabled;
        var effectAge = m_age - m_delay;
        var launchLength = Math.Min(PulseLength, m_distance);
        float frontDistance;
        float backDistance;
        if (effectAge < LaunchDurationSeconds)
        {
            frontDistance = launchLength * (effectAge / LaunchDurationSeconds);
            backDistance = 0.0f;
        }
        else
        {
            var travelledDistance =
                (effectAge - LaunchDurationSeconds) * TravelSpeedMetersPerSecond;
            frontDistance = Math.Min(launchLength + travelledDistance, m_distance);
            backDistance = Math.Min(travelledDistance, m_distance);
        }

        var pulseLength = Math.Max(frontDistance - backDistance, 0.01f);
        var centerDistance = (frontDistance + backDistance) * 0.5f;
        var center = m_start + m_direction * centerDistance;
        m_pulse.Scale = new Vector3(m_radius, pulseLength, m_radius);
        m_pulse.Position = center;
        m_light.Position = center;
        if (backDistance >= m_distance - 0.001f || m_distance <= 0.001f)
        {
            ResetImmediately();
            if (!m_reusable) QueueFree();
        }
    }

    public override void _ExitTree()
    {
        ResetImmediately();
    }
}
