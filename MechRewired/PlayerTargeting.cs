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
    private const float ObjectiveHighlightRange = 300.0f;
    private const int LaserDamage = 7;

    private readonly PlayerMech m_playerMech;
    private readonly PlayerMission m_playerMission;
    private readonly IReadOnlyList<DebugTriangle> m_sceneTriangles;
    private readonly IReadOnlyList<BattlefieldActor> m_actors;
    private readonly IReadOnlyDictionary<(string SourcePath, int ObjectId), BattlefieldActor> m_actorsByObject;
    private readonly AudioStreamPlayer m_laserSound;
    private int m_nextLaserSide = -1;

    public PlayerTargeting(
        PlayerMech playerMech,
        PlayerMission playerMission,
        IReadOnlyList<DebugTriangle> sceneTriangles,
        IReadOnlyList<BattlefieldActor> actors,
        AudioStreamWav laserSound)
    {
        ArgumentNullException.ThrowIfNull(playerMech);
        ArgumentNullException.ThrowIfNull(playerMission);
        ArgumentNullException.ThrowIfNull(sceneTriangles);
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(laserSound);
        Name = "PlayerTargeting";
        m_playerMech = playerMech;
        m_playerMission = playerMission;
        m_actors = actors;
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
        m_sceneTriangles = sceneTriangles;
        m_laserSound = new AudioStreamPlayer
        {
            Name = "LaserSound",
            Stream = laserSound,
            MaxPolyphony = 8,
            VolumeDb = -2.0f
        };
        AddChild(m_laserSound);
        playerMech.FireRequested += FireLaser;
        playerMech.TargetRequested += SelectUnderReticle;
    }

    public BattlefieldActor SelectedActor { get; private set; }

    public IReadOnlyList<BattlefieldActor> Actors => m_actors;

    public BattlefieldActor ObjectiveActor { get; private set; }

    public Vector3 ObjectiveAimPosition { get; private set; }

    public override void _Ready()
    {
        GD.Print(
            $"MechRewired: targeting online ({m_actors.Count} battlefield actors; " +
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
        if (!TryRaycast(out var actor, out _, out _) || actor == null)
        {
            SelectedActor = null;
            GD.Print("MechRewired: targeting reticle found no targetable actor.");
            return;
        }

        if (!IsSelectable(actor))
        {
            SelectedActor = null;
            GD.Print("MechRewired: targeting reticle found no named targetable actor.");
            return;
        }

        SelectedActor = actor;
        GD.Print(
            $"MechRewired: targeted {actor.Description} in BWD/{actor.SourceResourceName}.BWD " +
            $"({actor.Health}/{actor.MaximumHealth} health).");
        m_playerMission.Apply(new MissionEvent(
            MissionEventKind.TargetInspected,
            actor.SourceResourceName));
    }

    public void FireLaser()
    {
        var aimOrigin = m_playerMech.CockpitCamera.GlobalPosition;
        var direction = -m_playerMech.Torso.GlobalBasis.Z.Normalized();
        var torsoBasis = m_playerMech.Torso.GlobalBasis.Orthonormalized();
        var start = m_playerMech.CockpitMount.GlobalPosition +
                    torsoBasis.X * (m_nextLaserSide * 2.8f) -
                    torsoBasis.Y * 0.45f -
                    torsoBasis.Z * 1.2f;
        m_nextLaserSide *= -1;
        var end = aimOrigin + direction * LaserRange;
        if (TryRaycast(out var actor, out _, out var hitPosition))
        {
            end = hitPosition;
            if (actor != null)
            {
                if (actor.IsDamageable)
                {
                    actor.ApplyDamage(LaserDamage, hitPosition, m_sceneTriangles);
                }
                else
                {
                    GD.Print(
                        $"MechRewired: laser struck indestructible {actor.Description}; " +
                        "target or inspect it instead.");
                }

                SelectedActor = actor.IsDestroyed || !IsSelectable(actor) ? null : actor;
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
        if (!DebugTriangleRaycaster.TryFindNearest(
                candidates,
                origin,
                direction,
                out var triangle,
                out distance) ||
            distance > LaserRange)
        {
            actor = null;
            hitPosition = default;
            return false;
        }

        m_actorsByObject.TryGetValue(
            (triangle.SourceResourcePath, triangle.ObjectId),
            out actor);
        hitPosition = origin + direction * distance;
        return true;
    }

    private void OnActorDestroyed(BattlefieldActor actor)
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

    private bool IsSelectable(BattlefieldActor actor) =>
        !actor.IsDestroyed &&
        actor.HasDisplayName &&
        (actor.IsDamageable || m_playerMission.IsActiveObjectiveTarget(actor));

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
            ObjectiveAimPosition = FindVisibleAimPosition(ObjectiveActor);
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

    private Vector3 FindVisibleAimPosition(BattlefieldActor actor)
    {
        var cameraPosition = m_playerMech.CockpitCamera.GlobalPosition;
        var visibleTriangles = m_sceneTriangles.Where(triangle =>
                !m_actorsByObject.TryGetValue(
                    (triangle.SourceResourcePath, triangle.ObjectId),
                    out var triangleActor) ||
                !triangleActor.IsDestroyed)
            .ToArray();
        var actorTriangles = visibleTriangles
            .Where(triangle =>
                m_actorsByObject.TryGetValue(
                    (triangle.SourceResourcePath, triangle.ObjectId),
                    out var triangleActor) &&
                ReferenceEquals(triangleActor, actor))
            .Select(triangle => new
            {
                Triangle = triangle,
                Center = (triangle.A + triangle.B + triangle.C) / 3.0f
            })
            .OrderBy(candidate => candidate.Center.DistanceSquaredTo(cameraPosition))
            .ToArray();
        foreach (var candidate in actorTriangles)
        {
            var offset = candidate.Center - cameraPosition;
            var distance = offset.Length();
            if (distance <= 0.001f ||
                !DebugTriangleRaycaster.TryFindNearest(
                    visibleTriangles,
                    cameraPosition,
                    offset / distance,
                    out var hitTriangle,
                    out var hitDistance) ||
                hitDistance > distance + 0.05f ||
                !m_actorsByObject.TryGetValue(
                    (hitTriangle.SourceResourcePath, hitTriangle.ObjectId),
                    out var hitActor) ||
                !ReferenceEquals(hitActor, actor))
            {
                continue;
            }

            return cameraPosition + offset.Normalized() * hitDistance;
        }

        return actorTriangles.Length > 0
            ? actorTriangles[0].Center
            : actor.TargetPosition;
    }
}
