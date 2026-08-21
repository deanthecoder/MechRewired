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
/// Selects the debug-only material view used to investigate cockpit-frame rendering.
/// </summary>
public enum CockpitFrameDiagnosticMode
{
    Lit,
    Albedo,
    GeometricNormal,
    NormalMap,
    Roughness,
    Metallic,
    DirectSun
}

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
    private const float DefaultFrameMetallic = 0.75f;
    private const float DefaultFrameRoughness = 0.60f;
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
    private MeshInstance3D m_frameMesh;
    private float m_frameTextureScale = DefaultFrameTextureScale;
    private float m_frameMetallic = DefaultFrameMetallic;
    private float m_frameRoughness = DefaultFrameRoughness;
    private CockpitFrameDiagnosticMode m_frameDiagnosticMode;

    public PlayerCockpit()
    {
        Name = "CockpitInterior";
    }

    public float Width { get; } = 0.75f;

    public float Height { get; } = 0.7f;

    public float Length { get; } = 2.25f;

    public float PostThickness { get; } = 0.04f;

    public float SideTaper { get; }

    public CockpitFrameDiagnosticMode FrameDiagnosticMode
    {
        get => m_frameDiagnosticMode;
        set
        {
            m_frameDiagnosticMode = value;
            ApplyFrameDiagnosticMaterial();
        }
    }

    public string FrameDiagnosticModeName => ToDiagnosticName(FrameDiagnosticMode);

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

    /// <summary>
    /// Selects a named material view for a cockpit-frame investigation.
    /// </summary>
    public bool TrySetFrameDiagnosticMode(string name)
    {
        switch (name.Trim().ToLowerInvariant())
        {
            case "lit":
            case "default":
                FrameDiagnosticMode = CockpitFrameDiagnosticMode.Lit;
                return true;
            case "albedo":
            case "basecolor":
                FrameDiagnosticMode = CockpitFrameDiagnosticMode.Albedo;
                return true;
            case "normal":
            case "normals":
            case "geometricnormal":
                FrameDiagnosticMode = CockpitFrameDiagnosticMode.GeometricNormal;
                return true;
            case "normalmap":
                FrameDiagnosticMode = CockpitFrameDiagnosticMode.NormalMap;
                return true;
            case "roughness":
                FrameDiagnosticMode = CockpitFrameDiagnosticMode.Roughness;
                return true;
            case "metallic":
            case "metalness":
                FrameDiagnosticMode = CockpitFrameDiagnosticMode.Metallic;
                return true;
            case "directsun":
            case "sun":
                FrameDiagnosticMode = CockpitFrameDiagnosticMode.DirectSun;
                return true;
            default:
                return false;
        }
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

        // Keep the cockpit as one PBR mesh.  This is not merely an optimisation: the old
        // construction used individual, deliberately over-long meshes for every brace.  At
        // each joint their end caps occupied the same space, which made reflected highlights
        // flicker as the depth buffer chose a different cap from frame to frame.
        m_frameMaterial = CreateFrameMaterial();
        var vertices = GetCrossSectionVertices();
        var rearwardOffset = FrameOffsetZ + Length * RearwardOffsetFactor;
        var crossSectionCentreZ = RearZ - Length * 0.5f + rearwardOffset;
        var frameBuilder = new SurfaceTool();
        frameBuilder.Begin(Mesh.PrimitiveType.Triangles);

        for (var index = 0; index < vertices.Length; index++)
        {
            var vertex = vertices[index];
            var halfRailWidth = GetHalfRailWidth(index);
            AppendRailAlongX(
                frameBuilder,
                halfRailWidth * 2.0f,
                new Vector3(0.0f, vertex.Y, crossSectionCentreZ + vertex.X));

            var nextIndex = (index + 1) % vertices.Length;
            var next = vertices[nextIndex];
            AppendBeamBetween(
                frameBuilder,
                ToBracePosition(vertex, index, -1.0f, crossSectionCentreZ),
                ToBracePosition(next, nextIndex, -1.0f, crossSectionCentreZ));
            AppendBeamBetween(
                frameBuilder,
                ToBracePosition(vertex, index, 1.0f, crossSectionCentreZ),
                ToBracePosition(next, nextIndex, 1.0f, crossSectionCentreZ));

            // A compact shared joint cap covers the deliberately open rail ends.  It reads as
            // a manufactured connector rather than a gap, without restoring overlapping caps.
            AppendJoint(frameBuilder, ToBracePosition(vertex, index, -1.0f, crossSectionCentreZ));
            AppendJoint(frameBuilder, ToBracePosition(vertex, index, 1.0f, crossSectionCentreZ));
        }

        frameBuilder.GenerateNormals();
        frameBuilder.GenerateTangents();
        var frameMesh = frameBuilder.Commit();
        frameMesh.SurfaceSetMaterial(0, m_frameMaterial);
        m_frameMesh = new MeshInstance3D
        {
            Name = "CockpitFrame",
            Mesh = frameMesh,
            Layers = RenderLayer
        };
        AddChild(m_frameMesh);
        ApplyFrameDiagnosticMaterial();
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

    private void AppendBeamBetween(
        SurfaceTool frameBuilder,
        Vector3 start,
        Vector3 end)
    {
        var difference = end - start;
        var midpoint = (start + end) * 0.5f;
        var zAxis = difference.Normalized();
        var reference = Mathf.Abs(zAxis.Dot(Vector3.Up)) > 0.95f ? Vector3.Right : Vector3.Up;
        var xAxis = reference.Cross(zAxis).Normalized();
        var yAxis = zAxis.Cross(xAxis).Normalized();
        AppendChamferedRail(
            frameBuilder,
            difference.Length(),
            new Transform3D(new Basis(xAxis, yAxis, zAxis), midpoint));
    }

    private void AppendRailAlongX(
        SurfaceTool frameBuilder,
        float length,
        Vector3 position)
    {
        AppendChamferedRail(
            frameBuilder,
            length,
            new Transform3D(new Basis(Vector3.Up, Mathf.Pi * 0.5f), position));
    }

    /// <summary>
    /// Builds an octagonal rail with long flat faces and small clipped corners. It is an
    /// engineered chamfered square rather than a regular cylinder, so it still reads as a
    /// sturdy cockpit spar while catching a useful highlight along its edges.
    /// </summary>
    private void AppendChamferedRail(
        SurfaceTool surfaceTool,
        float length,
        Transform3D transform)
    {
        // Rail faces are deliberately planar. Do not average their normals with adjacent
        // chamfers or with another member that happens to meet at the same position.
        surfaceTool.SetSmoothGroup(uint.MaxValue);
        var halfThickness = PostThickness * 0.5f;
        var chamfer = PostThickness * RailChamferRatio;
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
        for (var index = 0; index < profile.Length; index++)
        {
            var nextIndex = (index + 1) % profile.Length;
            var a = profile[index];
            var b = profile[nextIndex];
            var u0 = index / (float)profile.Length;
            var u1 = (index + 1) / (float)profile.Length;
            AddTransformedTriangle(
                surfaceTool,
                transform,
                a,
                -halfLength,
                b,
                -halfLength,
                b,
                halfLength,
                new Vector2(u0, 0.0f),
                new Vector2(u1, 0.0f),
                new Vector2(u1, 1.0f));
            AddTransformedTriangle(
                surfaceTool,
                transform,
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

        // Rails meet at every end. Leaving the ends open removes coplanar caps at those
        // intersections; the adjoining faces form a continuous-looking welded frame.
    }

    private void AppendJoint(SurfaceTool surfaceTool, Vector3 centre)
    {
        const int LongitudeSegments = 10;
        const int LatitudeSegments = 6;
        // Unlike the engineered rails, the connector is rounded and should shade smoothly.
        // Its distinct group also prevents Godot blending its normals into the rail faces.
        surfaceTool.SetSmoothGroup(0);
        var radius = PostThickness * 0.78f;

        for (var latitude = 0; latitude < LatitudeSegments; latitude++)
        {
            var theta0 = Mathf.Pi * latitude / LatitudeSegments;
            var theta1 = Mathf.Pi * (latitude + 1) / LatitudeSegments;
            for (var longitude = 0; longitude < LongitudeSegments; longitude++)
            {
                var phi0 = Mathf.Tau * longitude / LongitudeSegments;
                var phi1 = Mathf.Tau * (longitude + 1) / LongitudeSegments;
                var a = centre + ToSpherePoint(radius, theta0, phi0);
                var b = centre + ToSpherePoint(radius, theta0, phi1);
                var c = centre + ToSpherePoint(radius, theta1, phi1);
                var d = centre + ToSpherePoint(radius, theta1, phi0);
                var u0 = longitude / (float)LongitudeSegments;
                var u1 = (longitude + 1) / (float)LongitudeSegments;
                var v0 = latitude / (float)LatitudeSegments;
                var v1 = (latitude + 1) / (float)LatitudeSegments;
                AddTriangle(surfaceTool, a, b, c, new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1));
                AddTriangle(surfaceTool, a, c, d, new Vector2(u0, v0), new Vector2(u1, v1), new Vector2(u0, v1));
            }
        }
    }

    private static Vector3 ToSpherePoint(float radius, float theta, float phi) =>
        new(
            radius * Mathf.Sin(theta) * Mathf.Cos(phi),
            radius * Mathf.Cos(theta),
            radius * Mathf.Sin(theta) * Mathf.Sin(phi));

    private static void AddTransformedTriangle(
        SurfaceTool surfaceTool,
        Transform3D transform,
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
        // SurfaceTool treats clockwise triangles as front-facing.  Keep the generated
        // cockpit normals pointing out of its rails rather than into them by reversing
        // the conventional counter-clockwise order used by the profile construction.
        surfaceTool.SetUV(uvA);
        surfaceTool.AddVertex(transform * new Vector3(a.X, a.Y, az));
        surfaceTool.SetUV(uvC);
        surfaceTool.AddVertex(transform * new Vector3(c.X, c.Y, cz));
        surfaceTool.SetUV(uvB);
        surfaceTool.AddVertex(transform * new Vector3(b.X, b.Y, bz));
    }

    private static void AddTriangle(
        SurfaceTool surfaceTool,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector2 uvA,
        Vector2 uvB,
        Vector2 uvC)
    {
        surfaceTool.SetUV(uvA);
        surfaceTool.AddVertex(a);
        surfaceTool.SetUV(uvC);
        surfaceTool.AddVertex(c);
        surfaceTool.SetUV(uvB);
        surfaceTool.AddVertex(b);
    }

    private static Vector2 ToUv(Vector2 point, float thickness) => point / thickness + Vector2.One * 0.5f;

    private StandardMaterial3D CreateFrameMaterial()
    {
        var material = new StandardMaterial3D
        {
            AlbedoTexture = GD.Load<Texture2D>(FrameAlbedoTexturePath),
            NormalEnabled = true,
            NormalTexture = GD.Load<Texture2D>(FrameNormalTexturePath),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            Uv1Triplanar = true,
            Uv1TriplanarSharpness = 12.0f,
            Uv1WorldTriplanar = false
        };
        // Metal029's metalness map is uniformly white and its roughness averages about 0.30.
        // Multiplying those by the previous controls produced a near-black, mirror-like frame
        // whose sky reflections looked like light arriving from the wrong direction. Keep the
        // useful colour and normal detail, but model the cockpit as dark painted steel with
        // restrained metal response and a broad, stable reflection.
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
        if (FrameDiagnosticMode is CockpitFrameDiagnosticMode.Albedo or
            CockpitFrameDiagnosticMode.NormalMap or
            CockpitFrameDiagnosticMode.Roughness or
            CockpitFrameDiagnosticMode.Metallic)
        {
            ApplyFrameDiagnosticMaterial();
        }
    }

    private void ApplyFrameDiagnosticMaterial()
    {
        if (m_frameMesh == null)
        {
            return;
        }

        m_frameMesh.MaterialOverride = FrameDiagnosticMode switch
        {
            CockpitFrameDiagnosticMode.Lit => null,
            CockpitFrameDiagnosticMode.Albedo => CreateTextureDiagnosticMaterial(FrameAlbedoTexturePath),
            CockpitFrameDiagnosticMode.NormalMap => CreateTextureDiagnosticMaterial(FrameNormalTexturePath),
            CockpitFrameDiagnosticMode.Roughness => CreateTextureDiagnosticMaterial(FrameRoughnessTexturePath),
            CockpitFrameDiagnosticMode.Metallic => CreateTextureDiagnosticMaterial(FrameMetalnessTexturePath),
            CockpitFrameDiagnosticMode.GeometricNormal => CreateGeometricNormalDiagnosticMaterial(),
            CockpitFrameDiagnosticMode.DirectSun => CreateDirectSunDiagnosticMaterial(),
            _ => null
        };
    }

    private StandardMaterial3D CreateTextureDiagnosticMaterial(string texturePath) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoTexture = GD.Load<Texture2D>(texturePath),
        TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        Uv1Triplanar = true,
        Uv1TriplanarSharpness = 12.0f,
        Uv1WorldTriplanar = false,
        Uv1Scale = Vector3.One * FrameTextureScale
    };

    private static ShaderMaterial CreateGeometricNormalDiagnosticMaterial() =>
        CreateUnshadedDiagnosticMaterial(
            """
            varying vec3 world_geometric_normal;

            void vertex() {
                world_geometric_normal = normalize(MODEL_NORMAL_MATRIX * NORMAL);
            }

            void fragment() {
                ALBEDO = world_geometric_normal * 0.5 + 0.5;
            }
            """);

    private static ShaderMaterial CreateDirectSunDiagnosticMaterial() => new()
    {
        Shader = new Shader
        {
            Code =
                """
                shader_type spatial;
                render_mode ambient_light_disabled, shadows_disabled, specular_disabled;

                void fragment() {
                    ALBEDO = vec3(1.0);
                    METALLIC = 0.0;
                    ROUGHNESS = 1.0;
                }

                void light() {
                    // LIGHT and NORMAL are both supplied by Godot in the same space. This
                    // intentionally tests the actual SunLight direction, not a C# duplicate.
                    float direct = max(dot(normalize(NORMAL), normalize(LIGHT)), 0.0);
                    DIFFUSE_LIGHT = vec3(direct);
                }
                """
        }
    };

    private static ShaderMaterial CreateUnshadedDiagnosticMaterial(string body) => new()
    {
        Shader = new Shader
        {
            Code =
                """
                shader_type spatial;
                render_mode unshaded, cull_back;
                """ + body
        }
    };

    private static string ToDiagnosticName(CockpitFrameDiagnosticMode mode) => mode switch
    {
        CockpitFrameDiagnosticMode.GeometricNormal => "geometric normal",
        CockpitFrameDiagnosticMode.NormalMap => "normal map",
        CockpitFrameDiagnosticMode.DirectSun => "direct sun",
        _ => mode.ToString().ToLowerInvariant()
    };
}
