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
    private Vector3 m_start;
    private Vector3 m_direction;
    private float m_distance;
    private float m_delay;
    private readonly MeshInstance3D m_tracer;
    private float m_age;
    private bool m_reusable;
    public bool IsActive { get; private set; }
    private static readonly CylinderMesh s_mesh = new() { TopRadius = 0.012f, BottomRadius = 0.012f, Height = 1.0f, RadialSegments = 6, Rings = 1 };
    private static readonly StandardMaterial3D s_material = new()
    {
        AlbedoColor = Color.FromHtml("ffc050"), EmissionEnabled = true,
        Emission = Color.FromHtml("ffc050"), EmissionEnergyMultiplier = 4.0f,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
    };

    public BallisticTracerEffect(Vector3 start, Vector3 end, float delay)
        : this()
    {
        Launch(start, end, delay, false);
    }

    public BallisticTracerEffect()
    {
        m_tracer = new MeshInstance3D
        {
            Mesh = s_mesh,
            MaterialOverride = s_material,
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        AddChild(m_tracer);
        Visible = false;
        SetProcess(false);
    }

    internal void Launch(Vector3 start, Vector3 end, float delay, bool reusable = true)
    {
        m_start = start;
        m_distance = start.DistanceTo(end);
        m_direction = m_distance > 0.0001f ? start.DirectionTo(end) : Vector3.Forward;
        m_delay = delay;
        m_age = 0.0f;
        m_reusable = reusable;
        m_tracer.Basis = new Basis(new Quaternion(Vector3.Up, m_direction));
        m_tracer.Scale = Vector3.One;
        m_tracer.Position = start;
        m_tracer.Visible = false;
        IsActive = true;
        Visible = true;
        SetProcess(true);
    }

    internal void ResetImmediately()
    {
        IsActive = false;
        Visible = false;
        SetProcess(false);
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

        m_tracer.Visible = true;
        var frontDistance = Math.Min((m_age - m_delay) * SpeedMetersPerSecond, m_distance);
        var backDistance = Math.Max(frontDistance - TracerLength, 0.0f);
        var length = Math.Max(frontDistance - backDistance, 0.01f);
        m_tracer.Scale = new Vector3(1.0f, length, 1.0f);
        m_tracer.Position = m_start + m_direction * ((frontDistance + backDistance) * 0.5f);
        if (frontDistance >= m_distance - 0.001f || m_distance <= 0.001f)
        {
            ResetImmediately();
            if (!m_reusable) QueueFree();
        }
    }
}
