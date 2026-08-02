// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Numerics;
using System.Text;
using MechRewired.Resources;
using NUnit.Framework;

namespace MechRewired.Tests.Resources;

/// <summary>
/// Verifies the BWD tags needed to assemble an MW2 level.
/// </summary>
/// <remarks>
/// Synthetic tags keep the tests independent of copyrighted game data.
/// </remarks>
[TestFixture]
public sealed class MechWarriorWorldFileTests
{
    [Test]
    public void LoadAssociatesEachIncludeWithTheCurrentBlockTransform()
    {
        var data = WriteWorld(writer =>
        {
            WriteTransformTag(writer, "BLKX", Vector3.One, new Vector3(0.0f, -90.0f, 0.0f), new Vector3(12.5f, 0.0f, -3.0f));
            WriteIncludeTag(writer, 42, "TESTAREA");
        });

        var world = MechWarriorWorldFile.Load(data);

        Assert.That(world.Includes, Has.Count.EqualTo(1));
        Assert.That(world.Includes[0].ResourceIndex, Is.EqualTo(42));
        Assert.That(world.Includes[0].Name, Is.EqualTo("TESTAREA"));
        Assert.That(world.Includes[0].Transform.RotationDegrees, Is.EqualTo(new Vector3(0.0f, -90.0f, 0.0f)));
        Assert.That(world.Includes[0].Transform.Translation, Is.EqualTo(new Vector3(12.5f, 0.0f, -3.0f)));
    }

    [Test]
    public void LoadAppliesTheParentAndRelativeObjectTransforms()
    {
        var data = WriteWorld(writer =>
        {
            WriteObjectTag(writer, 7, -2, 101, new Vector3(2.0f, 3.0f, 4.0f));
            WriteObjectTag(writer, 8, 7, 102, new Vector3(5.0f, 6.0f, 7.0f));
        });
        var parent = new MechWarriorWorldTransform(Vector3.One, Vector3.Zero, new Vector3(10.0f, 20.0f, 30.0f));

        var world = MechWarriorWorldFile.Load(data, parent);

        Assert.That(world.Objects, Has.Count.EqualTo(2));
        Assert.That(world.Objects[0].ModelResourceIndex, Is.EqualTo(101));
        Assert.That(world.Objects[0].Transform.Translation, Is.EqualTo(new Vector3(12.0f, 23.0f, 34.0f)));
        Assert.That(world.Objects[1].Transform.Translation, Is.EqualTo(new Vector3(17.0f, 29.0f, 41.0f)));
    }

    [Test]
    public void LoadRejectsATagThatExtendsPastTheResource()
    {
        var data = WriteWorld(writer =>
        {
            WriteFixedAscii(writer, "INCL", 4);
            writer.Write(200);
        });

        var exception = Assert.Throws<InvalidDataException>(() => MechWarriorWorldFile.Load(data));

        Assert.That(exception.Message, Does.Contain("INCL tag"));
        Assert.That(exception.Message, Does.Contain("exceeds"));
    }

    private static byte[] WriteWorld(Action<BinaryWriter> writeTags)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, true);
        WriteFixedAscii(writer, "BWD", 3);
        writer.Write(new byte[0x34 - 3]);
        writeTags(writer);
        return stream.ToArray();
    }

    private static void WriteTransformTag(
        BinaryWriter writer,
        string name,
        Vector3 scale,
        Vector3 rotation,
        Vector3 translation)
    {
        WriteFixedAscii(writer, name, 4);
        writer.Write(44);
        WriteVector(writer, scale, 1.0f);
        WriteVector(writer, rotation, 65536.0f);
        WriteVector(writer, translation, 100.0f);
    }

    private static void WriteIncludeTag(BinaryWriter writer, short resourceIndex, string name)
    {
        WriteFixedAscii(writer, "INCL", 4);
        writer.Write(20);
        writer.Write(resourceIndex);
        WriteFixedAscii(writer, name, 9);
        writer.Write((byte)0);
    }

    private static void WriteObjectTag(
        BinaryWriter writer,
        short id,
        short relativeTo,
        short modelResourceIndex,
        Vector3 translation)
    {
        WriteFixedAscii(writer, "OBJ", 4);
        writer.Write(60);
        writer.Write(id);
        writer.Write(relativeTo);
        writer.Write((short)0);
        WriteVector(writer, Vector3.One, 1.0f);
        WriteVector(writer, Vector3.Zero, 65536.0f);
        WriteVector(writer, translation, 100.0f);
        writer.Write(new byte[6]);
        writer.Write(modelResourceIndex);
        writer.Write((short)0);
    }

    private static void WriteVector(BinaryWriter writer, Vector3 vector, float scale)
    {
        writer.Write((int)(vector.X * scale));
        writer.Write((int)(vector.Y * scale));
        writer.Write((int)(vector.Z * scale));
    }

    private static void WriteFixedAscii(BinaryWriter writer, string value, int length)
    {
        var data = new byte[length];
        Encoding.ASCII.GetBytes(value.AsSpan(), data);
        writer.Write(data);
    }
}
