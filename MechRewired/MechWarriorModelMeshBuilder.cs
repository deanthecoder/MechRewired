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
    private const float MechSurfaceMetallic = 0.55f;
    private const float MechSurfaceRoughness = 0.58f;

    /// <summary>
    /// Builds a flat-shaded render mesh from one decoded WTB model.
    /// </summary>
    public static ArrayMesh Build(
        MechWarriorModel model,
        MechWarriorPalette palette,
        MechWarriorLuminosityTable luminosityTable,
        int illuminationLevel) =>
        Build(
            model,
            palette,
            luminosityTable,
            illuminationLevel,
            new Dictionary<byte, MechWarriorIndexedImage>());

    /// <summary>
    /// Builds a WTB mesh, splitting indexed textured materials from its remaining flat-shaded polygons.
    /// </summary>
    public static ArrayMesh Build(
        MechWarriorModel model,
        MechWarriorPalette palette,
        MechWarriorLuminosityTable luminosityTable,
        int illuminationLevel,
        IReadOnlyDictionary<byte, MechWarriorIndexedImage> materialImages)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(luminosityTable);
        ArgumentNullException.ThrowIfNull(materialImages);

        var mesh = new ArrayMesh();
        var flatPolygons = model.Polygons
            .Where(polygon => !materialImages.ContainsKey(polygon.MaterialIndex))
            .ToArray();
        if (flatPolygons.Length > 0)
        {
            using var surfaceTool = BeginTriangles();
            foreach (var polygon in flatPolygons)
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

            CommitSurface(surfaceTool, mesh, new StandardMaterial3D
            {
                AlbedoColor = Colors.White,
                Metallic = 0.0f,
                Roughness = 0.9f,
                VertexColorUseAsAlbedo = true
            });
        }

        foreach (var materialGroup in model.Polygons
                     .Where(polygon => materialImages.ContainsKey(polygon.MaterialIndex))
                     .GroupBy(polygon => polygon.MaterialIndex))
        {
            var indexedImage = materialImages[materialGroup.Key];
            var textureCoordinateScale = new Vector2(
                1.0f / indexedImage.Width,
                1.0f / indexedImage.Height);
            using var surfaceTool = BeginTriangles();
            foreach (var polygon in materialGroup)
            {
                for (var triangleIndex = 1; triangleIndex < polygon.VertexIndices.Count - 1; triangleIndex++)
                {
                    AddTexturedVertex(surfaceTool, model.Vertices[polygon.VertexIndices[0]], textureCoordinateScale);
                    AddTexturedVertex(surfaceTool, model.Vertices[polygon.VertexIndices[triangleIndex + 1]], textureCoordinateScale);
                    AddTexturedVertex(surfaceTool, model.Vertices[polygon.VertexIndices[triangleIndex]], textureCoordinateScale);
                }
            }

            CommitSurface(surfaceTool, mesh, new StandardMaterial3D
            {
                AlbedoTexture = BuildTexture(indexedImage, palette, luminosityTable, illuminationLevel),
                Metallic = 0.0f,
                Roughness = 0.9f,
                Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest
            });
        }

        if (mesh.GetSurfaceCount() == 0)
        {
            throw new InvalidOperationException("Godot did not create a model mesh.");
        }

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

    /// <summary>
    /// Applies a restrained painted-metal finish to an independently-built mech mesh.
    /// </summary>
    /// <remarks>
    /// Mech meshes are not cached with scenery, so their PBR material instances can be safely tuned
    /// without making the entire battlefield reflective.
    /// </remarks>
    public static void ApplyMechSurfaceFinish(ArrayMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        for (var surfaceIndex = 0; surfaceIndex < mesh.GetSurfaceCount(); surfaceIndex++)
        {
            if (mesh.SurfaceGetMaterial(surfaceIndex) is not StandardMaterial3D material)
            {
                continue;
            }

            material.Metallic = MechSurfaceMetallic;
            material.Roughness = MechSurfaceRoughness;
        }
    }

    private static void AddVertex(SurfaceTool surfaceTool, MechWarriorModelVertex vertex, Rgb color)
    {
        surfaceTool.SetColor(new Color(color.R / 255.0f, color.G / 255.0f, color.B / 255.0f));
        surfaceTool.SetUV(new Vector2(vertex.TextureCoordinate.X, vertex.TextureCoordinate.Y));
        AddPosition(surfaceTool, vertex);
    }

    private static void AddTexturedVertex(
        SurfaceTool surfaceTool,
        MechWarriorModelVertex vertex,
        Vector2 textureCoordinateScale)
    {
        surfaceTool.SetUV(new Vector2(
            vertex.TextureCoordinate.X * textureCoordinateScale.X,
            vertex.TextureCoordinate.Y * textureCoordinateScale.Y));
        AddPosition(surfaceTool, vertex);
    }

    private static SurfaceTool BeginTriangles()
    {
        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);
        surfaceTool.SetSmoothGroup(uint.MaxValue);
        return surfaceTool;
    }

    private static void CommitSurface(SurfaceTool surfaceTool, ArrayMesh mesh, Godot.Material material)
    {
        surfaceTool.GenerateNormals();
        if (surfaceTool.Commit(mesh) == null)
        {
            throw new InvalidOperationException("Godot did not create a model mesh surface.");
        }

        mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
    }

    private static ImageTexture BuildTexture(
        MechWarriorIndexedImage indexedImage,
        MechWarriorPalette palette,
        MechWarriorLuminosityTable luminosityTable,
        int illuminationLevel)
    {
        using var image = Image.CreateEmpty(
            indexedImage.Width,
            indexedImage.Height,
            false,
            Image.Format.Rgba8);
        for (var y = 0; y < indexedImage.Height; y++)
        {
            for (var x = 0; x < indexedImage.Width; x++)
            {
                // MW2 XEL scanlines are stored bottom-to-top. Preserve the original
                // orientation before the material samples the source UV coordinates.
                var paletteIndex = indexedImage.GetPixel(x, indexedImage.Height - y - 1);
                if (paletteIndex == byte.MaxValue)
                {
                    image.SetPixel(x, y, Colors.Transparent);
                    continue;
                }

                var color = palette[luminosityTable.GetPaletteIndex(paletteIndex, illuminationLevel)];
                image.SetPixel(x, y, new Color(
                    color.R / 255.0f,
                    color.G / 255.0f,
                    color.B / 255.0f));
            }
        }

        return ImageTexture.CreateFromImage(image);
    }

    private static void AddPosition(SurfaceTool surfaceTool, MechWarriorModelVertex vertex)
    {
        surfaceTool.AddVertex(
            MechWarriorCoordinateSystem.ToGodotPosition(vertex.Position) * SourceUnitScale);
    }
}
