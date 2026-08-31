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
/// Runs a data-driven hostile combatant using its authored MEK weapons, ammunition, and armour sections.
/// </summary>
/// <remarks>
/// Original GPS ranges, spawn data, chassis hierarchy, weapon loadout, ammunition and MEK movement data drive
/// the actor. Zero-movement MEKs retain the same combat pipeline as fixed emplacements while keeping
/// their authored yaw/pitch joints stationary at the deployment point.
/// </remarks>
public partial class EnemyMech : Node3D
{
    private const float ChassisTurnDegreesPerSecond = 32.0f;
    private const float TorsoTurnDegreesPerSecond = 58.0f;
    private const float MaximumTorsoYawRadians = Mathf.Pi / 2.0f;
    private const float MaximumTorsoPitchRadians = Mathf.Pi / 5.0f;
    private const float SensorIntervalSeconds = 0.2f;
    private const float TargetMemorySeconds = 4.0f;
    private const float FireDecisionIntervalSeconds = 0.60f;
    private const float MaximumSustainedHeatFraction = 0.75f;
    private const float BaseAimErrorDegrees = 0.35f;
    private const float AimErrorPerGunnerySkillDegrees = 0.30f;
    private const float MaximumRangeAimErrorDegrees = 0.75f;
    private const int MissilePoolSize = 24;

    private readonly PlayerMech m_playerMech;
    private readonly BattlefieldEffects m_battlefieldEffects;
    private readonly Func<Vector3, float> m_surfaceHeightProvider;
    private readonly Func<IReadOnlyList<SceneryObstacle>> m_sceneryObstacleProvider;
    private readonly IReadOnlyList<DebugTriangle> m_sceneTriangles;
    private readonly float m_maximumSpeedMetersPerSecond;
    private readonly float m_wakeRange;
    private readonly float m_observationRange;
    private readonly float m_atmosphericVisibilityRange;
    private readonly float m_weaponRange;
    private readonly IReadOnlyDictionary<string, AudioStreamWav> m_weaponSounds;
    private readonly AudioStreamPlayer3D m_weaponSound;
    private readonly MechRig m_mechRig;
    private readonly EnemyCombatMovement m_combatMovement;
    private readonly MechDamageModel m_damageModel;
    private readonly MechHeat m_heat;
    private readonly Random m_random;
    private readonly List<EnemyWeapon> m_weapons = new();
    private readonly List<MissileEffect> m_missilePool = new();
    private readonly List<PendingMissile> m_pendingMissiles = new();
    private readonly List<(MeshInstance3D Mesh, string PartName)> m_destructibleParts = new();
    private Aabb m_localBounds;
    private float m_modelBottomY;
    private float m_footprintRadius;
    private float m_fireDecisionCooldown;
    private float m_sensorCooldown;
    private float m_movementBlockedLogCooldown;
    private float m_targetMemoryRemaining;
    private int m_nextWeapon;
    private bool m_isAlerted;
    private bool m_hasLineOfSight;
    private bool m_hasGaitSample;
    private Vector3 m_previousGaitPosition;
    private float m_previousGaitYaw;
    private int m_footfallCount;
    private Vector3 m_lastKnownTargetPosition;
    private EnemyCombatMovementMode? m_movementMode;
    private Node3D m_aimPitchPivot;
    private Vector3 m_aimPitchRestRotation;

