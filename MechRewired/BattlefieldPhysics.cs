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
/// Owns collision layers and static physics geometry for the decoded battlefield.
/// </summary>
public static class BattlefieldPhysics
{
    public const uint TerrainLayer = 1u << 7;
    public const uint WreckageLayer = 1u << 8;

    public static StaticBody3D AddTerrainCollision(
        Node parent,
        IEnumerable<DebugTriangle> sceneTriangles)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(sceneTriangles);
        var terrainTriangles = sceneTriangles.Where(triangle =>
                triangle.ResourcePath == "IMPLICIT/GROUND" ||
                triangle.ResourcePath.StartsWith("POLY/T_", StringComparison.Ordinal))
            .ToArray();
        var faces = new Vector3[terrainTriangles.Length * 3];
        for (var index = 0; index < terrainTriangles.Length; index++)
        {
            faces[index * 3] = terrainTriangles[index].A;
            faces[index * 3 + 1] = terrainTriangles[index].B;
            faces[index * 3 + 2] = terrainTriangles[index].C;
        }

        var shape = new ConcavePolygonShape3D
        {
            Data = faces,
            BackfaceCollision = true
        };
        var body = new StaticBody3D
        {
            Name = "DecodedTerrainPhysics",
            CollisionLayer = TerrainLayer,
            CollisionMask = 0
        };
        body.AddChild(new CollisionShape3D
        {
            Name = "TerrainTriangleMesh",
            Shape = shape
        });
        parent.AddChild(body);
        GD.Print(
            $"MechRewired: created static terrain physics from {terrainTriangles.Length:N0} " +
            "double-sided derived/implicit triangles.");
        return body;
    }
}
