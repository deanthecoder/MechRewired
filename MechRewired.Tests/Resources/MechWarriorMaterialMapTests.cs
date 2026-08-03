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
using System.Text;
using MechRewired.Resources;
using NUnit.Framework;

namespace MechRewired.Tests.Resources;

[TestFixture]
public sealed class MechWarriorMaterialMapTests
{
    [Test]
    public void LoadAssociatesMaterialWithPrecedingImageInSelectedBank()
    {
        var data = new byte[0x34 + 20 + 12 + 12];
        "BWD"u8.CopyTo(data);
        WriteImageTag(data, 0x34, 714, "v1mistwl");
        WriteMaterialTag(data, 0x48, 0x0125);
        WriteMaterialTag(data, 0x54, 0x0026);

        var map = MechWarriorMaterialMap.Load(data, 1);

        Assert.That(map.Images, Has.Count.EqualTo(1));
        Assert.That(map.Images[37], Is.EqualTo(new MechWarriorMaterialImage(37, 714, "v1mistwl")));
    }

    private static void WriteImageTag(byte[] data, int offset, short resourceIndex, string name)
    {
        Encoding.ASCII.GetBytes("BMPJ").CopyTo(data, offset);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset + 4), 20);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset + 8), resourceIndex);
        Encoding.ASCII.GetBytes(name).CopyTo(data, offset + 10);
    }

    private static void WriteMaterialTag(byte[] data, int offset, ushort materialId)
    {
        Encoding.ASCII.GetBytes("BMID").CopyTo(data, offset);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset + 4), 12);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 8), materialId);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset + 10), -1);
    }
}
