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
    private const float MaximumDebrisLifetime = 12.0f;

    private readonly List<Node3D> m_activeRepresentations = new();
    private readonly List<Node3D> m_destroyedRepresentations = new();
    private readonly IReadOnlyList<ArrayMesh> m_explosionDebrisMeshes;
    private readonly List<DebrisState> m_debris = new();
    private IReadOnlyList<DebugTriangle> m_terrainTriangles = Array.Empty<DebugTriangle>();
    private Node3D m_effectObserver;
    private SceneryObstacle m_activeObstacle;
    private SceneryObstacle m_destroyedObstacle;

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
        m_explosionDebrisMeshes = explosionDebrisMeshes;
        Name = $"{definition.SourceEntry.Name}-{definition.ObjectId}";
        Health = definition.Health;
        MaximumHealth = definition.Health;
    }

    public MechWarriorLevelActor Definition { get; }

    public string SourceResourceName => Path.GetFileNameWithoutExtension(Definition.SourceEntry.Name);

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
            foreach (var meshInstance in representations
                         .SelectMany(representation => representation.GetChildren())
                         .OfType<MeshInstance3D>())
            {
                var meshBounds = meshInstance.GlobalTransform * meshInstance.GetAabb();
                bounds = hasBounds ? bounds.Merge(meshBounds) : meshBounds;
                hasBounds = true;
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
            return;
        }

        var elapsed = (float)delta;
        for (var index = m_debris.Count - 1; index >= 0; index--)
        {
            var debris = m_debris[index];
            debris.Age += elapsed;
            if (debris.Settled)
            {
                continue;
            }

            debris.Velocity += Vector3.Down * DebrisGravity * elapsed;
            debris.Representation.GlobalPosition += debris.Velocity * elapsed;
            debris.Representation.RotateObjectLocal(Vector3.Right, debris.AngularVelocity.X * elapsed);
            debris.Representation.RotateObjectLocal(Vector3.Up, debris.AngularVelocity.Y * elapsed);
            debris.Representation.RotateObjectLocal(Vector3.Back, debris.AngularVelocity.Z * elapsed);

            if (debris.Velocity.Y <= 0.0f &&
                TryGetTerrainHeight(debris.Representation.GlobalPosition, out var terrainHeight))
            {
                var lowestPoint = GetWorldBounds(debris.Representation).Position.Y;
                if (lowestPoint <= terrainHeight)
                {
                    debris.Representation.GlobalPosition += Vector3.Up * (terrainHeight - lowestPoint);
                    if (debris.Velocity.Y < -0.8f && debris.Age < MaximumDebrisLifetime)
                    {
                        debris.Velocity = new Vector3(
                            debris.Velocity.X * 0.58f,
                            -debris.Velocity.Y * 0.22f,
                            debris.Velocity.Z * 0.58f);
                        debris.AngularVelocity *= 0.65f;
                    }
                    else
                    {
                        SettleDebris(debris, terrainHeight);
                    }
                }
            }

            if (debris.Age >= MaximumDebrisLifetime)
            {
                if (TryGetTerrainHeight(debris.Representation.GlobalPosition, out var finalTerrainHeight))
                {
                    SettleDebris(debris, finalTerrainHeight);
                }
                else
                {
                    debris.Settled = true;
                }
            }
        }
    }

    public void AddRepresentation(Node3D representation, bool destroyed)
    {
        ArgumentNullException.ThrowIfNull(representation);
        AddChild(representation);
        representation.Visible = destroyed ? IsDestroyed : !IsDestroyed;
        (destroyed ? m_destroyedRepresentations : m_activeRepresentations).Add(representation);
    }

    public void ConfigureSceneryObstacles(
        SceneryObstacle activeObstacle,
        SceneryObstacle destroyedObstacle)
    {
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

    public void ApplyDamage(
        int damage,
        Vector3 hitPosition,
        IReadOnlyList<DebugTriangle> sceneTriangles)
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

        if (IsWithinEffectPersistenceRange(explosionBounds.GetCenter()))
        {
            LaunchExplosionDebris(hitPosition, explosionBounds, sceneTriangles);
        }
        else
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
        Aabb explosionBounds,
        IReadOnlyList<DebugTriangle> sceneTriangles)
    {
        m_terrainTriangles = sceneTriangles.Where(triangle =>
                triangle.ResourcePath == "IMPLICIT/GROUND" ||
                triangle.ResourcePath.StartsWith("POLY/T_", StringComparison.Ordinal))
            .ToArray();
        var random = new RandomNumberGenerator
        {
            Seed = unchecked((ulong)(Definition.ObjectId * 7919 + 104729))
        };
        var pieceCount = Math.Clamp(4 + MaximumHealth / 10, 5, 10);
        for (var index = 0; index < pieceCount; index++)
        {
            var representation = new Node3D
            {
                Name = $"ExplosionDebris-{index + 1}"
            };
            var meshInstance = new MeshInstance3D
            {
                Mesh = m_explosionDebrisMeshes[index % m_explosionDebrisMeshes.Count],
                CastShadow = GeometryInstance3D.ShadowCastingSetting.DoubleSided
            };
            representation.AddChild(meshInstance);
            AddChild(representation);
            var center = explosionBounds.GetCenter() + new Vector3(
                random.RandfRange(-explosionBounds.Size.X * 0.18f, explosionBounds.Size.X * 0.18f),
                random.RandfRange(0.0f, Math.Max(explosionBounds.Size.Y * 0.25f, 0.5f)),
                random.RandfRange(-explosionBounds.Size.Z * 0.18f, explosionBounds.Size.Z * 0.18f));
            representation.GlobalPosition = center;
            representation.Rotation = new Vector3(
                random.RandfRange(0.0f, Mathf.Tau),
                random.RandfRange(0.0f, Mathf.Tau),
                random.RandfRange(0.0f, Mathf.Tau));
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
            m_debris.Add(new DebrisState(representation, velocity, angularVelocity));
        }

        GD.Print(
            $"MechRewired: launched {m_debris.Count} original MW2 explosion chunks from " +
            $"{Description} with low-gravity debris physics.");
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
            debris.Representation.QueueFree();
        }

        m_debris.Clear();
    }

    private bool TryGetTerrainHeight(Vector3 position, out float height)
    {
        const float rayHeight = 10000.0f;
        var origin = new Vector3(position.X, rayHeight, position.Z);
        if (!DebugTriangleRaycaster.TryFindNearest(
                m_terrainTriangles,
                origin,
                Vector3.Down,
                out _,
                out var distance))
        {
            height = 0.0f;
            return false;
        }

        height = origin.Y - distance;
        return true;
    }

    private static void SettleDebris(DebrisState debris, float terrainHeight)
    {
        var rotation = debris.Representation.Rotation;
        debris.Representation.Rotation = new Vector3(0.0f, rotation.Y, 0.0f);
        var lowestPoint = GetWorldBounds(debris.Representation).Position.Y;
        debris.Representation.GlobalPosition += Vector3.Up * (terrainHeight - lowestPoint);
        debris.Velocity = Vector3.Zero;
        debris.AngularVelocity = Vector3.Zero;
        debris.Settled = true;
    }

    private static Aabb GetWorldBounds(Node3D representation)
    {
        var bounds = new Aabb();
        var hasBounds = false;
        foreach (var meshInstance in representation.GetChildren().OfType<MeshInstance3D>())
        {
            var meshBounds = meshInstance.GlobalTransform * meshInstance.GetAabb();
            bounds = hasBounds ? bounds.Merge(meshBounds) : meshBounds;
            hasBounds = true;
        }

        return hasBounds
            ? bounds
            : new Aabb(representation.GlobalPosition, Vector3.Zero);
    }

    private sealed class DebrisState(
        Node3D representation,
        Vector3 velocity,
        Vector3 angularVelocity)
    {
        public Node3D Representation { get; } = representation;

        public Vector3 Velocity { get; set; } = velocity;

        public Vector3 AngularVelocity { get; set; } = angularVelocity;

        public float Age { get; set; }

        public bool Settled { get; set; }
    }
}
