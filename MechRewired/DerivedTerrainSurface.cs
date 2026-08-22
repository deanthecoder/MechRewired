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
using NumericsVector3 = System.Numerics.Vector3;

namespace MechRewired;

/// <summary>
/// Runtime rendering and collision representations sampled from one smooth terrain control surface.
/// </summary>
public sealed record DerivedTerrainSurface(
    ArrayMesh RenderMesh,
    IReadOnlyList<DebugTriangle> CollisionTriangles,
    int SourceTriangleCount,
    int RenderTriangleCount);

public static class DerivedTerrainSurfaceBuilder
{
    // Curved interpolation needs far fewer samples than the earlier linear subdivision pass:
    // eight segments per source edge is already 64 render triangles per curved source triangle.
    public const int RenderSubdivisions = 8;
    public const int CollisionSubdivisions = 3;
    public const float SmoothingAngleDegrees = 30.0f;
    public const float SmoothingStrength = 0.70f;

    private const string DerivedResourcePath = "POLY/T_DERIVED.WTB";

    public static DerivedTerrainSurface Build(IEnumerable<DebugTriangle> sceneTriangles)
    {
        ArgumentNullException.ThrowIfNull(sceneTriangles);
        var source = sceneTriangles
            .Where(IsAuthoredTerrain)
            .Select(triangle => new TerrainSourceTriangle(
                ToNumerics(triangle.A),
                ToNumerics(triangle.B),
                ToNumerics(triangle.C)))
            .ToArray();
        var render = TerrainMeshDeriver.Build(
            source,
            RenderSubdivisions,
            SmoothingAngleDegrees,
            SmoothingStrength,
            ApplyMacroRelief);
        var collision = TerrainMeshDeriver.Build(
            source,
            CollisionSubdivisions,
            SmoothingAngleDegrees,
            SmoothingStrength,
            ApplyMacroRelief);

        return new DerivedTerrainSurface(
            BuildGodotMesh(render),
            BuildDebugTriangles(collision),
            source.Length,
            render.TriangleCount);
    }

    public static bool IsAuthoredTerrain(DebugTriangle triangle) =>
        triangle.ResourcePath.StartsWith("POLY/T_", StringComparison.Ordinal) &&
        !string.Equals(triangle.ResourcePath, DerivedResourcePath, StringComparison.Ordinal);

    private static ArrayMesh BuildGodotMesh(DerivedTerrainMesh derived)
    {
        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);
        for (var index = 0; index < derived.Vertices.Count; index++)
        {
            surfaceTool.SetNormal(ToGodot(derived.Normals[index]));
            surfaceTool.SetColor(TerrainSurfaceMaterial.DesertBaseColor);
            surfaceTool.AddVertex(ToGodot(derived.Vertices[index]));
        }

        for (var index = 0; index < derived.Indices.Count; index += 3)
        {
            // The derivation uses conventional upward-facing winding. Godot considers clockwise
            // triangles front-facing, so reverse each index triplet while retaining its supplied normal.
            surfaceTool.AddIndex(derived.Indices[index]);
            surfaceTool.AddIndex(derived.Indices[index + 2]);
            surfaceTool.AddIndex(derived.Indices[index + 1]);
        }

        var mesh = new ArrayMesh();
        if (surfaceTool.Commit(mesh) == null)
        {
            throw new InvalidOperationException("Godot did not create the derived terrain mesh.");
        }

        return mesh;
    }

    private static IReadOnlyList<DebugTriangle> BuildDebugTriangles(DerivedTerrainMesh derived)
    {
        var triangles = new DebugTriangle[derived.TriangleCount];
        for (var triangleIndex = 0; triangleIndex < triangles.Length; triangleIndex++)
        {
            var index = triangleIndex * 3;
            triangles[triangleIndex] = new DebugTriangle(
                "DERIVED/TERRAIN",
                DerivedResourcePath,
                -1,
                0,
                triangleIndex,
                ToGodot(derived.Vertices[derived.Indices[index]]),
                ToGodot(derived.Vertices[derived.Indices[index + 1]]),
                ToGodot(derived.Vertices[derived.Indices[index + 2]]));
        }

        return triangles;
    }

    private static NumericsVector3 ApplyMacroRelief(NumericsVector3 position)
    {
        // Keep every zero-height join fixed so the curved hills still meet the implicit floor.
        var heightFade = SmoothStep(0.0f, 4.0f, position.Y);
        if (heightFade <= 0.0f)
        {
            return position;
        }

        var noise = new NumericsVector3(
            LayeredNoise(new NumericsVector3(position.Y, position.Z, 0.0f), 0.011f, 8.2f, -4.7f),
            LayeredNoise(new NumericsVector3(position.X, position.Z, 0.0f), 0.013f, -12.4f, 5.8f),
            LayeredNoise(new NumericsVector3(position.X, position.Y, 0.0f), 0.011f, 3.6f, 14.1f)) -
            new NumericsVector3(0.5f);
        return position + noise *
            (TerrainSurfaceMaterial.MountainMacroReliefMetres *
             TerrainSurfaceMaterial.GeometryDisplacementStrength * 2.0f * heightFade);
    }

    private static float LayeredNoise(
        NumericsVector3 position,
        float scale,
        float offsetX,
        float offsetY)
    {
        var x = position.X * scale + offsetX;
        var y = position.Y * scale + offsetY;
        return ValueNoise(x, y) * 0.62f +
               ValueNoise(x * 2.07f + 17.3f, y * 2.07f - 9.1f) * 0.27f +
               ValueNoise(x * 4.19f - 6.7f, y * 4.19f + 23.4f) * 0.11f;
    }

    private static float ValueNoise(float x, float y)
    {
        var cellX = MathF.Floor(x);
        var cellY = MathF.Floor(y);
        var localX = SmoothInterpolation(x - cellX);
        var localY = SmoothInterpolation(y - cellY);
        return Lerp(
            Lerp(Hash(cellX, cellY), Hash(cellX + 1.0f, cellY), localX),
            Lerp(Hash(cellX, cellY + 1.0f), Hash(cellX + 1.0f, cellY + 1.0f), localX),
            localY);
    }

    private static float Hash(float x, float y)
    {
        var value = MathF.Sin(x * 127.1f + y * 311.7f) * 43758.5453f;
        return value - MathF.Floor(value);
    }

    private static float SmoothInterpolation(float value) => value * value * (3.0f - 2.0f * value);

    private static float SmoothStep(float minimum, float maximum, float value) =>
        SmoothInterpolation(Math.Clamp((value - minimum) / (maximum - minimum), 0.0f, 1.0f));

    private static float Lerp(float first, float second, float amount) =>
        first + (second - first) * amount;

    private static NumericsVector3 ToNumerics(Vector3 value) => new(value.X, value.Y, value.Z);

    private static Vector3 ToGodot(NumericsVector3 value) => new(value.X, value.Y, value.Z);
}
