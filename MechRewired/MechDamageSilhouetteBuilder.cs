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
using MechRewired.Resources;
using MechRewired.Simulation;

namespace MechRewired;

/// <summary>
/// Assigns an original monochrome damage silhouette to the authored OBJL groups in a mech chassis.
/// </summary>
public static class MechDamageSilhouetteBuilder
{
    private const float MinimumBoundsSize = 0.001f;
    private const byte ExpandedStrokeAlpha = 144;

    public static MechDamageSilhouette Build(
        MechWarriorProjectArchive archive,
        MechWarriorShapeImage shape,
        MechWarriorMechChassis chassis)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(chassis);

        var opaquePixels = GetOpaquePixels(shape);
        if (opaquePixels.Count == 0)
        {
            throw new InvalidDataException("The mech damage silhouette contains no opaque pixels.");
        }

        var projectedEdges = BuildProjectedEdges(archive, chassis);
        if (projectedEdges.Count == 0)
        {
            throw new InvalidDataException(
                $"The mech chassis contains no OBJL-grouped polygon edges " +
                $"({chassis.DamageSectionsByObjectId.Count} OBJL records, " +
                $"{chassis.Objects.Count} decoded objects).");
        }

        var shapeBounds = GetBounds(opaquePixels);
        var geometryBounds = GetBounds(projectedEdges.SelectMany(edge => new[] { edge.Start, edge.End }));
        var fittedEdges = FitEdges(projectedEdges, geometryBounds, shapeBounds);
        var labels = new MechDamageSection?[checked(shape.Width * shape.Height)];
        foreach (var pixel in opaquePixels)
        {
            labels[(int)pixel.Y * shape.Width + (int)pixel.X] = fittedEdges
                .MinBy(edge => DistanceSquaredToSegment(pixel, edge.Start, edge.End))
                .Section;
        }