    public EnemyMech(
        MechWarriorMissionGamePiece definition,
        MechWarriorMechFile mechDefinition,
        PlayerMech playerMech,
        BattlefieldEffects battlefieldEffects,
        IReadOnlyDictionary<string, AudioStreamWav> weaponSounds,
        MechDamageSilhouette damageSilhouette,
        float atmosphericVisibilityRange,
        Func<Vector3, float> surfaceHeightProvider,
        Func<IReadOnlyList<SceneryObstacle>> sceneryObstacleProvider,
        IReadOnlyList<DebugTriangle> sceneTriangles)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(mechDefinition);
        ArgumentNullException.ThrowIfNull(playerMech);
        ArgumentNullException.ThrowIfNull(battlefieldEffects);
        ArgumentNullException.ThrowIfNull(weaponSounds);
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
        m_weaponSounds = weaponSounds;
        Name = $"Enemy-{definition.Specification.DisplayName}-{definition.Specification.GroupId}";
        m_damageModel = new MechDamageModel(mechDefinition.Sections);
        var effectiveHeatSinks = Math.Max(mechDefinition.HeatSinkCount, 1);
        m_heat = new MechHeat(
            MechHeat.GetCriticalHeatThreshold(effectiveHeatSinks),
            effectiveHeatSinks / 10.0);
        m_random = new Random(HashCode.Combine(definition.Specification.GroupId, definition.Specification.MechResourceIndex));
        // Combat manoeuvres use the authored walking/cruising speed. Running is reserved for later pursuit states.
        m_maximumSpeedMetersPerSecond = (float)(mechDefinition.CruisingSpeedKph / 3.6);
        m_weaponRange = Math.Max(definition.Specification.TargetRange, 120);
        m_atmosphericVisibilityRange = atmosphericVisibilityRange;
        m_wakeRange = (float)EnemyAwareness.GetWakeRange(
            definition.Specification.SleepRange,
            definition.Specification.TargetRange);
        m_observationRange = (float)EnemyAwareness.GetVisualAcquisitionRange(
            definition.Specification.TargetRange,
            definition.Specification.SleepRange,
            atmosphericVisibilityRange);
        m_combatMovement = new EnemyCombatMovement(m_weaponRange, definition.Specification.GroupId);

        Legs = new Node3D { Name = "Legs" };
        Torso = new Node3D { Name = "Torso" };
        AddChild(Legs);
        AddChild(Torso);
        m_mechRig = new MechRig { Name = "MechRig" };
        AddChild(m_mechRig);
        m_weaponSound = new AudioStreamPlayer3D
        {
            Name = "WeaponSound",
            MaxDistance = 1200.0f,
            UnitSize = 18.0f,
            MaxPolyphony = 16,
            VolumeDb = -1.0f
        };
        AddChild(m_weaponSound);
    }

    public MechWarriorMissionGamePiece Definition { get; }

    public MechWarriorMechFile MechDefinition { get; }

    public MechDamageSilhouette DamageSilhouette { get; }

    public Node3D Legs { get; }

    public Node3D Torso { get; }

    public string Description => Definition.Specification.DisplayName;

    public int Health => m_damageModel.Health;

    public int MaximumHealth => m_damageModel.MaximumHealth;

    public double Heat => m_heat.CurrentHeat;

    public double MaximumHeat => m_heat.MaximumHeat;

    public string WeaponLoadout => string.Join(
        ", ",
        m_weapons.Select(weapon =>
            weapon.Definition.SourceId + ":" + weapon.Definition.Specification.HudName +
            (weapon.Ammo >= 0 ? " " + weapon.Ammo : string.Empty)));

    public bool IsDestroyed { get; private set; }

    public bool IsStationaryEmplacement => MechDefinition.WalkingMovementPoints == 0;

    public MechDamageModel Damage => m_damageModel;

    public bool IsImmobilized =>
        IsStationaryEmplacement ||
        m_damageModel.IsSectionDestroyed(MechDamageSection.LeftLeg) ||
        m_damageModel.IsSectionDestroyed(MechDamageSection.RightLeg);

    /// <summary>
    /// Whether the mech's reactor is offline and therefore unavailable to target sensors.
    /// </summary>
    /// <remarks>
    /// Hostiles begin dormant until the player reaches their authored GPS sleep range or lands a weapon hit.
    /// Line of sight controls visual contact and firing only; it does not keep a nearby guard asleep behind terrain.
    /// </remarks>
    public bool IsPoweredDown { get; private set; } = true;

    public Aabb WorldBounds => GlobalTransform * m_localBounds;

    public Vector3 TargetPosition => WorldBounds.GetCenter();

    public event Action<EnemyMech> Destroyed;

    /// <summary>Raised once when this hostile activates its reactor/sensors.</summary>
    public event Action<EnemyMech> PoweredUp;

    public override void _Ready()
    {
        if (MechDefinition.Weapons.Any(weapon => weapon.Specification.Kind == MechWeaponKind.Missile))
        {
            CreateMissilePool();
        }
    }

    public bool RegisterGaitPart(Node3D node, string partName) =>
        m_mechRig.RegisterPart(node, partName);

    public void RegisterDestructiblePart(MeshInstance3D mesh, string partName)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentException.ThrowIfNullOrWhiteSpace(partName);
        m_destructibleParts.Add((mesh, partName));
    }

    public void ConfigureAimPitchPivot(Node3D pivot)
    {
        ArgumentNullException.ThrowIfNull(pivot);
        m_aimPitchPivot = pivot;
        m_aimPitchRestRotation = pivot.Rotation;
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
            $"MechRewired: {Description} tracking state: poweredDown={IsPoweredDown}; alerted={m_isAlerted}; " +
            $"lineOfSight={m_hasLineOfSight}; immobilized={IsImmobilized}; chassis " +
            $"{Mathf.RadToDeg(Rotation.Y):F1} degrees; torso {Mathf.RadToDeg(Torso.Rotation.Y):F1} degrees; " +
            $"target relative {Mathf.RadToDeg(relativeTargetYaw):F1} degrees, limited to " +
            $"{Mathf.RadToDeg(limitedTargetYaw):F1} degrees (±{Mathf.RadToDeg(MaximumTorsoYawRadians):F0}); " +
            $"heat {m_heat.CurrentHeat:F1}/{m_heat.MaximumHeat:F0}; destroyed [{destroyed}].");
    }

