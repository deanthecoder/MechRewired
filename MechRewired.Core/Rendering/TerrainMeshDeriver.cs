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
/// One upward-facing triangle from the original MW2 terrain surface.
/// </summary>
public readonly record struct TerrainSourceTriangle(Vector3 A, Vector3 B, Vector3 C);

/// <summary>
/// Indexed terrain geometry derived from the authored MW2 control surface.
/// </summary>
public sealed record DerivedTerrainMesh(
    IReadOnlyList<Vector3> Vertices,
    IReadOnlyList<Vector3> Normals,
    IReadOnlyList<int> Indices,
    IReadOnlyList<DerivedTerrainBoundaryEdge> BoundaryEdges)
{
    public int TriangleCount => Indices.Count / 3;
}

/// <summary>
/// One directed exterior edge of a derived terrain mesh. The direction retains the source
/// triangle winding, so a sealing face made from the edge can face away from the terrain.
/// </summary>
public readonly record struct DerivedTerrainBoundaryEdge(int FirstVertexIndex, int SecondVertexIndex);

/// <summary>
/// Tessellates the original low-poly terrain as a smooth curved control surface.
/// Original vertices remain fixed, connected edges remain watertight, and flat source regions
/// stay exactly planar. A lower tessellation level can therefore be used as a simplified collision
/// approximation of the same surface used for rendering.
/// </summary>
public static class TerrainMeshDeriver
{
    // Adjacent MW2 terrain pieces occasionally disagree at a nominally shared vertex by well under
    // a metre, despite their meaningful control points being tens of metres apart.
    private const float SourceWeldToleranceMetres = 1.0f;
    private const float DerivedWeldToleranceMetres = 0.001f;
    private const int NormalRelaxationIterations = 2;
    private const float NormalRelaxationStrength = 0.65f;

    public static DerivedTerrainMesh Build(
        IEnumerable<TerrainSourceTriangle> sourceTriangles,
        int subdivisions,
        float smoothingAngleDegrees = 30.0f,
        float smoothingStrength = 0.70f,
        Func<Vector3, Vector3> displace = null)
    {
        ArgumentNullException.ThrowIfNull(sourceTriangles);
        if (subdivisions < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(subdivisions));
        }

        smoothingAngleDegrees = Math.Clamp(smoothingAngleDegrees, 0.0f, 89.0f);
        smoothingStrength = Math.Clamp(smoothingStrength, 0.0f, 1.0f);
        var source = BuildSourceTopology(sourceTriangles);
        if (source.Triangles.Count == 0)
        {
            return new DerivedTerrainMesh(
                Array.Empty<Vector3>(),
                Array.Empty<Vector3>(),
                Array.Empty<int>(),
                Array.Empty<DerivedTerrainBoundaryEdge>());
        }

