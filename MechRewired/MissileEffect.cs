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
/// Reusable emissive missile projectile with restrained target-seeking steering.
/// </summary>
public partial class MissileEffect : Node3D
{
    private const float SpeedMetersPerSecond = 100.0f;
    private const float TurnRate = 4.5f;
    private const float CloseRangeTurnRate = 8.0f;
    private const float ImpactRadius = 4.0f;
    private readonly OmniLight3D m_light;
    private Vector3 m_direction;
    private float m_range;
    private float m_distanceTravelled;
    private Vector3? m_previousTargetPosition;
    private Vector3 m_targetVelocity;
    private Func<Vector3?> m_targetPosition;
    private Action<Vector3> m_impact;

    public MissileEffect(bool carriesLight)
    {
        Name = "PooledMissile";
        var bodyMaterial = new StandardMaterial3D
        {
            AlbedoColor = Color.FromHtml("565d63"),
            Metallic = 0.7f,
            Roughness = 0.35f
        };
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = 0.055f,
                BottomRadius = 0.07f,
                Height = 0.75f,
                RadialSegments = 8,
                Rings = 1
            },
            MaterialOverride = bodyMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        });

        var exhaustMaterial = new StandardMaterial3D
        {
            AlbedoColor = Color.FromHtml("fff070"),
            EmissionEnabled = true,
            Emission = Color.FromHtml("ffb020"),
            EmissionEnergyMultiplier = 10.0f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        AddChild(new MeshInstance3D
        {
            Position = Vector3.Down * 0.52f,
            Mesh = new CylinderMesh
            {
                TopRadius = 0.025f,
                BottomRadius = 0.14f,
                Height = 0.4f,
                RadialSegments = 8,
                Rings = 1
            },
            MaterialOverride = exhaustMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        });

        m_light = new OmniLight3D
        {
            Position = Vector3.Down * 0.35f,
            LightColor = Color.FromHtml("ffb020"),
            LightEnergy = 3.5f,
            OmniRange = 6.0f,
            ShadowEnabled = false,
            Visible = carriesLight
        };
        AddChild(m_light);
        Deactivate();
    }

    public bool IsActive { get; private set; }

    public float Age { get; private set; }

    public void Launch(
        Vector3 position,
        Vector3 direction,
        float range,
        Func<Vector3?> targetPosition,
        Action<Vector3> impact)
    {
        GlobalPosition = position;
        m_direction = direction.Normalized();
        m_range = range;
        m_distanceTravelled = 0.0f;
        m_previousTargetPosition = null;
        m_targetVelocity = Vector3.Zero;
        m_targetPosition = targetPosition;
        m_impact = impact;
        Age = 0.0f;
        IsActive = true;
        Visible = true;
        SetProcess(true);
        OrientToDirection();
    }

    public override void _Process(double delta)
    {
        if (!IsActive)
        {
            SetProcess(false);
            return;
        }

        var elapsed = (float)delta;
        Age += elapsed;
        var target = m_targetPosition?.Invoke();
        if (target.HasValue)
        {
            if (m_previousTargetPosition.HasValue && elapsed > 0.0001f)
            {
                var measuredVelocity = (target.Value - m_previousTargetPosition.Value) / elapsed;
                m_targetVelocity = m_targetVelocity.Lerp(
                    measuredVelocity,
                    Math.Min(elapsed * 8.0f, 1.0f));
            }

            m_previousTargetPosition = target.Value;
            var targetDistance = GlobalPosition.DistanceTo(target.Value);
            var leadSeconds = Math.Min(targetDistance / SpeedMetersPerSecond, 1.25f);
            var aimPosition = target.Value + m_targetVelocity * leadSeconds * 0.75f;
            var toTarget = GlobalPosition.DirectionTo(aimPosition);
            if (!toTarget.IsZeroApprox())
            {
                var angle = m_direction.AngleTo(toTarget);
                var turnRate = Mathf.Lerp(
                    CloseRangeTurnRate,
                    TurnRate,
                    Math.Min(targetDistance / 120.0f, 1.0f));
                var maximumTurn = turnRate * elapsed;
                m_direction = angle <= maximumTurn
                    ? toTarget
                    : m_direction.Slerp(toTarget, maximumTurn / angle).Normalized();
            }
        }

        var distance = SpeedMetersPerSecond * elapsed;
        var nextPosition = GlobalPosition + m_direction * distance;
        if (target.HasValue && DistanceToSegment(target.Value, GlobalPosition, nextPosition) <= ImpactRadius)
        {
            var impact = target.Value;
            m_impact?.Invoke(impact);
            Deactivate();
            return;
        }

        GlobalPosition = nextPosition;
        m_distanceTravelled += distance;
        OrientToDirection();
        if (m_distanceTravelled >= m_range)
        {
            Deactivate();
        }
    }

    private void OrientToDirection()
    {
        if (!m_direction.IsZeroApprox())
        {
            GlobalBasis = new Basis(new Quaternion(Vector3.Up, m_direction));
        }
    }

    private void Deactivate()
    {
        IsActive = false;
        Visible = false;
        SetProcess(false);
        m_targetPosition = null;
        m_impact = null;
    }

    private static float DistanceToSegment(Vector3 point, Vector3 start, Vector3 end)
    {
        var segment = end - start;
        var lengthSquared = segment.LengthSquared();
        if (lengthSquared <= 0.000001f)
        {
            return point.DistanceTo(start);
        }

        var position = Math.Clamp((point - start).Dot(segment) / lengthSquared, 0.0f, 1.0f);
        return point.DistanceTo(start + segment * position);
    }
}