#if DEBUG
    /// <summary>Destroys this hostile through the normal combat/destruction path for DEBUG testing.</summary>
    public void DebugDestroy()
    {
        if (IsDestroyed)
        {
            return;
        }

        ApplyDamage(Math.Max(Health, 1), TargetPosition, MechDamageSection.CenterTorso, false);
    }
#endif

    public void ConfigureVisuals(
        Aabb localBounds,
        Vector3 torsoPivot,
        IReadOnlyList<MechWeaponMountDefinition> weaponMounts)
    {
        m_localBounds = localBounds;
        m_modelBottomY = localBounds.Position.Y;
        m_footprintRadius = Mathf.Max(localBounds.Size.X, localBounds.Size.Z) * 0.35f;
        Torso.Position = torsoPivot;
        var mountsBySection = new Dictionary<MechDamageSection, List<Marker3D>>();
        foreach (var definition in weaponMounts)
        {
            var mount = new Marker3D
            {
                Name = $"WeaponMount{definition.Id}-{definition.Section}",
                Position = definition.RotatesWithTorso
                    ? definition.Position - torsoPivot
                    : definition.Position
            };
            (definition.RotatesWithTorso ? Torso : Legs).AddChild(mount);
            if (!mountsBySection.TryGetValue(definition.Section, out var mounts))
            {
                mounts = [];
                mountsBySection.Add(definition.Section, mounts);
            }

            mounts.Add(mount);
        }

        Marker3D fallback = null;
        if (mountsBySection.Count == 0)
        {
            fallback = new Marker3D
            {
                Name = "FallbackWeaponMount",
                Position = new Vector3(localBounds.Size.X * 0.45f, localBounds.Size.Y * 0.65f, -localBounds.Size.Z * 0.45f)
            };
            Torso.AddChild(fallback);
        }

        var nextMountBySection = new Dictionary<MechDamageSection, int>();
        foreach (var weapon in MechDefinition.Weapons)
        {
            var mount = fallback ?? GetNextMount(weapon.Section, mountsBySection, nextMountBySection);
            if (mount == null)
            {
                GD.PushWarning(
                    $"MechRewired: {Description} has no POFO marker for {weapon.Specification.HudName} " +
                    $"and will not fire that mount.");
                continue;
            }

            var ammo = weapon.Specification.UsesAmmo
                ? MechDefinition.AmmoBins.Count(bin => bin.AssociatedWeaponId == weapon.SourceId) *
                  weapon.Specification.AmmoPerBin
                : -1;
            m_weapons.Add(new EnemyWeapon(weapon, mount, ammo));
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (IsDestroyed || m_playerMech.IsDestroyed || m_localBounds.Size == Vector3.Zero)
        {
            return;
        }

        var elapsed = (float)delta;
        if (!IsStationaryEmplacement)
        {
            UpdateGait(elapsed);
        }
        m_heat.Advance(elapsed);
        m_fireDecisionCooldown = Math.Max(0.0f, m_fireDecisionCooldown - elapsed);
        m_sensorCooldown = Math.Max(0.0f, m_sensorCooldown - elapsed);
        m_movementBlockedLogCooldown = Math.Max(0.0f, m_movementBlockedLogCooldown - elapsed);
        foreach (var weapon in m_weapons)
        {
            weapon.Cooldown = Math.Max(0.0f, weapon.Cooldown - elapsed);
        }

        UpdatePendingMissiles(elapsed);
        var playerTargetPosition = m_playerMech.TargetPosition;
        var playerOffset = playerTargetPosition - TargetPosition;
        var playerPlanarOffset = new Vector3(playerOffset.X, 0.0f, playerOffset.Z);
        var playerDistance = playerPlanarOffset.Length();
        if (IsPoweredDown)
        {
            if (m_sensorCooldown > 0.0f)
            {
                return;
            }

            m_sensorCooldown = SensorIntervalSeconds;
            if (!EnemyAwareness.CanWake(playerDistance, m_wakeRange))
            {
                return;
            }

            PowerUp();
            m_hasLineOfSight = EnemyAwareness.CanObserve(
                playerDistance,
                m_observationRange,
                HasLineOfSight(TargetPosition, playerTargetPosition));
            if (m_hasLineOfSight)
            {
                m_targetMemoryRemaining = TargetMemorySeconds;
                m_lastKnownTargetPosition = playerTargetPosition;
            }

            GD.Print(
                $"MechRewired: {Description} woke at {playerDistance:F0}m " +
                $"(GPS target {Definition.Specification.TargetRange}m; sleep " +
                $"{Definition.Specification.SleepRange}m; wake range {m_wakeRange:F0}m; " +
                $"atmospheric visibility {m_atmosphericVisibilityRange:F0}m; visual range " +
                $"{m_observationRange:F0}m; line of sight {m_hasLineOfSight}).");
            if (!m_hasLineOfSight)
            {
                return;
            }
        }
        else
        {
            if (m_sensorCooldown <= 0.0f)
            {
                m_sensorCooldown = SensorIntervalSeconds;
                m_hasLineOfSight = EnemyAwareness.CanObserve(
                    playerDistance,
                    m_observationRange,
                    HasLineOfSight(TargetPosition, playerTargetPosition));
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
        var desiredTorsoYaw = IsStationaryEmplacement
            ? relativeYaw
            : Mathf.Clamp(relativeYaw, -MaximumTorsoYawRadians, MaximumTorsoYawRadians);
        var desiredPitch = Mathf.Clamp(
            Mathf.Atan2(targetOffset.Y, Math.Max(distance, 0.01f)),
            -MaximumTorsoPitchRadians,
            MaximumTorsoPitchRadians);
        var turnStep = Mathf.DegToRad(TorsoTurnDegreesPerSecond) * elapsed;
        if (IsStationaryEmplacement && m_aimPitchPivot != null)
        {
            Torso.Rotation = new Vector3(
                0.0f,
                MoveTowardAngle(Torso.Rotation.Y, desiredTorsoYaw, turnStep),
                0.0f);
            m_aimPitchPivot.Rotation = new Vector3(
                Mathf.MoveToward(
                    m_aimPitchPivot.Rotation.X,
                    m_aimPitchRestRotation.X + desiredPitch,
                    turnStep),
                m_aimPitchRestRotation.Y,
                m_aimPitchRestRotation.Z);
        }
        else
        {
            Torso.Rotation = new Vector3(
                Mathf.MoveToward(Torso.Rotation.X, desiredPitch, turnStep),
                MoveTowardAngle(Torso.Rotation.Y, desiredTorsoYaw, turnStep),
                0.0f);
        }

        if (!IsStationaryEmplacement)
        {
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
        }

        var playerYaw = Mathf.Atan2(-playerPlanarOffset.X, -playerPlanarOffset.Z);
        var aimYaw = Mathf.Abs(Mathf.AngleDifference(Torso.GlobalRotation.Y, playerYaw));
        if (m_hasLineOfSight &&
            aimYaw <= Mathf.DegToRad(12.0f) &&
            m_fireDecisionCooldown <= 0.0f &&
            TryFireNextWeapon(playerDistance))
        {
            m_fireDecisionCooldown = FireDecisionIntervalSeconds;
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

        if (IsStationaryEmplacement &&
            m_damageModel.GetMaximum(section).InternalStructure == 0)
        {
            section = MechDamageSection.CenterTorso;
        }

        var result = m_damageModel.ApplyDamage(section, damage, fromRear);
        m_combatMovement.NotifyDamage();
        GD.Print(
            $"MechRewired: weapon hit {Description} ({Definition.Specification.ConfigurationName}) " +
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

            var wasPoweredDown = IsPoweredDown;
            if (wasPoweredDown)
            {
                PowerUp();
            }

            // A hit identifies the attacker's direction, but it does not grant a view through terrain.
            m_hasLineOfSight = false;
            m_targetMemoryRemaining = TargetMemorySeconds;
            m_sensorCooldown = 0.0f;
            m_lastKnownTargetPosition = m_playerMech.TargetPosition;
            GD.Print(
                $"MechRewired: {Description} {(wasPoweredDown ? "alerted" : "updated search")} by weapon impact; " +
                "turning toward the last known attacker position.");

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

    private bool TryFireNextWeapon(float playerDistance)
    {
        if (m_weapons.Count == 0)
        {
            return false;
        }

        for (var offset = 0; offset < m_weapons.Count; offset++)
        {
            var index = (m_nextWeapon + offset) % m_weapons.Count;
            var weapon = m_weapons[index];
            if (!IsWeaponOperational(weapon) ||
                !CanSustainHeat(weapon) ||
                playerDistance > weapon.Definition.Specification.RangeMeters)
            {
                continue;
            }

            m_nextWeapon = (index + 1) % m_weapons.Count;
            return FireWeapon(weapon);
        }

        return false;
    }

    private bool FireWeapon(EnemyWeapon weapon)
    {
        var start = weapon.Mount.GlobalPosition;
        var aimedEnd = m_playerMech.WorldBounds.GetCenter();
        var aimDirection = ApplyGunneryError(
            start.DirectionTo(aimedEnd),
            start.DistanceTo(aimedEnd),
            weapon.Definition.Specification.RangeMeters);
        var end = m_playerMech.TryRaycastSections(start, aimDirection, out var playerHit)
            ? playerHit.Position
            : start + aimDirection * (float)weapon.Definition.Specification.RangeMeters;
        if (!HasLineOfSight(start, end))
        {
            return false;
        }

        if (!TryConsumeAmmunition(weapon))
        {
            return false;
        }

        weapon.Cooldown = (float)weapon.Definition.Specification.RecycleSeconds;
        PlayWeaponSound(weapon.Definition.Specification.SoundResourceName, start);
        switch (weapon.Definition.Specification.Kind)
        {
            case MechWeaponKind.Laser:
            case MechWeaponKind.PulseLaser:
                FireLaserWeapon(weapon, start, end);
                break;

            case MechWeaponKind.Ballistic:
                FireBallisticWeapon(weapon, start, end);
                break;

            case MechWeaponKind.Missile:
                QueueMissileSalvo(weapon, start);
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }

        m_heat.Add(weapon.Definition.Specification.Heat);

        if (weapon.Definition.Specification.Kind != MechWeaponKind.Missile)
        {
            if (playerHit != null)
            {
                ApplyWeaponDamage(weapon.Definition, end, playerHit);
            }
        }

        GD.Print(
            $"MechRewired: {Description} fired {weapon.Definition.Specification.Name} " +
            $"instance {weapon.Definition.SourceId}" +
            $"; heat {m_heat.CurrentHeat:F1}/{m_heat.MaximumHeat:F0}" +
            (weapon.Ammo >= 0 ? $"; ammo {weapon.Ammo}." : "."));
        return true;
    }

    private void FireLaserWeapon(EnemyWeapon weapon, Vector3 start, Vector3 end)
    {
        var specification = weapon.Definition.Specification;
        var pulseCount = specification.Kind == MechWeaponKind.PulseLaser
            ? specification.ProjectilesPerShot
            : 1;
        var basis = Torso.GlobalBasis.Orthonormalized();
        var color = ColorFromRgb(specification.BeamColorRgb);
        for (var pulse = 0; pulse < pulseCount; pulse++)
        {
            var lateral = basis.X * ((pulse - (pulseCount - 1) * 0.5f) * 0.05f);
            GetParent().AddChild(new LaserEffect(start + lateral, end + lateral, color, 0.055f));
        }
    }

    private void FireBallisticWeapon(EnemyWeapon weapon, Vector3 start, Vector3 end)
    {
        var basis = Torso.GlobalBasis.Orthonormalized();
        for (var tracer = 0; tracer < 4; tracer++)
        {
            var spread = basis.X * ((float)Random.Shared.NextDouble() - 0.5f) * 0.08f +
                         basis.Y * ((float)Random.Shared.NextDouble() - 0.5f) * 0.08f;
            GetParent().AddChild(new BallisticTracerEffect(start + spread, end + spread, tracer * 0.045f));
        }
    }

    private void QueueMissileSalvo(EnemyWeapon weapon, Vector3 start)
    {
        var specification = weapon.Definition.Specification;
        for (var index = 0; index < specification.ProjectilesPerShot; index++)
        {
            var angle = Mathf.Tau * index / specification.ProjectilesPerShot;
            var basis = Torso.GlobalBasis.Orthonormalized();
            var offset = basis.X * Mathf.Cos(angle) * 0.4f + basis.Y * Mathf.Sin(angle) * 0.4f;
            var missileStart = start + offset;
            var direction = ApplyGunneryError(
                missileStart.DirectionTo(m_playerMech.TargetPosition),
                missileStart.DistanceTo(m_playerMech.TargetPosition),
                specification.RangeMeters);
            var willTrackPlayer = m_playerMech.TryRaycastSections(missileStart, direction, out _);
            m_pendingMissiles.Add(new PendingMissile(
                index * 0.035f,
                missileStart,
                direction,
                (float)specification.RangeMeters,
                weapon.Definition,
                willTrackPlayer));
        }
    }

    private void UpdatePendingMissiles(float elapsed)
    {
        for (var index = m_pendingMissiles.Count - 1; index >= 0; index--)
        {
            var pending = m_pendingMissiles[index];
            pending.Delay -= elapsed;
            if (pending.Delay > 0.0f)
            {
                continue;
            }

            var missile = AcquireMissile();
            missile.Launch(
                pending.Start,
                pending.Direction,
                pending.Range,
                () => pending.WillTrackPlayer && !m_playerMech.IsDestroyed
                    ? m_playerMech.TargetPosition
                    : null,
                impact => ApplyWeaponDamage(pending.Weapon, impact, null),
                terrainImpact: m_battlefieldEffects.SpawnWeaponImpact);
            m_pendingMissiles.RemoveAt(index);
        }
    }

    private MissileEffect AcquireMissile()
    {
        CreateMissilePool();

        return m_missilePool.FirstOrDefault(missile => !missile.IsActive) ??
               m_missilePool.MaxBy(missile => missile.Age);
    }

    private void CreateMissilePool()
    {
        if (m_missilePool.Count > 0)
        {
            return;
        }

        for (var index = 0; index < MissilePoolSize; index++)
        {
            var missile = new MissileEffect(index % 4 == 0)
            {
                Name = $"{Name}-Missile{index + 1}"
            };
            GetParent().AddChild(missile);
            m_missilePool.Add(missile);
        }
    }

    private void ApplyWeaponDamage(
        MechMountedWeapon weapon,
        Vector3 impact,
        MechSectionHit playerHit)
    {
        if (m_playerMech.IsDestroyed)
        {
            return;
        }

        m_battlefieldEffects.SpawnWeaponImpact(impact);
        m_playerMech.ApplyDamage(
            weapon.Specification.Damage,
            Description,
            playerHit?.Section ?? MechDamageSection.CenterTorso,
            playerHit?.FromRear ?? false,
            impact);
    }

    private void PlayWeaponSound(string resourceName, Vector3 position)
    {
        if (!m_weaponSounds.TryGetValue(resourceName, out var sound))
        {
            GD.PushWarning($"MechRewired: {Description} has no sound for SNDS/{resourceName}.");
            return;
        }

        m_weaponSound.Stream = sound;
        m_weaponSound.GlobalPosition = position;
        m_weaponSound.Play();
    }

    private void PowerUp()
    {
        if (!IsPoweredDown)
        {
            m_isAlerted = true;
            return;
        }

        m_isAlerted = true;
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
        if (m_mechRig.Advance(distance, headingChange, speedFraction, delta))
        {
            var basis = GlobalBasis.Orthonormalized();
            var side = m_footfallCount++ % 2 == 0 ? -1.0f : 1.0f;
            var footPosition = GlobalPosition + basis.X * (side * Math.Max(m_footprintRadius * 0.42f, 0.8f)) -
                               basis.Z * 0.25f;
            footPosition.Y = GlobalPosition.Y + m_modelBottomY;
            m_battlefieldEffects.SpawnFootfallDust(footPosition, speedFraction);
        }
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

    private bool IsWeaponOperational(EnemyWeapon weapon) =>
        !m_damageModel.IsSectionDestroyed(weapon.Definition.Section) &&
        weapon.Cooldown <= 0.0f &&
        (weapon.Ammo < 0 || weapon.Ammo >= weapon.Definition.Specification.ProjectilesPerShot);

    private bool CanSustainHeat(EnemyWeapon weapon) =>
        m_heat.CurrentHeat + weapon.Definition.Specification.Heat <=
        m_heat.MaximumHeat * MaximumSustainedHeatFraction;

    private Vector3 ApplyGunneryError(Vector3 idealDirection, float distance, double range)
    {
        if (idealDirection.IsZeroApprox())
        {
            return idealDirection;
        }

        var skill = Math.Max(Definition.Specification.GunnerySkill, 0);
        var rangeFraction = Mathf.Clamp(distance / Math.Max((float)range, 1.0f), 0.0f, 1.0f);
        var errorDegrees = BaseAimErrorDegrees +
                           skill * AimErrorPerGunnerySkillDegrees +
                           rangeFraction * MaximumRangeAimErrorDegrees;
        var radius = Mathf.Tan(Mathf.DegToRad(errorDegrees)) * Mathf.Sqrt((float)m_random.NextDouble());
        var angle = (float)m_random.NextDouble() * Mathf.Tau;
        var reference = MathF.Abs(idealDirection.Y) < 0.95f ? Vector3.Up : Vector3.Right;
        var right = idealDirection.Cross(reference).Normalized();
        var up = right.Cross(idealDirection).Normalized();
        return (idealDirection + right * Mathf.Cos(angle) * radius + up * Mathf.Sin(angle) * radius).Normalized();
    }

    private static Marker3D GetNextMount(
        MechDamageSection section,
        IReadOnlyDictionary<MechDamageSection, List<Marker3D>> mountsBySection,
        IDictionary<MechDamageSection, int> nextMountBySection)
    {
        if (!mountsBySection.TryGetValue(section, out var mounts) || mounts.Count == 0)
        {
            return null;
        }

        var nextIndex = nextMountBySection.TryGetValue(section, out var value) ? value : 0;
        nextMountBySection[section] = nextIndex + 1;
        return mounts[nextIndex % mounts.Count];
    }

    private static Color ColorFromRgb(uint rgb) => new(
        ((rgb >> 16) & 0xff) / 255.0f,
        ((rgb >> 8) & 0xff) / 255.0f,
        (rgb & 0xff) / 255.0f);

    private static bool TryConsumeAmmunition(EnemyWeapon weapon)
    {
        if (weapon.Ammo < 0)
        {
            return true;
        }

        var required = weapon.Definition.Specification.ProjectilesPerShot;
        if (weapon.Ammo < required)
        {
            return false;
        }

        weapon.Ammo -= required;
        return true;
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
                Definition.Specification.GroupId * 7919 + 104729,
                splitIndividualParts: IsStationaryEmplacement);
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

    private static float MoveTowardAngle(float current, float target, float maximumDelta) =>
        current + Mathf.Clamp(
            Mathf.AngleDifference(current, target),
            -maximumDelta,
            maximumDelta);

    private sealed class EnemyWeapon(MechMountedWeapon definition, Marker3D mount, int ammo)
    {
        public MechMountedWeapon Definition { get; } = definition;

        public Marker3D Mount { get; } = mount;

        /// <summary><c>-1</c> represents an energy weapon with no ammunition requirement.</summary>
        public int Ammo { get; set; } = ammo;

        public float Cooldown { get; set; }
    }

    private sealed class PendingMissile(
        float delay,
        Vector3 start,
        Vector3 direction,
        float range,
        MechMountedWeapon weapon,
        bool willTrackPlayer)
    {
        public float Delay { get; set; } = delay;

        public Vector3 Start { get; } = start;

        public Vector3 Direction { get; } = direction;

        public float Range { get; } = range;

        public MechMountedWeapon Weapon { get; } = weapon;

        public bool WillTrackPlayer { get; } = willTrackPlayer;
    }
}
