// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know.
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Godot;

namespace MechRewired;

/// <summary>Native physics check for irregular explosion debris resting on flat terrain.</summary>
/// <remarks>Uses real physics frames to confirm an off-origin visual and hull settle together.</remarks>
public partial class ExplosionDebrisBodyCheck : Node3D
{
    private static readonly Vector3[] ChunkPoints =
    [
        new(3.0f, 1.0f, -2.0f), new(5.1f, 1.2f, -1.8f), new(3.4f, 3.0f, -1.7f),
        new(3.1f, 1.4f, 0.2f), new(4.2f, 1.1f, -0.6f)
    ];

#if DEBUG
    public override async void _Ready()
    {
        try
        {
            var ground = new StaticBody3D
            {
                CollisionLayer = BattlefieldPhysics.TerrainLayer,
                CollisionMask = 0
            };
            var groundShape = new BoxShape3D { Size = new Vector3(30, 0.5f, 30) };
            ground.AddChild(new CollisionShape3D { Shape = groundShape, Position = new Vector3(0, -0.25f, 0) });
            AddChild(ground);

            var mesh = new ArrayMesh();
            using var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = new Vector3[]
            {
                ChunkPoints[0], ChunkPoints[1], ChunkPoints[2],
                ChunkPoints[0], ChunkPoints[2], ChunkPoints[3],
                ChunkPoints[0], ChunkPoints[3], ChunkPoints[1],
                ChunkPoints[1], ChunkPoints[3], ChunkPoints[2],
                ChunkPoints[0], ChunkPoints[4], ChunkPoints[1],
                ChunkPoints[1], ChunkPoints[4], ChunkPoints[2],
                ChunkPoints[2], ChunkPoints[4], ChunkPoints[3],
                ChunkPoints[3], ChunkPoints[4], ChunkPoints[0]
            };
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            var hull = mesh.CreateConvexShape();
            var body = ExplosionDebrisBody.Create(mesh, hull, 3.4f);
            AddChild(body);
            body.Position = new Vector3(0, 4, 0);
            body.Rotation = new Vector3(0.45f, 0.32f, 0.27f);

            var visual = body.GetChild<MeshInstance3D>(0);
            var collision = body.GetChild<CollisionShape3D>(1);
            var expectedOffset = -mesh.GetAabb().GetCenter();
            if (visual.Position.DistanceTo(expectedOffset) > 0.001f || collision.Position.DistanceTo(expectedOffset) > 0.001f)
                throw new System.Exception("visual and hull do not share the centered mesh offset");
            if (visual.Position.Length() < 0.5f)
                throw new System.Exception("off-origin chunk did not exercise centering");

            var initialRotation = body.GlobalBasis.GetEuler();
            var changedRotation = false;
            var slept = false;
            for (var i = 0; i < 900; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                var rotation = body.GlobalBasis.GetEuler();
                if (rotation.DistanceTo(initialRotation) > 0.12f)
                    changedRotation = true;
                if (body.Sleeping)
                {
                    slept = true;
                    break;
                }
            }

            if (!changedRotation)
                throw new System.Exception("irregular chunk did not topple");
            if (!slept)
                throw new System.Exception("chunk did not settle to sleep");
            var sleepingTransform = body.GlobalTransform;
            for (var i = 0; i < 2; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                if (!body.Sleeping || body.GlobalTransform != sleepingTransform)
                    throw new System.Exception("sleeping chunk continued moving after settling");
            }
            var finalRotation = body.GlobalBasis.GetEuler();
            if (Mathf.Abs(finalRotation.X) < 0.08f && Mathf.Abs(finalRotation.Z) < 0.08f)
                throw new System.Exception("chunk was forced upright instead of retaining its resting tilt");
            var lowestGroundClearance = float.PositiveInfinity;
            foreach (var point in ChunkPoints)
                lowestGroundClearance = Mathf.Min(lowestGroundClearance, (body.GlobalTransform * (point + expectedOffset)).Y);
            if (lowestGroundClearance < -0.08f)
                throw new System.Exception($"chunk penetrated ground by {-lowestGroundClearance:F3} m");

            GD.Print($"EXPLOSION_DEBRIS_BODY_CHECK_PASS: toppled=true sleeping=true clearance={lowestGroundClearance:F3} rotation={finalRotation}");
            GetTree().Quit();
        }
        catch (System.Exception e)
        {
            GD.PushError(e.ToString());
            GetTree().Quit(1);
        }
    }
#else
    public override void _Ready()
    {
        GD.Print("EXPLOSION_DEBRIS_BODY_CHECK_SKIPPED: Debug build required");
        GetTree().Quit();
    }
#endif
}
