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
/// Animates original MW2 mech assemblies as convex rigid bodies against decoded triangle-mesh terrain.
/// </summary>
public partial class MechWreckage : Node3D
{
    private const float WreckageGravityScale = 0.56f;
    private const float MinimumMass = 1.5f;
    private const float MaximumMass = 18.0f;

    private readonly List<RigidBody3D> m_parts = new();
    private Node3D m_observer;
    private Vector3 m_origin;

    public static MechWreckage Spawn(
        Node parent,
        Node3D observer,
        string actorName,
        IReadOnlyList<(MeshInstance3D Mesh, string PartName)> sourceParts,
        Vector3 hitPosition,
        int randomSeed)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(sourceParts);
        var wreckage = new MechWreckage
        {
            Name = $"{actorName}-Wreckage",
            m_observer = observer
        };
        parent.AddChild(wreckage);
        wreckage.Build(sourceParts, hitPosition, randomSeed);
        return wreckage;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!GodotObject.IsInstanceValid(m_observer) ||
            m_observer.GlobalPosition.DistanceSquaredTo(m_origin) <=
            BattlefieldEffects.EffectPersistenceRadius * BattlefieldEffects.EffectPersistenceRadius)
        {
            return;
        }

        GD.Print($"MechRewired: permanently culled {m_parts.Count} physical mech wreckage assemblies beyond " +
                 $"{BattlefieldEffects.EffectPersistenceRadius:F0}m.");
        QueueFree();
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

        m_origin = validParts.Select(part => part.Mesh.GlobalPosition)
            .Aggregate(Vector3.Zero, (sum, value) => sum + value) / validParts.Length;
        var random = new RandomNumberGenerator { Seed = unchecked((ulong)randomSeed) };
        foreach (var group in validParts.GroupBy(part => MechBodySectionClassifier.Classify(part.PartName)))
        {
            var sourceMeshes = group.Select(part => part.Mesh).ToArray();
            var bounds = GetCombinedBounds(sourceMeshes);
            var center = bounds.GetCenter();
            var body = new RigidBody3D
            {
                Name = group.Key.ToString(),
                GravityScale = WreckageGravityScale,
                Mass = Mathf.Clamp(
                    bounds.Size.X * bounds.Size.Y * bounds.Size.Z * 0.08f,
                    MinimumMass,
                    MaximumMass),
                LinearDamp = 0.28f,
                AngularDamp = 0.42f,
                CanSleep = true,
                ContinuousCd = true,
                CollisionLayer = BattlefieldPhysics.WreckageLayer,
                CollisionMask = BattlefieldPhysics.TerrainLayer
            };
            AddChild(body);
            body.GlobalPosition = center;

            var colliderPoints = GetColliderPoints(body.GlobalTransform, sourceMeshes);
            if (colliderPoints.Length >= 4)
            {
                body.AddChild(new CollisionShape3D
                {
                    Name = $"{group.Key}ConvexHull",
                    Shape = new ConvexPolygonShape3D { Points = colliderPoints }
                });
            }

            foreach (var part in group)
            {
                // Transfer existing visuals rather than duplicating mesh data during an explosion.
                part.Mesh.Reparent(body, true);
            }

            var outward = new Vector3(center.X - hitPosition.X, 0.0f, center.Z - hitPosition.Z);
            if (outward.LengthSquared() < 0.04f)
            {
                var angle = Mathf.Tau * (int)group.Key / Enum.GetValues<MechBodySection>().Length;
                outward = new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle));
            }

            outward = outward.Normalized();
            var sideways = new Vector3(-outward.Z, 0.0f, outward.X);
            body.LinearVelocity =
                outward * random.RandfRange(2.2f, 4.8f) +
                sideways * random.RandfRange(-1.4f, 1.4f) +
                Vector3.Up * random.RandfRange(4.0f, 7.0f);
            body.AngularVelocity = new Vector3(
                random.RandfRange(-1.8f, 1.8f),
                random.RandfRange(-1.3f, 1.3f),
                random.RandfRange(-1.8f, 1.8f));
            m_parts.Add(body);
        }

        GD.Print($"MechRewired: detached {validParts.Length} original mech meshes into " +
                 $"{m_parts.Count} convex physical wreckage assemblies (low gravity; terrain collision).");
    }

    private static Vector3[] GetColliderPoints(
        Transform3D bodyTransform,
        IEnumerable<MeshInstance3D> meshes)
    {
        var worldToBody = bodyTransform.AffineInverse();
        var points = new List<Vector3>();
        foreach (var meshInstance in meshes)
        {
            var localToBody = worldToBody * meshInstance.GlobalTransform;
            for (var surfaceIndex = 0; surfaceIndex < meshInstance.Mesh.GetSurfaceCount(); surfaceIndex++)
            {
                var arrays = meshInstance.Mesh.SurfaceGetArrays(surfaceIndex);
                foreach (var vertex in arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                {
                    points.Add(localToBody * vertex);
                }
            }
        }

        return points.ToArray();
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
}
