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

namespace MechRewired;

/// <summary>
/// Provides the native 3D interpretation of the original cockpit shell.
/// </summary>
public partial class PlayerCockpit : Node3D
{
    public const uint RenderLayer = 1u << 2;

    private const float RearZ = 0.6f;
    private const float FrameOffsetZ = 0.5f;
    private const float RearwardOffsetFactor = 0.25f;
    private const float DefaultFrameTextureScale = 1.5f;
    private const float DefaultFrameMetallic = 0.65f;
    private const float DefaultFrameRoughness = 0.72f;
    private const float RailChamferRatio = 0.22f;
    private const string FrameAlbedoTexturePath =
        "res://Assets/Textures/Cockpit/Metal029/Metal029_1K-PNG_Color.png";
    private const string FrameMetalnessTexturePath =
        "res://Assets/Textures/Cockpit/Metal029/Metal029_1K-PNG_Metalness.png";
    private const string FrameNormalTexturePath =
        "res://Assets/Textures/Cockpit/Metal029/Metal029_1K-PNG_NormalGL.png";
    private const string FrameRoughnessTexturePath =
        "res://Assets/Textures/Cockpit/Metal029/Metal029_1K-PNG_Roughness.png";

    private StandardMaterial3D m_frameMaterial;
    private float m_frameTextureScale = DefaultFrameTextureScale;
    private float m_frameMetallic = DefaultFrameMetallic;
    private float m_frameRoughness = DefaultFrameRoughness;

    public PlayerCockpit()
    {
        Name = "CockpitInterior";
    }

    public float Width { get; } = 0.75f;

    public float Height { get; } = 0.7f;

    public float Length { get; } = 2.25f;

    public float PostThickness { get; } = 0.04f;

    public float SideTaper { get; }

    /// <summary>
    /// Controls the number of Metal029 repetitions across each metre of cockpit structure.
    /// </summary>
    [Export]
    public float FrameTextureScale
    {
        get => m_frameTextureScale;
        set
        {
            m_frameTextureScale = Mathf.Clamp(value, 0.1f, 12.0f);
            ApplyFrameMaterialProperties();
        }
    }

    /// <summary>
    /// Controls how strongly the cockpit frame behaves as painted metal.
    /// </summary>
    [Export]
    public float FrameMetallic
    {
        get => m_frameMetallic;
        set
        {
            m_frameMetallic = Mathf.Clamp(value, 0.0f, 1.0f);
            ApplyFrameMaterialProperties();
        }
    }

    /// <summary>
    /// Controls the cockpit frame's reflected-light blur.
    /// </summary>
    [Export]
    public float FrameRoughness
    {
        get => m_frameRoughness;
        set
        {
            m_frameRoughness = Mathf.Clamp(value, 0.0f, 1.0f);
            ApplyFrameMaterialProperties();
        }
    }

    public override void _Ready()
    {
        Rebuild();
    }

    public void SetPose(float pitchDegrees, float yaw, Vector3 gaitPosition, float gaitRoll)
    {
        Position = gaitPosition;
        Rotation = new Vector3(Mathf.DegToRad(pitchDegrees), yaw, gaitRoll);
    }

    private void Rebuild()
    {
        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }

        // Keep the cockpit as a shared PBR material. The separate beams are BoxMeshes, so
        // triplanar projection prevents a narrow face from stretching its source UV island.
        m_frameMaterial = CreateFrameMaterial();
        var vertices = GetCrossSectionVertices();
        var rearwardOffset = FrameOffsetZ + Length * RearwardOffsetFactor;
        var crossSectionCentreZ = RearZ - Length * 0.5f + rearwardOffset;

