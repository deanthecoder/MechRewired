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

/// <summary>Creates physical chunks from the original explosion artwork.</summary>
/// <remarks>Convex ground contact lets irregular chunks topple and sleep without resetting their tilt.</remarks>
public static class ExplosionDebrisBody
{
    /// <summary>Creates a centered, low-gravity body with matching visual and collision geometry.</summary>
    public static RigidBody3D Create(ArrayMesh mesh, ConvexPolygonShape3D hull, float gravity)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(hull);
        var bounds = mesh.GetAabb();
        var offset = -bounds.GetCenter();
        var body = new RigidBody3D
        {
            GravityScale = gravity / (float)ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8f).AsDouble(),
            Mass = Mathf.Clamp(bounds.Size.X * bounds.Size.Y * bounds.Size.Z * 0.08f, 1.5f, 18.0f),
            LinearDamp = 0.28f,
            AngularDamp = 0.42f,
            CanSleep = true,
            ContinuousCd = true,
            CollisionLayer = BattlefieldPhysics.WreckageLayer,
            CollisionMask = BattlefieldPhysics.TerrainLayer
        };
        body.AddChild(new MeshInstance3D
        {
            Mesh = mesh,
            Position = offset,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.DoubleSided
        });
        body.AddChild(new CollisionShape3D { Shape = hull, Position = offset });
        return body;
    }
}
