// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Collections;
using Godot;
using MechRewired.Simulation;
using NumericsVector2 = System.Numerics.Vector2;

namespace MechRewired;

/// <summary>Indexes explicitly immutable terrain while reading all moving scenery from the live list.</summary>
/// <remarks>Construct after scene-list structural edits. The supplied terrain records must never move;
/// all nonterrain slots are read afresh, including aircraft and authored paths. If list length changes,
/// queries safely fall back to the current full list.</remarks>
public sealed class TerrainRayIndex : IReadOnlyList<DebugTriangle>
{
    private readonly IReadOnlyList<DebugTriangle> m_scene;
    private readonly DebugTriangle[] m_terrain;
    private readonly int[] m_dynamicIndices;
    private readonly int m_initialCount;
    private readonly TerrainRayBoundsIndex m_index;

    public TerrainRayIndex(IReadOnlyList<DebugTriangle> scene, IReadOnlySet<int> dynamicTriangleIndices)
    {
        m_scene = scene;
        m_initialCount = scene.Count;
        var terrain = new List<DebugTriangle>();
        var dynamicIndices = new List<int>();
        for (var i = 0; i < scene.Count; i++)
        {
            if (!dynamicTriangleIndices.Contains(i) &&
                (DerivedTerrainSurfaceBuilder.IsAuthoredTerrain(scene[i]) || scene[i].ResourcePath == "IMPLICIT/GROUND" ||
                 scene[i].ResourcePath.StartsWith("POLY/T_", StringComparison.Ordinal)))
            {
                terrain.Add(scene[i]);
            }
            else
            {
                dynamicIndices.Add(i);
            }
        }
        m_terrain = terrain.ToArray();
        m_dynamicIndices = dynamicIndices.ToArray();
        m_index = new TerrainRayBoundsIndex(m_terrain.Select(triangle =>
        {
            var minimum = triangle.A.Min(triangle.B).Min(triangle.C);
            var maximum = triangle.A.Max(triangle.B).Max(triangle.C);
            return new TerrainRayBoundsIndex.Bounds(new NumericsVector2(minimum.X - 0.00001f, minimum.Z - 0.00001f),
                new NumericsVector2(maximum.X + 0.00001f, maximum.Z + 0.00001f));
        }).ToArray());
    }

    public bool VisitCandidates(Vector3 origin, Vector3 direction, float maximumDistance, Func<DebugTriangle, bool> visitor)
    {
        if (m_scene.Count != m_initialCount)
        {
            foreach (var triangle in m_scene)
                if (visitor(triangle)) return true;
            return false;
        }
        if (m_index.Visit(new NumericsVector2(origin.X, origin.Z), new NumericsVector2(direction.X, direction.Z), maximumDistance,
                index => visitor(m_terrain[index]))) return true;
        foreach (var index in m_dynamicIndices)
            if (visitor(m_scene[index])) return true;
        return false;
    }

    public DebugTriangle this[int index] => m_scene[index];
    public int Count => m_scene.Count;
    public IEnumerator<DebugTriangle> GetEnumerator() => m_scene.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
