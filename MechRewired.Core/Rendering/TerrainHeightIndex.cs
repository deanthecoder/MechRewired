// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Numerics;

namespace MechRewired.Rendering;

/// <summary>
/// One triangle available to CPU-side terrain-height queries.
/// </summary>
public readonly record struct TerrainHeightTriangle(Vector3 A, Vector3 B, Vector3 C);

/// <summary>
/// Accelerates vertical terrain queries by indexing triangles in horizontal grid cells.
/// </summary>
/// <remarks>
/// Gameplay asks for terrain height many times per frame. Keeping this independent of the render
/// and physics meshes avoids repeatedly scanning every derived triangle.
/// </remarks>
public sealed class TerrainHeightIndex
{
    private const float PointTolerance = 0.0001f;

    private readonly IReadOnlyList<TerrainHeightTriangle> m_triangles;
    private readonly Dictionary<Cell, int[]> m_cells;
    private readonly float m_cellSize;

    public TerrainHeightIndex(
        IReadOnlyList<TerrainHeightTriangle> triangles,
        float cellSize = 64.0f)
    {
        ArgumentNullException.ThrowIfNull(triangles);
        if (cellSize <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize));
        }

        m_triangles = triangles;
        m_cellSize = cellSize;
        var cells = new Dictionary<Cell, List<int>>();
        for (var triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
        {
            var triangle = triangles[triangleIndex];
            var minimumX = MathF.Min(triangle.A.X, MathF.Min(triangle.B.X, triangle.C.X));
            var maximumX = MathF.Max(triangle.A.X, MathF.Max(triangle.B.X, triangle.C.X));
            var minimumZ = MathF.Min(triangle.A.Z, MathF.Min(triangle.B.Z, triangle.C.Z));
            var maximumZ = MathF.Max(triangle.A.Z, MathF.Max(triangle.B.Z, triangle.C.Z));
            var minimumCell = GetCell(minimumX, minimumZ);
            var maximumCell = GetCell(maximumX, maximumZ);
            for (var z = minimumCell.Z; z <= maximumCell.Z; z++)
            {
                for (var x = minimumCell.X; x <= maximumCell.X; x++)
                {
                    var cell = new Cell(x, z);
                    if (!cells.TryGetValue(cell, out var indices))
                    {
                        indices = [];
                        cells.Add(cell, indices);
                    }

                    indices.Add(triangleIndex);
                }
            }
        }

        m_cells = cells.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        CandidateReferenceCount = m_cells.Values.Sum(indices => indices.Length);
        MaximumCellOccupancy = m_cells.Count == 0
            ? 0
            : m_cells.Values.Max(indices => indices.Length);
    }

    public int TriangleCount => m_triangles.Count;

    public int CellCount => m_cells.Count;

    public int CandidateReferenceCount { get; }

    public int MaximumCellOccupancy { get; }

    public float AverageCellOccupancy => CellCount == 0
        ? 0.0f
        : CandidateReferenceCount / (float)CellCount;

    public bool TryGetHeight(Vector2 position, out float height, out int triangleIndex)
    {
        height = float.NegativeInfinity;
        triangleIndex = -1;
        if (!m_cells.TryGetValue(GetCell(position.X, position.Y), out var candidates))
        {
            return false;
        }

        foreach (var candidateIndex in candidates)
        {
            if (!TryInterpolateHeight(m_triangles[candidateIndex], position, out var candidateHeight) ||
                candidateHeight <= height)
            {
                continue;
            }

            height = candidateHeight;
            triangleIndex = candidateIndex;
        }

        return triangleIndex >= 0;
    }

    private Cell GetCell(float x, float z) => new(
        (int)MathF.Floor(x / m_cellSize),
        (int)MathF.Floor(z / m_cellSize));

    private static bool TryInterpolateHeight(
        TerrainHeightTriangle triangle,
        Vector2 position,
        out float height)
    {
        var denominator =
            (triangle.B.Z - triangle.C.Z) * (triangle.A.X - triangle.C.X) +
            (triangle.C.X - triangle.B.X) * (triangle.A.Z - triangle.C.Z);
        if (MathF.Abs(denominator) <= 0.000001f)
        {
            height = 0.0f;
            return false;
        }

        var firstWeight = (
            (triangle.B.Z - triangle.C.Z) * (position.X - triangle.C.X) +
            (triangle.C.X - triangle.B.X) * (position.Y - triangle.C.Z)) / denominator;
        var secondWeight = (
            (triangle.C.Z - triangle.A.Z) * (position.X - triangle.C.X) +
            (triangle.A.X - triangle.C.X) * (position.Y - triangle.C.Z)) / denominator;
        var thirdWeight = 1.0f - firstWeight - secondWeight;
        if (firstWeight < -PointTolerance || secondWeight < -PointTolerance || thirdWeight < -PointTolerance)
        {
            height = 0.0f;
            return false;
        }

        height = triangle.A.Y * firstWeight +
                 triangle.B.Y * secondWeight +
                 triangle.C.Y * thirdWeight;
        return true;
    }

    private readonly record struct Cell(int X, int Z);
}
