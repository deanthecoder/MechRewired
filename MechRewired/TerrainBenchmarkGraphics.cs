// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.

using Godot;

namespace MechRewired;

/// <summary>Temporary terrain-only graphics changes for benchmark comparisons.</summary>
/// <remarks>All edits are in-memory and restored by Dispose. Physics meshes are never changed.</remarks>
public sealed class TerrainBenchmarkGraphics : IDisposable
{
    public const float ChunkSizeMetres = 256.0f;

    private readonly List<(ShaderMaterial Material, Shader OriginalShader)> m_materials = [];
    private readonly List<(MeshInstance3D Source, bool WasVisible)> m_sources = [];
    private readonly List<MeshInstance3D> m_chunks = [];
    private bool m_disposed;

    public int ChunkCount { get; private set; }
    public long SourceTriangles { get; private set; }
    public long ChunkTriangles { get; private set; }

    public TerrainBenchmarkGraphics(Node missionRoot, bool chunked, bool vertexLit)
    {
        ArgumentNullException.ThrowIfNull(missionRoot);
        try
        {
            var meshes = Descendants(missionRoot).OfType<MeshInstance3D>()
                .Where(mesh => mesh.IsVisibleInTree() && mesh.Mesh is ArrayMesh &&
                               mesh.CastShadow != GeometryInstance3D.ShadowCastingSetting.ShadowsOnly &&
                               GetTerrainMaterials(mesh).Any())
                .ToArray();
            SourceTriangles = meshes.Sum(mesh => CountTriangles((ArrayMesh)mesh.Mesh));
            if (SourceTriangles == 0)
                throw new InvalidOperationException("No visible terrain found for the benchmark.");

            foreach (var material in meshes.SelectMany(GetTerrainMaterials).Distinct())
            {
                m_materials.Add((material, material.Shader));
                TerrainSurfaceMaterial.SetTriplanarEnabled(material, false);
                if (vertexLit)
                {
                    var uvShader = material.Shader;
                    var code = uvShader.Code
                        .Replace("cull_back", "cull_back, vertex_lighting, specular_disabled", StringComparison.Ordinal)
                        .Replace("cull_disabled", "cull_disabled, vertex_lighting, specular_disabled", StringComparison.Ordinal);
                    material.Shader = new Shader { Code = code };
                }
            }

            if (chunked)
            {
                foreach (var source in meshes) SplitMesh(source);
                if (ChunkTriangles != SourceTriangles)
                    throw new InvalidOperationException($"Terrain chunking changed triangle count ({SourceTriangles} to {ChunkTriangles}).");
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (m_disposed) return;
        m_disposed = true;
        foreach (var (source, wasVisible) in m_sources)
            if (GodotObject.IsInstanceValid(source)) source.Visible = wasVisible;
        foreach (var chunk in m_chunks)
            if (GodotObject.IsInstanceValid(chunk)) chunk.QueueFree();
        foreach (var (material, originalShader) in m_materials)
            if (GodotObject.IsInstanceValid(material)) material.Shader = originalShader;
    }

    private void SplitMesh(MeshInstance3D source)
    {
        var mesh = (ArrayMesh)source.Mesh;
        var perCell = new Dictionary<(int X, int Z), ArrayMesh>();
        for (var surfaceIndex = 0; surfaceIndex < mesh.GetSurfaceCount(); surfaceIndex++)
        {
            var arrays = mesh.SurfaceGetArrays(surfaceIndex);
            var vertices = (Vector3[])arrays[(int)Mesh.ArrayType.Vertex].Obj;
            var normals = GetArray<Vector3>(arrays, Mesh.ArrayType.Normal);
            var colors = GetArray<Color>(arrays, Mesh.ArrayType.Color);
            var uvs = GetArray<Vector2>(arrays, Mesh.ArrayType.TexUV);
            var indices = GetArray<int>(arrays, Mesh.ArrayType.Index);
            if (indices.Length == 0) indices = Enumerable.Range(0, vertices.Length).ToArray();
            if (indices.Length % 3 != 0) throw new InvalidOperationException("Terrain surface index count is not a triangle multiple.");

            var builders = new Dictionary<(int X, int Z), SurfaceBuilder>();
            for (var i = 0; i < indices.Length; i += 3)
            {
                var ia = indices[i];
                var ib = indices[i + 1];
                var ic = indices[i + 2];
                var localCenter = (vertices[ia] + vertices[ib] + vertices[ic]) / 3.0f;
                var center = source.GlobalTransform * localCenter;
                var cell = ((int)Mathf.Floor(center.X / ChunkSizeMetres), (int)Mathf.Floor(center.Z / ChunkSizeMetres));
                if (!builders.TryGetValue(cell, out var builder)) builders[cell] = builder = new SurfaceBuilder();
                builder.Add(ia, vertices, normals, colors, uvs);
                builder.Add(ib, vertices, normals, colors, uvs);
                builder.Add(ic, vertices, normals, colors, uvs);
            }

            foreach (var (cell, builder) in builders)
            {
                if (!perCell.TryGetValue(cell, out var chunk)) perCell[cell] = chunk = new ArrayMesh();
                chunk.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, builder.ToArrays());
                chunk.SurfaceSetMaterial(chunk.GetSurfaceCount() - 1,
                    source.GetSurfaceOverrideMaterial(surfaceIndex) ?? mesh.SurfaceGetMaterial(surfaceIndex));
            }
        }

        var wasVisible = source.Visible;
        m_sources.Add((source, wasVisible));
        foreach (var chunkMesh in perCell.Values)
        {
            var chunk = new MeshInstance3D
            {
                Mesh = chunkMesh,
                MaterialOverride = source.MaterialOverride,
                CastShadow = source.CastShadow,
                Layers = source.Layers,
                Transform = source.Transform,
                Visible = wasVisible,
                VisibilityRangeBegin = source.VisibilityRangeBegin,
                VisibilityRangeEnd = source.VisibilityRangeEnd,
                VisibilityRangeBeginMargin = source.VisibilityRangeBeginMargin,
                VisibilityRangeEndMargin = source.VisibilityRangeEndMargin,
                LodBias = source.LodBias,
                ExtraCullMargin = source.ExtraCullMargin,
                Transparency = source.Transparency
            };
            source.GetParent().AddChild(chunk);
            m_chunks.Add(chunk);
            ChunkCount++;
            ChunkTriangles += CountTriangles(chunkMesh);
        }
        source.Visible = false;
    }

    private sealed class SurfaceBuilder
    {
        private readonly List<Vector3> m_vertices = [];
        private readonly List<Vector3> m_normals = [];
        private readonly List<Color> m_colors = [];
        private readonly List<Vector2> m_uvs = [];
        private readonly List<int> m_indices = [];
        private readonly Dictionary<int, int> m_vertexMap = [];
        private bool m_hasNormals;
        private bool m_hasColors;
        private bool m_hasUvs;

        public void Add(int index, Vector3[] vertices, Vector3[] normals, Color[] colors, Vector2[] uvs)
        {
            if (m_vertexMap.TryGetValue(index, out var mapped))
            {
                m_indices.Add(mapped);
                return;
            }
            m_vertexMap.Add(index, m_vertices.Count);
            m_indices.Add(m_vertices.Count);
            m_vertices.Add(vertices[index]);
            m_hasNormals |= normals.Length == vertices.Length;
            m_hasColors |= colors.Length == vertices.Length;
            m_hasUvs |= uvs.Length == vertices.Length;
            if (normals.Length == vertices.Length) m_normals.Add(normals[index]);
            if (colors.Length == vertices.Length) m_colors.Add(colors[index]);
            if (uvs.Length == vertices.Length) m_uvs.Add(uvs[index]);
        }

        public Godot.Collections.Array ToArrays()
        {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = m_vertices.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = m_indices.ToArray();
            if (m_hasNormals) arrays[(int)Mesh.ArrayType.Normal] = m_normals.ToArray();
            if (m_hasColors) arrays[(int)Mesh.ArrayType.Color] = m_colors.ToArray();
            if (m_hasUvs) arrays[(int)Mesh.ArrayType.TexUV] = m_uvs.ToArray();
            return arrays;
        }
    }

    private static T[] GetArray<T>(Godot.Collections.Array arrays, Mesh.ArrayType type) =>
        arrays[(int)type].VariantType == Variant.Type.Nil ? [] : (T[])arrays[(int)type].Obj;

    private static long CountTriangles(ArrayMesh mesh)
    {
        long count = 0;
        for (var surface = 0; surface < mesh.GetSurfaceCount(); surface++)
        {
            var arrays = mesh.SurfaceGetArrays(surface);
            var indices = arrays[(int)Mesh.ArrayType.Index];
            var length = indices.VariantType == Variant.Type.Nil
                ? mesh.SurfaceGetArrayLen(surface)
                : ((int[])indices.Obj).Length;
            count += length / 3;
        }
        return count;
    }

    private static IEnumerable<ShaderMaterial> GetTerrainMaterials(MeshInstance3D mesh)
    {
        if (mesh.MaterialOverride is ShaderMaterial material && TerrainSurfaceMaterial.SupportsTriplanarToggle(material))
            yield return material;
        if (mesh.Mesh == null) yield break;
        for (var index = 0; index < mesh.Mesh.GetSurfaceCount(); index++)
            if ((mesh.GetSurfaceOverrideMaterial(index) ?? mesh.Mesh.SurfaceGetMaterial(index)) is ShaderMaterial surfaceMaterial &&
                TerrainSurfaceMaterial.SupportsTriplanarToggle(surfaceMaterial)) yield return surfaceMaterial;
    }

    private static IEnumerable<Node> Descendants(Node root)
    {
        var pending = new Queue<Node>();
        pending.Enqueue(root);
        while (pending.Count > 0)
        {
            var node = pending.Dequeue();
            yield return node;
            foreach (Node child in node.GetChildren()) pending.Enqueue(child);
        }
    }
}
