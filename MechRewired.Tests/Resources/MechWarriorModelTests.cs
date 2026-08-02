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
using MechRewired.Resources;
using NUnit.Framework;

namespace MechRewired.Tests.Resources;

/// <summary>
/// Verifies WTB model decoding.
/// </summary>
/// <remarks>
/// Synthetic models exercise both known layouts without distributing original game data.
/// </remarks>
[TestFixture]
public sealed class MechWarriorModelTests
{
    [TestCase((byte)0)]
    [TestCase((byte)2)]
    public void LoadDecodesVerticesTextureCoordinatesPolygonsAndMaterials(byte subtype)
    {
        var data = BuildModel(subtype);

        var model = MechWarriorModel.Load(data);

        Assert.That(model.Subtype, Is.EqualTo(subtype));
        Assert.That(model.Vertices, Has.Count.EqualTo(4));
        Assert.That(model.Polygons, Has.Count.EqualTo(1));
        Assert.That(model.Vertices[1].Position, Is.EqualTo(new Vector3(100, -50, 25)));
        Assert.That(model.Vertices[1].TextureCoordinate, Is.EqualTo(new Vector2(4, -3)));
        Assert.That(model.Polygons[0].MaterialIndex, Is.EqualTo(34));
        Assert.That(model.Polygons[0].PaletteIndex, Is.EqualTo(120));
        Assert.That(model.Polygons[0].VertexIndices, Is.EqualTo(new[] { 0, 1, 2, 3 }));
    }

    [Test]
    public void LoadRejectsPolygonVertexReferencesOutsideTheModel()
    {
        var data = BuildModel(2);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(104), 4);

        var exception = Assert.Throws<InvalidDataException>(() => MechWarriorModel.Load(data));

        Assert.That(exception.Message, Does.Contain("polygon 0"));
        Assert.That(exception.Message, Does.Contain("vertex 4"));
    }

    [Test]
    public void LoadRejectsCompoundModelDataAndDirectsTheCallerToLoadAll()
    {
        var model = BuildModel(2);
        var data = new byte[model.Length * 2];
        model.CopyTo(data, 0);
        model.CopyTo(data, model.Length);

        var exception = Assert.Throws<InvalidDataException>(() => MechWarriorModel.Load(data));

        Assert.That(exception.Message, Does.Contain("LoadAll"));
        Assert.That(MechWarriorModel.LoadAll(data), Has.Count.EqualTo(2));
    }

    private static byte[] BuildModel(byte subtype)
    {
        const int vertexCount = 4;
        var vertexSize = subtype == 0 ? 40 : 16;
        var polygonSize = subtype == 0 ? 40 : 12;
        var polygonVertexCountOffset = subtype == 0 ? 30 : 2;
        var firstPolygonVertexOffset = subtype == 0 ? 32 : 4;
        var polygonOffset = 32 + vertexCount * vertexSize;
        var data = new byte[polygonOffset + polygonSize];

        "WTBO"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x18), vertexCount);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x1a), 1);
        data[0x1c] = subtype;

        for (var index = 0; index < vertexCount; index++)
        {
            var offset = 32 + index * vertexSize;
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset), index * 100);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset + 4), index * -50);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset + 8), index * 25);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset + 12), (short)(index * 4));
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset + 14), (short)(index * -3));
        }

        data[polygonOffset] = 34;
        data[polygonOffset + 1] = 120;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(polygonOffset + polygonVertexCountOffset), vertexCount);
        for (var index = 0; index < vertexCount; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                data.AsSpan(polygonOffset + firstPolygonVertexOffset + index * sizeof(ushort)),
                (ushort)index);
        }

        return data;
    }
}
