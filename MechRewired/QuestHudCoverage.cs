// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to use, modify, or distribute this software, without warranty.
using Godot;

namespace MechRewired;

/// <summary>
/// Displays only occupied regions of the original HUD texture, in one indexed mesh.
/// Instrument coverage persists between redraws; targeting coverage follows each new draw.
/// Cells never overlap, so translucent artwork is blended exactly once.
/// </summary>
public sealed class QuestHudCoverage
{
    private const int CellSize = 40;
    private const int Columns = 32;
    private const int Rows = 18;
    private readonly bool[][] m_layers = [new bool[Columns * Rows], new bool[Columns * Rows]];
    private readonly bool[] m_visible = new bool[Columns * Rows];
    private readonly ArrayMesh m_mesh = new();
    private readonly Vector3[] m_vertices = new Vector3[(Columns + 1) * (Rows + 1)];
    private readonly Vector2[] m_uvs = new Vector2[(Columns + 1) * (Rows + 1)];
    private int m_layer;
    public int VisibleCells { get; private set; }
    internal bool CoversPixel(int x, int y) => m_visible[y / CellSize * Columns + x / CellSize];

    public QuestHudCoverage(MeshInstance3D anchor)
    {
        var size = ((QuadMesh)anchor.Mesh).Size;
        // Adjacent cells share the very same position and UV vertices. Per-instance
        // transforms can round adjoining edges differently on mobile GPUs, leaving
        // seams in translucent panels when viewed at an angle.
        for (var y = 0; y <= Rows; y++)
        for (var x = 0; x <= Columns; x++)
        {
            var i = y * (Columns + 1) + x;
            m_uvs[i] = new Vector2((float)x / Columns, (float)y / Rows);
            m_vertices[i] = new Vector3((m_uvs[i].X - .5f) * size.X,
                (.5f - m_uvs[i].Y) * size.Y, 0);
        }
        anchor.AddChild(new MeshInstance3D
        {
            Name = "HudContentRegions", Mesh = m_mesh, MaterialOverride = anchor.MaterialOverride,
            Layers = PlayerCockpit.RenderLayer,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        });
        // Keep the original plane for gaze/projection math without drawing its empty area.
        anchor.Layers = 0;
    }

    public void Begin(bool targeting)
    {
        m_layer = targeting ? 1 : 0;
        Array.Clear(m_layers[m_layer]);
    }

    public void Include(Rect2 bounds)
    {
        var rect = bounds.Abs().Intersection(new Rect2(0, 0, 1280, 720));
        if (!rect.HasArea()) return;
        var left = Math.Clamp((int)MathF.Floor(rect.Position.X / CellSize), 0, Columns - 1);
        var top = Math.Clamp((int)MathF.Floor(rect.Position.Y / CellSize), 0, Rows - 1);
        var right = Math.Clamp((int)MathF.Ceiling(rect.End.X / CellSize), 0, Columns);
        var bottom = Math.Clamp((int)MathF.Ceiling(rect.End.Y / CellSize), 0, Rows);
        for (var y = top; y < bottom; y++)
        for (var x = left; x < right; x++)
            m_layers[m_layer][y * Columns + x] = true;
    }

    public void End()
    {
        using var cpuScope = QuestCpuTelemetry.Measure(QuestCpuTelemetry.Category.HudCoverage);
        var changed = false;
        for (var i = 0; i < m_visible.Length; i++)
        {
            var visible = m_layers[0][i] || m_layers[1][i];
            if (visible == m_visible[i]) continue;
            changed = true;
            m_visible[i] = visible;
            VisibleCells += visible ? 1 : -1;
        }
        if (!changed) return;
        m_mesh.ClearSurfaces();
        if (VisibleCells == 0) return;
        var indices = new int[VisibleCells * 6];
        var next = 0;
        for (var i = 0; i < m_visible.Length; i++)
        {
            if (!m_visible[i]) continue;
            var topLeft = i / Columns * (Columns + 1) + i % Columns;
            var bottomLeft = topLeft + Columns + 1;
            indices[next++] = topLeft;
            indices[next++] = topLeft + 1;
            indices[next++] = bottomLeft;
            indices[next++] = topLeft + 1;
            indices[next++] = bottomLeft + 1;
            indices[next++] = bottomLeft;
        }
        using var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = m_vertices;
        arrays[(int)Mesh.ArrayType.TexUV] = m_uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        m_mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
    }
}