        for (var index = 0; index < vertices.Length; index++)
        {
            var vertex = vertices[index];
            var halfRailWidth = GetHalfRailWidth(index);
            AddRailAlongX(
                $"LongitudinalPost{index + 1}",
                new Vector3(halfRailWidth * 2.0f, PostThickness, PostThickness),
                new Vector3(0.0f, vertex.Y, crossSectionCentreZ + vertex.X),
                m_frameMaterial);

            var nextIndex = (index + 1) % vertices.Length;
            var next = vertices[nextIndex];
            AddBeamBetween(
                $"LeftBrace{index + 1}",
                ToBracePosition(vertex, index, -1.0f, crossSectionCentreZ),
                ToBracePosition(next, nextIndex, -1.0f, crossSectionCentreZ),
                m_frameMaterial);
            AddBeamBetween(
                $"RightBrace{index + 1}",
                ToBracePosition(vertex, index, 1.0f, crossSectionCentreZ),
                ToBracePosition(next, nextIndex, 1.0f, crossSectionCentreZ),
                m_frameMaterial);
        }

    }

    private Vector2[] GetCrossSectionVertices()
    {
        var halfLength = Length * 0.5f;
        var shoulderLength = Length * 0.32f;
        var halfHeight = Height * 0.5f;
        return
        [
            new Vector2(-shoulderLength, halfHeight),
            new Vector2(shoulderLength, halfHeight),
            new Vector2(halfLength, 0.0f),
            new Vector2(shoulderLength, -halfHeight),
            new Vector2(-shoulderLength, -halfHeight),
            new Vector2(-halfLength, 0.0f)
        ];
    }

    private float GetHalfRailWidth(int vertexIndex) =>
        Width * 0.5f - (vertexIndex is 2 or 5 ? 0.0f : SideTaper);

    private Vector3 ToBracePosition(Vector2 vertex, int vertexIndex, float side, float centreZ) =>
        new(side * GetHalfRailWidth(vertexIndex), vertex.Y, centreZ + vertex.X);

    private void AddBeamBetween(
        string name,
        Vector3 start,
        Vector3 end,
        Godot.Material material)
    {
        var difference = end - start;
        var midpoint = (start + end) * 0.5f;
        var zAxis = difference.Normalized();
        var reference = Mathf.Abs(zAxis.Dot(Vector3.Up)) > 0.95f ? Vector3.Right : Vector3.Up;
        var xAxis = reference.Cross(zAxis).Normalized();
        var yAxis = zAxis.Cross(xAxis).Normalized();
        AddChild(new MeshInstance3D
        {
            Name = name,
            Mesh = CreateChamferedRailMesh(PostThickness, difference.Length() + PostThickness, material),
            Position = midpoint,
            Basis = new Basis(xAxis, yAxis, zAxis),
            Layers = RenderLayer
        });
    }

    private void AddRailAlongX(
        string name,
        Vector3 size,
        Vector3 position,
        Godot.Material material)
    {
        AddChild(new MeshInstance3D
        {
            Name = name,
            Mesh = CreateChamferedRailMesh(PostThickness, size.X, material),
            Position = position,
            RotationDegrees = new Vector3(0.0f, 90.0f, 0.0f),
            Layers = RenderLayer
        });
    }

    /// <summary>
    /// Builds an octagonal rail with long flat faces and small clipped corners. It is an
    /// engineered chamfered square rather than a regular cylinder, so it still reads as a
    /// sturdy cockpit spar while catching a useful highlight along its edges.
    /// </summary>
    private static ArrayMesh CreateChamferedRailMesh(float thickness, float length, Godot.Material material)
    {
        var halfThickness = thickness * 0.5f;
        var chamfer = thickness * RailChamferRatio;
        var halfLength = length * 0.5f;
        var profile = new[]
        {
            new Vector2(-halfThickness + chamfer, -halfThickness),
            new Vector2(halfThickness - chamfer, -halfThickness),
            new Vector2(halfThickness, -halfThickness + chamfer),
            new Vector2(halfThickness, halfThickness - chamfer),
            new Vector2(halfThickness - chamfer, halfThickness),
            new Vector2(-halfThickness + chamfer, halfThickness),
            new Vector2(-halfThickness, halfThickness - chamfer),
            new Vector2(-halfThickness, -halfThickness + chamfer)
        };
        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        for (var index = 0; index < profile.Length; index++)
        {
            var nextIndex = (index + 1) % profile.Length;
            var a = profile[index];
            var b = profile[nextIndex];
            var u0 = index / (float)profile.Length;
            var u1 = (index + 1) / (float)profile.Length;
            AddTriangle(
                surfaceTool,
                a,
                -halfLength,
                b,
                -halfLength,
                b,
                halfLength,
                new Vector2(u0, 0.0f),
                new Vector2(u1, 0.0f),
                new Vector2(u1, 1.0f));
            AddTriangle(
                surfaceTool,
                a,
                -halfLength,
                b,
                halfLength,
                a,
                halfLength,
                new Vector2(u0, 0.0f),
                new Vector2(u1, 1.0f),
                new Vector2(u0, 1.0f));
        }

        var centre = Vector2.Zero;
        for (var index = 0; index < profile.Length; index++)
        {
            var nextIndex = (index + 1) % profile.Length;
            var a = profile[index];
            var b = profile[nextIndex];
            AddTriangle(
                surfaceTool,
                centre,
                halfLength,
                a,
                halfLength,
                b,
                halfLength,
                Vector2.One * 0.5f,
                ToUv(a, thickness),
                ToUv(b, thickness));
            AddTriangle(
                surfaceTool,
                centre,
                -halfLength,
                b,
                -halfLength,
                a,
                -halfLength,
                Vector2.One * 0.5f,
                ToUv(b, thickness),
                ToUv(a, thickness));
        }

        surfaceTool.GenerateNormals();
        surfaceTool.GenerateTangents();
        var mesh = surfaceTool.Commit();
        mesh.SurfaceSetMaterial(0, material);
        return mesh;
    }

    private static void AddTriangle(
        SurfaceTool surfaceTool,
        Vector2 a,
        float az,
        Vector2 b,
        float bz,
        Vector2 c,
        float cz,
        Vector2 uvA,
        Vector2 uvB,
        Vector2 uvC)
    {
        surfaceTool.SetUV(uvA);
        surfaceTool.AddVertex(new Vector3(a.X, a.Y, az));
        surfaceTool.SetUV(uvB);
        surfaceTool.AddVertex(new Vector3(b.X, b.Y, bz));
        surfaceTool.SetUV(uvC);
        surfaceTool.AddVertex(new Vector3(c.X, c.Y, cz));
    }

    private static Vector2 ToUv(Vector2 point, float thickness) => point / thickness + Vector2.One * 0.5f;

    private StandardMaterial3D CreateFrameMaterial()
    {
        var material = new StandardMaterial3D
        {
            AlbedoTexture = GD.Load<Texture2D>(FrameAlbedoTexturePath),
            MetallicTexture = GD.Load<Texture2D>(FrameMetalnessTexturePath),
            MetallicTextureChannel = BaseMaterial3D.TextureChannel.Red,
            NormalEnabled = true,
            NormalTexture = GD.Load<Texture2D>(FrameNormalTexturePath),
            RoughnessTexture = GD.Load<Texture2D>(FrameRoughnessTexturePath),
            RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Red,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            Uv1Triplanar = true,
            Uv1TriplanarSharpness = 12.0f,
            Uv1WorldTriplanar = false
        };
        m_frameMaterial = material;
        ApplyFrameMaterialProperties();
        return material;
    }

    private void ApplyFrameMaterialProperties()
    {
        if (m_frameMaterial == null)
        {
            return;
        }

        m_frameMaterial.Metallic = m_frameMetallic;
        m_frameMaterial.Roughness = m_frameRoughness;
        m_frameMaterial.Uv1Scale = Vector3.One * m_frameTextureScale;
    }
}
