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
/// Runs the first data-driven hostile mech combat slice.
/// </summary>
/// <remarks>
/// Original GPS ranges, spawn data, chassis hierarchy and MEK movement data drive the actor. Detailed armor,
/// weapon loadouts, formations and leg animation can be layered on without changing mission spawning.
/// </remarks>
public partial class EnemyMech : Node3D
{
    private const float ChassisTurnDegreesPerSecond = 32.0f;
    private const float TorsoTurnDegreesPerSecond = 58.0f;
    private const float MaximumTorsoYawRadians = Mathf.Pi / 2.0f;
    private const float MaximumTorsoPitchRadians = Mathf.Pi / 5.0f;
    private const float MovementSpeedFactor = 0.42f;
    private const float MinimumStandOffDistance = 90.0f;
    private const float SensorIntervalSeconds = 0.2f;
    private const float TargetMemorySeconds = 4.0f;
    private const float InitialSensorHalfAngleDegrees = 70.0f;
    private const int LaserDamage = 5;

    private readonly PlayerMech m_playerMech;
    private readonly BattlefieldEffects m_battlefieldEffects;
    private readonly Func<Vector3, float> m_surfaceHeightProvider;
    private readonly IReadOnlyList<DebugTriangle> m_sceneTriangles;
    private readonly float m_maximumSpeedMetersPerSecond;
    private readonly float m_acquisitionRange;
    private readonly float m_weaponRange;
    private readonly float m_fireInterval;
    private readonly AudioStreamPlayer3D m_laserSound;
    private readonly List<Marker3D> m_weaponMounts = new();
    private Aabb m_localBounds;
    private float m_modelBottomY;
    private float m_fireCooldown;
    private float m_sensorCooldown;
    private float m_targetMemoryRemaining;
    private int m_nextWeaponMount;
    private bool m_acquired;
    private bool m_hasLineOfSight;

    public EnemyMech(
        MechWarriorMissionGamePiece definition,
        MechWarriorMechFile mechDefinition,
        PlayerMech playerMech,
        BattlefieldEffects battlefieldEffects,
        AudioStreamWav laserSound,
        Texture2D damageSilhouette,
        Func<Vector3, float> surfaceHeightProvider,
        IReadOnlyList<DebugTriangle> sceneTriangles)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(mechDefinition);
        ArgumentNullException.ThrowIfNull(playerMech);
        ArgumentNullException.ThrowIfNull(battlefieldEffects);
        ArgumentNullException.ThrowIfNull(laserSound);
        ArgumentNullException.ThrowIfNull(damageSilhouette);
        ArgumentNullException.ThrowIfNull(surfaceHeightProvider);
        ArgumentNullException.ThrowIfNull(sceneTriangles);

        Definition = definition;
        MechDefinition = mechDefinition;
        m_playerMech = playerMech;
        m_battlefieldEffects = battlefieldEffects;
        DamageSilhouette = damageSilhouette;
        m_surfaceHeightProvider = surfaceHeightProvider;
        m_sceneTriangles = sceneTriangles;
        Name = $"Enemy-{definition.Specification.DisplayName}-{definition.Specification.GroupId}";
        MaximumHealth = Math.Max(14, mechDefinition.Tonnage * 2);
        Health = MaximumHealth;
        m_maximumSpeedMetersPerSecond = (float)(mechDefinition.MaximumSpeedKph / 3.6) * MovementSpeedFactor;
        m_weaponRange = Math.Max(definition.Specification.TargetRange, 120);
        m_acquisitionRange = Math.Max(
            m_weaponRange,
            Math.Max(definition.Specification.SleepRange, definition.Specification.RubberbandRange));
        m_fireInterval = Mathf.Clamp(2.8f - definition.Specification.GunnerySkill * 0.12f, 1.35f, 2.8f);