        var originalLabels = labels;
        labels = Thicken(originalLabels, shape.Width, shape.Height);
        return new MechDamageSilhouette(
            shape.Width,
            shape.Height,
            BuildSectionTextures(labels, originalLabels, shape.Width, shape.Height));
    }

    private static IReadOnlyList<ProjectedEdge> BuildProjectedEdges(
        MechWarriorProjectArchive archive,
        MechWarriorMechChassis chassis)
    {
        var edges = new List<ProjectedEdge>();
        var objectsById = chassis.Objects.ToDictionary(chassisObject => chassisObject.Id);
        foreach (var chassisObject in chassis.Objects.Where(chassisObject =>
                     chassisObject.ModelResourceIndex >= 0))
        {
            if (!TryResolveDamageSection(chassisObject, objectsById, chassis, out var section))
            {
                continue;
            }

            var modelEntry = archive.GetEntry("POLY", chassisObject.ModelResourceIndex);
            if (modelEntry.Name.StartsWith("DUMMY", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var model = MechWarriorModel.LoadAll(archive.ReadEntry(modelEntry))
                .MaxBy(candidate => candidate.Polygons.Count) ??
                throw new InvalidDataException($"{modelEntry.Path} contains no mech model.");
            var transform = BuildTransform(chassisObject.Transform);
            var uniqueEdges = new HashSet<(int Start, int End)>();
            foreach (var polygon in model.Polygons)
            {
                for (var index = 0; index < polygon.VertexIndices.Count; index++)
                {
                    var first = polygon.VertexIndices[index];
                    var second = polygon.VertexIndices[(index + 1) % polygon.VertexIndices.Count];
                    var key = first < second ? (first, second) : (second, first);
                    if (!uniqueEdges.Add(key))
                    {
                        continue;
                    }

                    edges.Add(new ProjectedEdge(
                        Project(transform * ToGodotPosition(model.Vertices[first])),
                        Project(transform * ToGodotPosition(model.Vertices[second])),
                        section));
                }
            }
        }

        return edges.AsReadOnly();
    }

    private static bool TryResolveDamageSection(
        MechWarriorWorldObject chassisObject,
        IReadOnlyDictionary<int, MechWarriorWorldObject> objectsById,
        MechWarriorMechChassis chassis,
        out MechDamageSection section)
    {
        var objectId = chassisObject.Id;
        var visited = new HashSet<int>();
        while (visited.Add(objectId))
        {
            if (chassis.DamageSectionsByObjectId.TryGetValue(objectId, out section))
            {
                return true;
            }

            if (!objectsById.TryGetValue(objectId, out var current) || current.RelativeToId < 0)
            {
                break;
            }

            objectId = current.RelativeToId;
        }

        section = default;
        return false;
    }

    private static Transform3D BuildTransform(MechWarriorWorldTransform source)
    {
        var rotation = MechWarriorCoordinateSystem.ToGodotRotation(source.RotationDegrees) *
                       (Mathf.Pi / 180.0f);
        var basis = Basis.FromEuler(rotation).ScaledLocal(
            MechWarriorCoordinateSystem.ToGodotScale(source.Scale));
        return new Transform3D(
            basis,
            MechWarriorCoordinateSystem.ToGodotPosition(source.Translation));
    }

    private static Vector3 ToGodotPosition(MechWarriorModelVertex vertex) =>
        MechWarriorCoordinateSystem.ToGodotPosition(vertex.Position) *
        MechWarriorModelMeshBuilder.SourceUnitScale;

    private static Vector2 Project(Vector3 position) => new(position.X, -position.Y);

    private static IReadOnlyList<ProjectedEdge> FitEdges(
        IReadOnlyList<ProjectedEdge> edges,
        Rect2 geometryBounds,
        Rect2 shapeBounds)
    {
        var geometrySize = new Vector2(
            Math.Max(geometryBounds.Size.X, MinimumBoundsSize),
            Math.Max(geometryBounds.Size.Y, MinimumBoundsSize));
        var scale = Math.Min(
            shapeBounds.Size.X / geometrySize.X,
            shapeBounds.Size.Y / geometrySize.Y);
        var geometryCentre = geometryBounds.GetCenter();
        var shapeCentre = shapeBounds.GetCenter();
        return edges.Select(edge => new ProjectedEdge(
                (edge.Start - geometryCentre) * scale + shapeCentre,
                (edge.End - geometryCentre) * scale + shapeCentre,
                edge.Section))
            .ToArray();
    }

    private static IReadOnlyList<Vector2> GetOpaquePixels(MechWarriorShapeImage shape)
    {
        var pixels = new List<Vector2>();
        for (var y = 0; y < shape.Height; y++)
        {
            var sourceY = shape.Height - 1 - y;
            for (var x = 0; x < shape.Width; x++)
            {
                if (shape.IsOpaque(x, sourceY))
                {
                    pixels.Add(new Vector2(x, y));
                }
            }
        }

        return pixels.AsReadOnly();
    }

    private static Rect2 GetBounds(IEnumerable<Vector2> points)
    {
        using var enumerator = points.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            throw new ArgumentException("At least one point is required.", nameof(points));
        }

        var minimum = enumerator.Current;
        var maximum = enumerator.Current;
        while (enumerator.MoveNext())
        {
            minimum = minimum.Min(enumerator.Current);
            maximum = maximum.Max(enumerator.Current);
        }

        return new Rect2(minimum, maximum - minimum);
    }

    private static float DistanceSquaredToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        var segment = end - start;
        var lengthSquared = segment.LengthSquared();
        if (lengthSquared <= float.Epsilon)
        {
            return point.DistanceSquaredTo(start);
        }

        var fraction = Mathf.Clamp((point - start).Dot(segment) / lengthSquared, 0.0f, 1.0f);
        return point.DistanceSquaredTo(start + segment * fraction);
    }

    private static MechDamageSection?[] Thicken(
        IReadOnlyList<MechDamageSection?> source,
        int width,
        int height)
    {
        var output = source.ToArray();
        ReadOnlySpan<(int X, int Y)> neighbours =
        [
            (-1, 0),
            (1, 0),
            (0, -1),
            (0, 1)
        ];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var outputIndex = y * width + x;
                if (source[outputIndex].HasValue)
                {
                    continue;
                }

                foreach (var (offsetX, offsetY) in neighbours)
                {
                    var neighbourX = x + offsetX;
                    var neighbourY = y + offsetY;
                    if (neighbourX < 0 || neighbourX >= width || neighbourY < 0 || neighbourY >= height)
                    {
                        continue;
                    }

                    var section = source[neighbourY * width + neighbourX];
                    if (section.HasValue)
                    {
                        output[outputIndex] = section;
                        break;
                    }
                }
            }
        }

        return output;
    }

    private static IReadOnlyDictionary<MechDamageSection, Texture2D> BuildSectionTextures(
        IReadOnlyList<MechDamageSection?> labels,
        IReadOnlyList<MechDamageSection?> originalLabels,
        int width,
        int height)
    {
        var sectionPixels = Enum.GetValues<MechDamageSection>().ToDictionary(
            section => section,
            _ => new byte[checked(width * height * 4)]);
        for (var index = 0; index < labels.Count; index++)
        {
            if (!labels[index].HasValue)
            {
                continue;
            }

            var outputOffset = index * 4;
            var pixels = sectionPixels[labels[index]!.Value];
            pixels[outputOffset] = byte.MaxValue;
            pixels[outputOffset + 1] = byte.MaxValue;
            pixels[outputOffset + 2] = byte.MaxValue;
            pixels[outputOffset + 3] = originalLabels[index].HasValue
                ? byte.MaxValue
                : ExpandedStrokeAlpha;
        }

        return sectionPixels.ToDictionary(
            entry => entry.Key,
            entry => (Texture2D)ImageTexture.CreateFromImage(Image.CreateFromData(
                width,
                height,
                false,
                Image.Format.Rgba8,
                entry.Value)));
    }

    private readonly record struct ProjectedEdge(
        Vector2 Start,
        Vector2 End,
        MechDamageSection Section);
}
