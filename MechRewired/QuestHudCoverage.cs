// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to use, modify, or distribute this software, without warranty.
using Godot;

namespace MechRewired;

/// <summary>
/// Displays only occupied regions of the original HUD texture, in one instanced draw.
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
    private readonly MultiMesh m_mesh;
    private int m_layer;
    public int VisibleCells { get; private set; }
    internal bool CoversPixel(int x, int y) => m_visible[y / CellSize * Columns + x / CellSize];

    public QuestHudCoverage(MeshInstance3D anchor, Texture2D texture)
    {
        var size = ((QuadMesh)anchor.Mesh).Size;
        m_mesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = new QuadMesh { Size = size / new Vector2(Columns, Rows) },
            InstanceCount = Columns * Rows
        };
        for (var y = 0; y < Rows; y++)
        for (var x = 0; x < Columns; x++)
            m_mesh.SetInstanceTransform(y * Columns + x, new Transform3D(Basis.Identity,
                new Vector3(((x + .5f) / Columns - .5f) * size.X,
                    (.5f - (y + .5f) / Rows) * size.Y, 0)));
        var shader = new Shader { Code = """
            shader_type spatial;
            render_mode unshaded, cull_disabled, blend_mix;
            uniform sampler2D hud_texture : source_color, filter_linear;
            void vertex() {
                UV = (UV + INSTANCE_CUSTOM.xy) / vec2(32.0, 18.0);
                // Empty cells collapse to zero-area triangles before rasterization.
                VERTEX *= INSTANCE_CUSTOM.z;
            }
            void fragment() {
                vec4 pixel = texture(hud_texture, UV);
                ALBEDO = pixel.rgb;
                ALPHA = pixel.a;
            }
            """ };
        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("hud_texture", texture);
        anchor.AddChild(new MultiMeshInstance3D
        {
            Name = "HudContentRegions", Multimesh = m_mesh, MaterialOverride = material,
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
        for (var i = 0; i < m_visible.Length; i++)
        {
            var visible = m_layers[0][i] || m_layers[1][i];
            if (visible == m_visible[i]) continue;
            m_visible[i] = visible;
            VisibleCells += visible ? 1 : -1;
            m_mesh.SetInstanceCustomData(i, new Color(i % Columns, i / Columns, visible ? 1 : 0, 0));
        }
    }
}
