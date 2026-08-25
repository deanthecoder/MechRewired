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
using MechRewired.Simulation;
using NUnit.Framework;

namespace MechRewired.Tests.Resources;

[TestFixture]
public sealed class MechWarriorMechChassisTests
{
    [Test]
    public void LoadRetainsTheRootAndFirstRepresentationAlongsideWeaponMarkers()
    {
        var data = WriteChassis(writer =>
        {
            WriteObjectTag(writer, 0, -1, 100, new Vector3(10.0f, 0.0f, -5.0f));
            WriteRepresentationTag(writer);
            WriteObjectTag(writer, 1, 0, 101, new Vector3(0.0f, 2.0f, 3.0f));
            WriteThingTag(writer, 1);
            WritePointOfFireTag(writer, 1, 7);
            WriteDamageGroupTag(writer, 1, 6);
            WriteRepresentationTag(writer);
            WriteObjectTag(writer, 2, 0, 102, new Vector3(0.0f, 4.0f, 6.0f));
        });

        var chassis = MechWarriorMechChassis.Load(data);

        Assert.That(chassis.Objects, Has.Count.EqualTo(2));
        Assert.That(chassis.Objects[0].Id, Is.EqualTo(0));
        Assert.That(chassis.Objects[0].ModelResourceIndex, Is.EqualTo(100));
        Assert.That(chassis.Objects[0].Transform.Translation, Is.EqualTo(new Vector3(10.0f, 0.0f, -5.0f)));
        Assert.That(chassis.Objects[0].LocalTransform.Translation, Is.EqualTo(new Vector3(10.0f, 0.0f, -5.0f)));
        Assert.That(chassis.Objects[1].Id, Is.EqualTo(1));
        Assert.That(chassis.Objects[1].ModelResourceIndex, Is.EqualTo(101));
        Assert.That(chassis.Objects[1].Transform.Translation, Is.EqualTo(new Vector3(10.0f, 2.0f, -2.0f)));
        Assert.That(chassis.Objects[1].LocalTransform.Translation, Is.EqualTo(new Vector3(0.0f, 2.0f, 3.0f)));
        Assert.That(chassis.Objects.Select(worldObject => worldObject.Id), Does.Not.Contain(2));
        Assert.That(chassis.ThingObjectIds, Is.EqualTo(new[] { 1 }));
        Assert.That(chassis.PointsOfFire, Is.EqualTo(new[] { new MechWarriorPointOfFire(1, 7) }));
        Assert.That(chassis.PointsOfFire[0].Section, Is.EqualTo(MechDamageSection.LeftLeg));
        Assert.That(
            chassis.DamageSectionsByObjectId,
            Is.EqualTo(new Dictionary<int, MechDamageSection>
            {
                [1] = MechDamageSection.LeftArm
            }));
    }

    private static byte[] WriteChassis(Action<BinaryWriter> writeTags)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, true);
        WriteFixedAscii(writer, "BWD", 3);
        writer.Write(new byte[0x34 - 3]);
        writeTags(writer);
        return stream.ToArray();
    }

    private static void WriteRepresentationTag(BinaryWriter writer)
    {
        WriteFixedAscii(writer, "REPR", 4);
        writer.Write(8);
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
        writer.Write((ushort)0);
        WriteVector(writer, Vector3.One, 1.0f);
        WriteVector(writer, Vector3.Zero, 65536.0f);
        WriteVector(writer, translation, 100.0f);
        writer.Write(new byte[6]);
        writer.Write(modelResourceIndex);
        writer.Write((short)0);
    }

    private static void WriteThingTag(BinaryWriter writer, int objectId)
    {
        WriteFixedAscii(writer, "THNG", 4);
        writer.Write(12);
        writer.Write(objectId);
    }

    private static void WritePointOfFireTag(BinaryWriter writer, short objectId, short id)
    {
        WriteFixedAscii(writer, "POFO", 4);
        writer.Write(12);
        writer.Write(objectId);
        writer.Write(id);
    }

    private static void WriteDamageGroupTag(BinaryWriter writer, short objectId, short group)
    {
        WriteFixedAscii(writer, "OBJL", 4);
        writer.Write(12);
        writer.Write(objectId);
        writer.Write(group);
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
