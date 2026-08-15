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
using MechRewired.Missions;
using MechRewired.Resources;
using MechRewired.Simulation;

namespace MechRewired;

/// <summary>
/// Selects original-data actors beneath the torso reticle and fires the first laser weapon slice.
/// </summary>
/// <remarks>
/// Actor and mission resource identities remain intact so combat events can satisfy data-driven objectives.
/// </remarks>
public partial class PlayerTargeting : Node
{
    private const float TargetingRange = 1000.0f;
    private const float ObjectiveHighlightRange = 300.0f;
    private const float MissileLockSeconds = 1.5f;
    private const float MissileLockConeDegrees = 8.0f;
    private const float MissileGuidanceArmingDistance = 55.0f;
    private const float HeatWarningFraction = 0.8f;
    private const int MissilePoolSize = 64;

    private readonly PlayerMech m_playerMech;
    private readonly PlayerMechSounds m_playerMechSounds;
    private readonly PlayerMission m_playerMission;
    private readonly IReadOnlyList<DebugTriangle> m_sceneTriangles;
    private readonly IReadOnlyList<BattlefieldActor> m_actors;
    private readonly IReadOnlyList<EnemyMech> m_enemyMechs;
    private readonly IReadOnlyDictionary<(string SourcePath, int ObjectId), BattlefieldActor> m_actorsByObject;
    private readonly AudioStreamPlayer m_weaponSound;
    private readonly AudioStreamPlayer m_missileLockSound;
    private readonly AudioStreamPlayer m_fireModeSound;
    private readonly AudioStreamPlayer m_weaponUnavailableSound;
    private readonly AudioStreamPlayer m_enemyPowerUpSound;
    private readonly AudioStreamPlayer m_enemyMechDestroyedSound;
    private readonly AudioStreamPlayer m_thermalReportSound;
    private readonly AudioStreamPlayer m_shutdownEffectSound;
    private readonly BattlefieldEffects m_battlefieldEffects;
    private readonly MechHeat m_heat;
    private readonly double[] m_weaponCooldowns;
    private readonly Dictionary<ushort, int> m_ammunitionByWeapon;
    private readonly List<MissileEffect> m_missilePool = [];
    private readonly List<PendingMissile> m_pendingMissiles = [];
    private readonly List<PendingWeaponRepeat> m_pendingWeaponRepeats = [];
    private readonly Random m_missileLaunchRandom = new();
    private bool m_advanceAfterWeaponRepeats;
    private bool m_repeatedFireWasForcedGroup;
    private float m_missileLockProgress;
    private EnemyMech m_lockCandidate;
    private EnemyMech m_lockedEnemy;
    private bool m_heatWarningReported;

    public PlayerTargeting(
        PlayerMech playerMech,
        PlayerMission playerMission,
        IReadOnlyList<DebugTriangle> sceneTriangles,
        IReadOnlyList<BattlefieldActor> actors,
        IReadOnlyList<EnemyMech> enemyMechs,
        MechWarriorMechFile playerDefinition,
        PlayerMechSounds sounds,
        BattlefieldEffects battlefieldEffects)
    {
        ArgumentNullException.ThrowIfNull(playerMech);
        ArgumentNullException.ThrowIfNull(playerMission);
        ArgumentNullException.ThrowIfNull(sceneTriangles);
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(enemyMechs);
        ArgumentNullException.ThrowIfNull(playerDefinition);
        ArgumentNullException.ThrowIfNull(sounds);
        ArgumentNullException.ThrowIfNull(battlefieldEffects);
        if (playerDefinition.Weapons.Count == 0)
        {
            throw new InvalidDataException("The player MEK contains no supported mounted weapons.");
        }

        Name = "PlayerTargeting";
        m_playerMech = playerMech;
        m_playerMechSounds = sounds;
        m_playerMission = playerMission;
        m_actors = actors;
        m_enemyMechs = enemyMechs;
        var actorsByObject = new Dictionary<(string SourcePath, int ObjectId), BattlefieldActor>();
        foreach (var actor in actors)
        {
            foreach (var component in actor.Definition.Components)
            {
                actorsByObject.Add((component.SourceEntry.Path, component.Id), actor);
            }

            actor.Destroyed += OnActorDestroyed;
        }

        m_actorsByObject = actorsByObject;
        foreach (var enemyMech in enemyMechs)
        {
            enemyMech.Destroyed += OnEnemyDestroyed;
            enemyMech.PoweredUp += OnEnemyPoweredUp;
        }

        m_sceneTriangles = sceneTriangles;
        m_battlefieldEffects = battlefieldEffects;
        WeaponSelection = new PlayerWeaponSelection(playerDefinition.Weapons);
        m_ammunitionByWeapon = CreateAmmunitionByWeapon(playerDefinition);
        m_heat = new MechHeat(
            maximumHeat: MechHeat.GetCriticalHeatThreshold(playerDefinition.HeatSinkCount),
            coolingPerSecond: playerDefinition.HeatSinkCount / 10.0);
        m_weaponCooldowns = new double[playerDefinition.Weapons.Count];
        m_weaponSound = new AudioStreamPlayer
        {
            Name = "WeaponSound",
            MaxPolyphony = 16,
            VolumeDb = -2.0f
        };
        AddChild(m_weaponSound);
        m_missileLockSound = new AudioStreamPlayer
        {
            Name = "MissileLock",
            Stream = sounds.MissileLock,
            VolumeDb = -1.0f
        };
        AddChild(m_missileLockSound);
        m_fireModeSound = new AudioStreamPlayer
        {
            Name = "WeaponFireMode",
            VolumeDb = -1.0f
        };
        AddChild(m_fireModeSound);
        m_weaponUnavailableSound = new AudioStreamPlayer
        {
            Name = "WeaponUnavailable",
            Stream = sounds.WeaponUnavailable,
            VolumeDb = -1.0f
        };
        AddChild(m_weaponUnavailableSound);
        if (sounds.EnemyPowerUpDetected != null)
        {
            m_enemyPowerUpSound = new AudioStreamPlayer
            {
                Name = "EnemyPowerUpWarning",
                Stream = sounds.EnemyPowerUpDetected,
                VolumeDb = -1.0f
            };
            AddChild(m_enemyPowerUpSound);
        }
        m_enemyMechDestroyedSound = new AudioStreamPlayer
        {
            Name = "EnemyMechDestroyedReport",
            Stream = sounds.EnemyMechDestroyed,
            VolumeDb = -1.0f
        };
        AddChild(m_enemyMechDestroyedSound);
        m_thermalReportSound = new AudioStreamPlayer
        {
            Name = "ThermalReport",
            VolumeDb = -1.0f
        };
        AddChild(m_thermalReportSound);
        m_shutdownEffectSound = new AudioStreamPlayer
        {
            Name = "ShutdownEffect",
            Stream = sounds.ShutdownEffect,
            VolumeDb = -2.0f
        };
        AddChild(m_shutdownEffectSound);
        WeaponFireSounds = sounds.WeaponFireSounds;
        ChainFireSound = sounds.ChainFire;
        GroupFireSound = sounds.GroupFire;
        for (var index = 0; index < MissilePoolSize; index++)
        {
            var missile = new MissileEffect(index % 4 == 0);
            AddChild(missile);
            m_missilePool.Add(missile);
        }

        playerMech.FireRequested += () => FireWeapons(false);
        playerMech.CycleWeaponRequested += CycleWeapon;
        playerMech.AssignWeaponGroupRequested += AssignWeaponGroup;
        playerMech.CycleWeaponGroupRequested += CycleWeaponGroup;
        playerMech.FireWeaponGroupRequested += () => FireWeapons(true);
        playerMech.TargetRequested += SelectUnderReticle;
        playerMech.NextTargetRequested += SelectNextEnemy;
        playerMech.PreviousTargetRequested += SelectPreviousEnemy;
        playerMech.NearestEnemyTargetRequested += SelectNearestEnemy;
        playerMech.ClearTargetRequested += ClearTarget;
        playerMech.InspectTargetRequested += InspectSelectedActor;
        playerMech.ShutdownRequested += ToggleShutdown;
        playerMech.ShutdownOverrideRequested += ToggleShutdownOverride;
    }