        var vertexNormals = CalculateSourceVertexNormals(source);
        var smoothingWeights = CalculateSmoothingWeights(source, smoothingAngleDegrees);
        var vertices = new List<Vector3>();
        var indices = new List<int>(source.Triangles.Count * subdivisions * subdivisions * 3);
        var vertexLookup = new SpatialVertexLookup(DerivedWeldToleranceMetres);
        foreach (var triangle in source.Triangles)
        {
            // A triangle whose three control vertices have no detected curvature is already the
            // simplest exact representation of that plane. Only curved neighbourhoods need the
            // requested dense tessellation; shared-edge weights ensure transition edges agree.
            var triangleSubdivisions =
                smoothingWeights[triangle.A] > 0.0001f ||
                smoothingWeights[triangle.B] > 0.0001f ||
                smoothingWeights[triangle.C] > 0.0001f
                    ? subdivisions
                    : 1;
            for (var row = 0; row < triangleSubdivisions; row++)
            {
                for (var column = 0; column < triangleSubdivisions - row; column++)
                {
                    AddTriangle(
                        row,
                        column,
                        row + 1,
                        column,
                        row,
                        column + 1);
                    if (column < triangleSubdivisions - row - 1)
                    {
                        AddTriangle(
                            row + 1,
                            column,
                            row + 1,
                            column + 1,
                            row,
                            column + 1);
                    }
                }
            }

            void AddTriangle(
                int firstRow,
                int firstColumn,
                int secondRow,
                int secondColumn,
                int thirdRow,
                int thirdColumn)
            {
                indices.Add(GetVertex(firstRow, firstColumn));
                indices.Add(GetVertex(secondRow, secondColumn));
                indices.Add(GetVertex(thirdRow, thirdColumn));
            }

            int GetVertex(int row, int column)
            {
                var secondWeight = row / (float)triangleSubdivisions;
                var thirdWeight = column / (float)triangleSubdivisions;
                var firstWeight = 1.0f - secondWeight - thirdWeight;
                var first = source.Vertices[triangle.A];
                var second = source.Vertices[triangle.B];
                var third = source.Vertices[triangle.C];
                var linear = first * firstWeight + second * secondWeight + third * thirdWeight;

                // Phong tessellation projects the linear point onto the three smooth vertex
                // tangent planes, then blends the projections. It rounds the original joins while
                // retaining every authored vertex and producing identical points along shared edges.
                var projectedFirst = ProjectOntoPlane(linear, first, vertexNormals[triangle.A]);
                var projectedSecond = ProjectOntoPlane(linear, second, vertexNormals[triangle.B]);
                var projectedThird = ProjectOntoPlane(linear, third, vertexNormals[triangle.C]);
                var curved = projectedFirst * firstWeight +
                             projectedSecond * secondWeight +
                             projectedThird * thirdWeight;
                var localSmoothing = smoothingStrength * (
                    smoothingWeights[triangle.A] * firstWeight +
                    smoothingWeights[triangle.B] * secondWeight +
                    smoothingWeights[triangle.C] * thirdWeight);
                var position = Vector3.Lerp(linear, curved, localSmoothing);
                if (displace != null)
                {
                    position = displace(position);
                }

                if (vertexLookup.TryFind(position, vertices, out var existingIndex))
                {
                    return existingIndex;
                }

                var index = vertices.Count;
                vertices.Add(position);
                vertexLookup.Add(position, index);
                return index;
            }
        }

