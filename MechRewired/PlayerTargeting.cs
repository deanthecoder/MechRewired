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

namespace MechRewired;

/// <summary>
/// Selects original-data actors beneath the torso reticle and fires the first laser weapon slice.
/// </summary>
/// <remarks>
/// Actor and mission resource identities remain intact so combat events can satisfy data-driven objectives.
/// </remarks>
public partial class PlayerTargeting : Node
{
    private const float LaserRange = 1200.0f;
    private const float TargetingRange = 1000.0f;
    private const float ObjectiveHighlightRange = 300.0f;
    private const int LaserDamage = 7;

    private readonly PlayerMech m_playerMech;
    private readonly PlayerMission m_playerMission;
    private readonly IReadOnlyList<DebugTriangle> m_sceneTriangles;
    private readonly IReadOnlyList<BattlefieldActor> m_actors;
    private readonly IReadOnlyList<EnemyMech> m_enemyMechs;
    private readonly IReadOnlyDictionary<(string SourcePath, int ObjectId), BattlefieldActor> m_actorsByObject;
    private readonly AudioStreamPlayer m_laserSound;
    private readonly AudioStreamPlayer m_enemyPowerUpSound;
    private readonly AudioStreamPlayer m_enemyMechDestroyedSound;
    private readonly BattlefieldEffects m_battlefieldEffects;
    private int m_nextLaserSide = -1;

    public PlayerTargeting(
        PlayerMech playerMech,
        PlayerMission playerMission,
        IReadOnlyList<DebugTriangle> sceneTriangles,
        IReadOnlyList<BattlefieldActor> actors,
        IReadOnlyList<EnemyMech> enemyMechs,
        AudioStreamWav laserSound,
        AudioStreamWav enemyPowerUpSound,
        AudioStreamWav enemyMechDestroyedSound,
        BattlefieldEffects battlefieldEffects)
    {
        ArgumentNullException.ThrowIfNull(playerMech);
        ArgumentNullException.ThrowIfNull(playerMission);
        ArgumentNullException.ThrowIfNull(sceneTriangles);
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(enemyMechs);
        ArgumentNullException.ThrowIfNull(laserSound);
        ArgumentNullException.ThrowIfNull(enemyMechDestroyedSound);
        ArgumentNullException.ThrowIfNull(battlefieldEffects);
        Name = "PlayerTargeting";
        m_playerMech = playerMech;
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
        m_laserSound = new AudioStreamPlayer
        {
            Name = "LaserSound",
            Stream = laserSound,
            MaxPolyphony = 8,
            VolumeDb = -2.0f
        };
        AddChild(m_laserSound);
        if (enemyPowerUpSound != null)
        {
            m_enemyPowerUpSound = new AudioStreamPlayer
            {
                Name = "EnemyPowerUpWarning",
                Stream = enemyPowerUpSound,
                VolumeDb = -1.0f
            };
            AddChild(m_enemyPowerUpSound);
        }
        m_enemyMechDestroyedSound = new AudioStreamPlayer
        {
            Name = "EnemyMechDestroyedReport",
            Stream = enemyMechDestroyedSound,
            VolumeDb = -1.0f
        };
        AddChild(m_enemyMechDestroyedSound);
        playerMech.FireRequested += FireLaser;
        playerMech.TargetRequested += SelectUnderReticle;
        playerMech.NextTargetRequested += SelectNextEnemy;
        playerMech.PreviousTargetRequested += SelectPreviousEnemy;
        playerMech.NearestEnemyTargetRequested += SelectNearestEnemy;
        playerMech.ClearTargetRequested += ClearTarget;
        playerMech.InspectTargetRequested += InspectSelectedActor;
    }

    public BattlefieldActor SelectedActor { get; private set; }

    public EnemyMech SelectedEnemy { get; private set; }

    public IReadOnlyList<BattlefieldActor> Actors => m_actors;

    public IReadOnlyList<EnemyMech> EnemyMechs => m_enemyMechs;

    public BattlefieldActor ObjectiveActor { get; private set; }

    public Vector3 ObjectiveAimPosition { get; private set; }

    public override void _Ready()
    {
        GD.Print(
            $"MechRewired: targeting online ({m_actors.Count} battlefield actors, " +
            $"{m_enemyMechs.Count} hostile mechs; " +
            $"medium laser {LaserDamage} damage, alternating cockpit-aligned mounts, " +
            $"{LaserRange:F0}m range).");
    }

    public override void _Process(double delta)
    {
        _ = delta;
        UpdateObjectiveActor();
    }

    public void SelectUnderReticle()
    {
        if (!TryRaycast(out var actor, out var enemyMech, out _, out _, out _))
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

    public void FireLaser()
    {
        var aimOrigin = m_playerMech.CockpitCamera.GlobalPosition;
        var direction = -m_playerMech.Torso.GlobalBasis.Z.Normalized();
        var torsoBasis = m_playerMech.Torso.GlobalBasis.Orthonormalized();
        if (!m_playerMech.IsWeaponSideOperational(m_nextLaserSide))
        {
            m_nextLaserSide *= -1;
        }

        if (!m_playerMech.IsWeaponSideOperational(m_nextLaserSide))
        {
            GD.Print("MechRewired: no operational arm-mounted laser remains.");
            return;
        }

        var start = m_playerMech.CockpitMount.GlobalPosition +
                    torsoBasis.X * (m_nextLaserSide * 2.8f) -
                    torsoBasis.Y * 0.45f -
                    torsoBasis.Z * 1.2f;
        m_nextLaserSide *= -1;
        var end = aimOrigin + direction * LaserRange;
        if (TryRaycast(out var actor, out var enemyMech, out var enemyHit, out _, out var hitPosition))
        {
            end = hitPosition;
            if (enemyMech != null)
            {
                m_battlefieldEffects.SpawnWeaponImpact(hitPosition);
                enemyMech.ApplyDamage(
                    LaserDamage,
                    hitPosition,
                    enemyHit.Section,
                    enemyHit.FromRear);
                SelectedEnemy = enemyMech.IsDestroyed ? null : enemyMech;
                SelectedActor = null;
            }
            else if (actor != null)
            {
                if (actor.IsDamageable)
                {
                    m_battlefieldEffects.SpawnWeaponImpact(hitPosition);
                    actor.ApplyDamage(LaserDamage, hitPosition, m_sceneTriangles);
                }
                else
                {
                    GD.Print(
                        $"MechRewired: laser struck indestructible {actor.Description}; " +
                        "target or inspect it instead.");
                }

                SelectedActor = actor.IsDestroyed || !IsSelectable(actor) ? null : actor;
                SelectedEnemy = null;
            }
            else
            {
                GD.Print("MechRewired: laser struck non-targetable battlefield geometry.");
            }
        }
        else
        {
            GD.Print("MechRewired: laser fired; no target hit.");
        }

        GetParent().AddChild(new LaserEffect(start, end));
        m_laserSound.Play();
    }

    private bool TryRaycast(
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
            staticDistance <= LaserRange;
        enemyMech = null;
        enemyHit = null;
        var enemyDistance = float.PositiveInfinity;
        foreach (var candidate in m_enemyMechs.Where(candidate => !candidate.IsDestroyed))
        {
            if (candidate.TryRaycastSections(origin, direction, out var candidateHit) &&
                candidateHit.Distance <= LaserRange &&
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
}