    private IReadOnlyDictionary<string, AudioStreamWav> WeaponFireSounds { get; }

    private AudioStreamWav ChainFireSound { get; }

    private AudioStreamWav GroupFireSound { get; }

    public PlayerWeaponSelection WeaponSelection { get; }

    public float MissileLockProgress => m_missileLockProgress / MissileLockSeconds;

    public bool MissileLocked => m_lockedEnemy != null;

    public bool IsMissileLocking => GetSelectedMissile() != null && SelectedEnemy != null;

    public bool IsWeaponReady(int index) => m_weaponCooldowns[index] <= 0.0;

    public bool IsWeaponOperational(int index) => IsWeaponOperational(WeaponSelection.Weapons[index]);

    public int GetWeaponAmmo(int index) => GetWeaponAmmo(WeaponSelection.Weapons[index]);

    public double Heat => m_heat.CurrentHeat;

    public double MaximumHeat => m_heat.MaximumHeat;

    public double HeatFraction => m_heat.Fraction;

    public double HeatRate => m_heat.HeatRate;

    public bool IsShutdown => m_playerMech.IsShutdown;

    public bool IsShutdownOverride => m_playerMech.IsShutdownOverride;

    public BattlefieldActor SelectedActor { get; private set; }

    public EnemyMech SelectedEnemy { get; private set; }

    public IReadOnlyList<BattlefieldActor> Actors => m_actors;

    public IReadOnlyList<EnemyMech> EnemyMechs => m_enemyMechs;

    public BattlefieldActor ObjectiveActor { get; private set; }

    public Vector3 ObjectiveAimPosition { get; private set; }

    public override void _Ready()
    {
        var loadout = string.Join(", ", WeaponSelection.Weapons
            .GroupBy(weapon => weapon.Specification.Name)
            .Select(group => $"{group.Count()}x {group.Key}"));
        var mounts = string.Join(", ", WeaponSelection.Weapons.Select(weapon =>
            $"{weapon.SourceId}:{weapon.Specification.HudName}@{weapon.Section}"));
        var ammunition = string.Join(", ", WeaponSelection.Weapons
            .Where(weapon => weapon.Specification.UsesAmmo)
            .Select(weapon => $"{weapon.SourceId}:{weapon.Specification.HudName} {GetWeaponAmmo(weapon)}"));
        GD.Print(
            $"MechRewired: targeting online ({m_actors.Count} battlefield actors, " +
            $"{m_enemyMechs.Count} hostile mechs; " +
            $"authored player loadout {loadout}; mounts [{mounts}]; " +
            (string.IsNullOrWhiteSpace(ammunition) ? string.Empty : $"ammo [{ammunition}]; ") +
            $"cooling {m_heat.CoolingPerSecond:F1} heat/s; " +
            $"critical heat {m_heat.MaximumHeat:F0}; {MissilePoolSize} pooled missiles).");
    }

    public override void _Process(double delta)
    {
        m_heat.Advance(delta);
        EvaluateHeatState();
        for (var index = 0; index < m_weaponCooldowns.Length; index++)
        {
            m_weaponCooldowns[index] = Math.Max(0.0, m_weaponCooldowns[index] - delta);
        }

        UpdateMissileLock((float)delta);
        UpdatePendingMissiles((float)delta);
        UpdatePendingWeaponRepeats((float)delta);
        UpdateObjectiveActor();
    }

