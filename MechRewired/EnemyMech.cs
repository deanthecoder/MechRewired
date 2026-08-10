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
using MechRewired.Simulation;

namespace MechRewired;

/// <summary>
/// Runs the first data-driven hostile mech combat slice.
/// </summary>
/// <remarks>
/// Original GPS ranges, spawn data, chassis hierarchy and MEK movement data drive the actor. Detailed armor,
/// weapon loadouts, formations and navigation can be layered on without changing mission spawning.
/// </remarks>
public partial class EnemyMech : Node3D
{
    private const float ChassisTurnDegreesPerSecond = 32.0f;
    private const float TorsoTurnDegreesPerSecond = 58.0f;
    private const float MaximumTorsoYawRadians = Mathf.Pi / 2.0f;
    private const float MaximumTorsoPitchRadians = Mathf.Pi / 5.0f;
    private const float SensorIntervalSeconds = 0.2f;
    private const float TargetMemorySeconds = 4.0f;
    private const float InitialSensorHalfAngleDegrees = 70.0f;
    private const int LaserDamage = 5;

    private readonly PlayerMech m_playerMech;
    private readonly BattlefieldEffects m_battlefieldEffects;
    private readonly Func<Vector3, float> m_surfaceHeightProvider;
    private readonly Func<IReadOnlyList<SceneryObstacle>> m_sceneryObstacleProvider;
    private readonly IReadOnlyList<DebugTriangle> m_sceneTriangles;
    private readonly float m_maximumSpeedMetersPerSecond;
    private readonly float m_acquisitionRange;
    private readonly float m_weaponRange;
    private readonly float m_fireInterval;
    private readonly AudioStreamPlayer3D m_laserSound;
    private readonly MechRig m_mechRig;
    private readonly EnemyCombatMovement m_combatMovement;
    private readonly MechDamageModel m_damageModel;
    private readonly List<Marker3D> m_weaponMounts = new();
    private readonly List<(MeshInstance3D Mesh, string PartName)> m_destructibleParts = new();
    private Aabb m_localBounds;
    private float m_modelBottomY;
    private float m_footprintRadius;
    private float m_fireCooldown;
    private float m_sensorCooldown;
    private float m_movementBlockedLogCooldown;
    private float m_targetMemoryRemaining;
    private int m_nextWeaponMount;
    private bool m_acquired;
    private bool m_hasLineOfSight;
    private bool m_hasGaitSample;
    private Vector3 m_previousGaitPosition;
    private float m_previousGaitYaw;
    private Vector3 m_lastKnownTargetPosition;
    private EnemyCombatMovementMode? m_movementMode;

    public EnemyMech(
        MechWarriorMissionGamePiece definition,
        MechWarriorMechFile mechDefinition,
        PlayerMech playerMech,
        BattlefieldEffects battlefieldEffects,
        AudioStreamWav laserSound,
        MechDamageSilhouette damageSilhouette,
        Func<Vector3, float> surfaceHeightProvider,
        Func<IReadOnlyList<SceneryObstacle>> sceneryObstacleProvider,
        IReadOnlyList<DebugTriangle> sceneTriangles)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(mechDefinition);
        ArgumentNullException.ThrowIfNull(playerMech);
        ArgumentNullException.ThrowIfNull(battlefieldEffects);
        ArgumentNullException.ThrowIfNull(laserSound);
        ArgumentNullException.ThrowIfNull(damageSilhouette);
        ArgumentNullException.ThrowIfNull(surfaceHeightProvider);
        ArgumentNullException.ThrowIfNull(sceneryObstacleProvider);
        ArgumentNullException.ThrowIfNull(sceneTriangles);

        Definition = definition;
        MechDefinition = mechDefinition;
        m_playerMech = playerMech;
        m_battlefieldEffects = battlefieldEffects;
        DamageSilhouette = damageSilhouette;
        m_surfaceHeightProvider = surfaceHeightProvider;
        m_sceneryObstacleProvider = sceneryObstacleProvider;
        m_sceneTriangles = sceneTriangles;
        Name = $"Enemy-{definition.Specification.DisplayName}-{definition.Specification.GroupId}";
        m_damageModel = new MechDamageModel(mechDefinition.Sections);
        // Combat manoeuvres use the authored walking/cruising speed. Running is reserved for later pursuit states.
        m_maximumSpeedMetersPerSecond = (float)(mechDefinition.CruisingSpeedKph / 3.6);
        m_weaponRange = Math.Max(definition.Specification.TargetRange, 120);
        m_acquisitionRange = Math.Max(
            m_weaponRange,
            Math.Max(definition.Specification.SleepRange, definition.Specification.RubberbandRange));
        m_fireInterval = Mathf.Clamp(2.8f - definition.Specification.GunnerySkill * 0.12f, 1.35f, 2.8f);
        m_combatMovement = new EnemyCombatMovement(m_weaponRange, definition.Specification.GroupId);

