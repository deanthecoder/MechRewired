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
/// Closes raised exterior terrain edges down to the implicit ground plane.
/// </summary>
/// <remarks>
/// MW2 terrain WTBs commonly have hidden floor sealing faces. Once those faces are excluded and
/// the remaining upward surface is smoothed, an exterior hill edge can sit above the shared floor.
/// This closes that resultant opening regardless of whether it came from source data or derivation.
/// </remarks>
public static class TerrainMeshBoundarySealer
{
    private const float MinimumClearanceMetres = 0.02f;
    public const float GroundOverlapMetres = 0.50f;

    /// <summary>Builds outward-facing skirt triangles for every elevated exterior edge.</summary>
    public static IReadOnlyList<TerrainSourceTriangle> BuildSkirts(
        DerivedTerrainMesh terrain,
        float groundHeight) =>
        BuildSkirts(terrain, _ => groundHeight);

    /// <summary>Builds skirts against a varying ground-height field.</summary>
    public static IReadOnlyList<TerrainSourceTriangle> BuildSkirts(
        DerivedTerrainMesh terrain,
        Func<Vector3, float> groundHeightAt)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(groundHeightAt);
        var skirts = new List<TerrainSourceTriangle>();
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

            // Finish below the implicit plane instead of exactly on it. Equal-depth endpoints can
            // expose a one-pixel horizon crack after projection and depth quantisation, especially
            // along PINK's long, low mountain boundaries.
            var groundFirst = new Vector3(
                first.X,
                firstGroundHeight - GroundOverlapMetres,
                first.Z);
            var groundSecond = new Vector3(
                second.X,
                secondGroundHeight - GroundOverlapMetres,
                second.Z);
            // The terrain lies to the right of its directed boundary edge. Reverse the vertical
            // face winding so it points outward and remains visible from the surrounding floor.
            skirts.Add(new TerrainSourceTriangle(first, groundFirst, second));
            skirts.Add(new TerrainSourceTriangle(second, groundFirst, groundSecond));
        }

        return skirts.AsReadOnly();
    }
}
