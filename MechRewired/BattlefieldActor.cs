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
/// Owns one targetable battlefield entity and its active and destroyed representations.
/// </summary>
/// <remarks>
/// Original BWD health and alternate-object metadata drive the transition without modifying the source models.
/// </remarks>
public partial class BattlefieldActor : Node3D
{
    private const float DebrisGravity = 4.5f;

    private readonly List<Node3D> m_activeRepresentations = new();
    private readonly List<Node3D> m_destroyedRepresentations = new();
    private readonly IReadOnlyList<ArrayMesh> m_explosionDebrisMeshes;
    private readonly List<RigidBody3D> m_debris = new();
    private Node3D m_effectObserver;
    private SceneryObstacle m_activeObstacle;
    private SceneryObstacle m_destroyedObstacle;
    private SceneryObstacle m_initialActiveObstacle;
    private SceneryObstacle m_initialDestroyedObstacle;
    private Transform3D m_motionAnchor = Transform3D.Identity;
    private bool m_spawnExplosionDebris = true;

    public BattlefieldActor(
        MechWarriorLevelActor definition,
        IReadOnlyList<ArrayMesh> explosionDebrisMeshes)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(explosionDebrisMeshes);
        if (explosionDebrisMeshes.Count == 0)
        {
            throw new ArgumentException("At least one explosion debris mesh is required.", nameof(explosionDebrisMeshes));
        }

