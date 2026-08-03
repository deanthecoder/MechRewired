// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.Core;
using Godot;
using MechRewired.Resources;

namespace MechRewired;

/// <summary>
/// Converts decoded MechWarrior 2 model data into a Godot mesh.
/// </summary>
/// <remarks>
/// Polygons are fan-triangulated with DOS palette colors and reversed winding to compensate for the source-axis reflection.
/// Vertices are duplicated so generated normals retain the original flat-shaded appearance.
/// </remarks>
public static class MechWarriorModelMeshBuilder
{
    public const float SourceUnitScale = 0.01f;

    /// <summary>
    /// Builds a flat-shaded render mesh from one decoded WTB model.
    /// </summary>
    public static ArrayMesh Build(
        MechWarriorModel model,
        MechWarriorPalette palette,
        MechWarriorLuminosityTable luminosityTable,
        int illuminationLevel)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(luminosityTable);

        using var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);
        surfaceTool.SetSmoothGroup(uint.MaxValue);
        foreach (var polygon in model.Polygons)
        {
            var litColor = palette[luminosityTable.GetPaletteIndex(
                polygon.PaletteIndex,
                illuminationLevel)];
            for (var triangleIndex = 1; triangleIndex < polygon.VertexIndices.Count - 1; triangleIndex++)
            {
                AddVertex(surfaceTool, model.Vertices[polygon.VertexIndices[0]], litColor);
                AddVertex(surfaceTool, model.Vertices[polygon.VertexIndices[triangleIndex + 1]], litColor);
                AddVertex(surfaceTool, model.Vertices[polygon.VertexIndices[triangleIndex]], litColor);
            }
        }

        surfaceTool.GenerateNormals();
        var mesh = surfaceTool.Commit() ?? throw new InvalidOperationException("Godot did not create a model mesh.");
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            Metallic = 0.0f,
            Roughness = 0.9f,
            VertexColorUseAsAlbedo = true
        });
        return mesh;
    }

    /// <summary>
    /// Builds an unshaded diagnostic mesh containing every source polygon edge.
    /// </summary>
    public static ArrayMesh BuildWireframe(MechWarriorModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        using var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Lines);
        foreach (var polygon in model.Polygons)
        {
            for (var vertexIndex = 0; vertexIndex < polygon.VertexIndices.Count; vertexIndex++)
            {
                var nextVertexIndex = (vertexIndex + 1) % polygon.VertexIndices.Count;
                AddPosition(surfaceTool, model.Vertices[polygon.VertexIndices[vertexIndex]]);
                AddPosition(surfaceTool, model.Vertices[polygon.VertexIndices[nextVertexIndex]]);
            }
        }

        var mesh = surfaceTool.Commit() ?? throw new InvalidOperationException("Godot did not create a wireframe mesh.");
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            AlbedoColor = new Color("55ffff"),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        });
        return mesh;
    }

    private static void AddVertex(SurfaceTool surfaceTool, MechWarriorModelVertex vertex, Rgb color)
    {
        surfaceTool.SetColor(new Color(color.R / 255.0f, color.G / 255.0f, color.B / 255.0f));
        surfaceTool.SetUV(new Vector2(vertex.TextureCoordinate.X, vertex.TextureCoordinate.Y));
        AddPosition(surfaceTool, vertex);
    }

    private static void AddPosition(SurfaceTool surfaceTool, MechWarriorModelVertex vertex)
    {
        surfaceTool.AddVertex(
            MechWarriorCoordinateSystem.ToGodotPosition(vertex.Position) * SourceUnitScale);
    }
}
