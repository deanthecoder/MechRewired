// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Buffers.Binary;
using System.Numerics;

namespace MechRewired.Resources;

/// <summary>
/// Represents one decoded MechWarrior 2 WTB model.
/// </summary>
/// <remarks>
/// Both known vertex and polygon layouts are supported. Compound resources containing multiple WTB objects are intentionally deferred.
/// </remarks>
public sealed class MechWarriorModel
{
    private const int HeaderSize = 32;

    private MechWarriorModel(
        byte subtype,
        IReadOnlyList<MechWarriorModelVertex> vertices,
        IReadOnlyList<MechWarriorModelPolygon> polygons)
    {
        Subtype = subtype;
        Vertices = vertices;
        Polygons = polygons;
    }

    public byte Subtype { get; }

    public IReadOnlyList<MechWarriorModelVertex> Vertices { get; }

    public IReadOnlyList<MechWarriorModelPolygon> Polygons { get; }

    /// <summary>
    /// Decodes one complete WTB model payload.
    /// </summary>
    public static MechWarriorModel Load(ReadOnlySpan<byte> data)
    {
        EnsureRange(data, 0, HeaderSize, "WTB header");
        if (!data[..4].SequenceEqual("WTBO"u8))
        {
            throw new InvalidDataException("The model does not have the expected WTBO signature.");
        }

        var vertexCount = ReadUInt16(data, 0x18);
        var polygonCount = ReadUInt16(data, 0x1a);
        if (vertexCount == 0 || polygonCount == 0)
        {
            throw new InvalidDataException("A WTB model must contain both vertices and polygons.");
        }

        var subtype = data[0x1c];
        var layout = WtbLayout.ForSubtype(subtype);
        var vertices = ReadVertices(data, vertexCount, layout);
        var polygons = ReadPolygons(data, polygonCount, vertices.Count, layout, out var bytesConsumed);
        if (bytesConsumed != data.Length)
        {
            throw new InvalidDataException(
                $"The WTB model ends at byte {bytesConsumed:N0}, but its payload contains {data.Length:N0} bytes. " +
                "Compound or trailing WTB data is not supported yet.");
        }

        return new MechWarriorModel(subtype, vertices, polygons);
    }

    private static IReadOnlyList<MechWarriorModelVertex> ReadVertices(
        ReadOnlySpan<byte> data,
        int vertexCount,
        WtbLayout layout)
    {
        EnsureRange(data, HeaderSize, checked(vertexCount * layout.VertexSize), "WTB vertices");
        var vertices = new MechWarriorModelVertex[vertexCount];
        for (var index = 0; index < vertices.Length; index++)
        {
            var offset = HeaderSize + index * layout.VertexSize;
            var position = new Vector3(
                ReadInt32(data, offset),
                ReadInt32(data, offset + 4),
                ReadInt32(data, offset + 8));
            var textureCoordinate = new Vector2(
                ReadInt16(data, offset + 12),
                ReadInt16(data, offset + 14));
            vertices[index] = new MechWarriorModelVertex(position, textureCoordinate);
        }

        return Array.AsReadOnly(vertices);
    }

    private static IReadOnlyList<MechWarriorModelPolygon> ReadPolygons(
        ReadOnlySpan<byte> data,
        int polygonCount,
        int vertexCount,
        WtbLayout layout,
        out int bytesConsumed)
    {
        var polygons = new MechWarriorModelPolygon[polygonCount];
        var offset = checked(HeaderSize + vertexCount * layout.VertexSize);
        for (var polygonIndex = 0; polygonIndex < polygons.Length; polygonIndex++)
        {
            EnsureRange(data, offset, layout.ShortPolygonSize, $"WTB polygon {polygonIndex}");
            var polygonVertexCount = ReadUInt16(data, offset + layout.PolygonVertexCountOffset);
            if (polygonVertexCount is < 3 or > 7)
            {
                throw new InvalidDataException(
                    $"WTB polygon {polygonIndex} has {polygonVertexCount} vertices; the supported range is 3-7.");
            }

            var polygonSize = polygonVertexCount < 5 ? layout.ShortPolygonSize : layout.LongPolygonSize;
            EnsureRange(data, offset, polygonSize, $"WTB polygon {polygonIndex}");
            var indices = new int[polygonVertexCount];
            for (var vertexIndex = 0; vertexIndex < indices.Length; vertexIndex++)
            {
                var index = ReadUInt16(data, offset + layout.FirstPolygonVertexOffset + vertexIndex * sizeof(ushort));
                if (index >= vertexCount)
                {
                    throw new InvalidDataException(
                        $"WTB polygon {polygonIndex} references vertex {index}, but the model contains {vertexCount} vertices.");
                }

                indices[vertexIndex] = index;
            }

            polygons[polygonIndex] = new MechWarriorModelPolygon(
                data[offset],
                data[offset + 1],
                Array.AsReadOnly(indices));
            offset += polygonSize;
        }

        bytesConsumed = offset;
        return Array.AsReadOnly(polygons);
    }

    private static short ReadInt16(ReadOnlySpan<byte> data, int offset)
    {
        EnsureRange(data, offset, sizeof(short), "WTB data");
        return BinaryPrimitives.ReadInt16LittleEndian(data[offset..]);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset)
    {
        EnsureRange(data, offset, sizeof(ushort), "WTB data");
        return BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
    }

    private static int ReadInt32(ReadOnlySpan<byte> data, int offset)
    {
        EnsureRange(data, offset, sizeof(int), "WTB data");
        return BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
    }

    private static void EnsureRange(ReadOnlySpan<byte> data, int offset, int length, string description)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
        {
            throw new InvalidDataException(
                $"The {description} range ({offset:N0} + {length:N0} bytes) exceeds the {data.Length:N0}-byte model.");
        }
    }

    private sealed record WtbLayout(
        int VertexSize,
        int ShortPolygonSize,
        int LongPolygonSize,
        int PolygonVertexCountOffset,
        int FirstPolygonVertexOffset)
    {
        public static WtbLayout ForSubtype(byte subtype) =>
            subtype == 0
                ? new WtbLayout(40, 40, 46, 30, 32)
                : new WtbLayout(16, 12, 18, 2, 4);
    }
}