        Legs = new Node3D { Name = "Legs" };
        Torso = new Node3D { Name = "Torso" };
        AddChild(Legs);
        AddChild(Torso);
        m_laserSound = new AudioStreamPlayer3D
        {
            Name = "LaserSound",
            Stream = laserSound,
            MaxDistance = 1200.0f,
            UnitSize = 18.0f,
            MaxPolyphony = 4,
            VolumeDb = -1.0f
        };
        AddChild(m_laserSound);
    }

    public MechWarriorMissionGamePiece Definition { get; }

    public MechWarriorMechFile MechDefinition { get; }

    public Texture2D DamageSilhouette { get; }

    public Node3D Legs { get; }

    public Node3D Torso { get; }

    public string Description => Definition.Specification.DisplayName;

    public int Health { get; private set; }

    public int MaximumHealth { get; }

    public bool IsDestroyed { get; private set; }

    /// <summary>
    /// Whether the mech's reactor is offline and therefore unavailable to target sensors.
    /// </summary>
    /// <remarks>
    /// Until an authored initial power-state command is decoded, hostiles begin dormant. Their existing
    /// GPS/range, sensor-cone and line-of-sight data decides when they activate; a successful weapon hit
    /// also wakes them immediately.
    /// </remarks>
    public bool IsPoweredDown { get; private set; } = true;

    public Aabb WorldBounds => GlobalTransform * m_localBounds;

    public Vector3 TargetPosition => WorldBounds.GetCenter();

    public event Action<EnemyMech> Destroyed;

    /// <summary>Raised once when this hostile activates its reactor/sensors.</summary>
    public event Action<EnemyMech> PoweredUp;

    public void ConfigureVisuals(
        Aabb localBounds,
        Vector3 torsoPivot,
        IReadOnlyList<Vector3> weaponMountPositions)
    {
        m_localBounds = localBounds;
        m_modelBottomY = localBounds.Position.Y;
        Torso.Position = torsoPivot;
        foreach (var position in weaponMountPositions)
        {
            var mount = new Marker3D
            {
                Name = $"WeaponMount{m_weaponMounts.Count}",
                Position = position - torsoPivot
            };
            Torso.AddChild(mount);
            m_weaponMounts.Add(mount);
        }

        if (m_weaponMounts.Count == 0)
        {
            var fallback = new Marker3D
            {
                Name = "FallbackWeaponMount",
                Position = new Vector3(localBounds.Size.X * 0.45f, localBounds.Size.Y * 0.65f, -localBounds.Size.Z * 0.45f)
            };
            Torso.AddChild(fallback);
            m_weaponMounts.Add(fallback);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (IsDestroyed || m_playerMech.IsDestroyed || m_localBounds.Size == Vector3.Zero)
        {
            return;
        }

        var elapsed = (float)delta;
        m_fireCooldown = Math.Max(0.0f, m_fireCooldown - elapsed);
        m_sensorCooldown = Math.Max(0.0f, m_sensorCooldown - elapsed);
        var targetOffset = m_playerMech.TargetPosition - TargetPosition;
        var planarOffset = new Vector3(targetOffset.X, 0.0f, targetOffset.Z);
        var distance = planarOffset.Length();
        if (!m_acquired)
        {
            if (m_sensorCooldown > 0.0f)
            {
                return;
            }

            m_sensorCooldown = SensorIntervalSeconds;
            if (distance > m_acquisitionRange ||
                !IsInsideInitialSensorCone(planarOffset) ||
                !HasLineOfSight(TargetPosition, m_playerMech.TargetPosition))
            {
                return;
            }

            PowerUp();
            m_hasLineOfSight = true;
            m_targetMemoryRemaining = TargetMemorySeconds;
            GD.Print(
                $"MechRewired: {Description} acquired visible PlayerMech at {distance:F0}m " +
                $"(GPS target {Definition.Specification.TargetRange}m; sleep " +
                $"{Definition.Specification.SleepRange}m; rubberband " +
                $"{Definition.Specification.RubberbandRange}m).");
        }
        else
        {
            if (m_sensorCooldown <= 0.0f)
            {
                m_sensorCooldown = SensorIntervalSeconds;
                m_hasLineOfSight = HasLineOfSight(TargetPosition, m_playerMech.TargetPosition);
                if (m_hasLineOfSight)
                {
                    m_targetMemoryRemaining = TargetMemorySeconds;
                }
            }

            if (!m_hasLineOfSight)
            {
                m_targetMemoryRemaining = Math.Max(0.0f, m_targetMemoryRemaining - elapsed);
                if (m_targetMemoryRemaining <= 0.0f)
                {
                    m_acquired = false;
                    GD.Print($"MechRewired: {Description} lost contact with PlayerMech behind cover.");
                }

                return;
            }
        }

        if (distance <= 0.01f)
        {
            return;
        }

        var desiredYaw = Mathf.Atan2(-planarOffset.X, -planarOffset.Z);
        var relativeYaw = Mathf.AngleDifference(Rotation.Y, desiredYaw);
        var desiredTorsoYaw = Mathf.Clamp(relativeYaw, -MaximumTorsoYawRadians, MaximumTorsoYawRadians);
        Torso.Rotation = new Vector3(
            Mathf.MoveToward(
                Torso.Rotation.X,
                Mathf.Clamp(Mathf.Atan2(targetOffset.Y, Math.Max(distance, 0.01f)), -MaximumTorsoPitchRadians, MaximumTorsoPitchRadians),
                Mathf.DegToRad(TorsoTurnDegreesPerSecond) * elapsed),
            MoveTowardAngle(
                Torso.Rotation.Y,
                desiredTorsoYaw,
                Mathf.DegToRad(TorsoTurnDegreesPerSecond) * elapsed),
            0.0f);

        var standOffDistance = Math.Max(MinimumStandOffDistance, m_weaponRange * 0.45f);
        if (distance > standOffDistance)
        {
            Rotation = new Vector3(
                0.0f,
                MoveTowardAngle(
                    Rotation.Y,
                    desiredYaw,
                    Mathf.DegToRad(ChassisTurnDegreesPerSecond) * elapsed),
                0.0f);
            var chassisAlignment = Mathf.Abs(Mathf.AngleDifference(Rotation.Y, desiredYaw));
            if (chassisAlignment <= Mathf.DegToRad(25.0f))
            {
                var step = Math.Min(distance - standOffDistance, m_maximumSpeedMetersPerSecond * elapsed);
                var candidate = GlobalPosition - GlobalBasis.Z * step;
                candidate.Y = m_surfaceHeightProvider(candidate) - m_modelBottomY;
                GlobalPosition = candidate;
            }
        }

        var aimYaw = Mathf.Abs(Mathf.AngleDifference(Torso.GlobalRotation.Y, desiredYaw));
        if (m_hasLineOfSight &&
            distance <= m_weaponRange &&
            aimYaw <= Mathf.DegToRad(12.0f) &&
            m_fireCooldown <= 0.0f)
        {
            FireLaser();
            m_fireCooldown = m_fireInterval;
        }
    }

    public void ApplyDamage(int damage, Vector3 hitPosition)
    {
        if (IsDestroyed || damage <= 0)
        {
            return;
        }

        Health = Math.Max(0, Health - damage);
        GD.Print(
            $"MechRewired: laser hit {Description} ({Definition.Specification.ConfigurationName}) " +
            $"for {damage} damage ({Health}/{MaximumHealth}).");
        if (Health > 0)
        {
            if (!m_acquired)
            {
                PowerUp();
                m_hasLineOfSight = true;
                m_targetMemoryRemaining = TargetMemorySeconds;
                m_sensorCooldown = 0.0f;
                GD.Print($"MechRewired: {Description} alerted by weapon impact.");
            }

            return;
        }

        IsDestroyed = true;
        Legs.Visible = false;
        Torso.Visible = false;
        m_battlefieldEffects.SpawnDestruction(Name, Definition.Specification.GroupId, WorldBounds, hitPosition);
        GD.Print(
            $"MechRewired: destroyed hostile {Description}, piloted by {Definition.Specification.PilotName} " +
            $"(whole-mech health; component armor pending).");
        Destroyed?.Invoke(this);
    }

    private void FireLaser()
    {
        var mount = m_weaponMounts[m_nextWeaponMount % m_weaponMounts.Count];
        m_nextWeaponMount++;
        var start = mount.GlobalPosition;
        var end = m_playerMech.TargetPosition;
        if (!HasLineOfSight(start, end))
        {
            return;
        }

        GetParent().AddChild(new LaserEffect(start, end));
        m_laserSound.GlobalPosition = start;
        m_laserSound.Play();
        m_battlefieldEffects.SpawnWeaponImpact(end);
        m_playerMech.ApplyDamage(LaserDamage, Description);
    }

    private void PowerUp()
    {
        if (!IsPoweredDown)
        {
            m_acquired = true;
            return;
        }

        m_acquired = true;
        IsPoweredDown = false;
        PoweredUp?.Invoke(this);
    }

    private bool HasLineOfSight(Vector3 start, Vector3 end)
    {
        var distance = start.DistanceTo(end);
        if (distance <= 0.01f)
        {
            return true;
        }

        return !DebugTriangleRaycaster.TryFindNearest(
                   m_sceneTriangles,
                   start,
                   start.DirectionTo(end),
                   out _,
                   out var hitDistance) ||
               hitDistance >= distance - 1.0f;
    }

    private bool IsInsideInitialSensorCone(Vector3 planarOffset)
    {
        if (planarOffset.LengthSquared() <= 0.0001f)
        {
            return true;
        }

        var forward = -GlobalBasis.Z;
        forward.Y = 0.0f;
        forward = forward.Normalized();
        return forward.Dot(planarOffset.Normalized()) >=
               Mathf.Cos(Mathf.DegToRad(InitialSensorHalfAngleDegrees));
    }

    private static float MoveTowardAngle(float current, float target, float maximumDelta) =>
        current + Mathf.Clamp(
            Mathf.AngleDifference(current, target),
            -maximumDelta,
            maximumDelta);
}
