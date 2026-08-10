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
/// Every shot owns its own pulse and light, allowing rapid fire to leave several bolts in flight.
/// </remarks>
public partial class LaserEffect : Node3D
{
    private const float TravelSpeedMetersPerSecond = 520.0f;
    private const float PulseLength = 12.0f;
    private const float LaunchDurationSeconds = 0.08f;
    private readonly Vector3 m_start;
    private readonly Vector3 m_direction;
    private readonly float m_distance;
    private readonly MeshInstance3D m_pulse;
    private readonly OmniLight3D m_light;
    private float m_age;

    public LaserEffect(Vector3 start, Vector3 end)
    {
        m_start = start;
        m_distance = start.DistanceTo(end);
        m_direction = m_distance > 0.0001f
            ? start.DirectionTo(end)
            : Vector3.Forward;
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(1.0f, 0.08f, 0.02f),
            EmissionEnabled = true,
            Emission = new Color(1.0f, 0.015f, 0.0f),
            EmissionEnergyMultiplier = 12.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        var pulseMesh = new CylinderMesh
        {
            TopRadius = 0.06f,
            BottomRadius = 0.06f,
            Height = 1.0f,
            RadialSegments = 8,
            Rings = 1
        };
        m_pulse = new MeshInstance3D
        {
            Mesh = pulseMesh,
            MaterialOverride = material,
            Basis = new Basis(new Quaternion(Vector3.Up, m_direction)),
            Position = start,
            ExtraCullMargin = PulseLength,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        var haloMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(1.0f, 0.05f, 0.01f, 0.22f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            EmissionEnabled = true,
            Emission = new Color(1.0f, 0.01f, 0.0f),
            EmissionEnergyMultiplier = 3.0f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        m_pulse.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = 0.22f,
                BottomRadius = 0.22f,
                Height = 1.0f,
                RadialSegments = 8,
                Rings = 1
            },
            MaterialOverride = haloMaterial,
            ExtraCullMargin = PulseLength,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        });
        AddChild(m_pulse);

        m_light = new OmniLight3D
        {
            Position = start,
            LightColor = new Color(1.0f, 0.08f, 0.01f),
            LightEnergy = 5.0f,
            OmniRange = 8.0f,
            ShadowEnabled = false
        };
        AddChild(m_light);
    }

    public override void _Process(double delta)
    {
        m_age += (float)delta;
        var launchLength = Math.Min(PulseLength, m_distance);
        float frontDistance;
        float backDistance;
        if (m_age < LaunchDurationSeconds)
        {
            frontDistance = launchLength * (m_age / LaunchDurationSeconds);
            backDistance = 0.0f;
        }
        else
        {
            var travelledDistance =
                (m_age - LaunchDurationSeconds) * TravelSpeedMetersPerSecond;
            frontDistance = Math.Min(launchLength + travelledDistance, m_distance);
            backDistance = Math.Min(travelledDistance, m_distance);
        }

        var pulseLength = Math.Max(frontDistance - backDistance, 0.01f);
        var centerDistance = (frontDistance + backDistance) * 0.5f;
        var center = m_start + m_direction * centerDistance;
        m_pulse.Scale = new Vector3(1.0f, pulseLength, 1.0f);
        m_pulse.Position = center;
        m_light.Position = center;
        if (backDistance >= m_distance - 0.001f || m_distance <= 0.001f)
        {
            QueueFree();
        }
    }
}
