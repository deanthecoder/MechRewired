// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued.
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Numerics;

namespace MechRewired.Rendering;

/// <summary>
/// Blends raised exterior terrain edges into the implicit ground plane.
/// </summary>
/// <remarks>
/// MW2 terrain WTBs commonly have hidden floor sealing faces. Once those faces are excluded and
/// the remaining upward surface is smoothed, an exterior hill edge can sit above the shared floor.
/// A short outward apron closes that resultant opening without turning the base of every hill into
/// an implausible vertical retaining wall.
/// </remarks>
public static class TerrainMeshBoundarySealer
{
    private const float MinimumClearanceMetres = 0.02f;
    private const float MinimumApronReachMetres = 4.0f;
    private const float ApronReachPerMetreOfRise = 3.0f;
    private const float MaximumApronReachMetres = 42.0f;
    public const float GroundOverlapMetres = 0.50f;

    /// <summary>Builds outward-facing ground aprons for every elevated exterior edge.</summary>
    public static IReadOnlyList<TerrainSourceTriangle> BuildSkirts(
        DerivedTerrainMesh terrain,
        float groundHeight) =>
        BuildSkirts(terrain, _ => groundHeight);

    /// <summary>Builds ground aprons against a varying ground-height field.</summary>
    public static IReadOnlyList<TerrainSourceTriangle> BuildSkirts(
        DerivedTerrainMesh terrain,
        Func<Vector3, float> groundHeightAt)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(groundHeightAt);
        var skirts = new List<TerrainSourceTriangle>();
        var outwardDirections = CalculateBoundaryOutwardDirections(terrain);
        foreach (var edge in terrain.BoundaryEdges)
        {
            var first = terrain.Vertices[edge.FirstVertexIndex];
            var second = terrain.Vertices[edge.SecondVertexIndex];
            var firstGroundHeight = groundHeightAt(first);
            var secondGroundHeight = groundHeightAt(second);
            if (first.Y <= firstGroundHeight + MinimumClearanceMetres &&
                second.Y <= secondGroundHeight + MinimumClearanceMetres)
            {
                continue;
            }

            var groundFirst = CreateGroundApronEndpoint(
                first,
                outwardDirections.GetValueOrDefault(edge.FirstVertexIndex),
                firstGroundHeight,
                groundHeightAt);
            var groundSecond = CreateGroundApronEndpoint(
                second,
                outwardDirections.GetValueOrDefault(edge.SecondVertexIndex),
                secondGroundHeight,
                groundHeightAt);
            // The terrain lies to the right of its directed boundary edge. Reverse the apron
            // winding so it points outward and remains visible from the surrounding floor.
            skirts.Add(new TerrainSourceTriangle(first, groundFirst, second));
            skirts.Add(new TerrainSourceTriangle(second, groundFirst, groundSecond));
        }

        return skirts.AsReadOnly();
    }

    private static Dictionary<int, Vector2> CalculateBoundaryOutwardDirections(DerivedTerrainMesh terrain)
    {
        var accumulatedDirections = new Dictionary<int, Vector2>();
        foreach (var edge in terrain.BoundaryEdges)
        {
            var first = terrain.Vertices[edge.FirstVertexIndex];
            var second = terrain.Vertices[edge.SecondVertexIndex];
            var horizontalEdge = new Vector2(second.X - first.X, second.Z - first.Z);
            if (horizontalEdge.LengthSquared() <= 0.000001f)
            {
                continue;
            }

            // The height surface is wound with the terrain on the right, so its exterior is the
            // left-hand perpendicular in X/Z space. Accumulate at both endpoints to create one
            // continuous corner direction rather than an apron made of separate edge fins.
            var outward = Vector2.Normalize(new Vector2(-horizontalEdge.Y, horizontalEdge.X));
            AddDirection(edge.FirstVertexIndex, outward);
            AddDirection(edge.SecondVertexIndex, outward);
        }

        return accumulatedDirections.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.LengthSquared() <= 0.000001f
                ? Vector2.Zero
                : Vector2.Normalize(pair.Value));

        void AddDirection(int vertexIndex, Vector2 direction)
        {
            accumulatedDirections.TryGetValue(vertexIndex, out var accumulated);
            accumulatedDirections[vertexIndex] = accumulated + direction;
        }
    }

    private static Vector3 CreateGroundApronEndpoint(
        Vector3 edgeVertex,
        Vector2 outwardDirection,
        float edgeGroundHeight,
        Func<Vector3, float> groundHeightAt)
    {
        var rise = Math.Max(0.0f, edgeVertex.Y - edgeGroundHeight);
        var reach = Math.Clamp(
            rise * ApronReachPerMetreOfRise,
            MinimumApronReachMetres,
            MaximumApronReachMetres);
        var groundPoint = edgeVertex + new Vector3(
            outwardDirection.X * reach,
            0.0f,
            outwardDirection.Y * reach);
        // Finish below the local implicit plane instead of exactly on it. Equal-depth endpoints
        // can expose a one-pixel horizon crack after projection and depth quantisation.
        return new Vector3(
            groundPoint.X,
            groundHeightAt(groundPoint) - GroundOverlapMetres,
            groundPoint.Z);
    }
}
