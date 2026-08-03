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
    private const float PulseLength = 7.0f;
    private readonly Vector3 m_start;
    private readonly Vector3 m_direction;
    private readonly float m_distance;
    private readonly CylinderMesh m_pulseMesh;
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
            EmissionEnergyMultiplier = 8.0f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        };
        m_pulseMesh = new CylinderMesh
        {
            TopRadius = 0.04f,
            BottomRadius = 0.04f,
            Height = 0.01f,
            RadialSegments = 8,
            Rings = 1,
            Material = material
        };
        m_pulse = new MeshInstance3D
        {
            Mesh = m_pulseMesh,
            Basis = new Basis(new Quaternion(Vector3.Up, m_direction)),
            Position = start,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
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
        var travelledDistance = m_age * TravelSpeedMetersPerSecond;
        var frontDistance = Math.Min(travelledDistance, m_distance);
        var backDistance = Math.Max(0.0f, travelledDistance - PulseLength);
        var pulseLength = Math.Max(frontDistance - backDistance, 0.01f);
        var centerDistance = (frontDistance + backDistance) * 0.5f;
        var center = m_start + m_direction * centerDistance;
        m_pulseMesh.Height = pulseLength;
        m_pulse.Position = center;
        m_light.Position = center;
        if (backDistance >= m_distance - 0.001f || m_distance <= 0.001f)
        {
            QueueFree();
        }
    }
}