    public void SelectUnderReticle()
    {
        if (!TryRaycast(TargetingRange, out var actor, out var enemyMech, out _, out _, out _))
        {
            SelectedActor = null;
            SelectedEnemy = null;
            GD.Print("MechRewired: targeting reticle found no targetable actor.");
            return;
        }

        if (enemyMech != null)
        {
            SelectEnemy(enemyMech);
            return;
        }

        if (actor == null || !IsSelectable(actor))
        {
            SelectedActor = null;
            SelectedEnemy = null;
            GD.Print("MechRewired: targeting reticle found no named targetable actor.");
            return;
        }

        SelectedActor = actor;
        SelectedEnemy = null;
        GD.Print(
            $"MechRewired: targeted {actor.Description} in BWD/{actor.SourceResourceName}.BWD " +
            $"({actor.Health}/{actor.MaximumHealth} health).");
    }

    public void SelectNextEnemy() => CycleEnemy(1);

    public void SelectPreviousEnemy() => CycleEnemy(-1);

    public void SelectNearestEnemy()
    {
        var enemyMech = m_enemyMechs
            .Where(IsTargetableEnemy)
            .OrderBy(candidate => candidate.TargetPosition.DistanceSquaredTo(m_playerMech.GlobalPosition))
            .FirstOrDefault();
        if (enemyMech == null)
        {
            ClearTarget();
            GD.Print($"MechRewired: no powered hostile mechs are within {TargetingRange:F0}m targeting range.");
            return;
        }

        SelectEnemy(enemyMech);
    }

    public void ClearTarget()
    {
        SelectedActor = null;
        SelectedEnemy = null;
        GD.Print("MechRewired: targeting reset.");
    }

    public void InspectSelectedActor()
    {
        var actor = SelectedActor;
        if (actor == null &&
            ObjectiveActor != null &&
            m_playerMission.GetActiveObjectiveKind(ObjectiveActor) == MissionObjectiveKind.Inspect)
        {
            actor = ObjectiveActor;
        }

        if (actor == null || !IsSelectable(actor))
        {
            GD.Print("MechRewired: no inspectable target selected.");
            return;
        }

        GD.Print(
            $"MechRewired: inspected {actor.Description} in " +
            $"BWD/{actor.SourceResourceName}.BWD.");
        m_playerMission.Apply(new MissionEvent(
            MissionEventKind.TargetInspected,
            actor.SourceResourceName));
    }

    public void LogSelectedEnemyState() => SelectedEnemy?.LogCombatState();

    private void EvaluateHeatState()
    {
        if (m_heat.CurrentHeat < m_heat.MaximumHeat * HeatWarningFraction)
        {
            m_heatWarningReported = false;
            return;
        }

        if (!m_heatWarningReported)
        {
            m_heatWarningReported = true;
            PlayThermalReport(m_playerMechSounds.HeatCritical);
            GD.Print(
                $"MechRewired: heat level critical ({m_heat.CurrentHeat:F1}/{m_heat.MaximumHeat:F0}).");
        }

        if (m_playerMech.IsShutdown)
        {
            return;
        }

        if (!m_playerMech.IsShutdownOverride && m_heat.CurrentHeat >= m_heat.MaximumHeat)
        {
            InitiateThermalShutdown("critical heat threshold exceeded");
        }
        else if (m_playerMech.IsShutdownOverride &&
                 m_heat.CurrentHeat >= m_heat.MaximumOverrideHeat)
        {
            m_playerMech.SetShutdownOverride(false);
            InitiateThermalShutdown("override heat limit exceeded");
        }
    }

    private void ToggleShutdown()
    {
        if (m_playerMech.IsShutdown)
        {
            if (m_heat.CurrentHeat >= m_heat.MaximumHeat)
            {
                GD.Print(
                    $"MechRewired: restart denied; heat remains critical " +
                    $"({m_heat.CurrentHeat:F1}/{m_heat.MaximumHeat:F0}).");
                return;
            }

            m_playerMech.SetShutdownState(false, "manual restart");
            m_heatWarningReported = m_heat.CurrentHeat >= m_heat.MaximumHeat * HeatWarningFraction;
            return;
        }

        CancelPendingWeaponRepeats();
        PlayThermalReport(m_playerMechSounds.ShuttingDown);
        m_shutdownEffectSound.Play();
        m_playerMech.SetShutdownState(true, "manual shutdown");
    }

    private void ToggleShutdownOverride()
    {
        if (m_playerMech.IsShutdown)
        {
            GD.Print("MechRewired: shutdown override ignored while the reactor is offline.");
            return;
        }

        var enabled = !m_playerMech.IsShutdownOverride;
        m_playerMech.SetShutdownOverride(enabled);
        if (enabled)
        {
            PlayThermalReport(m_playerMechSounds.ShutdownOverride);
        }
        else
        {
            EvaluateHeatState();
        }
    }

    private void InitiateThermalShutdown(string reason)
    {
        CancelPendingWeaponRepeats();
        PlayThermalReport(m_playerMechSounds.ThermalShutdown);
        m_shutdownEffectSound.Play();
        m_playerMech.SetShutdownState(true, reason);
    }

    private void CancelPendingWeaponRepeats()
    {
        m_pendingWeaponRepeats.Clear();
        m_advanceAfterWeaponRepeats = false;
    }

    private void PlayThermalReport(AudioStreamWav report)
    {
        m_thermalReportSound.Stream = report;
        m_thermalReportSound.Play();
    }

