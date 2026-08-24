// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued.
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Godot;
using MechRewired.Rendering;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;

namespace MechRewired;

/// <summary>
/// Builds the fallback floor as a sparse grid, omitting cells that lie fully beneath raised terrain.
/// </summary>
public static class ImplicitGroundMeshBuilder
{
    private const float TargetVertexSpacingMetres = 24.0f;
    private const float CoveredHeightToleranceMetres = 0.02f;

    public static ImplicitGroundMesh Build(
        Vector2 size,
        Vector3 center,
        float groundHeight,
        Color color,
        IEnumerable<DebugTriangle> terrainTriangles,
        Func<NumericsVector2, float> groundHeightAt = null)
    {
        ArgumentNullException.ThrowIfNull(terrainTriangles);
        var usesRelief = groundHeightAt != null;
        var terrain = terrainTriangles
            .Select(triangle => new TerrainHeightTriangle(
                ToNumerics(triangle.A),
                ToNumerics(triangle.B),
                ToNumerics(triangle.C)))
            .ToArray();
        var terrainIndex = new TerrainHeightIndex(terrain, TargetVertexSpacingMetres);
        var cellsAcross = Mathf.Clamp(
            Mathf.CeilToInt(size.X / TargetVertexSpacingMetres),
            32,
            usesRelief ? 160 : 256);
        var cellsDeep = Mathf.Clamp(
            Mathf.CeilToInt(size.Y / TargetVertexSpacingMetres),
            32,
            usesRelief ? 160 : 256);
        var vertices = new Vector3[(cellsAcross + 1) * (cellsDeep + 1)];
        for (var z = 0; z <= cellsDeep; z++)
        {
            for (var x = 0; x <= cellsAcross; x++)
            {
                var localPosition = new Vector3(
                    -size.X / 2.0f + size.X * x / cellsAcross,
                    0.0f,
                    -size.Y / 2.0f + size.Y * z / cellsDeep);
                localPosition.Y = ResolveGroundHeight(localPosition) - groundHeight;
                vertices[GetVertexIndex(x, z)] = localPosition;
            }
        }

        var indices = new List<int>(cellsAcross * cellsDeep * 6);
        var removedTriangleCount = 0;
        for (var z = 0; z < cellsDeep; z++)
        {
            for (var x = 0; x < cellsAcross; x++)
            {
                AddTriangle(
                    GetVertexIndex(x, z),
                    GetVertexIndex(x, z + 1),
                    GetVertexIndex(x + 1, z + 1));
                AddTriangle(
                    GetVertexIndex(x, z),
                    GetVertexIndex(x + 1, z + 1),
                    GetVertexIndex(x + 1, z));
            }
        }

        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);
        var stepX = size.X / cellsAcross;
        var stepZ = size.Y / cellsDeep;
        foreach (var vertex in vertices)
        {
            surfaceTool.SetNormal(CalculateGroundNormal(vertex, stepX, stepZ));
            surfaceTool.SetColor(color);
            surfaceTool.AddVertex(vertex);
        }

        for (var index = 0; index < indices.Count; index += 3)
        {
            // The grid uses conventional upward-facing winding. Godot's front face is clockwise,
            // so mirror the derived-terrain index order before submitting it.
            surfaceTool.AddIndex(indices[index]);
            surfaceTool.AddIndex(indices[index + 2]);
            surfaceTool.AddIndex(indices[index + 1]);
        }

        var mesh = new ArrayMesh();
        if (surfaceTool.Commit(mesh) == null)
        {
            throw new InvalidOperationException("Godot did not create the sparse implicit-ground mesh.");
        }

        var worldTriangles = new List<TerrainSourceTriangle>(indices.Count / 3);
        for (var index = 0; index < indices.Count; index += 3)
        {
            worldTriangles.Add(new TerrainSourceTriangle(
                ToWorld(vertices[indices[index]]),
                ToWorld(vertices[indices[index + 1]]),
                ToWorld(vertices[indices[index + 2]])));
        }

        return new ImplicitGroundMesh(
            mesh,
            indices.Count / 3,
            removedTriangleCount,
            worldTriangles.AsReadOnly());

        int GetVertexIndex(int x, int z) => z * (cellsAcross + 1) + x;

        void AddTriangle(int first, int second, int third)
        {
            var firstPosition = vertices[first];
            var secondPosition = vertices[second];
            var thirdPosition = vertices[third];
            // Three covered corners do not prove that a coarse 24 m grid triangle lies wholly
            // below an irregular or concave mountain footprint. Retaining triangles whose edge
            // midpoints or centroid fall outside the authored surface prevents a background-
            // coloured moat from appearing between the terrain skirt and implicit ground.
            if (IsCovered(firstPosition) &&
                IsCovered(secondPosition) &&
                IsCovered(thirdPosition) &&
                IsCovered((firstPosition + secondPosition) * 0.5f) &&
                IsCovered((secondPosition + thirdPosition) * 0.5f) &&
                IsCovered((thirdPosition + firstPosition) * 0.5f) &&
                IsCovered((firstPosition + secondPosition + thirdPosition) / 3.0f))
            {
                removedTriangleCount++;
                return;
            }

            indices.Add(first);
            indices.Add(second);
            indices.Add(third);
        }

        bool IsCovered(Vector3 localPosition)
        {
            var worldPosition = new NumericsVector2(
                center.X + localPosition.X,
                center.Z + localPosition.Z);
            return terrainIndex.TryGetHeight(worldPosition, out var height, out _) &&
                   height > ResolveGroundHeight(localPosition) + CoveredHeightToleranceMetres;
        }

        NumericsVector3 ToWorld(Vector3 localPosition) => new(
            center.X + localPosition.X,
            groundHeight + localPosition.Y,
            center.Z + localPosition.Z);

        float ResolveGroundHeight(Vector3 localPosition)
        {
            if (groundHeightAt == null)
            {
                return groundHeight;
            }

            return groundHeightAt(new NumericsVector2(
                center.X + localPosition.X,
                center.Z + localPosition.Z));
        }

        Vector3 CalculateGroundNormal(Vector3 localPosition, float sampleStepX, float sampleStepZ)
        {
            if (!usesRelief)
            {
                return Vector3.Up;
            }

            var left = ResolveGroundHeight(localPosition - Vector3.Right * sampleStepX);
            var right = ResolveGroundHeight(localPosition + Vector3.Right * sampleStepX);
            var back = ResolveGroundHeight(localPosition - Vector3.Back * sampleStepZ);
            var front = ResolveGroundHeight(localPosition + Vector3.Back * sampleStepZ);
            var slopeX = (right - left) / (sampleStepX * 2.0f);
            var slopeZ = (front - back) / (sampleStepZ * 2.0f);
            return new Vector3(-slopeX, 1.0f, -slopeZ).Normalized();
        }
    }

    private static NumericsVector3 ToNumerics(Vector3 value) => new(value.X, value.Y, value.Z);
}

/// <summary>One sparse implicit-ground mesh and its triangle accounting.</summary>
public sealed record ImplicitGroundMesh(
    Mesh Mesh,
    int TriangleCount,
    int RemovedTriangleCount,
    IReadOnlyList<TerrainSourceTriangle> WorldTriangles);
