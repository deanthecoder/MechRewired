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
using MechRewired.Simulation;

namespace MechRewired;

/// <summary>
/// Animates detached original MW2 mech assemblies and settles them against decoded terrain.
/// </summary>
public partial class MechWreckage : Node3D
{
    private const float Gravity = 5.5f;
    private const float MaximumFlightSeconds = 12.0f;

    private readonly List<PartState> m_parts = new();
    private IReadOnlyList<DebugTriangle> m_terrainTriangles = Array.Empty<DebugTriangle>();
    private Node3D m_observer;
    private Vector3 m_origin;

    public static MechWreckage Spawn(
        Node parent,
        Node3D observer,
        string actorName,
        IReadOnlyList<(MeshInstance3D Mesh, string PartName)> sourceParts,
        IReadOnlyList<DebugTriangle> sceneTriangles,
        Vector3 hitPosition,
        int randomSeed)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(sourceParts);
        var wreckage = new MechWreckage
        {
            Name = $"{actorName}-Wreckage",
            m_observer = observer,
            m_terrainTriangles = sceneTriangles.Where(triangle =>
                    triangle.ResourcePath == "IMPLICIT/GROUND" ||
                    triangle.ResourcePath.StartsWith("POLY/T_", StringComparison.Ordinal))
                .ToArray()
        };
        parent.AddChild(wreckage);
        wreckage.Build(sourceParts, hitPosition, randomSeed);
        return wreckage;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (GodotObject.IsInstanceValid(m_observer) &&
            m_observer.GlobalPosition.DistanceSquaredTo(m_origin) >
            BattlefieldEffects.EffectPersistenceRadius * BattlefieldEffects.EffectPersistenceRadius)
        {
            GD.Print($"MechRewired: permanently culled {m_parts.Count} mech wreckage assemblies beyond " +
                     $"{BattlefieldEffects.EffectPersistenceRadius:F0}m.");
            QueueFree();
            return;
        }

        var elapsed = (float)delta;
        foreach (var part in m_parts.Where(part => !part.Settled))
        {
            part.Age += elapsed;
            part.Velocity += Vector3.Down * Gravity * elapsed;
            part.Representation.GlobalPosition += part.Velocity * elapsed;
            part.Representation.RotateObjectLocal(Vector3.Right, part.AngularVelocity.X * elapsed);
            part.Representation.RotateObjectLocal(Vector3.Up, part.AngularVelocity.Y * elapsed);
            part.Representation.RotateObjectLocal(Vector3.Back, part.AngularVelocity.Z * elapsed);

            if (part.Velocity.Y <= 0.0f && TryGetTerrainHeight(part.Representation.GlobalPosition, out var terrainHeight))
            {
                var lowestPoint = GetWorldBounds(part.Representation).Position.Y;
                if (lowestPoint <= terrainHeight)
                {
                    part.Representation.GlobalPosition += Vector3.Up * (terrainHeight - lowestPoint);
                    if (part.Velocity.Y < -1.2f && part.Age < MaximumFlightSeconds)
                    {
                        part.Velocity = new Vector3(
                            part.Velocity.X * 0.48f,
                            -part.Velocity.Y * 0.18f,
                            part.Velocity.Z * 0.48f);
                        part.AngularVelocity *= 0.55f;
                    }
                    else
                    {
                        Settle(part, terrainHeight);
                    }
                }
            }

            if (part.Age >= MaximumFlightSeconds)
            {
                if (TryGetTerrainHeight(part.Representation.GlobalPosition, out var finalTerrainHeight))
                {
                    Settle(part, finalTerrainHeight);
                }
                else
                {
                    part.Settled = true;
                }
            }
        }
    }

    private void Build(
        IReadOnlyList<(MeshInstance3D Mesh, string PartName)> sourceParts,
        Vector3 hitPosition,
        int randomSeed)
    {
        var validParts = sourceParts
            .Where(part => GodotObject.IsInstanceValid(part.Mesh) && part.Mesh.Mesh != null)
            .ToArray();
        if (validParts.Length == 0)
        {
            return;
        }

        m_origin = validParts.Select(part => part.Mesh.GlobalPosition).Aggregate(Vector3.Zero, (sum, value) => sum + value) /
                   validParts.Length;
        var random = new RandomNumberGenerator { Seed = unchecked((ulong)randomSeed) };
        foreach (var group in validParts.GroupBy(part => MechBodySectionClassifier.Classify(part.PartName)))
        {
            var center = GetCombinedBounds(group.Select(part => part.Mesh)).GetCenter();
            var representation = new Node3D { Name = group.Key.ToString() };
            AddChild(representation);
            representation.GlobalPosition = center;
            foreach (var part in group)
            {
                // Transfer the already-rendered original mesh instead of allocating and uploading a duplicate
                // during the explosion. This preserves its materials/decals and keeps destruction hitch-free.
                part.Mesh.Reparent(representation, true);
            }

            var outward = new Vector3(center.X - hitPosition.X, 0.0f, center.Z - hitPosition.Z);
            if (outward.LengthSquared() < 0.04f)
            {
                var angle = Mathf.Tau * (int)group.Key / Enum.GetValues<MechBodySection>().Length;
                outward = new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle));
            }

            outward = outward.Normalized();
            var sideways = new Vector3(-outward.Z, 0.0f, outward.X);
            m_parts.Add(new PartState(
                representation,
                outward * random.RandfRange(2.2f, 4.8f) +
                sideways * random.RandfRange(-1.4f, 1.4f) +
                Vector3.Up * random.RandfRange(4.0f, 7.0f),
                new Vector3(
                    random.RandfRange(-1.8f, 1.8f),
                    random.RandfRange(-1.3f, 1.3f),
                    random.RandfRange(-1.8f, 1.8f))));
        }

        GD.Print($"MechRewired: detached {validParts.Length} original mech meshes into " +
                 $"{m_parts.Count} logical wreckage assemblies.");
    }

    private bool TryGetTerrainHeight(Vector3 position, out float height)
    {
        const float rayHeight = 10000.0f;
        var origin = new Vector3(position.X, rayHeight, position.Z);
        if (!DebugTriangleRaycaster.TryFindNearest(
                m_terrainTriangles, origin, Vector3.Down, out _, out var distance))
        {
            height = 0.0f;
            return false;
        }

        height = origin.Y - distance;
        return true;
    }

    private static void Settle(PartState part, float terrainHeight)
    {
        var lowestPoint = GetWorldBounds(part.Representation).Position.Y;
        part.Representation.GlobalPosition += Vector3.Up * (terrainHeight - lowestPoint);
        part.Velocity = Vector3.Zero;
        part.AngularVelocity = Vector3.Zero;
        part.Settled = true;
    }

    private static Aabb GetCombinedBounds(IEnumerable<MeshInstance3D> meshes)
    {
        var bounds = new Aabb();
        var hasBounds = false;
        foreach (var mesh in meshes)
        {
            var meshBounds = mesh.GlobalTransform * mesh.GetAabb();
            bounds = hasBounds ? bounds.Merge(meshBounds) : meshBounds;
            hasBounds = true;
        }

        return bounds;
    }

    private static Aabb GetWorldBounds(Node3D representation) =>
        GetCombinedBounds(representation.GetChildren().OfType<MeshInstance3D>());

    private sealed class PartState(Node3D representation, Vector3 velocity, Vector3 angularVelocity)
    {
        public Node3D Representation { get; } = representation;
        public Vector3 Velocity { get; set; } = velocity;
        public Vector3 AngularVelocity { get; set; } = angularVelocity;
        public float Age { get; set; }
        public bool Settled { get; set; }
    }
}