    private void FireWeapons(bool forceGroup)
    {
        if (m_playerMech.IsShutdown)
        {
            m_weaponUnavailableSound.Play();
            GD.Print("MechRewired: weapon fire ignored; PlayerMech reactor is shut down.");
            return;
        }

        if (m_pendingWeaponRepeats.Count > 0)
        {
            return;
        }

        var indices = WeaponSelection.GetFireIndices(forceGroup);
        var fired = false;
        var fireOrdinal = 0;
        foreach (var index in indices)
        {
            if (m_weaponCooldowns[index] > 0.0 || !IsWeaponOperational(WeaponSelection.Weapons[index]))
            {
                continue;
            }

            if (!FireWeapon(index, indices.Count > 1 ? fireOrdinal * 0.07f : 0.0f))
            {
                continue;
            }
            if (m_playerMech.IsShutdown)
            {
                fired = true;
                break;
            }

            if (WeaponSelection.Weapons[index].Specification.Kind == MechWeaponKind.Ballistic)
            {
                m_pendingWeaponRepeats.Add(new PendingWeaponRepeat(
                    index,
                    4,
                    (float)WeaponSelection.Weapons[index].Specification.RecycleSeconds));
            }

            fireOrdinal++;
            fired = true;
        }

        if (!fired)
        {
            m_weaponUnavailableSound.Play();
            GD.Print(
                "MechRewired: selected weapon or group is unavailable " +
                "(recycling, destroyed section, or insufficient ammunition).");
            return;
        }

        if (m_pendingWeaponRepeats.Count > 0)
        {
            m_advanceAfterWeaponRepeats = true;
            m_repeatedFireWasForcedGroup = forceGroup;
            return;
        }

        AdvanceAfterFire(forceGroup);
    }

    private void AdvanceAfterFire(bool forcedGroup)
    {
        WeaponSelection.AdvanceAfterFire(forcedGroup, IsSelectableWeapon);
        if (!SelectedFireSetContainsMissile())
        {
            ResetMissileLock();
        }
    }

    private void UpdatePendingWeaponRepeats(float delta)
    {
        if (m_playerMech.IsShutdown)
        {
            m_pendingWeaponRepeats.Clear();
            m_advanceAfterWeaponRepeats = false;
            return;
        }

        for (var index = m_pendingWeaponRepeats.Count - 1; index >= 0; index--)
        {
            var repeat = m_pendingWeaponRepeats[index];
            repeat.Delay -= delta;
            if (repeat.Delay > 0.0f)
            {
                continue;
            }

            if (!FireWeapon(repeat.WeaponIndex))
            {
                m_pendingWeaponRepeats.RemoveAt(index);
                continue;
            }
            repeat.Remaining--;
            if (repeat.Remaining <= 0)
            {
                m_pendingWeaponRepeats.RemoveAt(index);
            }
            else
            {
                repeat.Delay += (float)WeaponSelection.Weapons[repeat.WeaponIndex].Specification.RecycleSeconds;
            }
        }

        if (m_pendingWeaponRepeats.Count == 0 && m_advanceAfterWeaponRepeats)
        {
            m_advanceAfterWeaponRepeats = false;
            AdvanceAfterFire(m_repeatedFireWasForcedGroup);
        }
    }

    private bool FireWeapon(int index, float visualDelay = 0.0f)
    {
        var weapon = WeaponSelection.Weapons[index];
        if (!IsWeaponOperational(weapon) || !TryConsumeAmmunition(weapon))
        {
            return false;
        }

        m_weaponCooldowns[index] = weapon.Specification.RecycleSeconds;
        m_heat.Add(weapon.Specification.Heat, m_playerMech.IsShutdownOverride);
        EvaluateHeatState();
        switch (weapon.Specification.Kind)
        {
            case MechWeaponKind.Laser:
            case MechWeaponKind.PulseLaser:
            case MechWeaponKind.Ballistic:
                FireDirectWeapon(weapon, visualDelay);
                break;

            case MechWeaponKind.Missile:
                QueueMissileSalvo(weapon);
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }

        PlayWeaponSound(weapon.Specification.SoundResourceName);
        GD.Print(
            $"MechRewired: fired {weapon.Specification.Name} instance {weapon.SourceId} " +
            $"from {weapon.Section} (slot {index + 1}/{WeaponSelection.Weapons.Count}); " +
            $"heat {m_heat.CurrentHeat:F1}/{m_heat.MaximumHeat:F0}" +
            (weapon.Specification.UsesAmmo ? $"; ammo {GetWeaponAmmo(weapon)}." : "."));
        return true;
    }

    private void FireDirectWeapon(MechMountedWeapon weapon, float visualDelay)
    {
        var aimOrigin = m_playerMech.CockpitCamera.GlobalPosition;
        var direction = -m_playerMech.Torso.GlobalBasis.Z.Normalized();
        var start = GetWeaponStart(weapon, 0);
        var end = aimOrigin + direction * (float)weapon.Specification.RangeMeters;
        if (TryRaycast(
                (float)weapon.Specification.RangeMeters,
                out var actor,
                out var enemyMech,
                out var enemyHit,
                out _,
                out var hitPosition))
        {
            end = hitPosition;
            ApplyDirectDamage(weapon.Specification.Damage, actor, enemyMech, enemyHit, hitPosition);
        }
        else
        {
            GD.Print($"MechRewired: {weapon.Specification.Name} fired; no target hit.");
        }

        var color = ColorFromRgb(weapon.Specification.BeamColorRgb);
        if (weapon.Specification.Kind == MechWeaponKind.Ballistic)
        {
            const int tracerCount = 4;
            var basis = m_playerMech.Torso.GlobalBasis.Orthonormalized();
            for (var tracer = 0; tracer < tracerCount; tracer++)
            {
                var spread = basis.X * ((m_missileLaunchRandom.NextSingle() - 0.5f) * 0.08f) +
                             basis.Y * ((m_missileLaunchRandom.NextSingle() - 0.5f) * 0.08f);
                GetParent().AddChild(new BallisticTracerEffect(
                    start + spread,
                    end + spread,
                    tracer * 0.045f));
            }

            return;
        }

        var pulseCount = weapon.Specification.Kind == MechWeaponKind.PulseLaser
            ? weapon.Specification.ProjectilesPerShot
            : 1;
        for (var pulse = 0; pulse < pulseCount; pulse++)
        {
            var lateral = m_playerMech.Torso.GlobalBasis.X.Normalized() * ((pulse - (pulseCount - 1) * 0.5f) * 0.05f);
            GetParent().AddChild(new LaserEffect(
                start + lateral,
                end + lateral,
                color,
                0.055f,
                visualDelay));
        }
    }

