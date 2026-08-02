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

    public PlayerCockpit()
    {
        Name = "CockpitInterior";
    }

    public float Width { get; } = 0.75f;

    public float Height { get; } = 0.7f;

    public float Length { get; } = 2.25f;

    public float PostThickness { get; } = 0.04f;

    public float SideTaper { get; }

    public override void _Ready()
    {
        Rebuild();
    }

    public void LogDimensions()
    {
        GD.Print(
            $"MechRewired: cockpit dimensions width {Width:F2}, height {Height:F2}, " +
            $"length {Length:F2}, post thickness {PostThickness:F2}, side taper {SideTaper:F2}.");
    }

    private void Rebuild()
    {
        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }

        var frameMaterial = CreateMaterial(new Color("3d4a51"), 0.7f, 0.45f);
        var vertices = GetCrossSectionVertices();
        var rearwardOffset = FrameOffsetZ + Length * RearwardOffsetFactor;
        var crossSectionCentreZ = RearZ - Length * 0.5f + rearwardOffset;

        for (var index = 0; index < vertices.Length; index++)
        {
            var vertex = vertices[index];
            var halfRailWidth = GetHalfRailWidth(index);
            AddBox(
                $"LongitudinalPost{index + 1}",
                new Vector3(halfRailWidth * 2.0f, PostThickness, PostThickness),
                new Vector3(0.0f, vertex.Y, crossSectionCentreZ + vertex.X),
                Vector3.Zero,
                frameMaterial);

            var nextIndex = (index + 1) % vertices.Length;
            var next = vertices[nextIndex];
            AddBeamBetween(
                $"LeftBrace{index + 1}",
                ToBracePosition(vertex, index, -1.0f, crossSectionCentreZ),
                ToBracePosition(next, nextIndex, -1.0f, crossSectionCentreZ),
                frameMaterial);
            AddBeamBetween(
                $"RightBrace{index + 1}",
                ToBracePosition(vertex, index, 1.0f, crossSectionCentreZ),
                ToBracePosition(next, nextIndex, 1.0f, crossSectionCentreZ),
                frameMaterial);
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
            Mesh = new BoxMesh
            {
                Size = new Vector3(PostThickness, PostThickness, difference.Length() + PostThickness),
                Material = material
            },
            Position = midpoint,
            Basis = new Basis(xAxis, yAxis, zAxis),
            Layers = RenderLayer
        });
    }

    private void AddBox(
        string name,
        Vector3 size,
        Vector3 position,
        Vector3 rotationDegrees,
        Godot.Material material)
    {
        AddChild(new MeshInstance3D
        {
            Name = name,
            Mesh = new BoxMesh
            {
                Size = size,
                Material = material
            },
            Position = position,
            RotationDegrees = rotationDegrees,
            Layers = RenderLayer
        });
    }

    private static StandardMaterial3D CreateMaterial(Color color, float roughness, float metallic) =>
        new()
        {
            AlbedoColor = color,
            EmissionEnabled = true,
            Emission = color,
            EmissionEnergyMultiplier = 0.35f,
            Roughness = roughness,
            Metallic = metallic
        };
}