        var normals = CalculateDerivedNormals(vertices, indices);
        return new DerivedTerrainMesh(
            vertices.AsReadOnly(),
            normals,
            indices.AsReadOnly(),
            FindBoundaryEdges(indices).AsReadOnly());
    }

    /// <summary>
    /// Grounds derived exterior vertices that sit just above the shared floor. This removes small
    /// gaps introduced when a nominal hill base is rounded upward between authored control points.
    /// </summary>
    public static DerivedTerrainMesh SnapLowExteriorVertices(
        DerivedTerrainMesh terrain,
        float groundHeight,
        float maximumHeightAboveGround,
        out int snappedVertexCount) =>
        SnapLowExteriorVertices(
            terrain,
            _ => groundHeight,
            maximumHeightAboveGround,
            out snappedVertexCount);

    /// <summary>Grounds derived exterior vertices against a varying floor-height field.</summary>
    public static DerivedTerrainMesh SnapLowExteriorVertices(
        DerivedTerrainMesh terrain,
        Func<Vector3, float> groundHeightAt,
        float maximumHeightAboveGround,
        out int snappedVertexCount)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(groundHeightAt);
        if (maximumHeightAboveGround <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumHeightAboveGround));
        }

        var vertices = terrain.Vertices.ToArray();
        var boundaryVertices = terrain.BoundaryEdges
            .SelectMany(edge => new[] { edge.FirstVertexIndex, edge.SecondVertexIndex })
            .ToHashSet();
        snappedVertexCount = 0;
        foreach (var index in boundaryVertices)
        {
            var vertex = vertices[index];
            var groundHeight = groundHeightAt(vertex);
            if (vertex.Y <= groundHeight + 0.0001f ||
                vertex.Y > groundHeight + maximumHeightAboveGround)
            {
                continue;
            }

            vertices[index] = new Vector3(vertex.X, groundHeight, vertex.Z);
            snappedVertexCount++;
        }

        return snappedVertexCount == 0
            ? terrain
            : terrain with
            {
                Vertices = vertices,
                Normals = CalculateDerivedNormals(vertices, terrain.Indices)
            };
    }

    /// <summary>
    /// Relaxes only interior heights while retaining the horizontal footprint and every exterior
    /// edge. This is suitable for a shadow proxy that should follow the authored landform without
    /// reproducing sharp control-triangle folds.
    /// </summary>
    public static DerivedTerrainMesh RelaxInteriorHeights(
        DerivedTerrainMesh terrain,
        int iterations,
        float strength)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        if (iterations < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations));
        }

        if (strength is <= 0.0f or > 1.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(strength));
        }

        var vertices = terrain.Vertices.ToArray();
        var boundaryVertices = terrain.BoundaryEdges
            .SelectMany(edge => new[] { edge.FirstVertexIndex, edge.SecondVertexIndex })
            .ToHashSet();
        var edges = FindEdges(terrain.Indices);
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var accumulatedHeights = new float[vertices.Length];
            var weights = new float[vertices.Length];
            foreach (var edge in edges)
            {
                var horizontalDelta = new Vector2(
                    vertices[edge.A].X - vertices[edge.B].X,
                    vertices[edge.A].Z - vertices[edge.B].Z);
                var weight = 1.0f / Math.Max(horizontalDelta.Length(), DerivedWeldToleranceMetres);
                accumulatedHeights[edge.A] += vertices[edge.B].Y * weight;
                accumulatedHeights[edge.B] += vertices[edge.A].Y * weight;
                weights[edge.A] += weight;
                weights[edge.B] += weight;
            }

            var relaxed = vertices.ToArray();
            for (var index = 0; index < vertices.Length; index++)
            {
                if (boundaryVertices.Contains(index) || weights[index] <= 0.0f)
                {
                    continue;
                }

                var neighbourHeight = accumulatedHeights[index] / weights[index];
                relaxed[index].Y = float.Lerp(vertices[index].Y, neighbourHeight, strength);
            }

            vertices = relaxed;
        }

        return terrain with
        {
            Vertices = vertices,
            Normals = CalculateDerivedNormals(vertices, terrain.Indices)
        };
    }

    private static List<DerivedTerrainBoundaryEdge> FindBoundaryEdges(IReadOnlyList<int> indices)
    {
        var edges = new Dictionary<Edge, DerivedTerrainBoundaryEdge>();
        var sharedEdges = new HashSet<Edge>();
        for (var index = 0; index < indices.Count; index += 3)
        {
            AddEdge(indices[index], indices[index + 1]);
            AddEdge(indices[index + 1], indices[index + 2]);
            AddEdge(indices[index + 2], indices[index]);
        }

        return edges
            .Where(pair => !sharedEdges.Contains(pair.Key))
            .Select(pair => pair.Value)
            .ToList();

        void AddEdge(int first, int second)
        {
            var edge = Edge.Create(first, second);
            if (!edges.TryAdd(edge, new DerivedTerrainBoundaryEdge(first, second)))
            {
                sharedEdges.Add(edge);
            }
        }
    }

    private static SourceTopology BuildSourceTopology(IEnumerable<TerrainSourceTriangle> sourceTriangles)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<IndexedTriangle>();
        var vertexLookup = new SpatialVertexLookup(SourceWeldToleranceMetres);
        foreach (var sourceTriangle in sourceTriangles)
        {
            var normal = Vector3.Cross(
                sourceTriangle.B - sourceTriangle.A,
                sourceTriangle.C - sourceTriangle.A);
            if (normal.LengthSquared() <= 0.000001f)
            {
                continue;
            }

            // Only the upward-facing height surface participates. MW2 terrain WTBs also contain
            // vertical and hidden sealing faces which must not be rounded into the playable land.
            if (Vector3.Normalize(normal).Y <= 0.001f)
            {
                continue;
            }

            var triangle = new IndexedTriangle(
                GetVertex(sourceTriangle.A),
                GetVertex(sourceTriangle.B),
                GetVertex(sourceTriangle.C));
            if (triangle.A != triangle.B && triangle.B != triangle.C && triangle.C != triangle.A)
            {
                triangles.Add(triangle);
            }
        }

        return new SourceTopology(vertices.AsReadOnly(), triangles.AsReadOnly());

        int GetVertex(Vector3 position)
        {
            if (vertexLookup.TryFind(position, vertices, out var existingIndex))
            {
                return existingIndex;
            }

            var index = vertices.Count;
            vertices.Add(position);
            vertexLookup.Add(position, index);
            return index;
        }
    }

    private static Vector3[] CalculateSourceVertexNormals(SourceTopology source)
    {
        var normals = new Vector3[source.Vertices.Count];
        foreach (var triangle in source.Triangles)
        {
            var weightedNormal = Vector3.Cross(
                source.Vertices[triangle.B] - source.Vertices[triangle.A],
                source.Vertices[triangle.C] - source.Vertices[triangle.A]);
            normals[triangle.A] += weightedNormal;
            normals[triangle.B] += weightedNormal;
            normals[triangle.C] += weightedNormal;
        }

        for (var index = 0; index < normals.Length; index++)
        {
            normals[index] = normals[index].LengthSquared() <= 0.000001f
                ? Vector3.UnitY
                : Vector3.Normalize(normals[index]);
        }

        return normals;
    }

    private static float[] CalculateSmoothingWeights(SourceTopology source, float smoothingAngleDegrees)
    {
        var faceNormals = source.Triangles.Select(triangle => Vector3.Normalize(Vector3.Cross(
            source.Vertices[triangle.B] - source.Vertices[triangle.A],
            source.Vertices[triangle.C] - source.Vertices[triangle.A]))).ToArray();
        var edges = new Dictionary<Edge, List<int>>();
        for (var triangleIndex = 0; triangleIndex < source.Triangles.Count; triangleIndex++)
        {
            var triangle = source.Triangles[triangleIndex];
            AddEdge(triangle.A, triangle.B, triangleIndex);
            AddEdge(triangle.B, triangle.C, triangleIndex);
            AddEdge(triangle.C, triangle.A, triangleIndex);
        }

        var weights = new float[source.Vertices.Count];
        var startAngle = Math.Max(3.0f, smoothingAngleDegrees * 0.35f);
        foreach (var (edge, adjacentTriangles) in edges)
        {
            if (adjacentTriangles.Count != 2)
            {
                continue;
            }

            var cosine = Math.Clamp(
                Vector3.Dot(faceNormals[adjacentTriangles[0]], faceNormals[adjacentTriangles[1]]),
                -1.0f,
                1.0f);
            var angle = MathF.Acos(cosine) * 180.0f / MathF.PI;
            var weight = SmoothStep(startAngle, smoothingAngleDegrees, angle);
            weights[edge.A] = Math.Max(weights[edge.A], weight);
            weights[edge.B] = Math.Max(weights[edge.B], weight);
        }

        return weights;

        void AddEdge(int first, int second, int triangleIndex)
        {
            var edge = Edge.Create(first, second);
            if (!edges.TryGetValue(edge, out var adjacentTriangles))
            {
                adjacentTriangles = new List<int>(2);
                edges.Add(edge, adjacentTriangles);
            }

            adjacentTriangles.Add(triangleIndex);
        }
    }

    private static IReadOnlyList<Vector3> CalculateDerivedNormals(
        IReadOnlyList<Vector3> vertices,
        IReadOnlyList<int> indices)
    {
        var normals = new Vector3[vertices.Count];
        for (var index = 0; index < indices.Count; index += 3)
        {
            var first = indices[index];
            var second = indices[index + 1];
            var third = indices[index + 2];
            var weightedNormal = Vector3.Cross(
                vertices[second] - vertices[first],
                vertices[third] - vertices[first]);
            normals[first] += weightedNormal;
            normals[second] += weightedNormal;
            normals[third] += weightedNormal;
        }

        for (var index = 0; index < normals.Length; index++)
        {
            normals[index] = normals[index].LengthSquared() <= 0.000001f
                ? Vector3.UnitY
                : Vector3.Normalize(normals[index]);
        }

        // A one-ring face average keeps shared vertices watertight, but its gradient can still
        // change abruptly along one of MW2's very large authored diagonals. Diffuse that normal
        // field over two derived-mesh rings so direct light no longer reveals the control
        // triangulation as a dark crease. Positions remain untouched, preserving the authored
        // silhouette and the separately sampled collision surface.
        var edges = FindEdges(indices);

        for (var iteration = 0; iteration < NormalRelaxationIterations; iteration++)
        {
            var accumulated = new Vector3[normals.Length];
            var weights = new float[normals.Length];
            foreach (var edge in edges)
            {
                var edgeLength = Vector3.Distance(vertices[edge.A], vertices[edge.B]);
                var weight = 1.0f / Math.Max(edgeLength, DerivedWeldToleranceMetres);
                accumulated[edge.A] += normals[edge.B] * weight;
                accumulated[edge.B] += normals[edge.A] * weight;
                weights[edge.A] += weight;
                weights[edge.B] += weight;
            }

            var relaxed = new Vector3[normals.Length];
            for (var index = 0; index < normals.Length; index++)
            {
                if (weights[index] <= 0.0f)
                {
                    relaxed[index] = normals[index];
                    continue;
                }

                var neighbourAverage = accumulated[index] / weights[index];
                var blended = Vector3.Lerp(
                    normals[index],
                    neighbourAverage,
                    NormalRelaxationStrength);
                relaxed[index] = blended.LengthSquared() <= 0.000001f
                    ? normals[index]
                    : Vector3.Normalize(blended);
            }

            normals = relaxed;
        }

        return normals;
    }

    private static HashSet<Edge> FindEdges(IReadOnlyList<int> indices)
    {
        var edges = new HashSet<Edge>();
        for (var index = 0; index < indices.Count; index += 3)
        {
            edges.Add(Edge.Create(indices[index], indices[index + 1]));
            edges.Add(Edge.Create(indices[index + 1], indices[index + 2]));
            edges.Add(Edge.Create(indices[index + 2], indices[index]));
        }

        return edges;
    }

    private static Vector3 ProjectOntoPlane(Vector3 point, Vector3 origin, Vector3 normal) =>
        point - Vector3.Dot(point - origin, normal) * normal;

    private static float SmoothStep(float minimum, float maximum, float value)
    {
        if (maximum <= minimum)
        {
            return value >= maximum ? 1.0f : 0.0f;
        }

        var amount = Math.Clamp((value - minimum) / (maximum - minimum), 0.0f, 1.0f);
        return amount * amount * (3.0f - 2.0f * amount);
    }

    private sealed record SourceTopology(
        IReadOnlyList<Vector3> Vertices,
        IReadOnlyList<IndexedTriangle> Triangles);

    private readonly record struct IndexedTriangle(int A, int B, int C);

    private readonly record struct Edge(int A, int B)
    {
        public static Edge Create(int first, int second) =>
            first <= second ? new Edge(first, second) : new Edge(second, first);
    }

    private readonly record struct QuantizedPosition(long X, long Y, long Z)
    {
        public static QuantizedPosition From(Vector3 position, float tolerance) => new(
            (long)MathF.Round(position.X / tolerance),
            (long)MathF.Round(position.Y / tolerance),
            (long)MathF.Round(position.Z / tolerance));
    }

    /// <summary>
    /// Uses the quantized cells only as a fast spatial index, then checks the surrounding cells by
    /// real distance. This avoids cracks when two effectively identical MW2 vertices happen to lie
    /// on opposite sides of a rounding boundary.
    /// </summary>
    private sealed class SpatialVertexLookup(float tolerance)
    {
        private static readonly int[] SearchOffsets = [0, -1, 1];
        private readonly Dictionary<QuantizedPosition, List<int>> m_cells = new();
        private readonly float m_toleranceSquared = tolerance * tolerance;

        public bool TryFind(
            Vector3 position,
            IReadOnlyList<Vector3> vertices,
            out int existingIndex)
        {
            var center = QuantizedPosition.From(position, tolerance);
            foreach (var xOffset in SearchOffsets)
            {
                foreach (var yOffset in SearchOffsets)
                {
                    foreach (var zOffset in SearchOffsets)
                    {
                        var cell = new QuantizedPosition(
                            center.X + xOffset,
                            center.Y + yOffset,
                            center.Z + zOffset);
                        if (!m_cells.TryGetValue(cell, out var candidates))
                        {
                            continue;
                        }

                        foreach (var candidate in candidates)
                        {
                            if (Vector3.DistanceSquared(vertices[candidate], position) <= m_toleranceSquared)
                            {
                                existingIndex = candidate;
                                return true;
                            }
                        }
                    }
                }
            }

            existingIndex = -1;
            return false;
        }

        public void Add(Vector3 position, int index)
        {
            var cell = QuantizedPosition.From(position, tolerance);
            if (!m_cells.TryGetValue(cell, out var indices))
            {
                indices = new List<int>(1);
                m_cells.Add(cell, indices);
            }

            indices.Add(index);
        }
    }
}