    private void ApplyDirectDamage(
        int damage,
        BattlefieldActor actor,
        EnemyMech enemyMech,
        MechSectionHit enemyHit,
        Vector3 hitPosition)
    {
        if (enemyMech != null)
        {
            m_battlefieldEffects.SpawnWeaponImpact(hitPosition);
            enemyMech.ApplyDamage(damage, hitPosition, enemyHit.Section, enemyHit.FromRear);
            SelectedEnemy = enemyMech.IsDestroyed ? null : enemyMech;
            SelectedActor = null;
        }
        else if (actor?.IsDamageable == true)
        {
            m_battlefieldEffects.SpawnWeaponImpact(hitPosition);
            actor.ApplyDamage(damage, hitPosition, m_sceneTriangles);
            SelectedActor = actor.IsDestroyed || !IsSelectable(actor) ? null : actor;
            SelectedEnemy = null;
        }
        else if (actor != null)
        {
            GD.Print(
                $"MechRewired: weapon struck indestructible {actor.Description}; " +
                "target or inspect it instead.");
        }
        else
        {
            GD.Print("MechRewired: weapon struck non-targetable battlefield geometry.");
        }
    }

    private void QueueMissileSalvo(MechMountedWeapon weapon)
    {
        var forward = -m_playerMech.Torso.GlobalBasis.Z.Normalized();
        var lockedTarget = MissileLocked && ReferenceEquals(SelectedEnemy, m_lockedEnemy)
            ? m_lockedEnemy
            : null;
        Vector3? fixedAimPosition = null;
        BattlefieldActor aimedActor = null;
        EnemyMech aimedEnemy = null;
        MechSectionHit aimedEnemyHit = null;
        if (lockedTarget == null &&
            TryRaycast(
                (float)weapon.Specification.RangeMeters,
                out aimedActor,
                out aimedEnemy,
                out aimedEnemyHit,
                out _,
                out var aimPosition))
        {
            fixedAimPosition = aimPosition;
        }

        for (var missile = 0; missile < weapon.Specification.ProjectilesPerShot; missile++)
        {
            var start = GetWeaponStart(weapon, missile, weapon.Specification.ProjectilesPerShot);
            Func<Vector3?> targetPosition = null;
            Action<Vector3> impact = null;
            if (lockedTarget != null)
            {
                targetPosition = () => lockedTarget.IsDestroyed ? null : lockedTarget.TargetPosition;
                impact = position => ApplyMissileDamage(lockedTarget, weapon.Specification.Damage, position);
            }
            else if (fixedAimPosition.HasValue)
            {
                var aim = fixedAimPosition.Value;
                targetPosition = () => aim;
                impact = position => ApplyFixedAimMissileDamage(
                    aimedActor,
                    aimedEnemy,
                    aimedEnemyHit,
                    weapon.Specification.Damage,
                    position);
            }

            m_pendingMissiles.Add(new PendingMissile(
                missile * 0.035f + m_missileLaunchRandom.NextSingle() * 0.015f,
                start,
                lockedTarget != null
                    ? forward
                    : fixedAimPosition.HasValue
                        ? start.DirectionTo(fixedAimPosition.Value)
                        : forward,
                (float)weapon.Specification.RangeMeters,
                targetPosition,
                impact,
                lockedTarget != null ? MissileGuidanceArmingDistance : 0.0f));
        }
    }

    private Vector3 GetWeaponStart(MechMountedWeapon weapon, int projectileIndex = 0, int projectileCount = 1)
    {
        var side = weapon.Section switch
        {
            MechDamageSection.LeftArm or MechDamageSection.LeftTorso => -1,
            MechDamageSection.RightArm or MechDamageSection.RightTorso => 1,
            _ => 0
        };
        var basis = m_playerMech.Torso.GlobalBasis.Orthonormalized();
        var rackOffset = Vector3.Zero;
        if (weapon.Specification.Kind == MechWeaponKind.Missile && projectileCount > 1)
        {
            const int missilesPerRing = 10;
            var ring = projectileIndex / missilesPerRing;
            var positionOnRing = projectileIndex % missilesPerRing;
            var angleJitter = (m_missileLaunchRandom.NextSingle() - 0.5f) * 0.35f;
            var angle = Mathf.Tau * positionOnRing / Math.Min(projectileCount, missilesPerRing) + angleJitter;
            var radius = 0.45f + ring * 0.38f + m_missileLaunchRandom.NextSingle() * 0.15f;
            rackOffset = basis.X * (Mathf.Cos(angle) * radius) +
                         basis.Y * (Mathf.Sin(angle) * radius);
        }

        if (m_playerMech.TryGetWeaponOrigin(weapon, out var authoredOrigin))
        {
            return authoredOrigin + rackOffset;
        }

        return m_playerMech.CockpitMount.GlobalPosition +
               basis.X * (side * 2.65f) +
               basis.Y * 0.42f -
               basis.Z * 1.2f +
               rackOffset;
    }

    private bool IsWeaponOperational(MechMountedWeapon weapon) =>
        !m_playerMech.Damage.IsSectionDestroyed(weapon.Section) &&
        !m_playerMech.Damage.IsSectionDestroyed(GetExternalWeaponSection(weapon)) &&
        (!weapon.Specification.UsesAmmo ||
         GetWeaponAmmo(weapon) >= weapon.Specification.ProjectilesPerShot);

    private int GetWeaponAmmo(MechMountedWeapon weapon) => weapon.Specification.UsesAmmo
        ? m_ammunitionByWeapon.GetValueOrDefault(weapon.SourceId)
        : -1;

