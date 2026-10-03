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
/// <summary>Focused native-engine coverage for cached meshes and indexed live scenery.</summary>
public partial class RayCacheCheck : Node
{
#if DEBUG
    public override void _Ready()
    {
        try
        {
            var root = new Node3D();
            AddChild(root);
            var part = new MeshInstance3D();
            root.AddChild(part);
            var resource = new ArrayMesh();
            part.Mesh = resource;
            SetTriangle(resource, 5);
            var parts = new[] {(part, "LEFT_ARM")};
            MechSectionHitTester.PrepareMesh(resource);
            if (!MechSectionHitTester.TryFindNearest(root, parts, Vector3.Zero, Vector3.Back, out var hit) || Mathf.Abs(hit.Distance - 5) > 0.001f) throw new System.Exception("initial");
            SetTriangle(resource, 10);
            if (!MechSectionHitTester.TryFindNearest(root, parts, Vector3.Zero, Vector3.Back, out hit) || Mathf.Abs(hit.Distance - 10) > 0.001f) throw new System.Exception("resource invalidation");
            part.Transform = new Transform3D(Basis.FromEuler(new Vector3(0, 0.73f, 0)).Scaled(new Vector3(2,3,4)), new Vector3(20,-3,10));
            var direction = (part.GlobalBasis * Vector3.Back).Normalized();
            if (!MechSectionHitTester.TryFindNearest(root, parts, part.GlobalPosition, direction, out hit) || Mathf.Abs(hit.Distance - (part.GlobalBasis * Vector3.Back).Length() * 10) > 0.001f) throw new System.Exception("scaled pose");
            part.Position += new Vector3(0,100,0);
            if (MechSectionHitTester.TryFindNearest(root, parts, new Vector3(20,-3,10), direction, out _)) throw new System.Exception("moved pose bounds");
            CheckSceneIndex();
            GD.Print("RAY_CACHE_CHECK_PASS");
            GetTree().Quit();
        }
        catch (System.Exception e)
        {
            GD.PushError(e.ToString());
            GetTree().Quit(1);
        }
    }
    private static void SetTriangle(ArrayMesh mesh, float z)
    {
        mesh.ClearSurfaces();
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = new Vector3[]{new(-2,-2,z),new(2,-2,z),new(0,2,z)};
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);
    }

    private static void CheckSceneIndex()
    {
        var scene = new System.Collections.Generic.List<DebugTriangle>();
        for (var x = 0; x < 128; x++)
        for (var z = 0; z < 64; z++)
            scene.Add(new DebugTriangle("terrain", "POLY/T_TEST", 0, 0, scene.Count,
                new Vector3(x * 4, 0, z * 4), new Vector3(x * 4 + 3, 0, z * 4), new Vector3(x * 4, 0, z * 4 + 3)));
        // A moving triangle uses a terrain-looking resource but is explicitly excluded from caching.
        var movingIndex = scene.Count;
        scene.Add(scene[0] with { A = new Vector3(1, 5, 1), B = new Vector3(2, 5, 1), C = new Vector3(1, 5, 2) });
        var indexed = new TerrainRayIndex(scene, new System.Collections.Generic.HashSet<int> {movingIndex});
        for (var i = 0; i < 32; i++)
        {
            var origin = new Vector3(i * 12 + 1, 100, i * 4 + 1);
            var brute = DebugTriangleRaycaster.TryFindNearest(scene, origin, Vector3.Down, out var original, out var originalDistance);
            var fast = DebugTriangleRaycaster.TryFindNearest(indexed, origin, Vector3.Down, out var actual, out var actualDistance);
            if (brute != fast || !ReferenceEquals(actual, original) || Mathf.Abs(originalDistance - actualDistance) > 0.001f)
                throw new System.Exception("terrain hit mismatch");
            if (DebugTriangleRaycaster.IsSegmentBlocked(scene, origin, origin + Vector3.Down * 120, 1) !=
                DebugTriangleRaycaster.IsSegmentBlocked(indexed, origin, origin + Vector3.Down * 120, 1))
                throw new System.Exception("terrain LOS mismatch");
        }
        scene[movingIndex] = scene[movingIndex] with { A = new Vector3(101, 8, 101), B = new Vector3(103, 8, 101), C = new Vector3(101, 8, 103) };
        if (!DebugTriangleRaycaster.TryFindNearest(indexed, new Vector3(101.5f, 10, 101.5f), Vector3.Down, out var moved, out var distance) ||
            !ReferenceEquals(moved, scene[movingIndex]) || Mathf.Abs(distance - 2) > 0.001f)
            throw new System.Exception("moving terrain cache");
        if (DebugTriangleRaycaster.TryFindNearest(indexed, new Vector3(101.5f, 10, 101.5f), Vector3.Down,
                out _, out _, triangle => !ReferenceEquals(triangle, scene[movingIndex]), 3))
            throw new System.Exception("destroyed predicate or range");
        if (!DebugTriangleRaycaster.TryFindNearest(indexed, new Vector3(101.5f, 10, 101.5f), Vector3.Down,
                out _, out _, null, 2))
            throw new System.Exception("inclusive range");
        scene.Add(scene[movingIndex] with { A = new Vector3(201, 9, 101), B = new Vector3(203, 9, 101), C = new Vector3(201, 9, 103) });
        if (!DebugTriangleRaycaster.TryFindNearest(indexed, new Vector3(201.5f, 10, 101.5f), Vector3.Down, out var added, out distance) ||
            !ReferenceEquals(added, scene[^1]) || Mathf.Abs(distance - 1) > 0.001f)
            throw new System.Exception("structural fallback");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 100; i++)
            DebugTriangleRaycaster.TryFindNearest(scene, new Vector3(101, 100, 101), Vector3.Down, out _, out _);
        var bruteMs = timer.Elapsed.TotalMilliseconds;
        // Index construction is intentionally not part of gameplay query timing.
        var rebuilt = new TerrainRayIndex(scene, new System.Collections.Generic.HashSet<int> {movingIndex});
        timer.Restart();
        for (var i = 0; i < 100; i++)
            DebugTriangleRaycaster.TryFindNearest(rebuilt, new Vector3(101, 100, 101), Vector3.Down, out _, out _);
        GD.Print($"RAY_INDEX_CHECK: triangles={scene.Count} queries=100 bruteMs={bruteMs:F3} indexedMs={timer.Elapsed.TotalMilliseconds:F3}");
    }
#endif
}
