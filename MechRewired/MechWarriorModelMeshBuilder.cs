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
    private const float MechDecalRoughness = 0.72f;
    private const float StructureSurfaceMetallic = 0.28f;
    private const float StructureSurfaceRoughness = 0.78f;
    private static readonly Dictionary<IndexedTextureCacheKey, SurfaceTextureSet> s_indexedTextureCache = new();
    private static SurfaceTextureSet s_paintedSurfaceMaps;
    private static SurfaceTextureSet s_structureSurfaceMaps;

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
        IReadOnlyDictionary<byte, MechWarriorIndexedImage> materialImages,
        int triangleSubdivisions = 1,
        bool preserveTexturePalette = false)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(luminosityTable);
        ArgumentNullException.ThrowIfNull(materialImages);
        if (triangleSubdivisions < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(triangleSubdivisions));
        }
        var smoothNormals = triangleSubdivisions > 1
            ? CalculateTerrainSmoothNormals(model)
            : null;
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
                    var firstIndex = polygon.VertexIndices[0];
                    var secondIndex = polygon.VertexIndices[triangleIndex + 1];
                    var thirdIndex = polygon.VertexIndices[triangleIndex];
                    AddSubdividedTriangle(
                        surfaceTool,
                        model.Vertices[firstIndex],
                        model.Vertices[secondIndex],
                        model.Vertices[thirdIndex],
                        triangleSubdivisions,
                        litColor,
                        Vector2.One,
                        smoothNormals?[firstIndex],
                        smoothNormals?[secondIndex],
                        smoothNormals?[thirdIndex]);
                }
            }

            CommitSurface(
                surfaceTool,
                mesh,
                new StandardMaterial3D
                {
                    AlbedoColor = Colors.White,
                    Metallic = 0.0f,
                    Roughness = 0.9f,
                    VertexColorUseAsAlbedo = true
                },
                generateNormals: smoothNormals == null,
                generateTangents: true);
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
                    var firstIndex = polygon.VertexIndices[0];
                    var secondIndex = polygon.VertexIndices[triangleIndex + 1];
                    var thirdIndex = polygon.VertexIndices[triangleIndex];
                    AddSubdividedTriangle(
                        surfaceTool,
                        model.Vertices[firstIndex],
                        model.Vertices[secondIndex],
                        model.Vertices[thirdIndex],
                        triangleSubdivisions,
                        null,
                        textureCoordinateScale,
                        smoothNormals?[firstIndex],
                        smoothNormals?[secondIndex],
                        smoothNormals?[thirdIndex]);
                }
            }

            var textures = BuildMaterialTextures(
                indexedImage,
                palette,
                luminosityTable,
                illuminationLevel,
                preserveTexturePalette);
            CommitSurface(
                surfaceTool,
                mesh,
                new StandardMaterial3D
                {
                    AlbedoTexture = textures.Albedo,
                    NormalEnabled = true,
                    NormalTexture = textures.Normal,
                    NormalScale = 0.28f,
                    RoughnessTexture = textures.Roughness,
                    MetallicTexture = textures.Metallic,
                    Metallic = 0.0f,
                    Roughness = 0.9f,
                    Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
                    TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                    TextureRepeat = true
                },
                generateNormals: smoothNormals == null,
                generateTangents: true);
        }

        if (mesh.GetSurfaceCount() == 0)
        {
            throw new InvalidOperationException("Godot did not create a model mesh.");
        }

        return mesh;
    }

    /// <summary>
    /// Builds an untextured diagnostic mesh using source palette indices without applying an
    /// MW2 luminosity-table level. This makes the baked LUMA contribution directly comparable
    /// with the palette art from the same camera and geometry.
    /// </summary>
    public static ArrayMesh BuildRawPalette(
        MechWarriorModel model,
        MechWarriorPalette palette)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(palette);

        var mesh = new ArrayMesh();
        using var surfaceTool = BeginTriangles();
        foreach (var polygon in model.Polygons)
        {
            var color = palette[polygon.PaletteIndex];
            for (var triangleIndex = 1; triangleIndex < polygon.VertexIndices.Count - 1; triangleIndex++)
            {
                AddVertex(surfaceTool, model.Vertices[polygon.VertexIndices[0]], color);
                AddVertex(surfaceTool, model.Vertices[polygon.VertexIndices[triangleIndex + 1]], color);
                AddVertex(surfaceTool, model.Vertices[polygon.VertexIndices[triangleIndex]], color);
            }
        }

        CommitSurface(surfaceTool, mesh, new StandardMaterial3D
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
            ApplySurfaceMaps(material, GetPaintedSurfaceMaps(), 0.24f, false);
        }
    }

    /// <summary>
    /// Keeps painted insignia non-metallic so its source colours remain readable over the hull.
    /// </summary>
    public static void ApplyMechDecalFinish(ArrayMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        for (var surfaceIndex = 0; surfaceIndex < mesh.GetSurfaceCount(); surfaceIndex++)
        {
            if (mesh.SurfaceGetMaterial(surfaceIndex) is not StandardMaterial3D material)
            {
                continue;
            }

            material.Metallic = 0.0f;
            material.Roughness = MechDecalRoughness;
            // Preserve original insignia colours and avoid covering them with generic plating.
            material.NormalScale = 0.12f;
        }
    }

    /// <summary>
    /// Applies a subtle tiled painted-metal response to palette-coloured scenery where MW2 did
    /// not provide a matching indexed material. The vertex-colour palette remains the albedo.
    /// </summary>
    public static void ApplyStructureSurfaceFinish(ArrayMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        for (var surfaceIndex = 0; surfaceIndex < mesh.GetSurfaceCount(); surfaceIndex++)
        {
            if (mesh.SurfaceGetMaterial(surfaceIndex) is not StandardMaterial3D material)
            {
                continue;
            }

            material.Metallic = StructureSurfaceMetallic;
            material.Roughness = StructureSurfaceRoughness;
            // Textured surfaces keep their authored UV orientation. Palette-only WTB surfaces
            // have no usable source texture projection, so use the restrained fallback triplanar map.
            ApplySurfaceMaps(
                material,
                GetStructureSurfaceMaps(),
                0.18f,
                material.AlbedoTexture == null);
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

    private static void AddSubdividedTriangle(
        SurfaceTool surfaceTool,
        MechWarriorModelVertex first,
        MechWarriorModelVertex second,
        MechWarriorModelVertex third,
        int subdivisions,
        Rgb color,
        Vector2 textureCoordinateScale,
        Vector3? firstNormal = null,
        Vector3? secondNormal = null,
        Vector3? thirdNormal = null)
    {
        for (var row = 0; row < subdivisions; row++)
        {
            for (var column = 0; column < subdivisions - row; column++)
            {
                AddInterpolatedVertex(
                    surfaceTool, first, second, third, row, column, subdivisions,
                    color, textureCoordinateScale, firstNormal, secondNormal, thirdNormal);
                AddInterpolatedVertex(
                    surfaceTool, first, second, third, row + 1, column, subdivisions,
                    color, textureCoordinateScale, firstNormal, secondNormal, thirdNormal);
                AddInterpolatedVertex(
                    surfaceTool, first, second, third, row, column + 1, subdivisions,
                    color, textureCoordinateScale, firstNormal, secondNormal, thirdNormal);
                if (column == subdivisions - row - 1)
                {
                    continue;
                }

                AddInterpolatedVertex(
                    surfaceTool, first, second, third, row + 1, column, subdivisions,
                    color, textureCoordinateScale, firstNormal, secondNormal, thirdNormal);
                AddInterpolatedVertex(
                    surfaceTool, first, second, third, row + 1, column + 1, subdivisions,
                    color, textureCoordinateScale, firstNormal, secondNormal, thirdNormal);
                AddInterpolatedVertex(
                    surfaceTool, first, second, third, row, column + 1, subdivisions,
                    color, textureCoordinateScale, firstNormal, secondNormal, thirdNormal);
            }
        }
    }

    private static void AddInterpolatedVertex(
        SurfaceTool surfaceTool,
        MechWarriorModelVertex first,
        MechWarriorModelVertex second,
        MechWarriorModelVertex third,
        int secondWeight,
        int thirdWeight,
        int subdivisions,
        Rgb color,
        Vector2 textureCoordinateScale,
        Vector3? firstNormal,
        Vector3? secondNormal,
        Vector3? thirdNormal)
    {
        var secondFactor = secondWeight / (float)subdivisions;
        var thirdFactor = thirdWeight / (float)subdivisions;
        var firstFactor = 1.0f - secondFactor - thirdFactor;
        if (color != null)
        {
            surfaceTool.SetColor(new Color(color.R / 255.0f, color.G / 255.0f, color.B / 255.0f));
        }

        surfaceTool.SetUV(new Vector2(
            first.TextureCoordinate.X * firstFactor +
            second.TextureCoordinate.X * secondFactor +
            third.TextureCoordinate.X * thirdFactor,
            first.TextureCoordinate.Y * firstFactor +
            second.TextureCoordinate.Y * secondFactor +
            third.TextureCoordinate.Y * thirdFactor) * textureCoordinateScale);
        if (firstNormal.HasValue && secondNormal.HasValue && thirdNormal.HasValue)
        {
            surfaceTool.SetNormal((
                firstNormal.Value * firstFactor +
                secondNormal.Value * secondFactor +
                thirdNormal.Value * thirdFactor).Normalized());
        }
        var position =
            MechWarriorCoordinateSystem.ToGodotPosition(first.Position) * firstFactor +
            MechWarriorCoordinateSystem.ToGodotPosition(second.Position) * secondFactor +
            MechWarriorCoordinateSystem.ToGodotPosition(third.Position) * thirdFactor;
        surfaceTool.AddVertex(position * SourceUnitScale);
    }

    private static SurfaceTool BeginTriangles()
    {
        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);
        surfaceTool.SetSmoothGroup(uint.MaxValue);
        return surfaceTool;
    }

    private static void CommitSurface(
        SurfaceTool surfaceTool,
        ArrayMesh mesh,
        Godot.Material material,
        bool generateNormals = true,
        bool generateTangents = false)
    {
        if (generateNormals)
        {
            surfaceTool.GenerateNormals();
        }
        if (generateTangents)
        {
            surfaceTool.GenerateTangents();
        }
        if (surfaceTool.Commit(mesh) == null)
        {
            throw new InvalidOperationException("Godot did not create a model mesh surface.");
        }

        mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
    }

    private static Vector3[] CalculateTerrainSmoothNormals(MechWarriorModel model)
    {
        var visibleNormals = new Vector3[model.Vertices.Count];
        var fallbackNormals = new Vector3[model.Vertices.Count];
        foreach (var polygon in model.Polygons)
        {
            for (var triangleIndex = 1; triangleIndex < polygon.VertexIndices.Count - 1; triangleIndex++)
            {
                var firstIndex = polygon.VertexIndices[0];
                var secondIndex = polygon.VertexIndices[triangleIndex + 1];
                var thirdIndex = polygon.VertexIndices[triangleIndex];
                var first = MechWarriorCoordinateSystem.ToGodotPosition(model.Vertices[firstIndex].Position);
                var second = MechWarriorCoordinateSystem.ToGodotPosition(model.Vertices[secondIndex].Position);
                var third = MechWarriorCoordinateSystem.ToGodotPosition(model.Vertices[thirdIndex].Position);
                // Godot's front faces use clockwise winding. Reverse the conventional CCW
                // cross product so supplied smoothing normals point out of the rendered land.
                var weightedNormal = (third - first).Cross(second - first);
                foreach (var vertexIndex in new[] { firstIndex, secondIndex, thirdIndex })
                {
                    fallbackNormals[vertexIndex] += weightedNormal;
                    // Terrain WTBs contain hidden sealing faces around Y=0. Excluding their
                    // downward/vertical normals keeps the visible land smoothly illuminated.
                    if (weightedNormal.Y > 0.0001f)
                    {
                        visibleNormals[vertexIndex] += weightedNormal;
                    }
                }
            }
        }

        for (var index = 0; index < visibleNormals.Length; index++)
        {
            var normal = visibleNormals[index].IsZeroApprox()
                ? fallbackNormals[index]
                : visibleNormals[index];
            visibleNormals[index] = normal.IsZeroApprox() ? Vector3.Up : normal.Normalized();
        }

        return visibleNormals;
    }

    private static SurfaceTextureSet BuildMaterialTextures(
        MechWarriorIndexedImage indexedImage,
        MechWarriorPalette palette,
        MechWarriorLuminosityTable luminosityTable,
        int illuminationLevel,
        bool preservePalette)
    {
        var key = new IndexedTextureCacheKey(
            indexedImage,
            palette,
            luminosityTable,
            illuminationLevel,
            preservePalette);
        if (s_indexedTextureCache.TryGetValue(key, out var cachedTextures))
        {
            return cachedTextures;
        }

        using var albedo = Image.CreateEmpty(
            indexedImage.Width,
            indexedImage.Height,
            false,
            Image.Format.Rgba8);
        using var normal = Image.CreateEmpty(
            indexedImage.Width,
            indexedImage.Height,
            false,
            Image.Format.Rgba8);
        using var roughness = Image.CreateEmpty(
            indexedImage.Width,
            indexedImage.Height,
            false,
            Image.Format.Rgba8);
        using var metallic = Image.CreateEmpty(
            indexedImage.Width,
            indexedImage.Height,
            false,
            Image.Format.Rgba8);
        var luminance = new float[indexedImage.Width * indexedImage.Height];
        for (var y = 0; y < indexedImage.Height; y++)
        {
            for (var x = 0; x < indexedImage.Width; x++)
            {
                // MW2 XEL scanlines are stored bottom-to-top. Preserve the original
                // orientation before the material samples the source UV coordinates.
                var paletteIndex = indexedImage.GetPixel(x, indexedImage.Height - y - 1);
                if (paletteIndex == byte.MaxValue)
                {
                    albedo.SetPixel(x, y, Colors.Transparent);
                    normal.SetPixel(x, y, new Color(0.5f, 0.5f, 1.0f));
                    roughness.SetPixel(x, y, Colors.White);
                    metallic.SetPixel(x, y, Colors.Black);
                    continue;
                }

                var color = palette[preservePalette
                    ? paletteIndex
                    : luminosityTable.GetPaletteIndex(paletteIndex, illuminationLevel)];
                var resolvedColor = new Color(
                    color.R / 255.0f,
                    color.G / 255.0f,
                    color.B / 255.0f);
                albedo.SetPixel(x, y, resolvedColor);
                luminance[y * indexedImage.Width + x] =
                    resolvedColor.R * 0.2126f + resolvedColor.G * 0.7152f + resolvedColor.B * 0.0722f;
            }
        }

        for (var y = 0; y < indexedImage.Height; y++)
        {
            for (var x = 0; x < indexedImage.Width; x++)
            {
                var pixel = y * indexedImage.Width + x;
                if (indexedImage.GetPixel(x, indexedImage.Height - y - 1) == byte.MaxValue)
                {
                    continue;
                }

                var centre = luminance[pixel];
                var left = SampleLuminance(luminance, indexedImage.Width, indexedImage.Height, x - 1, y, centre);
                var right = SampleLuminance(luminance, indexedImage.Width, indexedImage.Height, x + 1, y, centre);
                var above = SampleLuminance(luminance, indexedImage.Width, indexedImage.Height, x, y - 1, centre);
                var below = SampleLuminance(luminance, indexedImage.Width, indexedImage.Height, x, y + 1, centre);
                // Keep detail subordinate to the original low-resolution palette art.
                normal.SetPixel(x, y, new Color(
                    Mathf.Clamp(0.5f + (left - right) * 0.18f, 0.0f, 1.0f),
                    Mathf.Clamp(0.5f + (above - below) * 0.18f, 0.0f, 1.0f),
                    1.0f));
                var edge = MathF.Abs(left - right) + MathF.Abs(above - below);
                var roughnessValue = Mathf.Clamp(0.88f + (1.0f - centre) * 0.08f + edge * 0.05f, 0.0f, 1.0f);
                var metallicValue = Mathf.Clamp(0.76f + centre * 0.10f - edge * 0.08f, 0.0f, 1.0f);
                roughness.SetPixel(x, y, new Color(roughnessValue, roughnessValue, roughnessValue));
                metallic.SetPixel(x, y, new Color(metallicValue, metallicValue, metallicValue));
            }
        }

        var textures = new SurfaceTextureSet(
            ImageTexture.CreateFromImage(albedo),
            ImageTexture.CreateFromImage(normal),
            ImageTexture.CreateFromImage(roughness),
            ImageTexture.CreateFromImage(metallic));
        s_indexedTextureCache.Add(key, textures);
        return textures;
    }

    private static float SampleLuminance(
        IReadOnlyList<float> luminance,
        int width,
        int height,
        int x,
        int y,
        float fallback)
    {
        if (x < 0 || x >= width || y < 0 || y >= height)
        {
            return fallback;
        }

        var sample = luminance[y * width + x];
        return sample == 0.0f ? fallback : sample;
    }

    private static void ApplySurfaceMaps(
        StandardMaterial3D material,
        SurfaceTextureSet maps,
        float normalScale,
        bool triplanar)
    {
        material.NormalEnabled = true;
        material.NormalTexture ??= maps.Normal;
        material.RoughnessTexture ??= maps.Roughness;
        material.MetallicTexture ??= maps.Metallic;
        material.NormalScale = normalScale;
        material.TextureRepeat = true;
        if (!triplanar)
        {
            return;
        }

        material.Uv1Triplanar = true;
        material.Uv1WorldTriplanar = true;
        material.Uv1TriplanarSharpness = 10.0f;
        material.Uv1Scale = Vector3.One * 0.32f;
    }

    private static SurfaceTextureSet GetPaintedSurfaceMaps() =>
        s_paintedSurfaceMaps ??= CreateSurfaceMaps(0.90f, 0.78f);

    private static SurfaceTextureSet GetStructureSurfaceMaps() =>
        s_structureSurfaceMaps ??= CreateSurfaceMaps(0.88f, 0.72f);

    private static SurfaceTextureSet CreateSurfaceMaps(float baseRoughness, float baseMetallic)
    {
        const int size = 16;
        using var normal = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        using var roughness = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        using var metallic = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                // Palette-only structures have no original texture that can justify invented
                // panel lines. Keep their fallback maps almost flat, with only non-directional
                // microvariation to keep broad lit surfaces from looking perfectly synthetic.
                var noise = SurfaceNoise(x, y);
                var normalX = (SurfaceNoise((x + size - 1) % size, y) -
                               SurfaceNoise((x + 1) % size, y)) * 0.012f;
                var normalY = (SurfaceNoise(x, (y + size - 1) % size) -
                               SurfaceNoise(x, (y + 1) % size)) * 0.012f;
                normal.SetPixel(x, y, new Color(0.5f + normalX, 0.5f + normalY, 1.0f));
                var roughnessValue = Mathf.Clamp(baseRoughness + noise * 0.018f, 0.0f, 1.0f);
                var metallicValue = Mathf.Clamp(baseMetallic - noise * 0.012f, 0.0f, 1.0f);
                roughness.SetPixel(x, y, new Color(roughnessValue, roughnessValue, roughnessValue));
                metallic.SetPixel(x, y, new Color(metallicValue, metallicValue, metallicValue));
            }
        }

        return new SurfaceTextureSet(
            null,
            ImageTexture.CreateFromImage(normal),
            ImageTexture.CreateFromImage(roughness),
            ImageTexture.CreateFromImage(metallic));
    }

    private static float SurfaceNoise(int x, int y)
    {
        uint value = (uint)(x * 73_856_093 ^ y * 19_349_663);
        value = (value ^ value >> 13) * 1_274_126_177;
        return (value & 0xff) / 255.0f - 0.5f;
    }

    private static void AddPosition(SurfaceTool surfaceTool, MechWarriorModelVertex vertex)
    {
        surfaceTool.AddVertex(
            MechWarriorCoordinateSystem.ToGodotPosition(vertex.Position) * SourceUnitScale);
    }

    private sealed record SurfaceTextureSet(
        Texture2D Albedo,
        Texture2D Normal,
        Texture2D Roughness,
        Texture2D Metallic);

    private readonly record struct IndexedTextureCacheKey(
        MechWarriorIndexedImage Image,
        MechWarriorPalette Palette,
        MechWarriorLuminosityTable LuminosityTable,
        int IlluminationLevel,
        bool PreservePalette);
}