        Legs = new Node3D { Name = "Legs" };
        Torso = new Node3D { Name = "Torso" };
        AddChild(Legs);
        AddChild(Torso);
        m_mechRig = new MechRig { Name = "MechRig" };
        AddChild(m_mechRig);
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

    public MechDamageSilhouette DamageSilhouette { get; }

    public Node3D Legs { get; }

    public Node3D Torso { get; }

    public string Description => Definition.Specification.DisplayName;

    public int Health => m_damageModel.Health;

    public int MaximumHealth => m_damageModel.MaximumHealth;

    public bool IsDestroyed { get; private set; }

    public MechDamageModel Damage => m_damageModel;

    public bool IsImmobilized =>
        m_damageModel.IsSectionDestroyed(MechDamageSection.LeftLeg) ||
        m_damageModel.IsSectionDestroyed(MechDamageSection.RightLeg);

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

    public bool RegisterGaitPart(Node3D node, string partName) =>
        m_mechRig.RegisterPart(node, partName);

    public void RegisterDestructiblePart(MeshInstance3D mesh, string partName)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentException.ThrowIfNullOrWhiteSpace(partName);
        m_destructibleParts.Add((mesh, partName));
    }

    public bool TryRaycastSections(
        Vector3 origin,
        Vector3 direction,
        out MechSectionHit hit) =>
        MechSectionHitTester.TryFindNearest(
            this,
            m_destructibleParts.Where(part => IsAncestorOf(part.Mesh)),
            origin,
            direction,
            out hit);

    public void LogCombatState()
    {
        var playerOffset = m_playerMech.TargetPosition - TargetPosition;
        var desiredYaw = Mathf.Atan2(-playerOffset.X, -playerOffset.Z);
        var relativeTargetYaw = Mathf.AngleDifference(Rotation.Y, desiredYaw);
        var limitedTargetYaw = Mathf.Clamp(
            relativeTargetYaw,
            -MaximumTorsoYawRadians,
            MaximumTorsoYawRadians);
        var destroyed = string.Join(", ", Enum.GetValues<MechDamageSection>()
            .Where(m_damageModel.IsSectionDestroyed));
        GD.Print(
            $"MechRewired: {Description} tracking state: poweredDown={IsPoweredDown}; acquired={m_acquired}; " +
            $"lineOfSight={m_hasLineOfSight}; immobilized={IsImmobilized}; chassis " +
            $"{Mathf.RadToDeg(Rotation.Y):F1} degrees; torso {Mathf.RadToDeg(Torso.Rotation.Y):F1} degrees; " +
            $"target relative {Mathf.RadToDeg(relativeTargetYaw):F1} degrees, limited to " +
            $"{Mathf.RadToDeg(limitedTargetYaw):F1} degrees (±{Mathf.RadToDeg(MaximumTorsoYawRadians):F0}); " +
            $"destroyed [{destroyed}].");
    }

    public void ConfigureVisuals(
        Aabb localBounds,
        Vector3 torsoPivot,
        IReadOnlyList<Vector3> weaponMountPositions)
    {
        m_localBounds = localBounds;
        m_modelBottomY = localBounds.Position.Y;
        m_footprintRadius = Mathf.Max(localBounds.Size.X, localBounds.Size.Z) * 0.35f;
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
        UpdateGait(elapsed);
        m_fireCooldown = Math.Max(0.0f, m_fireCooldown - elapsed);
        m_sensorCooldown = Math.Max(0.0f, m_sensorCooldown - elapsed);
        m_movementBlockedLogCooldown = Math.Max(0.0f, m_movementBlockedLogCooldown - elapsed);
        var playerTargetPosition = m_playerMech.TargetPosition;
        var playerOffset = playerTargetPosition - TargetPosition;
        var playerPlanarOffset = new Vector3(playerOffset.X, 0.0f, playerOffset.Z);
        var playerDistance = playerPlanarOffset.Length();
        if (!m_acquired)
        {
            if (m_sensorCooldown > 0.0f)
            {
                return;
            }

            m_sensorCooldown = SensorIntervalSeconds;
            var hasLineOfSight = HasLineOfSight(TargetPosition, playerTargetPosition);
            if (!EnemyAwareness.CanAcquire(
                    playerDistance,
                    m_acquisitionRange,
                    GetInitialSensorAlignment(playerPlanarOffset),
                    Mathf.Cos(Mathf.DegToRad(InitialSensorHalfAngleDegrees)),
                    hasLineOfSight))
            {
                return;
            }

            PowerUp();
            m_hasLineOfSight = true;
            m_targetMemoryRemaining = TargetMemorySeconds;
            m_lastKnownTargetPosition = playerTargetPosition;
            GD.Print(
                $"MechRewired: {Description} acquired visible PlayerMech at {playerDistance:F0}m " +
                $"(GPS target {Definition.Specification.TargetRange}m; sleep " +
                $"{Definition.Specification.SleepRange}m; rubberband " +
                $"{Definition.Specification.RubberbandRange}m; close awareness " +
                $"{EnemyAwareness.GetCloseAwarenessRange(m_acquisitionRange):F0}m).");
        }
        else
        {
            if (m_sensorCooldown <= 0.0f)
            {
                m_sensorCooldown = SensorIntervalSeconds;
                m_hasLineOfSight = HasLineOfSight(TargetPosition, playerTargetPosition);
                if (m_hasLineOfSight)
                {
                    m_targetMemoryRemaining = TargetMemorySeconds;
                    m_lastKnownTargetPosition = playerTargetPosition;
                }
            }

            if (!m_hasLineOfSight)
            {
                m_targetMemoryRemaining = Math.Max(0.0f, m_targetMemoryRemaining - elapsed);
                if (m_targetMemoryRemaining <= 0.0f)
                {
                    m_acquired = false;
                    GD.Print($"MechRewired: {Description} lost contact with PlayerMech behind cover.");
                    return;
                }

            }
        }

        var movementTarget = m_hasLineOfSight ? playerTargetPosition : m_lastKnownTargetPosition;
        var targetOffset = movementTarget - TargetPosition;
        var planarOffset = new Vector3(targetOffset.X, 0.0f, targetOffset.Z);
        var distance = planarOffset.Length();
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

        var movement = m_combatMovement.Advance(
            elapsed,
            distance,
            m_hasLineOfSight,
            (double)m_playerMech.Health / m_playerMech.MaximumHealth);
        if (m_movementMode != movement.Mode)
        {
            m_movementMode = movement.Mode;
            GD.Print(
                $"MechRewired: {Description} combat movement {movement.Mode} at {playerDistance:F0}m " +
                $"({Health}/{MaximumHealth} health).");
        }

        ApplyCombatMovement(movement, planarOffset, elapsed);

        var playerYaw = Mathf.Atan2(-playerPlanarOffset.X, -playerPlanarOffset.Z);
        var aimYaw = Mathf.Abs(Mathf.AngleDifference(Torso.GlobalRotation.Y, playerYaw));
        if (m_hasLineOfSight &&
            playerDistance <= m_weaponRange &&
            aimYaw <= Mathf.DegToRad(12.0f) &&
            m_fireCooldown <= 0.0f)
        {
            FireLaser();
            m_fireCooldown = m_fireInterval;
        }
    }

    public void ApplyDamage(
        int damage,
        Vector3 hitPosition,
        MechDamageSection section,
        bool fromRear)
    {
        if (IsDestroyed || damage <= 0)
        {
            return;
        }

        var result = m_damageModel.ApplyDamage(section, damage, fromRear);
        m_combatMovement.NotifyDamage();
        GD.Print(
            $"MechRewired: laser hit {Description} ({Definition.Specification.ConfigurationName}) " +
            $"{section}{(result.RearArmorHit ? " rear" : string.Empty)} for {damage} damage " +
            $"({Health}/{MaximumHealth} aggregate; " +
            $"{m_damageModel.GetRemaining(section).InternalStructure}/" +
            $"{m_damageModel.GetMaximum(section).InternalStructure} internal).");
        if (!result.MechDestroyed)
        {
            if (result.SectionNewlyDestroyed)
            {
                DetachSection(section, hitPosition);
            }

            if (!m_acquired)
            {
                PowerUp();
                m_hasLineOfSight = true;
                m_targetMemoryRemaining = TargetMemorySeconds;
                m_sensorCooldown = 0.0f;
                m_lastKnownTargetPosition = m_playerMech.TargetPosition;
                GD.Print($"MechRewired: {Description} alerted by weapon impact.");
            }

            return;
        }

        IsDestroyed = true;
        DetachRemainingMech(hitPosition);
        Legs.Visible = false;
        Torso.Visible = false;
        m_battlefieldEffects.SpawnDestruction(Name, Definition.Specification.GroupId, WorldBounds, hitPosition);
        GD.Print(
            $"MechRewired: destroyed hostile {Description}, piloted by {Definition.Specification.PilotName} " +
            $"({section} destruction; authored sectional armor/internal structure).");
        Destroyed?.Invoke(this);
    }

    private void FireLaser()
    {
        var availableMounts = m_weaponMounts.Where(IsWeaponMountOperational).ToArray();
        if (availableMounts.Length == 0)
        {
            return;
        }

        var mount = availableMounts[m_nextWeaponMount % availableMounts.Length];
        m_nextWeaponMount++;
        var start = mount.GlobalPosition;
        var aimedEnd = m_playerMech.WorldBounds.GetCenter();
        var aimDirection = start.DirectionTo(aimedEnd);
        var end = m_playerMech.TryRaycastSections(start, aimDirection, out var playerHit)
            ? playerHit.Position
            : aimedEnd;
        if (!HasLineOfSight(start, end))
        {
            return;
        }

        GetParent().AddChild(new LaserEffect(start, end));
        m_laserSound.GlobalPosition = start;
        m_laserSound.Play();
        m_battlefieldEffects.SpawnWeaponImpact(end);
        m_playerMech.ApplyDamage(
            LaserDamage,
            Description,
            playerHit?.Section ?? MechDamageSection.CenterTorso,
            playerHit?.FromRear ?? false,
            end);
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

    private void UpdateGait(float delta)
    {
        if (!m_hasGaitSample)
        {
            m_hasGaitSample = true;
            m_previousGaitPosition = GlobalPosition;
            m_previousGaitYaw = GlobalRotation.Y;
            m_mechRig.Advance(0.0f, 0.0f, 0.0f, delta);
            return;
        }

        var distance = GlobalPosition.DistanceTo(m_previousGaitPosition);
        var headingChange = Mathf.Abs(Mathf.AngleDifference(m_previousGaitYaw, GlobalRotation.Y));
        var speedFraction = delta <= 0.0f || m_maximumSpeedMetersPerSecond <= 0.0f
            ? 0.0f
            : Mathf.Clamp(distance / delta / m_maximumSpeedMetersPerSecond, 0.0f, 1.0f);
        m_mechRig.Advance(distance, headingChange, speedFraction, delta);
        m_previousGaitPosition = GlobalPosition;
        m_previousGaitYaw = GlobalRotation.Y;
    }

    private void ApplyCombatMovement(
        EnemyCombatMovementStep movement,
        Vector3 planarOffset,
        float elapsed)
    {
        if (IsImmobilized)
        {
            return;
        }

        var radial = planarOffset.Normalized();
        var lateral = new Vector3(radial.Z, 0.0f, -radial.X);
        var direction = (radial * (float)movement.Radial + lateral * (float)movement.Lateral).Normalized();
        if (direction.LengthSquared() <= 0.0001f)
        {
            return;
        }

        // Keep the chassis combat-facing while the translation vector strafes or creates limited space.
        // This preserves the torso firing arc instead of making an enemy visibly turn and run away.
        var facingLateral = Mathf.Clamp((float)movement.Lateral, -0.7f, 0.7f);
        var facingDirection = (radial + lateral * facingLateral).Normalized();
        var desiredMovementYaw = Mathf.Atan2(-facingDirection.X, -facingDirection.Z);
        Rotation = new Vector3(
            0.0f,
            MoveTowardAngle(
                Rotation.Y,
                desiredMovementYaw,
                Mathf.DegToRad(ChassisTurnDegreesPerSecond) * elapsed),
            0.0f);
        if (Mathf.Abs(Mathf.AngleDifference(Rotation.Y, desiredMovementYaw)) > Mathf.DegToRad(28.0f))
        {
            return;
        }

        var step = m_maximumSpeedMetersPerSecond * (float)movement.SpeedFraction * elapsed;
        if (TryMove(direction, step, out var blockingObstacle))
        {
            return;
        }

        // Try the opposite side immediately, then retain it so an obstacle does not cause frame-by-frame jitter.
        var alternateDirection = (radial * (float)movement.Radial - lateral * (float)movement.Lateral).Normalized();
        SceneryObstacle alternateBlockingObstacle = null;
        if (alternateDirection.LengthSquared() > 0.0001f &&
            TryMove(alternateDirection, step, out alternateBlockingObstacle))
        {
            m_combatMovement.ReverseStrafeDirection();
            return;
        }

        if (m_movementBlockedLogCooldown <= 0.0f)
        {
            var obstacle = alternateBlockingObstacle ?? blockingObstacle;
            GD.Print(
                $"MechRewired: {Description} combat movement blocked by scenery " +
                $"'{obstacle?.Name ?? "unknown"}' while {movement.Mode}.");
            m_movementBlockedLogCooldown = 2.0f;
        }
    }

    private bool IsWeaponMountOperational(Marker3D mount)
    {
        if (mount.Position.X < -0.1f && m_damageModel.IsSectionDestroyed(MechDamageSection.LeftArm))
        {
            return false;
        }

        return mount.Position.X <= 0.1f ||
               !m_damageModel.IsSectionDestroyed(MechDamageSection.RightArm);
    }

    private void DetachSection(MechDamageSection section, Vector3 hitPosition)
    {
        var bodySections = section switch
        {
            MechDamageSection.LeftArm => new[] { MechBodySection.LeftArm },
            MechDamageSection.RightArm => new[] { MechBodySection.RightArm },
            MechDamageSection.LeftLeg => new[]
            {
                MechBodySection.LeftUpperLeg,
                MechBodySection.LeftLowerLeg,
                MechBodySection.LeftFoot
            },
            MechDamageSection.RightLeg => new[]
            {
                MechBodySection.RightUpperLeg,
                MechBodySection.RightLowerLeg,
                MechBodySection.RightFoot
            },
            _ => Array.Empty<MechBodySection>()
        };
        var parts = m_destructibleParts.Where(part =>
                IsAncestorOf(part.Mesh) &&
                bodySections.Contains(MechBodySectionClassifier.Classify(part.PartName)))
            .ToArray();
        if (parts.Length == 0)
        {
            return;
        }

        MechWreckage.Spawn(
            GetParent(),
            m_playerMech,
            $"{Name}-{section}",
            parts,
            hitPosition,
            Definition.Specification.GroupId * 7919 + 104729 + (int)section);
        GD.Print($"MechRewired: {Description} lost {section}; {parts.Length} authored meshes detached.");
    }

    private void DetachRemainingMech(Vector3 hitPosition)
    {
        var remaining = m_destructibleParts.Where(part => IsAncestorOf(part.Mesh)).ToArray();
        if (remaining.Length > 0)
        {
            MechWreckage.Spawn(
                GetParent(),
                m_playerMech,
                Name,
                remaining,
                hitPosition,
                Definition.Specification.GroupId * 7919 + 104729);
        }
    }

    private bool TryMove(Vector3 direction, float distance, out SceneryObstacle blockingObstacle)
    {
        var candidate = GlobalPosition + direction * distance;
        if (SceneryCollision.TryFindBlockingObstacle(
                new System.Numerics.Vector2(GlobalPosition.X, GlobalPosition.Z),
                new System.Numerics.Vector2(candidate.X, candidate.Z),
                m_footprintRadius,
                m_sceneryObstacleProvider(),
                out blockingObstacle))
        {
            return false;
        }

        blockingObstacle = null;
        candidate.Y = m_surfaceHeightProvider(candidate) - m_modelBottomY;
        GlobalPosition = candidate;
        return true;
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

    private float GetInitialSensorAlignment(Vector3 planarOffset)
    {
        if (planarOffset.LengthSquared() <= 0.0001f)
        {
            return 1.0f;
        }

        var forward = -GlobalBasis.Z;
        forward.Y = 0.0f;
        forward = forward.Normalized();
        return forward.Dot(planarOffset.Normalized());
    }

    private static float MoveTowardAngle(float current, float target, float maximumDelta) =>
        current + Mathf.Clamp(
            Mathf.AngleDifference(current, target),
            -maximumDelta,
            maximumDelta);
}
