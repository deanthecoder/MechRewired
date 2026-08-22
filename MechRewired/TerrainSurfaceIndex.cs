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
using MechRewired.Rendering;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;

namespace MechRewired;

/// <summary>
/// Shares one spatially indexed view of the decoded terrain across gameplay systems.
/// </summary>
public sealed class TerrainSurfaceIndex
{
    private readonly IReadOnlyList<DebugTriangle> m_triangles;
    private readonly TerrainHeightIndex m_index;

    public TerrainSurfaceIndex(IEnumerable<DebugTriangle> sceneTriangles)
    {
        ArgumentNullException.ThrowIfNull(sceneTriangles);
        m_triangles = sceneTriangles.Where(triangle =>
                triangle.ResourcePath == "IMPLICIT/GROUND" ||
                triangle.ResourcePath.StartsWith("POLY/T_", StringComparison.Ordinal))
            .ToArray();
        m_index = new TerrainHeightIndex(m_triangles.Select(triangle => new TerrainHeightTriangle(
                ToNumerics(triangle.A),
                ToNumerics(triangle.B),
                ToNumerics(triangle.C)))
            .ToArray());
    }

    public int TriangleCount => m_index.TriangleCount;

    public int CellCount => m_index.CellCount;

    public int MaximumCellOccupancy => m_index.MaximumCellOccupancy;

    public float AverageCellOccupancy => m_index.AverageCellOccupancy;

    public bool TryGetSurface(Vector3 position, out float height, out DebugTriangle triangle)
    {
        if (!m_index.TryGetHeight(
                new NumericsVector2(position.X, position.Z),
                out height,
                out var triangleIndex))
        {
            triangle = null;
            return false;
        }

        triangle = m_triangles[triangleIndex];
        return true;
    }

    public bool TryGetHeight(Vector3 position, out float height) =>
        TryGetSurface(position, out height, out _);

    private static NumericsVector3 ToNumerics(Vector3 value) =>
        new(value.X, value.Y, value.Z);
}