    private bool TryConsumeAmmunition(MechMountedWeapon weapon)
    {
        if (!weapon.Specification.UsesAmmo)
        {
            return true;
        }

        var required = weapon.Specification.ProjectilesPerShot;
        var available = GetWeaponAmmo(weapon);
        if (available < required)
        {
            m_weaponUnavailableSound.Play();
            GD.Print(
                $"MechRewired: {weapon.Specification.HudName} instance {weapon.SourceId} is out of ammunition " +
                $"({available}/{required} required)." );
            return false;
        }

        m_ammunitionByWeapon[weapon.SourceId] = available - required;
        return true;
    }

    private static Dictionary<ushort, int> CreateAmmunitionByWeapon(MechWarriorMechFile definition)
    {
        var ammunitionByWeapon = new Dictionary<ushort, int>();
        foreach (var weapon in definition.Weapons.Where(weapon => weapon.Specification.UsesAmmo))
        {
            var binCount = definition.AmmoBins.Count(bin => bin.AssociatedWeaponId == weapon.SourceId);
            ammunitionByWeapon.Add(weapon.SourceId, binCount * weapon.Specification.AmmoPerBin);
        }

        return ammunitionByWeapon;
    }

    private static MechDamageSection GetExternalWeaponSection(MechMountedWeapon weapon) =>
        weapon.Specification.Kind == MechWeaponKind.Missile
            ? weapon.Section switch
            {
                MechDamageSection.LeftTorso => MechDamageSection.LeftArm,
                MechDamageSection.RightTorso => MechDamageSection.RightArm,
                _ => weapon.Section
            }
            : weapon.Section;

    private void PlayWeaponSound(string resourceName)
    {
        if (!WeaponFireSounds.TryGetValue(resourceName, out var stream))
        {
            GD.PushWarning($"MechRewired: no decoded weapon sound for SNDS/{resourceName}.");
            return;
        }

        m_weaponSound.Stream = stream;
        m_weaponSound.Play();
    }

    private static Color ColorFromRgb(uint rgb) => new(
        ((rgb >> 16) & 0xff) / 255.0f,
        ((rgb >> 8) & 0xff) / 255.0f,
        (rgb & 0xff) / 255.0f);

    private void CycleWeapon()
    {
        WeaponSelection.CycleWeapon(1, IsSelectableWeapon);
        if (!SelectedFireSetContainsMissile())
        {
            ResetMissileLock();
        }

        LogWeaponSelection();
    }

    private void AssignWeaponGroup(int group)
    {
        WeaponSelection.AssignSelectedToGroup(group);
        GD.Print(
            $"MechRewired: assigned {WeaponSelection.SelectedWeapon.Specification.Name} " +
            $"to weapon group {group + 1}.");
    }

    private void CycleWeaponGroup()
    {
        WeaponSelection.CycleGroup(1, IsSelectableWeapon);
        ResetMissileLock();
        GD.Print($"MechRewired: selected weapon group {WeaponSelection.SelectedGroup + 1}.");
    }

    private void LogWeaponSelection()
    {
        var weapon = WeaponSelection.SelectedWeapon;
        GD.Print(
            $"MechRewired: selected weapon {WeaponSelection.SelectedWeaponIndex + 1}/" +
            $"{WeaponSelection.Weapons.Count}: {weapon.Specification.Name} " +
            $"({weapon.Specification.Damage} damage, {weapon.Specification.RangeMeters:F0}m, " +
            $"group {WeaponSelection.GetGroup(WeaponSelection.SelectedWeaponIndex) + 1}).");
    }

    private bool IsSelectableWeapon(int index) => IsWeaponOperational(index);

    private void UpdateMissileLock(float delta)
    {
        var missile = GetSelectedMissile();
        var candidate = missile != null && IsMissileLockCandidate(SelectedEnemy, missile)
            ? SelectedEnemy
            : null;
        if (!ReferenceEquals(candidate, m_lockCandidate))
        {
            m_lockCandidate = candidate;
            m_lockedEnemy = null;
            m_missileLockProgress = 0.0f;
        }

        if (candidate == null)
        {
            m_missileLockProgress = Math.Max(0.0f, m_missileLockProgress - delta * 2.0f);
            return;
        }

        if (ReferenceEquals(m_lockedEnemy, candidate))
        {
            return;
        }

        m_missileLockProgress = Math.Min(MissileLockSeconds, m_missileLockProgress + delta);
        if (m_missileLockProgress >= MissileLockSeconds)
        {
            m_lockedEnemy = candidate;
            m_missileLockSound.Play();
            GD.Print($"MechRewired: missile lock acquired on {candidate.Description}.");
        }
    }

    private MechMountedWeapon GetSelectedMissile() => WeaponSelection
        .GetFireIndices()
        .Select(index => WeaponSelection.Weapons[index])
        .FirstOrDefault(weapon =>
            weapon.Specification.Kind == MechWeaponKind.Missile && IsWeaponOperational(weapon));

    private bool SelectedFireSetContainsMissile() => GetSelectedMissile() != null;

    private bool IsMissileLockCandidate(EnemyMech enemy, MechMountedWeapon missile)
    {
        if (enemy == null || enemy.IsDestroyed || enemy.IsPoweredDown)
        {
            return false;
        }

        var origin = m_playerMech.CockpitCamera.GlobalPosition;
        var toTarget = enemy.TargetPosition - origin;
        if (toTarget.LengthSquared() > missile.Specification.RangeMeters * missile.Specification.RangeMeters)
        {
            return false;
        }

        var bounds = enemy.WorldBounds;
        var center = bounds.GetCenter();
        var top = bounds.Position.Y + bounds.Size.Y;
        Vector3[] aimPoints =
        [
            center,
            new Vector3(center.X, Mathf.Lerp(center.Y, top, 0.65f), center.Z),
            new Vector3(center.X, Mathf.Lerp(center.Y, top, 0.9f), center.Z)
        ];
        var forward = -m_playerMech.Torso.GlobalBasis.Z.Normalized();
        return aimPoints.Any(point =>
        {
            var direction = origin.DirectionTo(point);
            var angle = Mathf.RadToDeg(forward.AngleTo(direction));
            return angle <= MissileLockConeDegrees && HasLineOfSight(origin, point);
        });
    }

