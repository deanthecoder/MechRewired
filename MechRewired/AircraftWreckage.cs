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

namespace MechRewired;

/// <summary>
/// Transfers an aircraft's original destroyed model to one physical wreckage body.
/// </summary>
/// <remarks>
/// Flight remains archive-authored until the kill.  Once destroyed, the original
/// wreck model, its measured final path velocity, and the player hit position seed
/// a single terrain-colliding body rather than a scripted vertical fall.
/// </remarks>
public static class AircraftWreckage
{
    private const float MinimumMass = 4.0f;
    private const float MaximumMass = 28.0f;
    private const float CrashGravityScale = 1.35f;
    private const float CrashVelocityRetention = 0.35f;
    private const float BlastSpeed = 4.0f;
    private const float PitchTorquePerKilogram = 1.35f;

    public static RigidBody3D TrySpawn(
        BattlefieldActor actor,
        Vector3 flightVelocity,
        Vector3 hitPosition)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var wreckageRoots = actor.GetChildren()
            .OfType<Node3D>()
            .Where(root => root.Visible && root.GetChildren().OfType<MeshInstance3D>().Any())
            .ToArray();
        var meshes = wreckageRoots
            .SelectMany(root => root.GetChildren().OfType<MeshInstance3D>())
            .Where(mesh => mesh.Visible && mesh.Mesh != null)
            .ToArray();
        if (meshes.Length == 0)
        {
            return null;
        }

        var bounds = GetCombinedBounds(meshes);
        var center = bounds.GetCenter();
        var body = new RigidBody3D
        {
            Name = $"{actor.Description}-PhysicalWreckage",
            GravityScale = CrashGravityScale,
            Mass = Mathf.Clamp(
                bounds.Size.X * bounds.Size.Y * bounds.Size.Z * 0.08f,
                MinimumMass,
                MaximumMass),
            LinearDamp = 1.15f,
            AngularDamp = 3.2f,
            CanSleep = true,
            ContinuousCd = true,
            CollisionLayer = BattlefieldPhysics.WreckageLayer,
            CollisionMask = BattlefieldPhysics.TerrainLayer,
            PhysicsMaterialOverride = new PhysicsMaterial
            {
                Bounce = 0.0f,
                Friction = 1.0f
            }
        };
        actor.AddChild(body);
        body.GlobalPosition = center;

        var colliderPoints = GetColliderPoints(body.GlobalTransform, meshes);
        if (colliderPoints.Length >= 4)
        {
            body.AddChild(new CollisionShape3D
            {
                Name = "DestroyedAssemblyConvexHull",
                Shape = new ConvexPolygonShape3D { Points = colliderPoints }
            });
        }

        foreach (var root in wreckageRoots)
        {
            root.Reparent(body, true);
        }

        // The path velocity is exact archive data.  Breaking the rotor and tail sheds most
        // forward speed immediately, then the slightly above-normal gravity gives the falling
        // wreck enough visual weight at the scale of these battlefields.
        body.LinearVelocity = flightVelocity * CrashVelocityRetention;
        var blastDirection = center - hitPosition;
        if (blastDirection.LengthSquared() < 0.01f)
        {
            blastDirection = Vector3.Up;
        }

        blastDirection = (blastDirection.Normalized() + Vector3.Up * 0.25f).Normalized();
        body.ApplyImpulse(blastDirection * body.Mass * BlastSpeed, hitPosition - center);

        // A helicopter that loses lift also begins to pitch with its last forward motion.
        // This deterministic torque avoids an artificial random roll while ensuring a
        // centre-mass hit still leaves the wreckage in a physically readable tumble.
        var horizontalFlight = new Vector3(flightVelocity.X, 0.0f, flightVelocity.Z);
        if (horizontalFlight.LengthSquared() >= 0.01f)
        {
            body.ApplyTorqueImpulse(
                horizontalFlight.Normalized().Cross(Vector3.Up) * body.Mass * PitchTorquePerKilogram);
        }

        GD.Print(
            $"MechRewired: handed {actor.Description} to physical aircraft wreckage " +
            $"(authored velocity {flightVelocity.Length():F1} m/s; terrain collision).");
        return body;
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