        Definition = definition;
        SourceResourceName = Path.GetFileNameWithoutExtension(definition.SourceEntry.Name);
        m_explosionDebrisMeshes = explosionDebrisMeshes;
        Name = $"{definition.SourceEntry.Name}-{definition.ObjectId}";
        Health = definition.Health;
        MaximumHealth = definition.Health;
    }

    public MechWarriorLevelActor Definition { get; }

    public string SourceResourceName { get; }

    public string Description
    {
        get
        {
            var description = string.IsNullOrWhiteSpace(Definition.Description)
                ? Definition.Components[0].ModelEntry.Name
                : Definition.Description;
            return description.Equals("Chem.Plant", StringComparison.OrdinalIgnoreCase)
                ? "Chemical Plant"
                : description;
        }
    }

    public bool HasDisplayName =>
        !string.IsNullOrWhiteSpace(Definition.Description) &&
        !Definition.Description.Equals("none", StringComparison.OrdinalIgnoreCase);

    public int Health { get; private set; }

    public int MaximumHealth { get; }

    public bool IsDamageable => MaximumHealth > 0;

    public bool IsDestroyed { get; private set; }

    public Aabb DestructionBounds { get; private set; }

    public Aabb WorldBounds
    {
        get
        {
            var bounds = new Aabb();
            var hasBounds = false;
            var representations = IsDestroyed ? m_destroyedRepresentations : m_activeRepresentations;
            foreach (var representation in representations)
            {
                // Keep transforms and representation changes live without allocating child arrays.
                var childCount = representation.GetChildCount();
                for (var index = 0; index < childCount; index++)
                {
                    if (representation.GetChild(index) is not MeshInstance3D meshInstance) continue;
                    var meshBounds = meshInstance.GlobalTransform * meshInstance.GetAabb();
                    bounds = hasBounds ? bounds.Merge(meshBounds) : meshBounds;
                    hasBounds = true;
                }
            }

            return hasBounds
                ? bounds
                : new Aabb(
                    ToGlobal(MechWarriorCoordinateSystem.ToGodotPosition(
                        Definition.Components[0].Transform.Translation)),
                    Vector3.Zero);
        }
    }

    public Vector3 TargetPosition => WorldBounds.GetCenter();

    public SceneryObstacle SceneryObstacle => IsDestroyed ? m_destroyedObstacle : m_activeObstacle;

    public event Action<BattlefieldActor, Vector3> Destroyed;

    public override void _PhysicsProcess(double delta)
    {
        if (m_debris.Count > 0 && !IsWithinEffectPersistenceRange())
        {
            ClearExplosionDebris();
            GD.Print(
                $"MechRewired: culled explosion debris for {Description} beyond " +
                $"{BattlefieldEffects.EffectPersistenceRadius:F0}m.");
        }
    }

    public void AddRepresentation(Node3D representation, bool destroyed)
    {
        ArgumentNullException.ThrowIfNull(representation);
        AddChild(representation);
        representation.Visible = destroyed ? IsDestroyed : !IsDestroyed;
        (destroyed ? m_destroyedRepresentations : m_activeRepresentations).Add(representation);
    }

    /// <summary>
    /// Rebases the authored world-space representations beneath a movable assembly transform.
    /// </summary>
    public void SetMotionAnchor(Transform3D anchor)
    {
        var representations = m_activeRepresentations.Concat(m_destroyedRepresentations).ToArray();
        var worldTransforms = representations.Select(representation => representation.GlobalTransform).ToArray();
        GlobalTransform = anchor;
        for (var index = 0; index < representations.Length; index++)
        {
            representations[index].GlobalTransform = worldTransforms[index];
        }

        m_motionAnchor = anchor;
    }

    /// <summary>Moves an authored assembly and its collision footprint as one world-space unit.</summary>
    public void ApplyMotionTransform(Transform3D transform)
    {
        GlobalTransform = transform;
        var delta = transform * m_motionAnchor.AffineInverse();
        m_activeObstacle = TransformObstacle(m_initialActiveObstacle, delta);
        m_destroyedObstacle = TransformObstacle(m_initialDestroyedObstacle, delta);
    }

    public void ConfigureSceneryObstacles(
        SceneryObstacle activeObstacle,
        SceneryObstacle destroyedObstacle)
    {
        m_initialActiveObstacle = activeObstacle;
        m_initialDestroyedObstacle = destroyedObstacle;
        m_activeObstacle = activeObstacle;
        m_destroyedObstacle = destroyedObstacle;
    }

    /// <summary>
    /// Configures one-way cleanup of temporary explosion debris when the
    /// player leaves the actor's local battlefield area.
    /// </summary>
    public void ConfigureEffectPersistence(Node3D observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        m_effectObserver = observer;
    }

    /// <summary>
    /// Prevents generic CHUNKER debris when an actor supplies its own physical wreckage.
    /// </summary>
    public void SuppressGenericExplosionDebris() => m_spawnExplosionDebris = false;

    public void ApplyDamage(
        int damage,
        Vector3 hitPosition)
    {
        if (!IsDamageable || IsDestroyed || damage <= 0)
        {
            return;
        }

        Health = Math.Max(0, Health - damage);
        GD.Print(
            $"MechRewired: laser hit {Description} in BWD/{SourceResourceName}.BWD " +
            $"for {damage} damage ({Health}/{MaximumHealth}).");
        if (Health > 0)
        {
            return;
        }

        var explosionBounds = WorldBounds;
        DestructionBounds = explosionBounds;
        IsDestroyed = true;
        foreach (var representation in m_activeRepresentations)
        {
            representation.Visible = false;
        }

        foreach (var representation in m_destroyedRepresentations)
        {
            representation.Visible = true;
        }

        if (m_spawnExplosionDebris && IsWithinEffectPersistenceRange(explosionBounds.GetCenter()))
        {
            LaunchExplosionDebris(hitPosition, explosionBounds);
        }
        else if (m_spawnExplosionDebris)
        {
            GD.Print(
                $"MechRewired: skipped distant explosion debris for {Description} beyond " +
                $"{BattlefieldEffects.EffectPersistenceRadius:F0}m.");
        }
        GD.Print($"MechRewired: destroyed {Description} in BWD/{SourceResourceName}.BWD.");
        Destroyed?.Invoke(this, hitPosition);
    }

    private void LaunchExplosionDebris(
        Vector3 hitPosition,
        Aabb explosionBounds)
    {
        var random = new RandomNumberGenerator
        {
            Seed = unchecked((ulong)(Definition.ObjectId * 7919 + 104729))
        };
        var pieceCount = Math.Clamp(4 + MaximumHealth / 10, 5, 10);
        var hulls = new Dictionary<ArrayMesh, ConvexPolygonShape3D>();
        for (var index = 0; index < pieceCount; index++)
        {
            var mesh = m_explosionDebrisMeshes[index % m_explosionDebrisMeshes.Count];
            if (!hulls.TryGetValue(mesh, out var hull))
            {
                hull = mesh.CreateConvexShape();
                hulls.Add(mesh, hull);
            }
            var representation = ExplosionDebrisBody.Create(mesh, hull, DebrisGravity);
            representation.Name = $"ExplosionDebris-{index + 1}";
            AddChild(representation);
            var center = explosionBounds.GetCenter() + new Vector3(
                random.RandfRange(-explosionBounds.Size.X * 0.18f, explosionBounds.Size.X * 0.18f),
                random.RandfRange(0.0f, Math.Max(explosionBounds.Size.Y * 0.25f, 0.5f)),
                random.RandfRange(-explosionBounds.Size.Z * 0.18f, explosionBounds.Size.Z * 0.18f));
            representation.Rotation = new Vector3(
                random.RandfRange(0.0f, Mathf.Tau),
                random.RandfRange(0.0f, Mathf.Tau),
                random.RandfRange(0.0f, Mathf.Tau));
            // Keep the original mesh's launch position while centering its body on the hull.
            representation.GlobalPosition = center + representation.GlobalBasis * mesh.GetAabb().GetCenter();
            var outward = new Vector3(center.X - hitPosition.X, 0.0f, center.Z - hitPosition.Z);
            if (outward.LengthSquared() < 0.01f)
            {
                var angle = Mathf.Tau * index / Math.Max(m_destroyedRepresentations.Count, 1);
                outward = new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle));
            }

            outward = outward.Normalized();
            var sideways = new Vector3(-outward.Z, 0.0f, outward.X);
            var velocity = outward * random.RandfRange(2.5f, 5.0f) +
                           sideways * random.RandfRange(-1.5f, 1.5f) +
                           Vector3.Up * random.RandfRange(3.5f, 6.0f);
            var angularVelocity = new Vector3(
                random.RandfRange(-2.2f, 2.2f),
                random.RandfRange(-1.6f, 1.6f),
                random.RandfRange(-2.2f, 2.2f));
            representation.LinearVelocity = velocity;
            representation.AngularVelocity = angularVelocity;
            m_debris.Add(representation);
        }

        GD.Print(
            $"MechRewired: launched {m_debris.Count} original MW2 explosion chunks from " +
            $"{Description} with sleeping convex debris bodies and terrain collision.");
    }

    private bool IsWithinEffectPersistenceRange() =>
        !GodotObject.IsInstanceValid(m_effectObserver) ||
        IsWithinEffectPersistenceRange(DestructionBounds.GetCenter());

    private bool IsWithinEffectPersistenceRange(Vector3 position) =>
        !GodotObject.IsInstanceValid(m_effectObserver) ||
        m_effectObserver.GlobalPosition.DistanceSquaredTo(position) <=
        BattlefieldEffects.EffectPersistenceRadius * BattlefieldEffects.EffectPersistenceRadius;

    private void ClearExplosionDebris()
    {
        foreach (var debris in m_debris)
        {
            debris.QueueFree();
        }

        m_debris.Clear();
    }

    private static SceneryObstacle TransformObstacle(SceneryObstacle obstacle, Transform3D transform)
    {
        if (obstacle == null)
        {
            return null;
        }

        var walls = obstacle.Walls.Select(wall => new SceneryWallTriangle(
            TransformWallPoint(transform, wall.A),
            TransformWallPoint(transform, wall.B),
            TransformWallPoint(transform, wall.C))).ToArray();
        if (walls.Length == 0)
        {
            return obstacle;
        }

        var points = walls.SelectMany(wall => new[] { wall.A, wall.B, wall.C }).ToArray();
        return new SceneryObstacle(
            obstacle.Name,
            new System.Numerics.Vector2(points.Min(point => point.X), points.Min(point => point.Y)),
            new System.Numerics.Vector2(points.Max(point => point.X), points.Max(point => point.Y)),
            walls);
    }

    private static System.Numerics.Vector2 TransformWallPoint(
        Transform3D transform,
        System.Numerics.Vector2 point)
    {
        var moved = transform * new Vector3(point.X, 0.0f, point.Y);
        return new System.Numerics.Vector2(moved.X, moved.Z);
    }
}