    private bool HasLineOfSight(Vector3 origin, Vector3 target)
    {
        var distance = origin.DistanceTo(target);
        if (distance <= 0.001f)
        {
            return true;
        }

        var candidates = m_sceneTriangles.Where(triangle =>
            !m_actorsByObject.TryGetValue(
                (triangle.SourceResourcePath, triangle.ObjectId),
                out var actor) ||
            !actor.IsDestroyed);
        return !DebugTriangleRaycaster.TryFindNearest(
                   candidates,
                   origin,
                   origin.DirectionTo(target),
                   out _,
                   out var obstructionDistance) ||
               obstructionDistance >= distance - 2.0f;
    }

    private void ResetMissileLock()
    {
        m_lockCandidate = null;
        m_lockedEnemy = null;
        m_missileLockProgress = 0.0f;
    }

    private void UpdatePendingMissiles(float delta)
    {
        for (var index = m_pendingMissiles.Count - 1; index >= 0; index--)
        {
            var pending = m_pendingMissiles[index];
            pending.Delay -= delta;
            if (pending.Delay > 0.0f)
            {
                continue;
            }

            var missile = m_missilePool.FirstOrDefault(candidate => !candidate.IsActive) ??
                          m_missilePool.MaxBy(candidate => candidate.Age);
            missile.Launch(
                pending.Start,
                pending.Direction,
                pending.Range,
                pending.TargetPosition,
                pending.Impact,
                pending.GuidanceArmingDistance,
                m_battlefieldEffects.SpawnWeaponImpact);
            m_pendingMissiles.RemoveAt(index);
        }
    }

    private void ApplyMissileDamage(EnemyMech target, int damage, Vector3 impact)
    {
        if (target.IsDestroyed)
        {
            return;
        }

        m_battlefieldEffects.SpawnWeaponImpact(impact);
        target.ApplyDamage(damage, impact, MechDamageSection.CenterTorso, false);
        if (target.IsDestroyed && ReferenceEquals(SelectedEnemy, target))
        {
            SelectedEnemy = null;
        }
    }

    private void ApplyFixedAimMissileDamage(
        BattlefieldActor actor,
        EnemyMech enemy,
        MechSectionHit enemyHit,
        int damage,
        Vector3 impact)
    {
        m_battlefieldEffects.SpawnWeaponImpact(impact);
        if (enemy != null && !enemy.IsDestroyed)
        {
            enemy.ApplyDamage(damage, impact, enemyHit.Section, enemyHit.FromRear);
            if (enemy.IsDestroyed && ReferenceEquals(SelectedEnemy, enemy))
            {
                SelectedEnemy = null;
            }

            return;
        }

        if (actor?.IsDamageable == true)
        {
            actor.ApplyDamage(damage, impact, m_sceneTriangles);
            if (actor.IsDestroyed && ReferenceEquals(SelectedActor, actor))
            {
                SelectedActor = null;
            }
        }
    }

    private bool TryRaycast(
        float maximumRange,
        out BattlefieldActor actor,
        out EnemyMech enemyMech,
        out MechSectionHit enemyHit,
        out float distance,
        out Vector3 hitPosition)
    {
        var origin = m_playerMech.CockpitCamera.GlobalPosition;
        var direction = -m_playerMech.Torso.GlobalBasis.Z.Normalized();
        var candidates = m_sceneTriangles.Where(triangle =>
            !m_actorsByObject.TryGetValue(
                (triangle.SourceResourcePath, triangle.ObjectId),
                out var candidate) ||
            !candidate.IsDestroyed);
        var hitStatic = DebugTriangleRaycaster.TryFindNearest(
                candidates,
                origin,
                direction,
                out var triangle,
                out var staticDistance) &&
            staticDistance <= maximumRange;
        enemyMech = null;
        enemyHit = null;
        var enemyDistance = float.PositiveInfinity;
        foreach (var candidate in m_enemyMechs.Where(candidate => !candidate.IsDestroyed))
        {
            if (candidate.TryRaycastSections(origin, direction, out var candidateHit) &&
                candidateHit.Distance <= maximumRange &&
                candidateHit.Distance < enemyDistance)
            {
                enemyMech = candidate;
                enemyHit = candidateHit;
                enemyDistance = candidateHit.Distance;
            }
        }

        if (enemyMech != null && (!hitStatic || enemyDistance < staticDistance))
        {
            actor = null;
            distance = enemyDistance;
            hitPosition = origin + direction * distance;
            return true;
        }

        if (!hitStatic)
        {
            actor = null;
            enemyMech = null;
            enemyHit = null;
            distance = float.PositiveInfinity;
            hitPosition = default;
            return false;
        }

        distance = staticDistance;
        m_actorsByObject.TryGetValue(
            (triangle.SourceResourcePath, triangle.ObjectId),
            out actor);
        hitPosition = origin + direction * distance;
        return true;
    }

    private void OnActorDestroyed(BattlefieldActor actor, Vector3 hitPosition)
    {
        if (ReferenceEquals(ObjectiveActor, actor))
        {
            ObjectiveActor = null;
            ObjectiveAimPosition = default;
            UpdateObjectiveActor();
        }

        var remainingActors = m_actors.Any(candidate =>
            candidate.IsDamageable &&
            !candidate.IsDestroyed &&
            string.Equals(
                candidate.SourceResourceName,
                actor.SourceResourceName,
                StringComparison.OrdinalIgnoreCase));
        if (!remainingActors)
        {
            m_playerMission.Apply(new MissionEvent(
                MissionEventKind.TargetDestroyed,
                actor.SourceResourceName));
        }
    }

