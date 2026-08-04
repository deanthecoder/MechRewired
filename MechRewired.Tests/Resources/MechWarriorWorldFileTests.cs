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
            WriteObjectTag(writer, 7, -2, 101, new Vector3(2.0f, 3.0f, 4.0f), 5, 0x50);
            WriteObjectTag(writer, 8, 7, 102, new Vector3(5.0f, 6.0f, 7.0f));
        });
        var parent = new MechWarriorWorldTransform(Vector3.One, Vector3.Zero, new Vector3(10.0f, 20.0f, 30.0f));

        var world = MechWarriorWorldFile.Load(data, parent);

        Assert.That(world.Objects, Has.Count.EqualTo(2));
        Assert.That(world.Objects[0].ModelResourceIndex, Is.EqualTo(101));
        Assert.That(world.Objects[0].RelativeToId, Is.EqualTo(-2));
        Assert.That(world.Objects[0].CollisionType, Is.EqualTo(5));
        Assert.That(world.Objects[0].ObjectType, Is.EqualTo(0x50));
        Assert.That(world.Objects[0].Transform.Translation, Is.EqualTo(new Vector3(12.0f, 23.0f, 34.0f)));
        Assert.That(world.Objects[1].Transform.Translation, Is.EqualTo(new Vector3(17.0f, 29.0f, 41.0f)));
    }

    [Test]
    public void LoadAssociatesGameplayMetadataWithAnObjectAssembly()
    {
        var data = WriteWorld(writer =>
        {
            WriteObjectTag(writer, 7, -2, 101, Vector3.Zero);
            WriteEntityTag(writer, 7, 12, 250, "Dire Wolf");
        });

        var world = MechWarriorWorldFile.Load(data);

        Assert.That(world.Entities, Has.Count.EqualTo(1));
        Assert.That(world.Entities[0].ObjectId, Is.EqualTo(7));
        Assert.That(world.Entities[0].DestroyedObjectId, Is.EqualTo(12));
        Assert.That(world.Entities[0].Health, Is.EqualTo(250));
        Assert.That(world.Entities[0].Description, Is.EqualTo("Dire Wolf"));
    }

    [Test]
    public void LoadReadsPlanetLightingConfiguration()
    {
        var data = WriteWorld(writer =>
        {
            WriteTimeOfDayTag(writer, 1500);
            WriteLightingTag(writer, new Vector3(100.0f, 250.0f, -50.0f), 96, 1, 1500.0f);
            WriteLuminosityTableTag(writer, "FOG");
            WriteViewDistanceTag(writer, 500.0f);
        });

        var world = MechWarriorWorldFile.Load(data);

        Assert.That(world.TimeOfDay, Is.EqualTo(1500));
        Assert.That(world.Lighting.Position, Is.EqualTo(new Vector3(100.0f, 250.0f, -50.0f)));
        Assert.That(world.Lighting.AmbientLevel, Is.EqualTo(96));
        Assert.That(world.Lighting.Type, Is.EqualTo(1));
        Assert.That(world.Lighting.ShadeDistance, Is.EqualTo(1500.0f));
        Assert.That(world.LuminosityTable, Is.EqualTo("FOG"));
        Assert.That(world.ViewDistance, Is.EqualTo(500.0f));
    }

    [Test]
    public void LoadReadsMissionNavigationPoint()
    {
        var data = WriteWorld(writer => WriteNavigationPointTag(
            writer,
            new Vector3(125.0f, 3.5f, -80.0f),
            270,
            true,
            2,
            25,
            0x30,
            "Wolf deployment"));

        var world = MechWarriorWorldFile.Load(data);

        Assert.That(world.NavPoints, Has.Count.EqualTo(1));
        Assert.That(world.NavPoints[0].Position, Is.EqualTo(new Vector3(125.0f, 3.5f, -80.0f)));
        Assert.That(world.NavPoints[0].StartingAngle, Is.EqualTo(270));
        Assert.That(world.NavPoints[0].Targetable, Is.True);
        Assert.That(world.NavPoints[0].GroupId, Is.EqualTo(2));
        Assert.That(world.NavPoints[0].Radius, Is.EqualTo(25));
        Assert.That(world.NavPoints[0].ActionFlags, Is.EqualTo(0x30));
        Assert.That(world.NavPoints[0].Description, Is.EqualTo("Wolf deployment"));
    }

    [Test]
    public void LoadReadsScriptedObjectTask()
    {
        var data = WriteWorld(writer => WriteTaskTag(writer, 4, 0x20, "4;400,mecfire1,1"));

        var world = MechWarriorWorldFile.Load(data);

        Assert.That(world.Tasks, Has.Count.EqualTo(1));
        Assert.That(world.Tasks[0].Type, Is.EqualTo(4));
        Assert.That(world.Tasks[0].Flags, Is.EqualTo(0x20));
        Assert.That(world.Tasks[0].Command, Is.EqualTo("4;400,mecfire1,1"));
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
        Vector3 translation,
        ushort collisionType = 0,
        ushort objectType = 0)
    {
        WriteFixedAscii(writer, "OBJ", 4);
        writer.Write(60);
        writer.Write(id);
        writer.Write(relativeTo);
        writer.Write(collisionType);
        WriteVector(writer, Vector3.One, 1.0f);
        WriteVector(writer, Vector3.Zero, 65536.0f);
        WriteVector(writer, translation, 100.0f);
        writer.Write((short)0);
        writer.Write(objectType);
        writer.Write((short)0);
        writer.Write(modelResourceIndex);
        writer.Write((short)0);
    }

    private static void WriteEntityTag(
        BinaryWriter writer,
        short objectId,
        short destroyedObjectId,
        ushort health,
        string description)
    {
        var descriptionBytes = Encoding.ASCII.GetBytes(description);
        WriteFixedAscii(writer, "GT", 4);
        writer.Write(33 + descriptionBytes.Length);
        writer.Write(objectId);
        writer.Write(destroyedObjectId);
        writer.Write(new byte[12]);
        writer.Write(health);
        writer.Write(new byte[6]);
        writer.Write(descriptionBytes);
        writer.Write((byte)0);
    }

    private static void WriteTimeOfDayTag(BinaryWriter writer, int timeOfDay)
    {
        WriteFixedAscii(writer, "INIT", 4);
        writer.Write(28);
        writer.Write(new byte[8]);
        writer.Write(timeOfDay);
        writer.Write(new byte[8]);
    }

    private static void WriteLightingTag(
        BinaryWriter writer,
        Vector3 position,
        ushort ambientLevel,
        ushort type,
        float shadeDistance)
    {
        WriteFixedAscii(writer, "LITE", 4);
        writer.Write(40);
        writer.Write(new byte[8]);
        WriteVector(writer, position, 100.0f);
        writer.Write(ambientLevel);
        writer.Write(type);
        writer.Write((int)(shadeDistance * 100.0f));
        writer.Write(1);
    }

    private static void WriteLuminosityTableTag(BinaryWriter writer, string name)
    {
        WriteFixedAscii(writer, "LTBL", 4);
        writer.Write(24);
        writer.Write((short)1);
        WriteFixedAscii(writer, name, 8);
        writer.Write(new byte[6]);
    }

    private static void WriteViewDistanceTag(BinaryWriter writer, float distance)
    {
        WriteFixedAscii(writer, "VIEW", 4);
        writer.Write(16);
        writer.Write(64);
        writer.Write((int)(distance * 100.0f));
    }

    private static void WriteNavigationPointTag(
        BinaryWriter writer,
        Vector3 position,
        ushort startingAngle,
        bool targetable,
        ushort groupId,
        ushort radius,
        ushort actionFlags,
        string description)
    {
        WriteFixedAscii(writer, "NAVP", 4);
        writer.Write(80);
        WriteVector(writer, position, 100.0f);
        writer.Write((short)0);
        writer.Write(startingAngle);
        writer.Write((ushort)(targetable ? 1 : 0));
        writer.Write(new byte[4]);
        writer.Write(groupId);
        writer.Write(radius);
        writer.Write(actionFlags);
        WriteFixedAscii(writer, description, 21);
        writer.Write(new byte[23]);
    }

    private static void WriteTaskTag(BinaryWriter writer, int type, ushort flags, string command)
    {
        var commandBytes = Encoding.ASCII.GetBytes(command);
        WriteFixedAscii(writer, "TSK", 4);
        writer.Write(15 + commandBytes.Length);
        writer.Write(type);
        writer.Write(flags);
        writer.Write(commandBytes);
        writer.Write((byte)0);
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
