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
/// Renders one short machine-gun tracer without the long halo and travelling light used by lasers.
/// </summary>
public partial class BallisticTracerEffect : Node3D
{
    private const float SpeedMetersPerSecond = 360.0f;
    private const float TracerLength = 1.4f;
    private readonly Vector3 m_start;
    private readonly Vector3 m_direction;
    private readonly float m_distance;
    private readonly float m_delay;
    private readonly MeshInstance3D m_tracer;
    private float m_age;

    public BallisticTracerEffect(Vector3 start, Vector3 end, float delay)
    {
        m_start = start;
        m_distance = start.DistanceTo(end);
        m_direction = m_distance > 0.0001f ? start.DirectionTo(end) : Vector3.Forward;
        m_delay = delay;
        var color = Color.FromHtml("ffc050");
        m_tracer = new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = 0.012f,
                BottomRadius = 0.012f,
                Height = 1.0f,
                RadialSegments = 6,
                Rings = 1
            },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = color,
                EmissionEnabled = true,
                Emission = color,
                EmissionEnergyMultiplier = 4.0f,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
            },
            Basis = new Basis(new Quaternion(Vector3.Up, m_direction)),
            Position = start,
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        AddChild(m_tracer);
    }

    public override void _Process(double delta)
    {
        m_age += (float)delta;
        if (m_age < m_delay)
        {
            return;
        }

        m_tracer.Visible = true;
        var frontDistance = Math.Min((m_age - m_delay) * SpeedMetersPerSecond, m_distance);
        var backDistance = Math.Max(frontDistance - TracerLength, 0.0f);
        var length = Math.Max(frontDistance - backDistance, 0.01f);
        m_tracer.Scale = new Vector3(1.0f, length, 1.0f);
        m_tracer.Position = m_start + m_direction * ((frontDistance + backDistance) * 0.5f);
        if (frontDistance >= m_distance - 0.001f || m_distance <= 0.001f)
        {
            QueueFree();
        }
    }
}