    private void OnEnemyDestroyed(EnemyMech enemyMech)
    {
        m_enemyMechDestroyedSound.Play();
        if (ReferenceEquals(SelectedEnemy, enemyMech))
        {
            SelectedEnemy = null;
        }
    }

    private void OnEnemyPoweredUp(EnemyMech enemyMech)
    {
        m_enemyPowerUpSound?.Play();
        GD.Print($"MechRewired: enemy power up detected: {enemyMech.Description}.");
    }

    private bool IsSelectable(BattlefieldActor actor) =>
        !actor.IsDestroyed &&
        actor.HasDisplayName &&
        (actor.IsDamageable || m_playerMission.IsActiveObjectiveTarget(actor));

    private void CycleEnemy(int direction)
    {
        var liveEnemies = m_enemyMechs.Where(IsTargetableEnemy).ToArray();
        if (liveEnemies.Length == 0)
        {
            ClearTarget();
            GD.Print($"MechRewired: no powered hostile mechs are within {TargetingRange:F0}m targeting range.");
            return;
        }

        var selectedIndex = Array.IndexOf(liveEnemies, SelectedEnemy);
        var nextIndex = selectedIndex < 0
            ? direction > 0 ? 0 : liveEnemies.Length - 1
            : (selectedIndex + direction + liveEnemies.Length) % liveEnemies.Length;
        SelectEnemy(liveEnemies[nextIndex]);
    }

    private void SelectEnemy(EnemyMech enemyMech)
    {
        SelectedActor = null;
        SelectedEnemy = enemyMech;
        GD.Print(
            $"MechRewired: targeted hostile {enemyMech.Description} " +
            $"({enemyMech.Health}/{enemyMech.MaximumHealth} whole-mech health).");
    }

    private bool IsTargetableEnemy(EnemyMech enemyMech) =>
        !enemyMech.IsDestroyed &&
        !enemyMech.IsPoweredDown &&
        enemyMech.TargetPosition.DistanceSquaredTo(m_playerMech.GlobalPosition) <=
        TargetingRange * TargetingRange;

    private void UpdateObjectiveActor()
    {
        if (ObjectiveActor != null &&
            m_playerMission.IsActiveObjectiveTarget(ObjectiveActor) &&
            ObjectiveActor.TargetPosition.DistanceTo(m_playerMech.GlobalPosition) <= ObjectiveHighlightRange)
        {
            return;
        }

        ObjectiveActor = m_actors
            .Where(m_playerMission.IsActiveObjectiveTarget)
            .Select(actor => new
            {
                Actor = actor,
                Distance = actor.TargetPosition.DistanceTo(m_playerMech.GlobalPosition)
            })
            .Where(candidate => candidate.Distance <= ObjectiveHighlightRange)
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Actor.Definition.ObjectId)
            .Select(candidate => candidate.Actor)
            .FirstOrDefault();
        if (ObjectiveActor != null)
        {
            ObjectiveAimPosition = GetPolygonCentroidAnchor(ObjectiveActor);
            var modelNames = string.Join(", ", ObjectiveActor.Definition.Components
                .Select(component => component.ModelEntry.Name));
            var distance = ObjectiveActor.TargetPosition.DistanceTo(m_playerMech.GlobalPosition);
            GD.Print(
                $"MechRewired: highlighting objective target {ObjectiveActor.Description} " +
                $"object {ObjectiveActor.Definition.ObjectId} ({modelNames}) at {distance:F1}m.");
        }
        else
        {
            ObjectiveAimPosition = default;
        }
    }

    /// <summary>
    /// Gets the objective HUD anchor by giving every source polygon equal weight.
    /// </summary>
    /// <remarks>
    /// Debug triangles retain their source polygon identity, allowing triangulated polygons to
    /// contribute one centroid rather than gaining extra weight from their triangle count.
    /// This is intentionally independent of occlusion: the objective marker describes the
    /// object itself, rather than the nearest currently visible face.
    /// </remarks>
    private Vector3 GetPolygonCentroidAnchor(BattlefieldActor actor)
    {
        var polygonCentroids = m_sceneTriangles
            .Where(triangle =>
                m_actorsByObject.TryGetValue(
                    (triangle.SourceResourcePath, triangle.ObjectId),
                    out var triangleActor) &&
                ReferenceEquals(triangleActor, actor))
            .GroupBy(triangle => (
                triangle.SourceResourcePath,
                triangle.ObjectId,
                triangle.ModelIndex,
                triangle.PolygonIndex))
            .Select(polygon =>
            {
                var vertices = polygon
                    .SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C })
                    .Distinct()
                    .ToArray();
                return vertices.Aggregate(Vector3.Zero, (sum, vertex) => sum + vertex) /
                       vertices.Length;
            })
            .ToArray();

        return polygonCentroids.Length > 0
            ? polygonCentroids.Aggregate(Vector3.Zero, (sum, centroid) => sum + centroid) /
              polygonCentroids.Length
            : actor.TargetPosition;
    }

    private sealed class PendingMissile(
        float delay,
        Vector3 start,
        Vector3 direction,
        float range,
        Func<Vector3?> targetPosition,
        Action<Vector3> impact,
        float guidanceArmingDistance)
    {
        public float Delay { get; set; } = delay;

        public Vector3 Start { get; } = start;

        public Vector3 Direction { get; } = direction;

        public float Range { get; } = range;

        public Func<Vector3?> TargetPosition { get; } = targetPosition;

        public Action<Vector3> Impact { get; } = impact;

        public float GuidanceArmingDistance { get; } = guidanceArmingDistance;
    }

    private sealed class PendingWeaponRepeat(int weaponIndex, int remaining, float delay)
    {
        public int WeaponIndex { get; } = weaponIndex;

        public int Remaining { get; set; } = remaining;

        public float Delay { get; set; } = delay;
    }
}
